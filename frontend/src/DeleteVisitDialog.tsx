import {useEffect,useRef} from 'react';
import {Trash2} from 'lucide-react';
import {dateTime} from './api';
import type {Visit} from './types';

export default function DeleteVisitDialog({visit,busy,onConfirm,onCancel}:{
  visit:Visit;busy:boolean;onConfirm:()=>void;onCancel:()=>void;
}){
  const dialog=useRef<HTMLDialogElement>(null);
  const cancel=useRef<HTMLButtonElement>(null);
  useEffect(()=>{dialog.current?.showModal();cancel.current?.focus();return()=>dialog.current?.close();},[]);
  return <dialog ref={dialog} className="submission-dialog problem" aria-labelledby="delete-visit-title"
    onCancel={event=>{event.preventDefault();if(!busy)onCancel();}}>
    <span className="submission-icon"><Trash2 size={32}/></span>
    <h2 id="delete-visit-title">Είσαι σίγουρος ότι θέλεις να διαγράψεις την επίσκεψη;</h2>
    <p><strong>{visit.customerName}</strong><br/>{dateTime(visit.createdAtUtc)}</p>
    <p>Η επίσκεψη θα αφαιρεθεί από το ιστορικό και την εξαγωγή Excel.</p>
    <div className="submission-actions">
      <button ref={cancel} type="button" className="btn secondary" disabled={busy} onClick={onCancel}>Ακύρωση</button>
      <button type="button" className="btn danger" disabled={busy} onClick={onConfirm}>{busy?'Διαγραφή…':'Ναι, διαγραφή'}</button>
    </div>
  </dialog>;
}
