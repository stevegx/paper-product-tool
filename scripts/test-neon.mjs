// Runs only against an explicitly selected Neon test branch, never production.
import { spawn, execFileSync } from 'node:child_process';
import { once } from 'node:events';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { randomUUID } from 'node:crypto';
import assert from 'node:assert/strict';

const root = resolve(import.meta.dirname, '..');
process.loadEnvFile(resolve(root, '.env.local'));
assert.equal(process.env.NEON_BRANCH, 'codex-persistence-check', 'Select the dedicated test branch first.');
const folder = resolve(root, '.local/neon-publish');
const fixture = resolve(root, '.local/neon-smoke.xlsx');
execFileSync(process.env.PYTHON ?? 'C:/Program Files/Python313/python.exe', ['-c', `
import sys,zipfile,xml.sax.saxutils as x
parts={'[Content_Types].xml':'<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>', '_rels/.rels':'<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>', 'xl/workbook.xml':'<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Customers" sheetId="1" r:id="rId1"/></sheets></workbook>', 'xl/_rels/workbook.xml.rels':'<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>'}
rows=[['ΚΩΔΙΚΟΣ','ΑΦΜ','ΠΩΛΗΤΗΣ','ΠΕΛΑΤΗΣ','ID ΚΑΤΗΓΟΡΙΑΣ','ΚΑΤΗΓΟΡΙΑ','ΤΖΙΡΟΣ'],['NEON-SMOKE','012345678','Test Seller','Synthetic Neon Customer','100','Test category','25']]
xml='<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>'
for i,row in enumerate(rows,1):
 xml+='<row r="%s">'%i
 for j,v in enumerate(row):xml+='<c r="%s%s" t="inlineStr"><is><t>%s</t></is></c>'%(chr(65+j),i,x.escape(v))
 xml+='</row>'
parts['xl/worksheets/sheet1.xml']=xml+'</sheetData></worksheet>'
with zipfile.ZipFile(sys.argv[1],'w',zipfile.ZIP_DEFLATED) as z:
 for k,v in parts.items():z.writestr(k,v)
`, fixture]);
const password = `Neon-Test-${randomUUID()}`;
const email = `smoke-${randomUUID()}@example.test`;
const base = 'http://127.0.0.1:5084';
let server;
let diagnostics = '';
async function start() {
  server = spawn('C:/Program Files/dotnet/dotnet.exe', ['Paper.Api.dll', '--urls', base], {
    cwd: folder,
    env: { ...process.env, RENDER: 'true', ASPNETCORE_ENVIRONMENT: 'Production', Bootstrap__Email: email, Bootstrap__Password: password, Logging__LogLevel__Default: 'Warning' },
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  for (const stream of [server.stdout, server.stderr]) stream.on('data', data => { diagnostics = (diagnostics + data).slice(-6000); });
  for (let attempt = 0; attempt < 90; attempt++) {
    if (server.exitCode !== null) throw new Error(`Server exited: ${diagnostics}`);
    try { if ((await fetch(`${base}/healthz`)).ok) return; } catch {}
    await new Promise(resolve => setTimeout(resolve, 500));
  }
  throw new Error(`Server startup timed out: ${diagnostics}`);
}
async function stop() {
  if (server && server.exitCode === null) { const ended = once(server, 'exit'); server.kill(); await ended; }
}
class Client {
  cookies = new Map();
  async request(path, method = 'GET', body) {
    const headers = { 'X-Forwarded-Proto': 'https' };
    if (method !== 'GET') {
      const csrf = await this.request('/api/auth/csrf');
      headers['X-CSRF-TOKEN'] = (await csrf.json()).token;
    }
    if (this.cookies.size) headers.Cookie = [...this.cookies].map(([k, v]) => `${k}=${v}`).join('; ');
    if (body && !(body instanceof FormData)) headers['Content-Type'] = 'application/json';
    const response = await fetch(`${base}${path}`, { method, headers, body: body ? body instanceof FormData ? body : JSON.stringify(body) : undefined });
    for (const value of response.headers.getSetCookie()) {
      const entry = value.split(';')[0]; const index = entry.indexOf('=');
      this.cookies.set(entry.slice(0, index), entry.slice(index + 1));
    }
    return response;
  }
  async ok(path, method = 'GET', body) {
    const response = await this.request(path, method, body);
    assert.equal(response.status, 200, `${method} ${path}: ${await response.clone().text()}`);
    return response;
  }
}
try {
  await start();
  assert.match(await (await fetch(base)).text(), /id="root"/);
  const admin = new Client();
  await admin.ok('/api/auth/login', 'POST', { email, password });
  const form = new FormData(); form.set('year', '2026');
  form.set('file', new Blob([readFileSync(fixture)]), 'synthetic.xlsx');
  await admin.ok('/api/admin/import', 'POST', form);
  const sellerEmail = `seller-${randomUUID()}@example.test`;
  await admin.ok('/api/admin/users', 'POST', { email: sellerEmail, password, name: 'Neon Test Seller', role: 'Seller' });
  const seller = new Client(); await seller.ok('/api/auth/login', 'POST', { email: sellerEmail, password });
  const customers = await (await seller.ok('/api/customers?afm=012345678')).json();
  assert.equal(customers.length, 1);
  const survey = await (await seller.ok('/api/survey')).json();
  const section = survey.questions[0].sectionKey;
  const answers = survey.questions.filter(q => q.sectionKey === section).map(q => ({ questionId: q.id, optionIds: [q.options[0].id] }));
  const id = randomUUID();
  const input = { mutationId: randomUUID(), expectedVersion: 0, branchCode: 'NEON-SMOKE', surveyVersion: survey.version, occurredAtUtc: new Date().toISOString(), notes: 'Neon persistence check', answers };
  await seller.ok(`/api/visits/${id}`, 'PUT', input);
  console.log('PASS: production startup, React, secure login, user creation, Excel import, AFM search, questionnaire and visit submission');
  await stop(); await start();
  await seller.ok('/api/auth/me');
  const repeated = await (await seller.ok(`/api/visits/${id}`, 'PUT', input)).json();
  assert.equal(repeated.version, 1);
  const editor = await (await seller.ok(`/api/visits/${id}/editor`)).json();
  assert.equal(editor.visit.notes, input.notes);
  assert.ok(editor.survey.questions.length > 0);
  const snapshot = await (await seller.ok('/api/offline/snapshot')).json();
  assert.equal(snapshot.editors.filter(e => e.visit.id === id).length, 1);
  assert.equal(snapshot.customers.length, 1);
  const exportFile = await admin.ok('/api/admin/visits/export');
  assert.match(exportFile.headers.get('content-type'), /spreadsheetml/);
  assert.ok((await exportFile.arrayBuffer()).byteLength > 1000);
  console.log('PASS: after process restart, users, session keys, customer, visit, answers and idempotency remain; offline snapshot and Excel export work');
} finally { await stop(); }
