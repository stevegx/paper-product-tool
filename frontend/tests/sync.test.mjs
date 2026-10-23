import {test} from 'node:test';
import assert from 'node:assert/strict';
import {build} from 'esbuild';
import {renderToStaticMarkup} from 'react-dom/server';
import React from 'react';

async function bundle(entry,plugins=[]){
 const result=await build({entryPoints:[entry],bundle:true,write:false,format:'esm',platform:'node',plugins,logLevel:'silent'});
 return import('data:text/javascript;base64,'+Buffer.from(result.outputFiles[0].text+'\n// '+Math.random()).toString('base64'));
}
const user={id:'seller',role:'Seller'};
const question=id=>({id,title:id,sectionKey:'general',options:[{id:'yes',label:'Ναι'}],required:false});
function draft(id,version=0,answers={}){
 return {id,branchCode:id,userId:user.id,survey:{version:'v1',questions:[question('q1'),question('q2')]},
  answers,notes:'Δοκιμή',occurredAtUtc:'2026-10-17T07:00:00Z',serverVersion:version,revision:1,savedRevision:0};
}
async function harness(){
 const entries=new Map();const drafts=[];const calls=[];
 const state={user,connected:true,hook:null,fail:null};
 globalThis.window=new EventTarget();
 Object.defineProperty(globalThis,'navigator',{value:{onLine:true},configurable:true});
 const mock={
  get:async key=>structuredClone(entries.get(key)),readDraft:(_user,branch)=>drafts.find(d=>d.branchCode===branch),
  session:()=>state.user,allOutbox:async()=>[...entries].filter(([key])=>key.startsWith('outbox:')).map(([,value])=>structuredClone(value)),
  matchingEntries:async prefix=>[...entries].filter(([key])=>key.startsWith(prefix)).map(([,value])=>structuredClone(value)),
  mutate:async(key,fn)=>{const value=fn(structuredClone(entries.get(key)));if(value===undefined)entries.delete(key);else entries.set(key,structuredClone(value));return value;},
  put:async(key,value)=>entries.set(key,structuredClone(value)),signal:()=>{},
  saveDraft:value=>{const index=drafts.findIndex(draft=>draft.id===value.id);if(index>=0)drafts[index]=value;},
  draftForId:(_user,id)=>drafts.find(draft=>draft.id===id),draftsForUser:()=>drafts,
  outboxKey:(user,id)=>`outbox:${user}:${id}`,
  json:(body,method)=>({method,body:JSON.stringify(body)}),
 };
 mock.ApiError=class extends Error{constructor(message,status,data){super(message);this.status=status;this.data=data;}};
 mock.api=async(path,options)=>{
  if(path==='/auth/status'){if(!state.connected)throw new Error('offline');return {};}
  const body=JSON.parse(options.body);calls.push({path,body});
  if(state.hook)await state.hook(path,body);
  if(state.fail)await state.fail(path,body);
  if(path==='/customers'||path.startsWith('/customers/'))return {...body,code:path==='/customers'?`NEW-${body.mutationId.replaceAll('-','')}`:decodeURIComponent(path.split('/')[2]),customerVersion:body.expectedVersion+1,assignedSeller:'Seller',createdById:user.id};
  return {id:path.split('/')[2],version:body.expectedVersion+1,branchCode:body.branchCode,createdAtUtc:body.occurredAtUtc,answers:[],notes:body.notes};
 };
 globalThis.__paperSyncTest=mock;
 const plugin={name:'mock-transports',setup(builder){
  builder.onResolve({filter:/^\.\/(api|offlineStore)$/},args=>({path:args.path,namespace:'mock'}));
  builder.onLoad({filter:/.*/,namespace:'mock'},()=>({contents:
   'const m=globalThis.__paperSyncTest;export const {'+Object.keys(mock).join(',')+'}=m;',loader:'js'}));
 }};
 const module=await bundle('src/sync.ts',[plugin]);
 const customers=await bundle('src/customerQueue.ts',[plugin]);
 return {module,customers,entries,drafts,calls,state,mock};
}

const customerFields={name:'New customer',taxId:'099999999',profession:'Cafe',address:'Test street 1',postalCode:'10558',city:'Athens',phone:'2101234567',taxOffice:'Athens',customerNotes:'Optional customer notes',allowExistingTaxId:false};
test('Offline customer and visit survive in storage and are sent in dependency order',async()=>{
 const h=await harness();const code=h.customers.newCustomerCode(crypto.randomUUID());
 h.state.connected=false;await h.customers.queueCustomer(user,code,customerFields);
 await h.module.enqueue({...draft('visit'),branchCode:code},false);await h.module.syncNow();
 assert.equal((await h.customers.pendingCustomers(user.id)).length,1);assert.equal((await h.mock.allOutbox()).length,1);assert.equal(h.calls.length,0);
 h.state.connected=true;await h.module.syncNow();
 assert.deepEqual(h.calls.map(call=>call.path),['/customers','/visits/visit']);
 assert.equal((await h.customers.pendingCustomers(user.id)).length,0);assert.equal((await h.mock.allOutbox()).length,0);
 assert.equal(h.module.syncProgress().sent,2);
});
test('An edit appended while a customer is sending keeps both immutable operations',async()=>{
 const h=await harness();const code=h.customers.newCustomerCode(crypto.randomUUID());let added=false;
 const acknowledgements=[];window.addEventListener('paper-customer-saved',event=>acknowledgements.push(event.detail.branch.customerVersion));
 await h.customers.queueCustomer(user,code,customerFields);
 h.state.hook=async path=>{if(path==='/customers'&&!added){added=true;await h.customers.queueCustomer(user,code,{...customerFields,name:'Edited name'});}};
 await h.module.syncNow();
 assert.deepEqual(h.calls.map(call=>call.path),['/customers',`/customers/${code}`]);
 assert.deepEqual(h.calls.map(call=>call.body.expectedVersion),[0,1]);
 assert.equal(h.calls[0].body.name,'New customer');assert.equal(h.calls[1].body.name,'Edited name');
 assert.deepEqual(acknowledgements,[1,2]);
 const cached=await h.mock.get(`cache:${user.id}:/customers/${code}`);assert.equal(cached.branch.name,'Edited name');assert.equal(cached.branch.customerVersion,2);
});
test('An unresolved customer conflict blocks its visit and preserves unrelated sends',async()=>{
 const h=await harness();const code=h.customers.newCustomerCode(crypto.randomUUID());
 await h.customers.queueCustomer(user,code,customerFields);await h.module.enqueue({...draft('blocked'),branchCode:code},false);await h.module.enqueue(draft('other'),false);
 h.state.fail=async path=>{if(path==='/customers')throw new h.mock.ApiError('Changed',409,{code:'customer_conflict'});};
 await h.module.syncNow();
 assert.deepEqual(h.calls.map(call=>call.path),['/customers','/visits/other']);
 assert.equal((await h.customers.pendingCustomers(user.id))[0].state,'conflict');assert.equal((await h.mock.allOutbox())[0].visitId,'blocked');
});
test('Same customer submission is deduplicated and an existing AFM can be explicitly confirmed',async()=>{
 const h=await harness();const code=h.customers.newCustomerCode(crypto.randomUUID());
 await h.customers.queueCustomer(user,code,customerFields);await h.customers.queueCustomer(user,code,customerFields);
 assert.equal((await h.customers.pendingCustomers(user.id))[0].operations.length,1);
 h.state.fail=async()=>{throw new h.mock.ApiError('Check branch',409,{code:'existing_tax_id',existing:[{code:'other'}]});};
 await h.module.syncNow();assert.equal((await h.customers.pendingCustomers(user.id))[0].state,'review');
 h.state.fail=null;await h.customers.reviseCustomer(user,code,{...customerFields,allowExistingTaxId:true});await h.module.syncNow();
 assert.equal((await h.customers.pendingCustomers(user.id)).length,0);assert.equal(h.calls[1].body.allowExistingTaxId,true);
 assert.equal(h.calls[0].body.mutationId,h.calls[1].body.mutationId);
});

test('Automatic retry interval is five minutes and ignores focus/online spam',async()=>{
 const h=await harness();h.state.user=null;
 const realSet=globalThis.setInterval,realClear=globalThis.clearInterval;
 let duration,cleared=false;
 globalThis.setInterval=(_fn,ms)=>{duration=ms;return 9;};globalThis.clearInterval=id=>{assert.equal(id,9);cleared=true;};
 try{const stop=h.module.startSync();assert.equal(duration,300000);window.dispatchEvent(new Event('focus'));window.dispatchEvent(new Event('online'));assert.equal(h.calls.length,0);stop();assert.equal(cleared,true);}
 finally{globalThis.setInterval=realSet;globalThis.clearInterval=realClear;}
});
test('No server connection keeps every queued customer and schedules a later retry',async()=>{
 const h=await harness();h.state.connected=false;
 await h.module.enqueue(draft('A'),false);await h.module.enqueue(draft('B'),false);
 await h.module.syncNow();assert.equal((await h.mock.allOutbox()).length,2);assert.equal(h.calls.length,0);
 assert.match(h.module.syncProgress().message,/5 λεπτά/);
});
test('One send flushes all customers with a confirmed batch counter',async()=>{
 const h=await harness();await h.module.enqueue(draft('A'),false);await h.module.enqueue(draft('B'),false);
 await h.module.syncNow();assert.equal(h.calls.length,2);assert.equal((await h.mock.allOutbox()).length,0);
 assert.equal(h.module.syncProgress().sent,2);assert.equal(h.module.syncProgress().total,2);
});
test('Complete saved-visit corrections are sent, new visits and partial sections remain drafts',async()=>{
 const h=await harness();h.drafts.push(draft('corrected',2,{q1:['yes'],q2:['yes']}),draft('partial',2,{q1:['yes']}),draft('new',0,{q1:['yes'],q2:['yes']}));
 await h.module.syncNow();assert.deepEqual(h.calls.map(call=>call.body.branchCode),['corrected']);
 assert.equal(h.drafts[0].savedRevision,h.drafts[0].revision);assert.equal(h.drafts[0].serverVersion,3);
});
test('A conflicting visit stays queued while another customer can be sent',async()=>{
 const h=await harness();await h.module.enqueue(draft('A'),false);await h.module.enqueue(draft('B'),false);
 h.state.fail=async path=>{if(path.endsWith('/A'))throw new h.mock.ApiError('conflict',409,{});};
 await h.module.syncNow();const remaining=await h.mock.allOutbox();assert.equal(remaining.length,1);assert.equal(remaining[0].state,'conflict');assert.equal(h.module.syncProgress().sent,1);
});
test('A dropped connection stops the batch without hammering other customers',async()=>{
 const h=await harness();await h.module.enqueue(draft('A'),false);await h.module.enqueue(draft('B'),false);
 h.state.fail=async()=>{throw new Error('connection dropped');};
 await h.module.syncNow();assert.equal(h.calls.length,1);assert.equal((await h.mock.allOutbox()).length,2);
});
test('A different customer added while sending is included in the same flush',async()=>{
 const h=await harness();await h.module.enqueue(draft('A'),false);
 h.state.hook=async path=>{if(path.endsWith('/A'))await h.module.enqueue(draft('B'),false);};
 await h.module.syncNow();assert.deepEqual(h.calls.map(call=>call.body.branchCode),['A','B']);assert.equal((await h.mock.allOutbox()).length,0);
});
test('Repeated clicks for the same revision retain one mutation and one visit',async()=>{
 const h=await harness();const d=draft('A');for(let i=0;i<8;i++)await h.module.enqueue(d,false);
 assert.equal((await h.mock.allOutbox())[0].operations.length,1);
 await Promise.all([h.module.syncNow(),h.module.syncNow(),h.module.syncNow()]);assert.equal(h.calls.length,1);
});
test('Saved answer view omits empty questions and whole empty sections',async()=>{
 const {default:View}=await bundle('src/VisitAnswers.tsx');
 const visit={notes:'Σημείωση',answers:[
  {questionId:'1',title:'Απαντημένη',sectionKey:'bar',sectionTitle:'Μπαρ',selectedOptions:['Ναι']},
  {questionId:'2',title:'Κενή ερώτηση',sectionKey:'bar',sectionTitle:'Μπαρ',selectedOptions:[]},
  {questionId:'3',title:'Άδεια',sectionKey:'kitchen',sectionTitle:'Κενή ενότητα',selectedOptions:[]},
 ]};
 const html=renderToStaticMarkup(React.createElement(View,{visit}));
 assert.match(html,/Απαντημένη/);assert.match(html,/Μπαρ/);assert.match(html,/Σημείωση/);
 assert.doesNotMatch(html,/Κενή ερώτηση|Κενή ενότητα|Χωρίς επιλογή|δεν συμπληρώθηκε/);
});

test('Empty answers and whitespace notes are rejected; notes-only visits remain valid',async()=>{
 const h=await harness();const empty=draft('empty');empty.notes=' \n ';empty.answers={};
 await assert.rejects(()=>h.module.enqueue(empty,false),/κενή επίσκεψη/);
 assert.equal((await h.mock.allOutbox()).length,0);
 empty.notes='Μόνο σημειώσεις';await h.module.enqueue(empty,false);assert.equal((await h.mock.allOutbox()).length,1);
});
test('Clearing all answers does not automatically send an empty correction',async()=>{
 const h=await harness();const empty=draft('cleared',2);empty.notes='';empty.answers={};h.drafts.push(empty);
 await h.module.syncNow();assert.equal(h.calls.length,0);assert.equal((await h.mock.allOutbox()).length,0);
});
test('Deleted server visits are kept for review and never retried automatically',async()=>{
 const h=await harness();await h.module.enqueue(draft('deleted'),false);
 h.state.fail=async()=>{throw new h.mock.ApiError('deleted',410,{code:'visit_deleted'});};
 await h.module.syncNow();assert.equal((await h.mock.allOutbox())[0].state,'deleted');
 assert.equal(await h.mock.get('deleted-visit:seller:deleted'),true);
 await h.module.syncNow();assert.equal(h.calls.length,1);
});
test('Only visible valid options contribute to the answered count',async()=>{
 const rules=await bundle('src/surveyRules.ts');const questions=[question('q1'),question('q2')];
 assert.equal(rules.hasAnsweredQuestions(questions,{q1:['removed-option'],oldQuestion:['yes']}),false);
 assert.equal(rules.hasAnsweredQuestions(questions,{q1:[],q2:[]}),false);
 assert.equal(rules.hasAnsweredQuestions(questions,{q2:['yes']}),true);
});
