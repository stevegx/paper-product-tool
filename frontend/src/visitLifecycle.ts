import {get,put,mutate,readDraft,saveDraft,signal,outboxKey} from './offlineStore';
import type {Draft,Outbox} from './offlineStore';
import type {Detail,User,Visit} from './types';

export async function markVisitDeleted(user:User,visit:Pick<Visit,'id'|'branchCode'>){
  await put(`deleted-visit:${user.id}:${visit.id}`,true);
  const draft=readDraft(user.id,visit.branchCode);
  if(draft?.id===visit.id)saveDraft({...draft,deletedOnServer:true});
  await mutate<Outbox>(outboxKey(user.id,visit.id),current=>current?{...current,state:'deleted',
    message:'Η επίσκεψη διαγράφηκε. Οι τοπικές αλλαγές δεν θα σταλούν.'}:current);
  await put(`cache:${user.id}:/visits/${visit.id}/editor`,undefined);
  await mutate<Detail>(`cache:${user.id}:/customers/${encodeURIComponent(visit.branchCode)}`,current=>
    current?{...current,history:current.history.filter(item=>item.id!==visit.id)}:current);
  window.dispatchEvent(new CustomEvent('paper-visit-saved',{detail:{userId:user.id,branchCode:visit.branchCode}}));
  signal();
}

export async function markVisitRestored(user:User,visit:Visit){
  await put(`deleted-visit:${user.id}:${visit.id}`,undefined);
  const draft=readDraft(user.id,visit.branchCode);
  if(draft?.id===visit.id){
    const pending=await get<Outbox>(outboxKey(user.id,visit.id));
    if(pending||draft.revision!==draft.savedRevision){
      await mutate<Outbox>(outboxKey(user.id,visit.id),current=>({
        visitId:visit.id,userId:user.id,branchCode:visit.branchCode,
        operations:current?.operations??[],state:'conflict',current:visit,
        message:'Η επίσκεψη επαναφέρθηκε. Έλεγξε τις τοπικές διορθώσεις πριν τις στείλεις.',
      }));
    }
    saveDraft({...draft,deletedOnServer:false,serverVersion:visit.version});
  }
  signal();
}

export async function replaceDeletedDraft(draft:Draft,copy:boolean){
  const next:Draft={...draft,id:crypto.randomUUID(),serverVersion:0,revision:0,savedRevision:-1,
    occurredAtUtc:new Date().toISOString(),updatedAt:new Date().toISOString(),lastSentAt:undefined,
    deletedOnServer:false,answers:copy?draft.answers:{},notes:copy?draft.notes:''};
  // Preserve the new draft before removing the rejected old delivery.
  saveDraft(next);
  await mutate<Outbox>(outboxKey(draft.userId,draft.id),()=>undefined);
  signal();return next;
}
