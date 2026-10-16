// Windows: per-user install, auto-update from a fake GitHub release feed (served here, reached by the VM at 127.0.0.1:8099
// through `ssh -R 8099:127.0.0.1:8099`), restart into the new version, uninstall. Runs as the interactive user (scheduled tasks).
// ZIP: the 9.9.9 win-x64 build. VM_SH / CDP_PORT as in e2e-windows.mjs; the app build to install is in C:\mr\app.
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import http from 'node:http';
import fs from 'node:fs';
import crypto from 'node:crypto';
import {attach, sleep, test, check, summary} from './cdp.mjs';

// async on purpose: the release feed below is served by this same process and must keep answering while the VM works
const vm = ps => promisify(execFile)('sh', ['-c', `${process.env.VM_SH} "powershell -NoProfile -OutputFormat Text -EncodedCommand ${Buffer.from("$ProgressPreference = 'SilentlyContinue'; " + ps, 'utf16le').toString('base64')}"`], {timeout: 90000})
  .then(r => r.stdout.trim(), e => String(e.stdout || '') + e.stderr);
const CDP = +(process.env.CDP_PORT || 9222);
const USER = process.env.WIN_USER || 'LAB\\mr';

const zip = fs.readFileSync(process.env.ZIP);
const digest = 'sha256:' + crypto.createHash('sha256').update(zip).digest('hex');
let tamper = false;
const srv = http.createServer((q, r) => {
  if (q.url === '/latest') return r.end(JSON.stringify({tag_name: 'v9.9.9', assets: [{name: 'MagicRemote-9.9.9-win-x64.zip', browser_download_url: 'http://127.0.0.1:8099/pkg.zip', digest: tamper ? 'sha256:' + '0'.repeat(64) : digest}]}));
  if (q.url === '/pkg.zip') return r.end(zip);
  r.statusCode = 404; r.end();
}).listen(8099, '127.0.0.1');

// launchers run by scheduled tasks in the user's desktop session (WebView2 needs it); the env is inherited by the updated app too
const env = `set WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9222\r\nset MAGICREMOTE_DATA=C:\\mr\\data-upd\r\nset MAGICREMOTE_UPDATE_URL=http://127.0.0.1:8099/latest\r\n`;
const task = (name, body) => vm(`[IO.File]::WriteAllBytes('C:\\mr\\${name}.cmd', [Convert]::FromBase64String('${Buffer.from('@echo off\r\n' + env + body + '\r\n').toString('base64')}')); schtasks /create /tn ${name} /tr C:\\mr\\${name}.cmd /sc once /st 23:59 /ru ${USER} /it /f | Out-Null`);
const run = name => vm(`schtasks /run /tn ${name} | Out-Null`);
const INST = '$env:INST';
const inst = `$sid = (New-Object Security.Principal.NTAccount('${USER}')).Translate([Security.Principal.SecurityIdentifier]).Value; $prof = (Get-ItemProperty "HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\ProfileList\\$sid").ProfileImagePath; $env:INST = "$prof\\AppData\\Local\\Programs\\MagicRemote"; `;
const version = async () => (await vm(inst + `& "${INST}\\MagicRemote.exe" --version | Out-String`)).trim();
const pageOf = url => t => t.url.startsWith('https://app.magicremote/') && (!url || t.id !== url);

await vm(`Get-Process MagicRemote -EA 0 | Stop-Process -Force; Start-Sleep 1; Remove-Item -Recurse -Force C:\\mr\\data-upd -EA 0`);
await task('mrinst', 'C:\\mr\\app\\MagicRemote.exe --install > C:\\mr\\install.log');
await task('mrstart', 'start "" "%LOCALAPPDATA%\\Programs\\MagicRemote\\MagicRemote.exe"');
await task('mruninst', '"%LOCALAPPDATA%\\Programs\\MagicRemote\\MagicRemote.exe" --uninstall > C:\\mr\\uninstall.log');

async function app() {
  const p = await attach(CDP, pageOf(), 40000);
  await p.waitFor(`typeof S !== 'undefined' && S.D?.platform === 'win'`, 30000, 'ui');
  await p.ev(`(() => { window.__toasts = []; const t = H.toast; H.toast = m => { __toasts.push(m.msg); t(m); }; window.__upd = null; const u = H.update; H.update = m => { __upd = m; u(m); }; post({t:'ready'}); return true; })()`);
  return p;
}
const quit = async () => { await vm(`Get-Process MagicRemote -EA 0 | Stop-Process -Force`); await sleep(1500); };

await test('install: --install copies the app, adds Start menu shortcut and Apps & features entry', async () => {
  await run('mrinst');
  await sleep(8000);
  const r = await vm(inst + `"exe=" + (Test-Path "${INST}\\MagicRemote.exe"); "lnk=" + (Test-Path "$prof\\AppData\\Roaming\\Microsoft\\Windows\\Start Menu\\Programs\\MagicRemote.lnk"); "reg=" + (Get-ItemProperty "Registry::HKEY_USERS\\$sid\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\MagicRemote" -EA 0).UninstallString`);
  check(/exe=True/.test(r) && /lnk=True/.test(r) && /reg=".*MagicRemote\.exe" --uninstall/.test(r), r);
  check((await version()) === '1.1.0', 'installed version');
});

await test('update: corrupted download (digest mismatch) is rejected, app keeps running', async () => {
  tamper = true;
  await run('mrstart');
  const p = await app();
  await p.waitFor(`__upd && __upd.version === '9.9.9'`, 30000, 'update offer');
  await p.ev(`post({t:'doUpdate'}); true`);
  await p.waitFor(`__toasts.some(t => t.includes('SHA-256'))`, 30000, 'digest refusal');
  check((await version()) === '1.1.0', 'files replaced anyway');
  check(/True/.test(await vm(`[bool](Get-Process MagicRemote -EA 0)`)), 'app quit');
  p.close();
  tamper = false;
  await quit();
});

await test('update: offer shown, download verified, app restarts as 9.9.9 with data kept', async () => {
  await run('mrstart');
  const p = await app();
  await p.ev(`post({t:'addProfile', name:'Stays after update', color:'#2fb67c'}); true`);
  await p.waitFor(`S.D.profiles.some(x => x.name === 'Stays after update')`);
  await p.waitFor(`__upd && __upd.version === '9.9.9' && __upd.current === '1.1.0'`, 30000, 'update offer');
  const pid = await vm(`(Get-Process MagicRemote).Id`);
  await p.ev(`post({t:'doUpdate'}); true`);
  p.close();
  let now = pid;
  for (let i = 0; i < 60 && (!now || now === pid); i++) { await sleep(1000); now = await vm(`(Get-Process MagicRemote -EA 0).Id`); }
  check(now && now !== pid, 'app did not restart');
  const v = await version();
  check(v === '9.9.9', 'not updated: ' + v);
  check(/False/.test(await vm(inst + `Test-Path "${INST}.new"`)), 'staging folder left behind');
  const q = await attach(CDP, pageOf(), 40000);
  await q.waitFor(`typeof S !== 'undefined' && S.D?.profiles.some(x => x.name === 'Stays after update')`, 30000, 'data after restart');
  q.close();
  await quit();
});

await test('uninstall: removes app, shortcut and entry, keeps data', async () => {
  await run('mruninst');
  await sleep(8000);
  const r = await vm(inst + `"exe=" + (Test-Path "${INST}"); "lnk=" + (Test-Path "$prof\\AppData\\Roaming\\Microsoft\\Windows\\Start Menu\\Programs\\MagicRemote.lnk"); "reg=" + (Test-Path "Registry::HKEY_USERS\\$sid\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\MagicRemote"); "data=" + (Test-Path C:\\mr\\data-upd\\data.json)`);
  check(/exe=False/.test(r) && /lnk=False/.test(r) && /reg=False/.test(r) && /data=True/.test(r), r);
});

await vm(`schtasks /delete /tn mrinst /f; schtasks /delete /tn mrstart /f; schtasks /delete /tn mruninst /f`);
srv.close();
process.exit(summary());
