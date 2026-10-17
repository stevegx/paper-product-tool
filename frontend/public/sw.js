const SHELL='paper-shell-v4';
self.addEventListener('install',event=>event.waitUntil((async()=>{
 const cache=await caches.open(SHELL);await cache.addAll(['/','/manifest.webmanifest','/icon.svg']);
 const html=await (await cache.match('/')).text();
 const assets=Array.from(html.matchAll(/(?:src|href)="([^"]+\.(?:js|css))"/g),match=>match[1]);
 await cache.addAll(assets);await self.skipWaiting();
})()));
self.addEventListener('activate',event=>event.waitUntil(self.clients.claim()));
function allowed(url){return url.origin===self.location.origin&&!url.pathname.startsWith('/api')&&!url.pathname.endsWith('/sw.js');}
self.addEventListener('message',event=>{if(event.data?.type==='CACHE_URLS')event.waitUntil(caches.open(SHELL).then(cache=>Promise.all(event.data.urls.filter(value=>allowed(new URL(value,self.location.origin))).map(value=>cache.add(value)))).then(()=>event.ports[0]?.postMessage({ready:true})).catch(()=>event.ports[0]?.postMessage({ready:false})));});
self.addEventListener('fetch',event=>{
 if(event.request.method!=='GET'||!allowed(new URL(event.request.url)))return;
 event.respondWith((async()=>{const cache=await caches.open(SHELL);const old=await cache.match(event.request)||await caches.match(event.request);const network=fetch(event.request).then(async response=>{if(response.ok)await cache.put(event.request,response.clone());return response;});
 if(event.request.mode==='navigate'){try{return await Promise.race([network,new Promise((_,reject)=>setTimeout(()=>reject(new Error('slow')),3000))]);}catch{return old||await cache.match('/')||Response.error();}}
 if(old){void network.catch(()=>{});return old;}try{return await network;}catch{return Response.error();}})());
});
