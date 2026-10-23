import {api,json,ApiError} from './api';
import {allOutbox,mutate,put,session,signal,saveDraft,draftForId,draftsForUser,outboxKey,get} from './offlineStore';
import {markVisitDeleted} from './visitLifecycle';
import {incompleteQuestions,hasVisitContent} from './surveyRules';
import type {Draft,Outbox,Mutation} from './offlineStore';
import type {Visit,Detail} from './types';
import {customerKey,pendingCustomers,sendCustomerChanges} from './customerQueue';

export const SYNC_INTERVAL_MS = 5 * 60 * 1000;
export type SyncProgress = {running:boolean;sent:number;total:number;message:string;checkedAt?:string};
let progress:SyncProgress={running:false,sent:0,total:0,message:''};
let busy=false;
export const syncProgress=()=>progress;
function report(update:Partial<SyncProgress>){
  progress={...progress,...update};
  window.dispatchEvent(new CustomEvent('paper-sync-progress',{detail:progress}));
}

export async function enqueue(draft:Draft,send=true){
  if(draft.deletedOnServer)throw new Error('Η επίσκεψη έχει διαγραφεί. Ξεκίνησε νέα επίσκεψη αν χρειάζεται.');
  if(!hasVisitContent(draft.survey.questions,draft.answers,draft.notes))throw new Error('Δεν αποστέλλεται κενή επίσκεψη. Συμπλήρωσε μία ενότητα ή γράψε σημειώσεις.');
  if(incompleteQuestions(draft.survey.questions,draft.answers).length)throw new Error('Ολοκλήρωσε την ενότητα που ξεκίνησες πριν την αποστολή.');
  await mutate<Outbox>(outboxKey(draft.userId,draft.id),current=>{
    if(current?.state==='deleted')throw new Error('Η επίσκεψη έχει διαγραφεί.');
    if(current?.state==='conflict')throw new Error('Η επίσκεψη άλλαξε. Δες πρώτα τη νεότερη καταχώριση.');
    if(current?.state==='error')current=undefined;
    if(current?.operations.some(operation=>operation.revision===draft.revision))return current;
    if(draft.savedRevision===draft.revision&&!current?.operations.length)return undefined;
    const tail=current?.operations.at(-1);
    const id=crypto.randomUUID();
    const operation:Mutation={id,revision:draft.revision,attempted:false,body:{
      mutationId:id,expectedVersion:tail?tail.body.expectedVersion+1:draft.serverVersion,
      branchCode:draft.branchCode,surveyVersion:draft.survey.version,
      occurredAtUtc:draft.occurredAtUtc,notes:draft.notes,
      answers:draft.survey.questions.map(question=>({questionId:question.id,optionIds:draft.answers[question.id]??[]})),
    }};
    return {visitId:draft.id,branchCode:draft.branchCode,userId:draft.userId,
      operations:[...(current?.operations??[]),operation],state:'waiting'};
  });
  signal();
  if(send)void syncNow().catch(()=>{});
}

async function collectCorrections(userId:string){
  const blocked=new Set((await allOutbox(userId))
    .filter(record=>record.state==='conflict'||record.state==='error'||record.state==='deleted').map(record=>record.visitId));
  for(const draft of draftsForUser(userId)){
    // New visits need an explicit first submission. Never send an incomplete section.
    if(!draft.deletedOnServer&&hasVisitContent(draft.survey.questions,draft.answers,draft.notes)&&draft.serverVersion>0&&draft.revision!==draft.savedRevision&&!blocked.has(draft.id)&&
      !incompleteQuestions(draft.survey.questions,draft.answers).length){
      await enqueue(draft,false);
    }
  }
}

async function acknowledge(userId:string,record:Outbox,operation:Mutation,visit:Visit){
  if(await get<boolean>(`deleted-visit:${userId}:${record.visitId}`))return;
  await mutate<Outbox>(outboxKey(userId,record.visitId),current=>{
    if(!current)return current;
    const operations=current.operations.filter(item=>item.id!==operation.id);
    return operations.length?{...current,operations,state:'waiting'}:undefined;
  });
  const draft=draftForId(userId,record.visitId);
  if(draft)saveDraft({...draft,serverVersion:visit.version,savedRevision:operation.revision,lastSentAt:new Date().toISOString()});
  if(draft)await put(`cache:${userId}:/visits/${visit.id}/editor`,{
    visit,survey:draft.survey,answers:Object.fromEntries(operation.body.answers.map(answer=>[answer.questionId,answer.optionIds])),
  });
  await put(`last-visit:${userId}:${record.branchCode}`,visit);
  await mutate<Detail>(`cache:${userId}:/customers/${encodeURIComponent(record.branchCode)}`,current=>current?{
    ...current,history:[visit,...current.history.filter(item=>item.id!==visit.id)]
      .sort((a,b)=>b.createdAtUtc.localeCompare(a.createdAtUtc)).slice(0,10),
  }:current);
  window.dispatchEvent(new CustomEvent('paper-visit-saved',{detail:{userId,branchCode:record.branchCode}}));
  signal();
}

export async function syncNow(){
  const user=session();
  if(!user||busy)return;
  busy=true;
  const owner=crypto.randomUUID();
  const lockKey=`sync-lock:${user.id}`;
  let ownsLease=false;
  const completed=new Set<string>();
  const seen=new Set<string>();
  const attempted=new Set<string>();
  try{
    const lease=await mutate<{owner:string;until:number}>(lockKey,current=>
      !current||current.until<Date.now()?{owner,until:Date.now()+45000}:current);
    if(lease?.owner!==owner)return;
    ownsLease=true;
    await collectCorrections(user.id);
    let records=await allOutbox(user.id);
    const customers=await pendingCustomers(user.id);
    records.forEach(record=>seen.add(record.visitId));
    customers.forEach(record=>seen.add(`customer:${record.branchCode}`));
    report({running:true,sent:0,total:seen.size,message:records.length||customers.length?'Έλεγχος σύνδεσης…':'Δεν υπάρχουν δεδομένα για αποστολή.'});
    if(!records.length&&!customers.length)return;
    // navigator.onLine alone also reports Wi-Fi networks without Internet access.
    if(!navigator.onLine){report({message:'Χωρίς σύνδεση. Οι απαντήσεις παραμένουν στη συσκευή. Νέα δοκιμή σε 5 λεπτά.'});return;}
    try{await api('/auth/status',{timeoutMs:5000});}
    catch{window.dispatchEvent(new CustomEvent('paper-network',{detail:'cached'}));report({message:'Δεν υπάρχει σύνδεση με την εφαρμογή. Νέα δοκιμή σε 5 λεπτά.'});return;}
    const customerResult=await sendCustomerChanges(user,code=>{
      seen.add(`customer:${code}`);
      completed.add(`customer:${code}`);report({sent:completed.size,total:seen.size});
    },async()=>{
      const lease=await mutate<{owner:string;until:number}>(lockKey,current=>current?.owner===owner?{owner,until:Date.now()+45000}:current);
      return lease?.owner===owner;
    });
    if(customerResult.connectionLost)return;
    const blockedCustomers=new Set((await pendingCustomers(user.id)).map(record=>record.branchCode));
    while(records.length){
      let processed=false;
      for(const record of records){
        if(record.state==='conflict'||record.state==='error'||record.state==='deleted'||blockedCustomers.has(record.branchCode))continue;
        if(await get(customerKey(user.id,record.branchCode)))continue;
        seen.add(record.visitId);
        for(const operation of record.operations){
          if(attempted.has(operation.id))break;
          if(session()?.id!==user.id)return;
          const renewed=await mutate<{owner:string;until:number}>(lockKey,current=>
            current?.owner===owner?{owner,until:Date.now()+45000}:current);
          if(renewed?.owner!==owner)return;
          const key=outboxKey(user.id,record.visitId);
          const current=await mutate<Outbox>(key,item=>item?{...item,state:'sending',
            operations:item.operations.map(value=>value.id===operation.id?{...value,attempted:true}:value)}:item);
          if(!current?.operations.some(item=>item.id===operation.id))break;
          attempted.add(operation.id);processed=true;completed.delete(record.visitId);
          report({sent:completed.size,total:seen.size,message:'Αποστολή αποθηκευμένων απαντήσεων…'});signal();
          try{
            const visit=await api<Visit>(`/visits/${record.visitId}`,json(operation.body,'PUT'));
            await acknowledge(user.id,record,operation,visit);
            const remaining=await allOutbox(user.id);
            if(!remaining.some(item=>item.visitId===record.visitId))completed.add(record.visitId);
            report({sent:completed.size,total:seen.size});
          }catch(error){
            const status=error instanceof ApiError?error.status:0;
            const details=error instanceof ApiError?error.data:undefined;
            const message=status===410?'Η επίσκεψη έχει διαγραφεί. Οι τοπικές αλλαγές δεν στάλθηκαν.':status===401?'Συνδέσου ξανά για να σταλεί η επίσκεψη.':
              status===409?'Η επίσκεψη άλλαξε από άλλον χρήστη. Χρειάζεται έλεγχος.':
              status===400||status===403?String(details?.message??'Έλεγξε τις απαντήσεις της επίσκεψης.'):
              'Αποθηκεύτηκε στη συσκευή. Νέα δοκιμή σε 5 λεπτά.';
            await mutate<Outbox>(key,item=>item?{...item,
              state:status===410?'deleted':status===409?'conflict':status===401?'auth':status===400||status===403?'error':'waiting',
              message,current:details?.current as Visit|undefined}:item);
            if(status===410)await markVisitDeleted(user,{id:record.visitId,branchCode:record.branchCode});
            signal();report({message});
            // A failed connection affects the whole queue. Do not hammer each customer.
            if(!status||status>=500||status===401)return;
            break;
          }
        }
      }
      // Include another customer's submission added while this batch was in progress.
      records=(await allOutbox(user.id)).filter(record=>record.operations.some(operation=>!attempted.has(operation.id)));
      if(!processed)break;
    }
    const remaining=await allOutbox(user.id);
    const remainingCustomers=await pendingCustomers(user.id);
    report({sent:completed.size,total:seen.size,message:remaining.length||remainingCustomers.length?
      `${remaining.length} επισκέψεις και ${remainingCustomers.length} καρτέλες πελατών παραμένουν στη συσκευή. Όσες χρειάζονται έλεγχο εμφανίζονται παρακάτω.`:
      'Στάλθηκαν όλες οι αποθηκευμένες απαντήσεις και οι καρτέλες πελατών.'});
  }catch{
    report({message:'Δεν ολοκληρώθηκε η αποστολή. Οι απαντήσεις παραμένουν στη συσκευή.'});
  }finally{
    if(ownsLease){
      await mutate<{owner:string;until:number}>(lockKey,current=>current?.owner===owner?undefined:current).catch(()=>{});
      report({running:false,checkedAt:new Date().toISOString()});
    }
    busy=false;signal();
  }
}

export async function resolveConflict(draft:Draft,version:number){
  await mutate<Outbox>(outboxKey(draft.userId,draft.id),()=>undefined);
  saveDraft({...draft,serverVersion:version,savedRevision:-1});signal();
}
export function startSync(){
  progress={running:false,sent:0,total:0,message:''};
  const kick=()=>void syncNow();
  const timer=setInterval(kick,SYNC_INTERVAL_MS);
  kick();
  return()=>clearInterval(timer);
}
