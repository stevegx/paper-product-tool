import { existsSync } from 'node:fs';
import { resolve } from 'node:path';
import { spawn } from 'node:child_process';

const root = resolve(import.meta.dirname, '..');
const envFile = existsSync(resolve(root, '.env')) ? '.env' : '.env.local';
if (existsSync(resolve(root, envFile))) process.loadEnvFile(resolve(root, envFile));
if (!process.env.DATABASE_URL) throw new Error('Run neon deploy to populate DATABASE_URL before starting.');
const windowsDotnet = resolve(process.env.ProgramFiles ?? 'C:/Program Files', 'dotnet/dotnet.exe');
const dotnet = process.platform === 'win32' && existsSync(windowsDotnet) ? windowsDotnet : 'dotnet';
const child = spawn(dotnet, ['run', '--project', resolve(root, 'backend/Paper.Api'), '--no-launch-profile', '--urls', `http://127.0.0.1:${process.env.PORT ?? 5083}`], {
  cwd: root,
  env: { ...process.env, ASPNETCORE_ENVIRONMENT: process.env.ASPNETCORE_ENVIRONMENT ?? 'Development' },
  stdio: 'inherit',
});
child.on('error', error => { console.error(error.message); process.exitCode = 1; });
child.on('exit', code => { process.exitCode = code ?? 1; });
for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, () => child.kill(signal));
