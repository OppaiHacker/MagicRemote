// E2E for the Windows build, headless: MagicRemote runs in a Windows 11 VM joined to a lab domain (lab.local),
// its WebView2 exposes CDP (WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9222), forwarded to CDP_PORT here.
// Targets: the domain controller (Windows Server 2022, NLA) and a Linux xrdp server.
// VM_SH: command that runs one PowerShell command line in the client VM (e.g. an ssh wrapper).
import {execFileSync} from 'node:child_process';
import {attach, sleep, test, check, summary} from './cdp.mjs';

const DC = process.env.DC || 'mrwindo-9bf7s7j.lab.local', DC_IP = process.env.DC_IP || '172.20.0.3';
const LNX = process.env.LNX || '172.20.0.4';
const PASS = process.env.PASS || 'MagicR3mote!';
const OUT = process.env.OUT || '.';
const SHOT = process.env.SHOT; // optional: command taking an output .png path (screenshot of the VM screen)
const vm = ps => { try { return execFileSync('sh', ['-c', `${process.env.VM_SH} "powershell -NoProfile -OutputFormat Text -EncodedCommand ${Buffer.from("$ProgressPreference = 'SilentlyContinue'; " + ps, 'utf16le').toString('base64')}"`], {encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'], timeout: 90000}).trim(); } catch (e) { return String(e.stdout || '') + e.stderr; } };
// PowerShell on the DC, through WinRM from the client VM
const dc = ps => vm(`$c = New-Object PSCredential('LAB\\mr', (ConvertTo-SecureString '${PASS}' -AsPlainText -Force)); Invoke-Command -ComputerName ${DC_IP} -Credential $c -ScriptBlock { ${ps} } 2>&1`);
const shot = name => { if (SHOT) try { execFileSync('sh', ['-c', `${SHOT} ${OUT}/${name}.png`]); } catch {} };

const p = await attach(+(process.env.CDP_PORT || 9222), t => t.url.startsWith('https://app.magicremote/'));
const {ev, waitFor} = p;
await waitFor(`typeof S !== 'undefined' && S.D?.platform === 'win' && document.querySelector('#mlist') !== null`, 30000, 'ui ready');
await ev(`window.__toasts = []; const _t = H.toast; H.toast = m => { __toasts.push(m.msg); _t(m); }; true`);
await ev(`window.__dl = []; H.download = m => { __dl.push(m); }; true`);
const cred = label => `S.D.creds.find(c => c.label === ${JSON.stringify(label)})?.id ?? null`;
const machine = (o, label = 'Lab') => ev(`(() => { const m = {...DEF, id: uid(), profile: S.D.profile, cred: ${cred(label)}, hue: 200, certCheck: false, ...${JSON.stringify(o)}}; post({t:'saveMachine', m, connect:false}); return m.id; })()`);
const sessionOf = id => `S.sessions.find(s => s.mid === '${id}')`;
const settle = (id, ms = 45000) => waitFor(`['connected', 'disconnected'].includes(${sessionOf(id)}?.state) && ${sessionOf(id)}`, ms, 'session state');
const connected = async (id, ms) => { const s = await settle(id, ms); if (s.state !== 'connected') throw new Error('disconnected: ' + s.error); };
const close = async id => { await ev(`post({t:'close', sid:${sessionOf(id)}.id}); true`); await waitFor(`!${sessionOf(id)}`, 15000, 'tab gone'); };
const listPane = async (loc, path = null) => {
  const req = await ev(`(() => { S.panes.T = newPane('${loc}'); S.panes.T.path = ${JSON.stringify(path)}; listPane('T'); return S.panes.T.req; })()`);
  await waitFor(`S.panes.T && S.panes.T.req === ${req} && !S.panes.T.loading`, 30000, 'listing ' + loc);
  return ev(`({path: S.panes.T.path, err: S.panes.T.err, items: (S.panes.T.items || []).map(i => i.name)})`);
};
// mr's RDP sessions on the DC (quser), "" when none
const dcSessions = () => dc(`quser 2>&1 | Select-String -Pattern 'mr ' | % { $_.Line.Trim() }`);
// PowerShell inside the user's desktop session (clipboard, windows): a scheduled task, as the session user
const inSession = ps => vm(`$f = 'C:\\mr\\ins.ps1'; [IO.File]::WriteAllBytes($f, [Convert]::FromBase64String('${Buffer.from(ps).toString('base64')}')); schtasks /create /tn mrins /tr "powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File $f" /sc once /st 23:59 /ru LAB\\mr /it /f | Out-Null; schtasks /run /tn mrins | Out-Null; Start-Sleep 5; schtasks /delete /tn mrins /f | Out-Null`);
const logoffAll = () => dc(`quser 2>&1 | Select-String 'mr ' | % { logoff (($_.Line.Trim() -split '\\s+') | ? { $_ -match '^\\d+$' } | Select -First 1) }`);
logoffAll();
dc(`Remove-Item C:\\Users\\mr\\Desktop\\*.txt -EA 0`); // files left by earlier runs

console.log('MagicRemote Windows E2E against', DC, '(' + DC_IP + ') and', LNX);

await test('bridge: WebView2 UI talks to the backend', async () => {
  check(await ev(`S.D.profiles.length >= 1 && !!S.D.profile`), 'no profile data');
});

await test('vault: credentials stored with DPAPI (no plaintext in data.json)', async () => {
  await ev(`post({t:'addCred', label:'Lab', user:'LAB\\\\mr', pass:'${PASS}'}); post({t:'addCred', label:'Wrong', user:'LAB\\\\mr', pass:'wrong-password'});
    post({t:'addCred', label:'Operator', user:'LAB\\\\op', pass:'0per@tor!'}); post({t:'addCred', label:'Linux', user:'mr', pass:'${PASS}'}); true`);
  await waitFor(`S.D.creds.length >= 4`);
  const data = vm(`Get-Content C:\\mr\\data\\data.json -Raw`);
  check(data.includes('LAB\\\\op') && !data.includes(PASS) && !data.includes('0per@tor!'), 'plaintext password in data.json');
});

let ids = {};
await test('machines saved and reachable', async () => {
  ids.nla = await machine({name: 'DC NLA', host: DC});
  ids.bad = await machine({name: 'DC wrong password', host: DC}, 'Wrong');
  ids.ra = await machine({name: 'DC Restricted Admin', host: DC, auth: 'restricted'});
  ids.rcg = await machine({name: 'DC Remote Credential Guard', host: DC, auth: 'rcg'});
  ids.app = await machine({name: 'Notepad', host: DC, app: 'notepad.exe', appArgs: 'C:\\Windows\\win.ini'});
  ids.rel = await machine({name: 'DC relative mouse', host: DC, relMouse: true, scale: 'fit'});
  ids.priv = await machine({name: 'DC private share', host: DC_IP, share: `\\\\${DC_IP}\\priv`}, 'Operator');
  ids.ports = await machine({name: 'DC COM ports', host: DC, usb: true});
  ids.lnx = await machine({name: 'Linux xrdp', host: LNX, os: 'lnx'}, 'Linux');
  await waitFor(`S.D.machines.length >= 9`);
  await waitFor(`S.reach['${ids.nla}'] === true && S.reach['${ids.lnx}'] === true`, 40000, 'reachability');
});

await test('RDP: NLA logon with the vault password (embedded ActiveX)', async () => {
  await ev(`connect('${ids.nla}')`);
  await connected(ids.nla);
  check(await ev(`${sessionOf(ids.nla)}.external !== true`), 'should be embedded');
  await sleep(5000);
  shot('win-rdp-nla');
  check(/mr\s+rdp-tcp#\d+\s+\d+\s+Active/i.test(dcSessions()), 'no active RDP session for mr on the DC: ' + dcSessions());
});

// VNCDO: vncdotool command for the VM screen (e.g. "vncdo -s 172.20.0.2::5900"): real pointer input, not injected events
if (process.env.VNCDO) await test('drag & drop: a real drag from Explorer onto the RDP picture copies, with Shift moves', async () => {
  const drag = shift => execFileSync('sh', ['-c', `${process.env.VNCDO} move 240 609 pause 0.5 mousedown 1 pause 0.3 move 260 590 pause 0.2 ${shift ? 'keydown shift ' : ''}move 400 450 pause 0.2 move 600 300 pause 0.2 move 800 250 pause 0.2 move 805 252 pause 0.8 mouseup 1${shift ? ' keyup shift' : ''}`]);
  vm(`Remove-Item -Recurse C:\\mr\\dragtest -EA 0; mkdir C:\\mr\\dragtest | Out-Null; Set-Content C:\\mr\\dragtest\\dragged.txt 'drag and drop'`);
  // Explorer with the file, docked at the bottom left, the session picture stays visible on the right
  inSession(`Add-Type 'using System; using System.Runtime.InteropServices; public static class W { [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f); [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); }'
(New-Object -ComObject Shell.Application).Windows() | % { $_.Quit() }
for ($i = 0; $i -lt 40 -and @((New-Object -ComObject Shell.Application).Windows()).Count; $i++) { Start-Sleep -Milliseconds 250 }
Start-Process explorer.exe C:\\mr\\dragtest
for ($i = 0; $i -lt 40 -and -not $w; $i++) { Start-Sleep -Milliseconds 250; $w = (New-Object -ComObject Shell.Application).Windows() | ? { $_.LocationURL -like '*dragtest*' } | select -First 1 }
Start-Sleep 1
"$($w.HWND)" | Set-Content C:\\mr\\ins.log
[W]::SetWindowPos([IntPtr]$w.HWND, [IntPtr]-1, 0, 430, 640, 330, 0x40) | Out-Null; [W]::SetForegroundWindow([IntPtr]$w.HWND) | Out-Null`);
  await ev(`S.transfers.clear(); true`);
  drag(false);
  await waitFor(`[...S.transfers.values()].some(t => t.name === 'dragged.txt' && t.state === 'done')`, 30000, 'copy');
  check(/drag and drop/.test(dc(`Get-Content C:\\Users\\mr\\Desktop\\dragged.txt`)), 'not on the DC desktop');
  check(/True/.test(vm(`Test-Path C:\\mr\\dragtest\\dragged.txt`)), 'copy removed the source');
  shot('win-drag-drop');
  await ev(`S.transfers.clear(); true`);
  drag(true);
  await waitFor(`[...S.transfers.values()].some(t => t.state === 'done')`, 30000, 'move');
  check(/False/.test(vm(`Test-Path C:\\mr\\dragtest\\dragged.txt`)), 'Shift-drag left the source');
  inSession(`(New-Object -ComObject Shell.Application).Windows() | % { $_.Quit() }`);
});

await test('RDP: Ctrl+Alt+Del opens the secure screen in the session', async () => {
  await ev(`post({t:'cad', sid:${sessionOf(ids.nla)}.id}); true`);
  await sleep(3000);
  shot('win-rdp-cad');
  // the security options screen runs on the Winlogon desktop: LogonUI.exe appears in mr's session
  const sid = dc(`(quser 2>&1 | Select-String 'mr .*Active' | Select -First 1).Line -replace '.*rdp-tcp#\\d+\\s+(\\d+).*','$1'`);
  check(/LogonUI/.test(dc(`Get-Process LogonUI -EA 0 | ? SessionId -eq ${sid} | % Name`)), 'LogonUI not running in session ' + sid);
  await ev(`post({t:'cad', sid:${sessionOf(ids.nla)}.id}); true`); // harmless second press; Esc is sent by the server-side timeout
});

await test('files: upload into the session over SMB (C$, Windows SSO)', async () => {
  const sid = await ev(`${sessionOf(ids.nla)}.id`);
  const l = await listPane(sid);
  check(!l.err && /\\Users\\mr\\Desktop$/i.test(l.path), 'home: ' + JSON.stringify(l));
  vm(`Set-Content C:\\mr\\from-host.txt 'Hello from Windows äö' -Encoding UTF8`);
  await ev(`S.transfers.clear(); post({t:'xfer', from:{loc:'host', path:'C:\\\\mr'}, to:{loc:'${sid}', path:null}, names:['from-host.txt'], move:false}); true`);
  await waitFor(`[...S.transfers.values()].some(t => t.name === 'from-host.txt' && t.state === 'done')`, 30000, 'copy done');
  check(/Hello from Windows/.test(dc(`Get-Content C:\\Users\\mr\\Desktop\\from-host.txt`)), 'file not on the DC desktop');
});

await test('drag & drop: the window under the RDP picture accepts file drops; a drop becomes a transfer into the session', async () => {
  const sid = await ev(`${sessionOf(ids.nla)}.id`);
  vm(`Set-Content C:\\mr\\dropped.txt 'from drag' -Encoding UTF8`);
  await ev(`window.__dd = null; H.dropdiag = m => __dd = m.r; S.transfers.clear(); post({t:'dropdiag', sid:'${sid}', file:'C:\\\\mr\\\\dropped.txt'}); true`);
  const r = await waitFor(`__dd`, 10000, 'dropdiag');
  check(r.registered && r.enter !== 0 && r.drop !== 0, 'drop target: ' + JSON.stringify(r));
  await waitFor(`[...S.transfers.values()].some(t => t.name === 'dropped.txt' && t.state === 'done')`, 30000, 'transfer');
  check(/from drag/.test(dc(`Get-Content C:\\Users\\mr\\Desktop\\dropped.txt`)), 'not on the DC desktop');
});

await test('clipboard: a copy on this PC enters the history; reusing an entry puts it back on the clipboard', async () => {
  inSession(`Set-Clipboard 'clipboard-windows-77'`);
  await waitFor(`S.clip.some(c => c.text === 'clipboard-windows-77')`, 15000, 'history');
  inSession(`Set-Clipboard 'other'`);
  await waitFor(`S.clip.some(c => c.text === 'other')`, 15000, 'second copy');
  await ev(`post({t:'clipUse', id: S.clip.find(c => c.text === 'clipboard-windows-77').id}); true`);
  await sleep(800);
  inSession(`Get-Clipboard | Set-Content C:\\mr\\clip-out.txt`);
  check(vm(`Get-Content C:\\mr\\clip-out.txt`) === 'clipboard-windows-77', 'clipboard not restored');
});

await test('RDP: wrong vault password: Windows asks again; Cancel ends with a clear error', async () => {
  await ev(`__toasts = []; connect('${ids.bad}')`);
  let ui = '';
  for (let i = 0; i < 30 && !ui; i++) { await sleep(1000); ui = vm(`Get-Process CredentialUIBroker -EA 0 | ? SessionId -ne 0 | % Id`); }
  check(ui, 'no "credentials did not work" prompt');
  await sleep(1500);
  shot('win-rdp-badpass');
  // CANCEL: command that presses Esc on the VM's screen (real Cancel); without it the prompt process is ended
  if (process.env.CANCEL) execFileSync('sh', ['-c', process.env.CANCEL]); else vm(`Stop-Process -Id ${ui.split(/\s+/)[0]} -Force`);
  const s = await settle(ids.bad);
  check(s.state === 'disconnected' && /Logon cancelled|Logon failed/.test(s.error || ''), 'got ' + JSON.stringify(s));
  console.log('       error: ' + s.error);
  await close(ids.bad);
});

await test('SMB: share the Windows login cannot open uses the vault password (LAB\\op)', async () => {
  check(!/True/.test(vm(`Test-Path \\\\${DC_IP}\\priv\\secret.txt 2>&1`)), 'LAB\\mr can already open the share — test is meaningless');
  await ev(`connect('${ids.priv}')`);
  await settle(ids.priv); // LAB\op may not log on over RDP; the files pane still works
  const l = await listPane(await ev(`${sessionOf(ids.priv)}.id`));
  check(!l.err && l.items.includes('secret.txt'), 'listing: ' + JSON.stringify(l));
  await close(ids.priv);
});

await close(ids.nla).catch(() => {});

await test('RDP: Restricted Admin logon (no credentials sent to the host)', async () => {
  await ev(`connect('${ids.ra}')`);
  await connected(ids.ra);
  await sleep(3000);
  shot('win-rdp-restricted');
  // a Restricted Admin logon is a network-only logon type: the session has no reusable credentials, so SMB to the DC itself fails
  const ev4624 = dc(`Get-WinEvent -FilterHashtable @{LogName='Security'; Id=4624; StartTime=(Get-Date).AddMinutes(-2)} -EA 0 | ? { $_.Properties[5].Value -eq 'mr' -and $_.Properties[8].Value -eq 10 } | Select -First 1 | % { $_.Properties[26].Value }`);
  console.log('       4624 RestrictedAdminMode: ' + (ev4624 || '?'));
  check(!ev4624 || /Yes|%%1842/.test(ev4624), 'logon event says not restricted: ' + ev4624);
  await close(ids.ra);
});

await test('RDP: Remote Credential Guard logon (Kerberos)', async () => {
  await ev(`connect('${ids.rcg}')`);
  await connected(ids.rcg);
  await sleep(3000);
  shot('win-rdp-rcg');
  await close(ids.rcg);
});

await test('RemoteApp: notepad opens as its own window, no full desktop', async () => {
  logoffAll(); // a disconnected desktop session would be reused (server behaviour): start from none
  await ev(`__toasts = []; connect('${ids.app}')`);
  await connected(ids.app, 60000);
  check(await ev(`${sessionOf(ids.app)}.external === true`), 'RemoteApp session should be external');
  await waitFor(`true`);
  let procs = '';
  for (let i = 0; i < 20 && !/notepad/i.test(procs); i++) { await sleep(1000); procs = dc(`Get-Process notepad -IncludeUserName -EA 0 | % { $_.UserName + ' ' + $_.SessionId }`); }
  check(/LAB\\mr/i.test(procs), 'notepad not started for mr on the DC: ' + procs);
  check(!/explorer/i.test(dc(`$s = (Get-Process notepad -EA 0 | Select -First 1).SessionId; Get-Process explorer -EA 0 | ? SessionId -eq $s | % Name`)), 'explorer runs in the RemoteApp session');
  shot('win-remoteapp');
  await close(ids.app);
  for (let i = 0; i < 10 && /notepad/i.test(dc(`Get-Process notepad -EA 0 | % Name`)); i++) await sleep(1000);
});

await test('RDP: relative mouse mode + smart sizing connect', async () => {
  await ev(`connect('${ids.rel}')`);
  await connected(ids.rel);
  await close(ids.rel);
});

await test('COM: this PC\'s serial port is redirected into the session and can be opened there', async () => {
  logoffAll(); // the check runs in "mr's session": make it the only one
  await ev(`connect('${ids.ports}')`);
  await connected(ids.ports);
  await sleep(3000);
  // DOS device names are per session: ask from inside mr's session (scheduled task on the DC)
  const out = dc(`Set-Content C:\\mr-ports.cmd "@echo off\`r\`nchange port /query > C:\\mr-ports.txt 2>&1\`r\`nmode COM1 >> C:\\mr-ports.txt 2>&1"; schtasks /create /tn mrports /tr C:\\mr-ports.cmd /sc once /st 23:59 /ru LAB\\mr /it /f | Out-Null; schtasks /run /tn mrports | Out-Null; Start-Sleep 4; schtasks /delete /tn mrports /f | Out-Null; Get-Content C:\\mr-ports.txt`);
  check(/COM1 = .*RdpDrPort.*tsclient\\COM1/.test(out) && /Baud/.test(out), 'COM1 not redirected: ' + out);
  await close(ids.ports);
});

await test('RDP to Linux (xrdp) from Windows', async () => {
  await ev(`connect('${ids.lnx}')`);
  await connected(ids.lnx);
  await sleep(4000);
  shot('win-rdp-xrdp');
  await close(ids.lnx);
});

await test('.rdp: import (UTF-16, mstsc) and export', async () => {
  await ev(`(() => { const t = 'full address:s:imported.example:3390\\r\\nusername:s:LAB\\\\mr\\r\\nuse multimon:i:1\\r\\nremoteapplicationmode:i:1\\r\\nremoteapplicationprogram:s:||calc\\r\\nremoteapplicationname:s:Calculator\\r\\n';
    const b = new Uint8Array(2 + t.length * 2); b[0] = 0xFF; b[1] = 0xFE; for (let i = 0; i < t.length; i++) { b[2 + i * 2] = t.charCodeAt(i) & 255; b[3 + i * 2] = t.charCodeAt(i) >> 8; }
    importRdp([new File([b], 'srv.rdp')]); })()`);
  await waitFor(`S.D.machines.some(m => m.host === 'imported.example')`, 10000, 'imported');
  const m = await ev(`S.D.machines.find(m => m.host === 'imported.example')`);
  check(m.port === 3390 && m.app === '||calc' && m.mon === 'all' && m.cred, 'fields: ' + JSON.stringify(m));
  await ev(`post({t:'exportRdp', id:'${m.id}'}); true`);
  await waitFor(`__dl.length > 0`);
  const dl = await ev(`__dl[0]`);
  check(dl.text.includes('full address:s:imported.example:3390') && dl.text.includes('username:s:LAB\\mr'), 'export: ' + JSON.stringify(dl));
});

await test('no RDP session of mr left on the DC after closing the tabs', async () => {
  let q = '';
  for (let i = 0; i < 15 && /Active/.test(q = dcSessions()); i++) await sleep(1000);
  check(!/Active/.test(q), 'still active: ' + q);
});

p.close();
process.exit(summary());
