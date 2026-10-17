import {useEffect,useRef} from 'react';
import {CheckCircle2,CloudOff,LoaderCircle,TriangleAlert} from 'lucide-react';

export default function SubmissionFeedback({sent,sending,problem,onClose}:{
  sent:boolean;sending:boolean;problem?:string;onClose:()=>void;
}){
  const dialog=useRef<HTMLDialogElement>(null);
  useEffect(()=>{dialog.current?.showModal();return()=>dialog.current?.close();},[]);
  const title=problem?'Η αποστολή χρειάζεται έλεγχο':sent?'Η επίσκεψη στάλθηκε!':
    sending?'Η επίσκεψη αποστέλλεται…':'Η επίσκεψη αποθηκεύτηκε στη συσκευή';
  return <dialog ref={dialog} className={`submission-dialog ${sent?'confirmed':problem?'problem':'stored'}`}
    aria-labelledby="submission-title" onCancel={event=>{event.preventDefault();onClose();}}>
    <span className="submission-icon">{problem?<TriangleAlert size={34}/>:sent?<CheckCircle2 size={38}/>:
      sending?<LoaderCircle size={34} className="spin"/>:<CloudOff size={34}/>}</span>
    <h2 id="submission-title">{title}</h2>
    <p>{problem??(sent?'Οι απαντήσεις καταχωρίστηκαν κανονικά. Μπορείς να συνεχίσεις στον επόμενο πελάτη.':
      sending?'Οι απαντήσεις είναι ήδη αποθηκευμένες στη συσκευή. Περιμένουμε επιβεβαίωση αποστολής.':
      'Οι απαντήσεις παραμένουν αποθηκευμένες εδώ. Θα γίνει νέα προσπάθεια αποστολής στον επόμενο έλεγχο, ανά 5 λεπτά, όσο είναι ανοιχτή η εφαρμογή.')}</p>
    <div className="submission-actions"><button type="button" className="btn primary" onClick={onClose}>
      {sent?'Εντάξει':sending?'Συνέχεια εργασίας':'Το κατάλαβα'}</button>
      {sent&&<a href="#/visits" className="btn secondary" onClick={onClose}>Προβολή επισκέψεων</a>}
    </div>
  </dialog>;
}
