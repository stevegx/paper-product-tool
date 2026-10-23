import {api,json,ApiError} from './api';
import {get,mutate,matchingEntries,signal,session} from './offlineStore';
import type {Branch,Detail,User} from './types';

export type CustomerFields={name:string;taxId:string;profession:string;address:string;postalCode:string;city:string;phone:string;taxOffice:string;customerNotes:string;allowExistingTaxId:boolean};
export type CustomerOperation={id:string;create:boolean;body:CustomerFields&{mutationId:string;expectedVersion:number}};
export type CustomerPending={branchCode:string;userId:string;operations:CustomerOperation[];state:'waiting'|'sending'|'review'|'conflict'|'auth';message?:string;existing?:Branch[];current?:Branch};
export const customerKey=(userId:string,code:string)=>`customer-outbox:${userId}:${code}`;
export const pendingCustomers=(userId:string)=>matchingEntries<CustomerPending>(`customer-outbox:${userId}:`);
export const newCustomerCode=(id:string)=>`NEW-${id.replaceAll('-','').toLowerCase()}`;

export async function cacheCustomer(user:User,branch:Branch){
  const path=`cache:${user.id}:/customers/${encodeURIComponent(branch.code)}`;
  await mutate<Detail>(path,detail=>({...detail,branch,year:detail?.year??new Date().getFullYear(),total:detail?.total??0,categories:detail?.categories??[],history:detail?.history??[]}));
  await mutate<Branch[]>(`cache:${user.id}:/customers?afm=${branch.taxId}`,rows=>[...(rows??[]).filter(row=>row.code!==branch.code),branch]);
}
export async function queueCustomer(user:User,code:string,fields:CustomerFields,branch?:Branch){
  let operation:CustomerOperation|undefined;
  const record=await mutate<CustomerPending>(customerKey(user.id,code),current=>{
    if(current?.state==='conflict')throw new Error('Σύγκρινε πρώτα τις αλλαγές του πελάτη πριν αποθηκεύσεις ξανά.');
    const tail=current?.operations.at(-1);
    if(tail&&JSON.stringify(fields)===JSON.stringify(Object.fromEntries(Object.entries(tail.body).filter(([key])=>key!=='mutationId'&&key!=='expectedVersion'))))return current;
    const create=!tail&&!(branch?.customerVersion);
    const id=create?code.slice(4).replace(/^(.{8})(.{4})(.{4})(.{4})(.{12})$/,'$1-$2-$3-$4-$5'):crypto.randomUUID();
    operation={id,create,body:{...fields,mutationId:id,expectedVersion:tail?tail.body.expectedVersion+1:branch?.customerVersion??0}};
    return {branchCode:code,userId:user.id,operations:[...(current?.operations??[]),operation],state:'waiting'};
  });
  if(record){
    const tail=record.operations.at(-1)!;
    await cacheCustomer(user,{...branch,...tail.body,code,assignedSeller:branch?.assignedSeller??user.name,createdById:branch?.createdById??user.id,customerVersion:branch?.customerVersion??0,pendingCreation:!(branch?.customerVersion),pendingCustomerChange:true});
  }
  signal();return record;
}

export async function sendCustomerChanges(user:User,onSent:(code:string)=>void,beforeSend?:()=>Promise<boolean>){
  const seen=new Set<string>();let connectionLost=false;
  // Include edits appended during an in-flight customer request.
  while(true){
    const rows=await pendingCustomers(user.id);let attempted=false;
    for(const row of rows){
      if(['review','conflict'].includes(row.state))continue;
      const operation=row.operations.find(value=>!seen.has(value.id));if(!operation)continue;
      if(session()?.id!==user.id)return {connectionLost:true};
      if(beforeSend&&!await beforeSend())return {connectionLost:true};
      seen.add(operation.id);attempted=true;
      await mutate<CustomerPending>(customerKey(user.id,row.branchCode),current=>current?{...current,state:'sending'}:current);signal();
      try{
        const branch=await api<Branch>(operation.create?'/customers':`/customers/${encodeURIComponent(row.branchCode)}`,json(operation.body,operation.create?'POST':'PUT'));
        const local=await get<CustomerPending>(customerKey(user.id,row.branchCode));
        const tail=local?.operations.filter(value=>value.id!==operation.id).at(-1);
        // Cache the acknowledgement before removing the durable retry record.
        await cacheCustomer(user,{...branch,...tail?.body,pendingCreation:false,pendingCustomerChange:!!tail});
        const remaining=await mutate<CustomerPending>(customerKey(user.id,row.branchCode),current=>{
          if(!current)return current;const operations=current.operations.filter(value=>value.id!==operation.id);
          return operations.length?{...current,operations,state:'waiting'}:undefined;
        });
        window.dispatchEvent(new CustomEvent('paper-customer-saved',{detail:{userId:user.id,branchCode:row.branchCode,branch}}));
        if(!remaining)onSent(row.branchCode);
      }catch(error){
        const status=error instanceof ApiError?error.status:0;const data=error instanceof ApiError?error.data:undefined;
        const state=status===409&&data?.code!=='existing_tax_id'?'conflict':status===400||status===403||status===409?'review':status===401?'auth':'waiting';
        await mutate<CustomerPending>(customerKey(user.id,row.branchCode),current=>current?{...current,state,message:String(data?.message??(status===401?'Συνδέσου ξανά για να σταλεί ο πελάτης.':'Ο πελάτης παραμένει στη συσκευή. Θα δοκιμάσουμε ξανά σε 5 λεπτά.')),existing:data?.existing as Branch[]|undefined,current:data?.current as Branch|undefined}:current);
        if(!status||status>=500||status===401){connectionLost=true;break;}
      }
      signal();
    }
    if(!attempted||connectionLost)break;
  }
  signal();return {connectionLost};
}

export async function reviseCustomer(user:User,code:string,fields:CustomerFields,branch?:Branch,replace=false){
  await mutate<CustomerPending>(customerKey(user.id,code),current=>{
    if(!current)return current;
    if(current.state==='review'){
      // A rejected request did not commit. Keep its identity unless the body changes.
      const first=current.operations[0];
      return {...current,state:'waiting',message:undefined,existing:undefined,operations:[{...first,body:{...fields,mutationId:first.id,expectedVersion:first.body.expectedVersion}}]};
    }
    if(current.state==='conflict'&&replace)return undefined;
    if(current.state==='auth')return {...current,state:'waiting'};
    return current;
  });
  await queueCustomer(user,code,fields,branch);
}
