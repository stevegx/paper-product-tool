import {useEffect,useRef,useState} from 'react';
import {ArrowLeft,CheckCircle2,CloudUpload,Save} from 'lucide-react';
import {api} from './api';
import {get,mutate,signal} from './offlineStore';
import {cacheCustomer,customerKey,newCustomerCode,queueCustomer,reviseCustomer} from './customerQueue';
import type {CustomerFields,CustomerPending} from './customerQueue';
import type {Branch,Detail,User} from './types';
import {syncNow} from './sync';

const empty:CustomerFields={name:'',taxId:'',profession:'',address:'',postalCode:'',city:'',phone:'',taxOffice:'',customerNotes:'',allowExistingTaxId:false};
const inputs:{key:keyof Omit<CustomerFields,'allowExistingTaxId'|'customerNotes'>;label:string;max:number;numeric?:boolean}[]=[
 {key:'name',label:'Επωνυμία',max:512},{key:'taxId',label:'ΑΦΜ',max:9,numeric:true},{key:'profession',label:'Επάγγελμα',max:256},
 {key:'address',label:'Διεύθυνση',max:512},{key:'postalCode',label:'ΤΚ',max:5,numeric:true},{key:'city',label:'Πόλη',max:128},
 {key:'phone',label:'Τηλέφωνο',max:32},{key:'taxOffice',label:'ΔΟΥ',max:128},
];
function fieldsFrom(branch:Branch):CustomerFields{return {...empty,...Object.fromEntries(Object.keys(empty).filter(key=>key!=='allowExistingTaxId').map(key=>[key,branch[key as keyof Branch]??'']))} as CustomerFields;}
export default function CustomerForm({user,go,code:editCode}:{user:User;go:(path:string)=>void;code?:string}){
 const draftKey=`paper-customer-form:${user.id}:${editCode??'new'}`;
 const [initial]=useState(()=>{try{return JSON.parse(localStorage.getItem(draftKey)??'null') as {code:string;fields:CustomerFields;base?:Branch}|null;}catch{return null;}});
 const [code]=useState(editCode??initial?.code??newCustomerCode(crypto.randomUUID()));
 const [fields,setFields]=useState<CustomerFields>(initial?.fields??{...empty,taxId:sessionStorage.getItem('last-tax')??''});
 const [branch,setBranch]=useState<Branch>(),[pending,setPending]=useState<CustomerPending>(),[existing,setExisting]=useState<Branch[]>([]);
 const [loading,setLoading]=useState(!!editCode),[busy,setBusy]=useState(false),[saved,setSaved]=useState(false),[error,setError]=useState('');
 const [confirmed,setConfirmed]=useState(false);
 const [latest,setLatest]=useState<Branch>();const sending=useRef(false);
 useEffect(()=>{
  if(!editCode)return;let live=true;
  Promise.all([api<Detail>(`/customers/${encodeURIComponent(code)}`),get<CustomerPending>(customerKey(user.id,code))]).then(([detail,pending])=>{
   if(!live)return;if(!detail.branch.createdById)throw new Error('Η επεξεργασία αφορά νέους πελάτες που ανοίχτηκαν από την εφαρμογή.');
   setBranch(initial?.base??detail.branch);setPending(pending);setExisting(pending?.existing??[]);
   const queued=pending?.operations.at(-1)?.body;
   setFields(initial?.fields??(queued?Object.fromEntries(Object.keys(empty).map(key=>[key,queued[key as keyof CustomerFields]])) as CustomerFields:fieldsFrom(detail.branch)));
  }).catch(error=>{if(live)setError(error.message);}).finally(()=>{if(live)setLoading(false);});return()=>{live=false;};
 },[code,user.id]);
 useEffect(()=>{
  if(loading||saved)return;
  try{localStorage.setItem(draftKey,JSON.stringify({code,fields,base:branch}));}catch{setError('Δεν υπάρχει διαθέσιμος χώρος στη συσκευή. Μην κλείσεις τη φόρμα πριν σταλεί ο πελάτης.');}
 },[fields,code,branch,loading,saved]);
 useEffect(()=>{
  let live=true;
  const refresh=()=>{void Promise.all([get<CustomerPending>(customerKey(user.id,code)),get<Detail>(`cache:${user.id}:/customers/${encodeURIComponent(code)}`)]).then(([value,detail])=>{if(live){setPending(value);setConfirmed(!value&&!!detail?.branch.customerVersion&&!detail.branch.pendingCustomerChange);if(value?.existing)setExisting(value.existing);}});};
  window.addEventListener('paper-sync',refresh);refresh();return()=>{live=false;window.removeEventListener('paper-sync',refresh);};
 },[code,user.id]);
 useEffect(()=>{
  if(!/^\d{9}$/.test(fields.taxId)){setExisting([]);return;}let live=true;
  const timer=setTimeout(()=>{void api<Branch[]>(`/customers?afm=${fields.taxId}`).then(rows=>{if(live)setExisting(rows.filter(row=>row.code!==code));}).catch(()=>{});},350);
  return()=>{live=false;clearTimeout(timer);};
 },[fields.taxId,code]);
 const change=(key:keyof CustomerFields,value:string|boolean)=>{setFields(current=>({...current,[key]:value,...(key==='taxId'?{allowExistingTaxId:false}:{})}));setError('');};
 const needsBranchConfirmation=existing.length>0&&(!branch||branch.pendingCreation||branch.taxId!==fields.taxId);
 async function submit(event:React.FormEvent){
  event.preventDefault();if(sending.current)return;sending.current=true;setBusy(true);setError('');
  try{
   if(!/^\d{9}$/.test(fields.taxId)||!/^\d{5}$/.test(fields.postalCode))throw new Error('Έλεγξε το ΑΦΜ (9 ψηφία) και τον ΤΚ (5 ψηφία).');
   if(!/^[+()0-9\s.-]+$/.test(fields.phone)||fields.phone.replace(/\D/g,'').length<10||fields.phone.replace(/\D/g,'').length>15)throw new Error('Συμπλήρωσε τηλέφωνο με 10–15 ψηφία.');
   if(needsBranchConfirmation&&!fields.allowExistingTaxId)throw new Error('Έλεγξε τις υπάρχουσες καρτέλες και επιβεβαίωσε ότι ανοίγεις διαφορετικό υποκατάστημα.');
   if(pending?.state==='review'||pending?.state==='auth')await reviseCustomer(user,code,fields,branch!);
   else await queueCustomer(user,code,fields,branch);
   localStorage.removeItem(draftKey);sessionStorage.setItem('last-tax',fields.taxId);setSaved(true);void syncNow();
  }catch(error){setError(error instanceof Error?error.message:'Δεν αποθηκεύτηκε ο πελάτης.');}finally{setBusy(false);sending.current=false;}
 }
 async function compare(){
  setError('');try{const detail=await api<Detail>(`/customers/${encodeURIComponent(code)}?fresh=1`);setLatest(detail.branch);}catch(error){setError(error instanceof Error?error.message:'Η σύγκριση χρειάζεται σύνδεση.');}
 }
 if(loading)return <div className="loading">Φόρτωση στοιχείων πελάτη…</div>;
 const review=pending?.state==='review'||pending?.state==='conflict';
 return <><button className="back-link" onClick={()=>go('/customers')}><ArrowLeft size={17}/>Πίσω στους πελάτες</button>
 <div className="page-heading"><div><span className="eyebrow">ΚΑΡΤΕΛΑ ΠΕΛΑΤΗ</span><h1>{editCode?'Επεξεργασία πελάτη':'Νέος πελάτης'}</h1><p>Όλα τα στοιχεία είναι υποχρεωτικά, εκτός από τις σημειώσεις.</p></div></div>
 {saved&&!review?<section className="panel customer-confirmation" role="status"><CheckCircle2 size={28}/><h2>{confirmed?'Ο πελάτης αποθηκεύτηκε στον server':'Αποθηκεύτηκε στη συσκευή'}</h2><p>{confirmed?'Η καρτέλα είναι διαθέσιμη στην ομάδα.':'Τα στοιχεία κρατήθηκαν στη συσκευή. Θα σταλούν με την επόμενη σύνδεση και τον αυτόματο έλεγχο ανά 5 λεπτά. Μπορείς να συνεχίσεις με επίσκεψη.'}</p><button className="btn primary" onClick={()=>go(`/customers/${encodeURIComponent(code)}`)}>Άνοιγμα καρτέλας και καταγραφή επίσκεψης</button></section>:<section className="panel customer-form-panel">
 {pending?.message&&<div className="notice" role="status">{pending.message}</div>}
 {existing.length>0&&<aside className="customer-existing"><h3>Υπάρχουν καρτέλες με το ίδιο ΑΦΜ</h3><p>Έλεγξε ότι δεν υπάρχει ήδη το ίδιο κατάστημα.</p>{existing.map(row=><p key={row.code}><a href={`#/customers/${encodeURIComponent(row.code)}`}>{row.name} · {row.code}</a>{row.address&&<small> · {row.address}, {row.city}</small>}</p>)}</aside>}
 {pending?.state==='conflict'&&<div className="conflict-box"><p>Οι δικές σου αλλαγές παραμένουν στη συσκευή.</p><button className="btn secondary" type="button" onClick={()=>void compare()}>Σύγκριση με τα νεότερα στοιχεία</button>{latest&&<><div className="table-wrap"><table><thead><tr><th>Πεδίο</th><th>Οι αλλαγές σου</th><th>Νεότερα στοιχεία</th></tr></thead><tbody>{[...inputs,{key:'customerNotes' as const,label:'Σημειώσεις',max:2000}].map(input=><tr key={input.key}><th>{input.label}</th><td>{fields[input.key]||'—'}</td><td>{latest[input.key]||'—'}</td></tr>)}</tbody></table></div><div className="conflict-actions"><button className="btn secondary" onClick={async()=>{await mutate(customerKey(user.id,code),()=>undefined);await cacheCustomer(user,latest);setBranch(latest);setFields(fieldsFrom(latest));setLatest(undefined);localStorage.removeItem(draftKey);signal();}}>Χρήση νεότερων στοιχείων</button><button className="btn primary" disabled={busy} onClick={async()=>{setBusy(true);try{await reviseCustomer(user,code,fields,latest,true);localStorage.removeItem(draftKey);setLatest(undefined);setSaved(true);void syncNow();}catch(error){setError(error instanceof Error?error.message:'Δεν αποθηκεύτηκαν οι αλλαγές.');}finally{setBusy(false);}}}>Κράτησε τις δικές μου αλλαγές για αποστολή</button></div></>}</div>}
 <form onSubmit={submit}><div className="customer-input-grid">{inputs.map(input=><label key={input.key}><span>{input.label} <b aria-hidden="true">*</b></span><input required maxLength={input.max} inputMode={input.numeric?'numeric':input.key==='phone'?'tel':'text'} value={fields[input.key]} disabled={busy||pending?.state==='conflict'} onChange={event=>change(input.key,input.numeric?event.target.value.replace(/\D/g,''):event.target.value)} autoComplete="off"/></label>)}</div>
 <label className="customer-notes">Σημειώσεις πελάτη (προαιρετικό)<textarea maxLength={2000} rows={4} value={fields.customerNotes} disabled={busy||pending?.state==='conflict'} onChange={event=>change('customerNotes',event.target.value)}/></label>
 {needsBranchConfirmation&&<label className="inline-checkbox"><input type="checkbox" checked={fields.allowExistingTaxId} disabled={busy} onChange={event=>change('allowExistingTaxId',event.target.checked)}/>Έλεγξα τις καρτέλες: πρόκειται για διαφορετικό υποκατάστημα.</label>}
 {error&&<div className="notice" role="alert">{error}</div>}
 <div className="form-footer"><span><Save size={15}/> Η φόρμα κρατιέται αυτόματα στη συσκευή.</span><button className="btn primary" disabled={busy||pending?.state==='conflict'}><CloudUpload size={17}/>{busy?'Αποθήκευση…':editCode?'Αποθήκευση αλλαγών':'Δημιουργία πελάτη'}</button></div></form></section>}
 </>;
}
