import {useEffect,useState} from 'react';
import {CloudDownload,RefreshCw,WifiOff,CloudUpload,CheckCircle2} from 'lucide-react';
import {api,dateTime} from './api';
import {allOutbox,get,storeSnapshot,draftsForUser} from './offlineStore';
import {incompleteQuestions,hasVisitContent} from './surveyRules';
import type {Snapshot,Outbox} from './offlineStore';
import type {User,Detail} from './types';
import {startSync,syncNow,syncProgress} from './sync';

async function shellReady(){
 if(!('serviceWorker'in navigator))throw new Error('Αυτός ο browser δεν υποστηρίζει άνοιγμα χωρίς σύνδεση.');
 await navigator.serviceWorker.register('/sw.js');
 const registration=await navigator.serviceWorker.ready;
 const worker=registration.active;
 if(!worker)throw new Error('Η προετοιμασία χωρίς σύνδεση δεν ολοκληρώθηκε.');
 const urls=performance.getEntriesByType('resource').map(entry=>entry.name);
 await new Promise<void>((resolve,reject)=>{
  const channel=new MessageChannel();
  const timeout=setTimeout(()=>{channel.port1.close();reject(new Error('Η προετοιμασία καθυστέρησε. Δοκίμασε ξανά.'));},20000);
  channel.port1.onmessage=event=>{
   clearTimeout(timeout);channel.port1.close();
   if(event.data?.ready)resolve();
   else reject(new Error('Δεν κατέβηκαν όλα τα αρχεία. Πάτησε ξανά «Λήψη για offline».'));
  };
  worker.postMessage({type:'CACHE_URLS',urls},[channel.port2]);
 });
}

export default function OfflineStatus({user}:{user:User}){
 const [pending,setPending]=useState(0),[prepared,setPrepared]=useState<string>();
 const [busy,setBusy]=useState(false),[error,setError]=useState(''),[offline,setOffline]=useState(!navigator.onLine);
 const [progress,setProgress]=useState(syncProgress());
 const [records,setRecords]=useState<(Outbox&{customerName:string})[]>([]);
 async function refresh(){
  const outbox=await allOutbox(user.id);
  const ids=new Set(outbox.map(item=>item.visitId));
  for(const draft of draftsForUser(user.id)){
   if(!draft.deletedOnServer&&hasVisitContent(draft.survey.questions,draft.answers,draft.notes)&&draft.serverVersion>0&&draft.revision!==draft.savedRevision&&
     !incompleteQuestions(draft.survey.questions,draft.answers).length)ids.add(draft.id);
  }
  setPending(ids.size);
  setRecords(await Promise.all(outbox.map(async item=>{
   const detail=await get<Detail>(`cache:${user.id}:/customers/${encodeURIComponent(item.branchCode)}`);
   return {...item,customerName:detail?.branch.name??item.branchCode};
  })));
  setPrepared(await get<string>(`prepared:${user.id}`));
 }
 async function prepare(){
  setBusy(true);setError('');
  try{
   await shellReady();
   const snapshot=await api<Snapshot>('/offline/snapshot',{timeoutMs:60000});
   await storeSnapshot(snapshot,user);
   if(navigator.storage?.persist)await navigator.storage.persist();
   await refresh();
  }catch(error){setError(error instanceof Error?error.message:'Δεν ολοκληρώθηκε η προετοιμασία.');}
  finally{setBusy(false);}
 }
 useEffect(()=>{
  if('serviceWorker'in navigator)void navigator.serviceWorker.register('/sw.js').catch(()=>setError('Δεν ολοκληρώθηκε η προετοιμασία για άνοιγμα χωρίς σύνδεση.'));
  const stop=startSync();
  const update=()=>void refresh().catch(()=>setError('Δεν είναι διαθέσιμη η αποθήκευση στη συσκευή.'));
  const syncUpdate=()=>setProgress(syncProgress());
  const online=()=>setOffline(false);
  const off=()=>setOffline(true);
  const network=(event:Event)=>setOffline((event as CustomEvent).detail==='cached');
  window.addEventListener('paper-sync',update);
  window.addEventListener('paper-sync-progress',syncUpdate);
  window.addEventListener('online',online);
  window.addEventListener('offline',off);
  window.addEventListener('paper-network',network);
  update();syncUpdate();
  void get<string>(`prepared:${user.id}`).then(value=>{if(!value&&navigator.onLine)void prepare();});
  return()=>{
   stop();window.removeEventListener('paper-sync',update);
   window.removeEventListener('paper-sync-progress',syncUpdate);
   window.removeEventListener('online',online);window.removeEventListener('offline',off);
   window.removeEventListener('paper-network',network);
  };
 },[user.id]);
 const needsReview=records.filter(item=>['conflict','error','auth','deleted'].includes(item.state));
 const count=progress.running?`${progress.sent}/${progress.total}`:pending?`0/${pending}`:`${progress.sent}/${progress.total}`;
 const showMessage=progress.message&&(progress.running||!pending||progress.sent<progress.total);
 return <section className={`offline-bar ${offline?'is-offline':''}`} aria-label="Αποθήκευση και αποστολή">
  <div className="connection-label">{offline?<WifiOff size={17}/>:<CloudDownload size={17}/>}<span>
   {offline?'Χωρίς σύνδεση':prepared?'Έτοιμο για χρήση χωρίς σύνδεση':busy?'Προετοιμασία για χρήση χωρίς σύνδεση…':'Χρειάζεται προετοιμασία για χρήση χωρίς σύνδεση'}
   {pending>0&&<b> · {pending} {pending===1?'επίσκεψη περιμένει':'επισκέψεις περιμένουν'} αποστολή</b>}
  </span></div>
  <div className="sync-actions">
   <button className="text-button" disabled={busy||offline} onClick={()=>void prepare()}>{busy?'Προετοιμασία…':prepared?'Ανανέωση δεδομένων':'Λήψη για offline'}</button>
   <button className="btn primary sync-send" disabled={progress.running||pending===0} onClick={()=>void syncNow()}>
    {progress.running?<RefreshCw size={16} className="spin"/>:<CloudUpload size={17}/>}
    Αποστολή απαντήσεων ({count})
   </button>
  </div>
  <div className="sync-explanation">Αυτόματος έλεγχος ανά 5 λεπτά, όσο η εφαρμογή είναι ανοιχτή. Περιλαμβάνει τις ολοκληρωμένες διορθώσεις ήδη σταλμένων επισκέψεων.</div>
  {showMessage&&<div className={`sync-result ${!pending&&!progress.running?'success':''}`} role="status">
   {!pending&&!progress.running&&<CheckCircle2 size={15}/>}{progress.message}
   {progress.checkedAt&&!progress.running&&<small>Τελευταίος έλεγχος: {dateTime(progress.checkedAt)}</small>}
  </div>}
  {needsReview.length>0&&<details className="queue-review"><summary>{needsReview.length} {needsReview.length===1?'επίσκεψη χρειάζεται':'επισκέψεις χρειάζονται'} έλεγχο</summary>
   <ul>{needsReview.map(item=><li key={item.visitId}><a href={`#/customers/${encodeURIComponent(item.branchCode)}`}>{item.customerName}</a><span>{item.message??'Άνοιξε την επίσκεψη για έλεγχο.'}</span></li>)}</ul>
  </details>}
  {error&&<p role="alert">{error}</p>}
 </section>;
}
