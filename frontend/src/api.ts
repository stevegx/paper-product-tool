import {session,get,put} from './offlineStore';
let csrf: string | null = null;
export class ApiError extends Error {
  constructor(
    message: string,
    public status: number,
    public data?: Record<string,unknown>,
  ) {
    super(message);
  }
}
export function resetCsrf() {
  csrf = null;
}
export async function api<T>(path: string, options: RequestInit & { timeoutMs?: number; csrfRetried?: boolean } = {}): Promise<T> {
  const {timeoutMs=options.method?12000:6000,csrfRetried=false,...fetchOptions}=options;
  const accountId=session()?.id;
  const cacheable=!options.method && (/^\/customers(?:\?|\/)/.test(path)||path==='/survey'||path==='/overview'||path==='/auth/me'||/^\/visits\/[^/]+\/editor$/.test(path));
  const controller=new AbortController();
  const timeout=setTimeout(()=>controller.abort(),timeoutMs);
  try {
  const headers = new Headers(options.headers);
  if (options.body && !(options.body instanceof FormData))
    headers.set('Content-Type', 'application/json');
  if (options.method && options.method !== 'GET') {
    if (!csrf) {
      const response = await fetch('/api/auth/csrf', { credentials: 'same-origin',signal:controller.signal });
      if (!response.ok) throw new ApiError('Αδυναμία σύνδεσης με τον server.', response.status);
      csrf = (await response.json()).token;
    }
    headers.set('X-CSRF-TOKEN', csrf!);
  }
  const response = await fetch(`/api${path}`, {
    ...fetchOptions,
    signal:options.signal??controller.signal,
    headers,
    credentials: 'same-origin',
    cache: 'no-store',
  });
  if (!response.ok) {
    const data = await response.json().catch(() => ({}));
    if(response.status===400 && !csrfRetried && String(data.message).includes('συνεδρία')){resetCsrf();return api<T>(path,{...options,csrfRetried:true});}
    const message =
      data.message ??
      {
        401: 'Το email ή ο κωδικός είναι λάθος, ή η συνεδρία έληξε.',
        403: 'Δεν έχεις πρόσβαση σε αυτή την ενέργεια.',
        404: 'Η εγγραφή δεν βρέθηκε.',
        429: 'Πολλές προσπάθειες. Δοκίμασε ξανά σε ένα λεπτό.',
      }[response.status] ??
      'Δεν ολοκληρώθηκε η ενέργεια. Δοκίμασε ξανά.';
    if (response.status === 401 && path !== '/auth/login' && path !== '/auth/me')
      window.dispatchEvent(new Event('session-expired'));
    throw new ApiError(message, response.status,data);
  }
  window.dispatchEvent(new CustomEvent('paper-network',{detail:'connected'}));
  const value=response.status === 204 ? (undefined as T) : await response.json();
  if(cacheable&&accountId)await put(`cache:${accountId}:${path}`,value).catch(()=>{});
  return value;
  }catch(error){
    if((!(error instanceof ApiError)||error.status>=500)&&cacheable){const value=accountId?await get<T>(`cache:${accountId}:${path}`).catch(()=>undefined):undefined;if(value!==undefined){window.dispatchEvent(new CustomEvent('paper-network',{detail:'cached'}));return value;}}
    if(!(error instanceof ApiError))throw new Error('Δεν υπάρχει σύνδεση ή η σύνδεση είναι αργή. Οι αποθηκευμένες απαντήσεις παραμένουν στη συσκευή.');
    throw error;
  }finally{clearTimeout(timeout);}
}
export async function download(path:string,name='επισκέψεις'){const response=await fetch(`/api${path}`,{credentials:'same-origin',cache:'no-store',signal:AbortSignal.timeout(60000)});if(!response.ok)throw new Error('Η εξαγωγή χρειάζεται σύνδεση και δικαιώματα διαχειριστή.');const url=URL.createObjectURL(await response.blob());const link=document.createElement('a');link.href=url;link.download=`${name}-${new Intl.DateTimeFormat('sv-SE',{timeZone:'Europe/Athens'}).format(new Date())}.xlsx`;link.click();setTimeout(()=>URL.revokeObjectURL(url),1000);}
export const json = (body: unknown, method = 'POST'): RequestInit => ({
  method,
  body: JSON.stringify(body),
});
export const money = (value: number) =>
  new Intl.NumberFormat('el-GR', { style: 'currency', currency: 'EUR' }).format(value);
export const dateTime = (value: string) =>
  new Intl.DateTimeFormat('el-GR', {
    dateStyle: 'medium',
    timeStyle: 'short',
    timeZone: 'Europe/Athens',
  }).format(new Date(value));
