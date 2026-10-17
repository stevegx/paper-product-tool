import type { User, Survey, Visit, Detail, Overview } from './types';
export type Editor = { visit: Visit; survey: Survey; answers: Record<string,string[]> };
export type Draft = { id:string; branchCode:string; userId:string; survey:Survey; answers:Record<string,string[]>; notes:string; occurredAtUtc:string; serverVersion:number; revision:number; savedRevision:number; updatedAt:string; lastSentAt?:string; deletedOnServer?:boolean; };
export type Mutation = { id:string; revision:number; attempted:boolean; body:{mutationId:string;expectedVersion:number;branchCode:string;surveyVersion:string;occurredAtUtc:string;notes:string;answers:{questionId:string;optionIds:string[]}[];} };
export type Outbox = { visitId:string; branchCode:string; userId:string; operations:Mutation[]; state:'waiting'|'sending'|'conflict'|'auth'|'error'|'deleted'; message?:string; current?:Visit; };
let database:Promise<IDBDatabase>|undefined;
const channel=typeof BroadcastChannel==='undefined'?undefined:new BroadcastChannel('paper-sync');
channel?.addEventListener('message',()=>window.dispatchEvent(new Event('paper-sync')));
export function signal(){window.dispatchEvent(new Event('paper-sync'));channel?.postMessage('changed');}
function db(){return database??=new Promise<IDBDatabase>((resolve,reject)=>{const request=indexedDB.open('paper-offline-v2',1);request.onupgradeneeded=()=>request.result.createObjectStore('entries');request.onsuccess=()=>resolve(request.result);request.onerror=()=>reject(request.error);});}
export async function get<T>(key:string):Promise<T|undefined>{const connection=await db();return new Promise((resolve,reject)=>{const request=connection.transaction('entries').objectStore('entries').get(key);request.onsuccess=()=>resolve(request.result);request.onerror=()=>reject(request.error);});}
export async function mutate<T>(key:string,fn:(current:T|undefined)=>T|undefined):Promise<T|undefined>{const connection=await db();return new Promise((resolve,reject)=>{const tx=connection.transaction('entries','readwrite');const store=tx.objectStore('entries');let result:T|undefined;const request=store.get(key);request.onsuccess=()=>{try{result=fn(request.result);if(result===undefined)store.delete(key);else store.put(result,key);}catch(error){tx.abort();reject(error);}};tx.oncomplete=()=>resolve(result);tx.onerror=()=>reject(tx.error);tx.onabort=()=>reject(tx.error??new Error('Η αποθήκευση στη συσκευή δεν ολοκληρώθηκε.'));});}
export async function put<T>(key:string,value:T){return mutate<T>(key,()=>value);}
export async function allOutbox(userId:string):Promise<Outbox[]>{const connection=await db();return new Promise((resolve,reject)=>{const tx=connection.transaction('entries');const request=tx.objectStore('entries').openCursor();const result:Outbox[]=[];request.onsuccess=()=>{const cursor=request.result;if(!cursor){resolve(result);return;}if(String(cursor.key).startsWith(`outbox:${userId}:`))result.push(cursor.value);cursor.continue();};request.onerror=()=>reject(request.error);});}
export function session():User|null {try{return JSON.parse(localStorage.getItem('paper-user')??'null');}catch{return null;}}
export function setSession(user:User|null){if(user)localStorage.setItem('paper-user',JSON.stringify(user));else localStorage.removeItem('paper-user');if(user)void cache('/auth/me',user).catch(()=>{});}
export function cached<T>(path:string){const user=session();return user?get<T>(`cache:${user.id}:${path}`):Promise.resolve(undefined);}
export function cache<T>(path:string,value:T){const user=session();return user?put(`cache:${user.id}:${path}`,value):Promise.resolve(undefined);}
function draftKey(userId:string,branchCode:string){return `paper-draft:${userId}:${branchCode}`;}
export function readDraft(userId:string,branchCode:string):Draft|null {const raw=localStorage.getItem(draftKey(userId,branchCode));return raw?JSON.parse(raw):null;}
export function saveDraft(draft:Draft){localStorage.setItem(draftKey(draft.userId,draft.branchCode),JSON.stringify(draft));signal();return draft;}
export function draftsForUser(userId:string):Draft[]{
 const drafts:Draft[]=[];
 for(let i=0;i<localStorage.length;i++){
  const key=localStorage.key(i)!;
  if(key.startsWith(`paper-draft:${userId}:`)){
   // A damaged draft must not prevent the other visits from being sent.
   try{drafts.push(JSON.parse(localStorage.getItem(key)!) as Draft);}catch{/* Keep the original data for recovery. */}
  }
 }
 return drafts;
}
export function draftForId(userId:string,id:string){for(let i=0;i<localStorage.length;i++){const key=localStorage.key(i)!;if(key.startsWith(`paper-draft:${userId}:`)){const value=JSON.parse(localStorage.getItem(key)! ) as Draft;if(value.id===id)return value;}}return null;}
export function outboxKey(userId:string,id:string){return `outbox:${userId}:${id}`;}
export function newDraft(user:User,branchCode:string,survey:Survey):Draft{return {id:crypto.randomUUID(),userId:user.id,branchCode,survey,answers:{},notes:'',occurredAtUtc:new Date().toISOString(),serverVersion:0,revision:0,savedRevision:-1,updatedAt:new Date().toISOString()};}
export function fromEditor(user:User,editor:Editor):Draft{return {id:editor.visit.id,userId:user.id,branchCode:editor.visit.branchCode,survey:editor.survey,answers:editor.answers,notes:editor.visit.notes,occurredAtUtc:editor.visit.createdAtUtc,serverVersion:editor.visit.version,revision:0,savedRevision:0,updatedAt:new Date().toISOString(),lastSentAt:editor.visit.updatedAtUtc??editor.visit.createdAtUtc};}
export type Snapshot = {generatedAtUtc:string;survey:Survey;overview:Overview;customers:Detail[];editors:Editor[];};
export async function storeSnapshot(snapshot:Snapshot,user:User){const connection=await db();return new Promise<void>((resolve,reject)=>{const tx=connection.transaction('entries','readwrite');const store=tx.objectStore('entries');const write=(path:string,value:unknown)=>store.put(value,`cache:${user.id}:${path}`);write('/survey',snapshot.survey);write('/overview',snapshot.overview);const taxGroups=new Map<string,Detail['branch'][]>();for(const detail of snapshot.customers){write(`/customers/${encodeURIComponent(detail.branch.code)}`,detail);taxGroups.set(detail.branch.taxId,[...(taxGroups.get(detail.branch.taxId)??[]),detail.branch]);}for(const [tax,branches]of taxGroups)write(`/customers?afm=${tax}`,branches);for(const editor of snapshot.editors)write(`/visits/${editor.visit.id}/editor`,editor);store.put(snapshot.generatedAtUtc,`prepared:${user.id}`);tx.oncomplete=()=>{signal();resolve();};tx.onerror=()=>reject(tx.error);});}
