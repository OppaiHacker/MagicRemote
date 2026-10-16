// E2E for the Linux build, headless: Xvfb + Chromium app window (driven over CDP) + FreeRDP 3,
// against a Linux server container (xrdp + XFCE, SSH/SFTP, FTPS). Run by tests/run-linux.sh inside the client container.
import {execSync, spawn} from 'node:child_process';
import fs from 'node:fs';
import {attach, sleep, test, check, summary} from './cdp.mjs';

const SRV = process.env.SRV || 'mr-lnx';
const APP = process.env.APP || '/tmp/app/MagicRemote';
const DATA = process.env.MAGICREMOTE_DATA;
const sh = c => execSync(c, {encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe']}).trim();
const shq = c => { try { return sh(c); } catch (e) { return ''; } };

const app = spawn(APP, [], {stdio: ['ignore', 'pipe', 'inherit']});
let appOut = '';
app.stdout.on('data', d => appOut += d);
let appExited = null;
app.on('exit', c => appExited = c);

const p = await attach(9222, t => t.url.startsWith('http://127.0.0.1:'));
const {ev, waitFor} = p;
const url = await waitFor(`location.href.startsWith('http') && location.href`);
const port = new URL(url).port;
await waitFor(`S.D.platform === 'linux' && document.querySelector('#mlist') !== null`, 20000, 'ui ready');

// helpers in the page
await ev(`window.__toasts = []; const _t = H.toast; H.toast = m => { __toasts.push(m.msg); _t(m); }; true`);
await ev(`window.__dl = []; const _d = H.download; H.download = m => { __dl.push(m); }; true`);
const machine = (o) => ev(`(() => { const m = {...DEF, id: uid(), profile: S.D.profile, cred: S.D.creds.find(c => c.user === 'mr')?.id ?? null, hue: 200, ...${JSON.stringify(o)}}; post({t:'saveMachine', m, connect:false}); return m.id; })()`);
const sessionOf = id => `S.sessions.find(s => s.mid === '${id}')`;
const connected = async (id, ms = 30000) => {
  await waitFor(`['connected', 'disconnected'].includes(${sessionOf(id)}?.state)`, ms, 'session state');
  const s = await ev(sessionOf(id));
  if (s.state !== 'connected') throw new Error('disconnected: ' + s.error);
};
await ev(`window.__errs = []; addEventListener('error', e => __errs.push(String(e.message))); true`);
const listPane = async (loc, path = null) => {
  const req = await ev(`(() => { S.panes.T = newPane('${loc}'); S.panes.T.path = ${JSON.stringify(path)}; listPane('T'); return S.panes.T.req; })()`);
  await waitFor(`S.panes.T && S.panes.T.req === ${req} && !S.panes.T.loading`, 20000, 'listing ' + loc);
  return ev(`({path: S.panes.T.path, err: S.panes.T.err, items: S.panes.T.items.map(i => i.name)})`);
};

console.log('MagicRemote Linux E2E against', SRV, 'via', url.split('#')[0]);

await test('bridge: UI talks to the backend over the WebSocket', async () => {
  check(await ev(`S.D.profiles.length >= 1 && !!S.D.profile`), 'no profile data');
});

await test('security: WebSocket without token, foreign Host and path traversal are refused', async () => {
  const noTok = shq(`curl -s -o /dev/null -w '%{http_code}' -H 'Connection: Upgrade' -H 'Upgrade: websocket' -H 'Sec-WebSocket-Version: 13' -H 'Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==' http://127.0.0.1:${port}/ws`);
  check(noTok === '403', 'ws without token: ' + noTok);
  const host = shq(`curl -s -o /dev/null -w '%{http_code}' -H 'Host: evil.example:${port}' http://127.0.0.1:${port}/`);
  check(['403', '404', '400'].includes(host), 'foreign host: ' + host);
  const trav = shq(`curl -s -o /dev/null -w '%{http_code}' --path-as-is http://127.0.0.1:${port}/../../etc/passwd`);
  check(trav === '404' || trav === '400', 'traversal: ' + trav);
  const up = shq(`curl -s -o /dev/null -w '%{http_code}' -X POST --data x 'http://127.0.0.1:${port}/upload?k=wrong&to=host&name=x'`);
  check(up === '403', 'upload with wrong token: ' + up);
});

await test('vault: credential stored encrypted (AES-GCM, 0600 key file without keyring)', async () => {
  await ev(`post({t:'addCred', label:'Lab', user:'mr', pass:'MagicR3mote!'}); post({t:'addCred', label:'Bad', user:'wrong', pass:'nope'}); true`);
  await waitFor(`S.D.creds.length >= 2`);
  const data = fs.readFileSync(`${DATA}/data.json`, 'utf8');
  check(!data.includes('MagicR3mote!'), 'plaintext password in data.json');
  const mode = (fs.statSync(`${DATA}/vault.key`).mode & 0o777).toString(8);
  check(mode === '600', 'key file mode ' + mode);
});

let rdpId, sshId, sftpId, ftpId;
await test('machines: RDP (Linux/xrdp), SSH, SFTP, FTPS saved', async () => {
  rdpId = await machine({name: 'Lab RDP', host: SRV, proto: 'rdp', port: 3389, os: 'lnx', certCheck: false});
  sshId = await machine({name: 'Lab SSH', host: SRV, proto: 'ssh', port: 22});
  sftpId = await machine({name: 'Lab SFTP', host: SRV, proto: 'sftp', port: 22});
  ftpId = await machine({name: 'Lab FTPS', host: SRV, proto: 'ftp', port: 21, ftps: true});
  await waitFor(`S.D.machines.length >= 4`);
  await waitFor(`S.reach['${rdpId}'] === true && S.reach['${ftpId}'] === true`, 40000, 'reachability');
});

await test('RDP: FreeRDP window opens and logs in (session connected, external)', async () => {
  await ev(`connect('${rdpId}')`);
  await connected(rdpId);
  check(await ev(`${sessionOf(rdpId)}.external === true`), 'not external');
  check(await ev(`document.querySelector('#stageMain').textContent.includes('in a separate window')`), 'stage card missing');
  const win = shq(`xdotool search --class MagicRemote`);
  check(win.split('\n').length >= 2, 'no FreeRDP window next to the app window: ' + win);
  check(!shq(`ps -eo args | grep -c '[/]p:MagicR3mote'`).match(/^[1-9]/), 'password visible in process list');
  await sleep(4000);
  sh(`xwd -root -display $DISPLAY | magick xwd:- /out/linux-rdp.png`);
});

await test('RDP: scale mode switch reconnects FreeRDP with smart sizing', async () => {
  const sid = await ev(`${sessionOf(rdpId)}.id`);
  await ev(`post({t:'scale', sid:'${sid}', v:'fit'})`);
  await waitFor(`${sessionOf(rdpId)}?.scale === 'fit'`);
  await sleep(500);
  await waitFor(`${sessionOf(rdpId)}?.state === 'connected'`, 30000, 'reconnected');
  await ev(`post({t:'scale', sid:'${sid}', v:'dynamic'})`);
  await waitFor(`${sessionOf(rdpId)}?.scale === 'dynamic' && ${sessionOf(rdpId)}?.state === 'connected'`, 30000);
});

await test('files: drop from desktop (upload) lands on the RDP host via SFTP', async () => {
  const sid = await ev(`${sessionOf(rdpId)}.id`);
  await ev(`(async () => { const q = new URLSearchParams({k:TOKEN, to:'${sid}', path:'', name:'from-desktop.txt'}); const r = await fetch('/upload?' + q, {method:'POST', body:'Hello from Linux host äö'}); return r.status; })()`);
  await waitFor(`__toasts.some(t => t.includes('from-desktop.txt'))`, 20000, 'transfer toast');
  const l = await listPane(sid);
  check(!l.err && l.items.includes('from-desktop.txt'), 'not listed on host: ' + JSON.stringify(l));
  check(l.path.endsWith('/Desktop'), 'home is not Desktop: ' + l.path);
});

await test('files: host -> RDP session copy of a 5 MB file, then session -> host move back', async () => {
  const sid = await ev(`${sessionOf(rdpId)}.id`);
  const home = (await listPane('host')).path;
  sh(`head -c 5000000 /dev/urandom > '${home}/big.bin'`);
  const sum = sh(`sha256sum '${home}/big.bin' | cut -d' ' -f1`);
  await ev(`post({t:'xfer', from:{loc:'host', path:${JSON.stringify(home)}}, to:{loc:'${sid}', path:null}, names:['big.bin'], move:false}); true`);
  await waitFor(`[...S.transfers.values()].some(t => t.name === 'big.bin' && t.state === 'done')`, 30000, 'copy done');
  fs.renameSync(`${home}/big.bin`, `${home}/big.orig`);
  await ev(`S.transfers.clear(); post({t:'xfer', from:{loc:'${sid}', path:null}, to:{loc:'host', path:${JSON.stringify(home)}}, names:['big.bin'], move:true}); true`);
  await waitFor(`[...S.transfers.values()].some(t => t.name === 'big.bin' && t.state === 'done')`, 30000, 'move done');
  check(sh(`sha256sum '${home}/big.bin' | cut -d' ' -f1`) === sum, 'checksum differs after round trip');
  const l = await listPane(sid);
  check(!l.items.includes('big.bin'), 'move left the source behind');
});

await test('SSH: terminal echo round trip', async () => {
  await ev(`connect('${sshId}')`);
  await connected(sshId);
  const sid = await ev(`${sessionOf(sshId)}.id`);
  await waitFor(`!!TERMS['${sid}']`, 5000).catch(async e => { throw new Error(e.message + ' ' + JSON.stringify(await ev('({errs: __errs, view: S.view, active: S.active, occ: S.occ})'))); });
  await ev(`window.__term = ''; const _w = H.term; H.term = m => { if (m.sid === '${sid}') __term += m.d; _w(m); }; post({t:'termIn', sid:'${sid}', d:'echo mr-$((6*7))\\n'}); true`);
  await waitFor(`__term.includes('mr-42')`, 10000, 'echo');
  const t0 = await ev(`(async () => { __term = ''; const t = performance.now(); post({t:'termIn', sid:'${sid}', d:'x'}); while (!__term.includes('x')) await new Promise(r => setTimeout(r, 1)); return performance.now() - t; })()`);
  console.log(`       key -> echo ${t0.toFixed(1)} ms`);
});

await test('SFTP and FTPS: session -> session copy (SFTP -> FTPS) and listing', async () => {
  await ev(`connect('${sftpId}')`);
  await connected(sftpId);
  await ev(`connect('${ftpId}')`);
  await connected(ftpId);
  const sftp = await ev(`${sessionOf(sftpId)}.id`), ftp = await ev(`${sessionOf(ftpId)}.id`);
  const l = await listPane(sftp);
  check(l.items.includes('from-desktop.txt'), 'sftp listing');
  await ev(`S.transfers.clear(); post({t:'xfer', from:{loc:'${sftp}', path:${JSON.stringify(l.path)}}, to:{loc:'${ftp}', path:null}, names:['from-desktop.txt'], move:false}); true`);
  await waitFor(`[...S.transfers.values()].some(t => t.state === 'done')`, 30000, 'ftp copy');
  const f = await listPane(ftp);
  check(f.items.includes('from-desktop.txt'), 'ftp listing: ' + JSON.stringify(f));
});

await test('clipboard: copy seen by the UI enters the shared history; reuse puts it on the X clipboard', async () => {
  await ev(`post({t:'clipSeen', type:'text', text:'clipboard-from-session-123'}); true`);
  await waitFor(`S.clip.some(c => c.text === 'clipboard-from-session-123')`);
  sh(`printf 'other' | xclip -selection clipboard -i >/dev/null 2>&1`);
  const id = await ev(`S.clip.find(c => c.text === 'clipboard-from-session-123').id`);
  await ev(`post({t:'clipUse', id:${id}}); true`);
  await sleep(800);
  check(shq(`xclip -selection clipboard -o`) === 'clipboard-from-session-123', 'X clipboard not set');
});

await test('vault: reveal requires polkit; without an agent it is refused', async () => {
  const id = await ev(`S.D.creds[0].id`);
  await ev(`__toasts = []; post({t:'revealCred', id:'${id}'}); true`);
  await waitFor(`__toasts.some(t => /polkit|Verification/.test(t))`, 15000, 'refusal toast');
  check(await ev(`!('${id}' in S.reveal)`), 'password revealed without verification');
});

await test('.rdp: import (UTF-16, mstsc) and export', async () => {
  await ev(`(() => { const t = 'full address:s:imported.example:3390\\r\\nusername:s:mr\\r\\nuse multimon:i:1\\r\\nremoteapplicationmode:i:1\\r\\nremoteapplicationprogram:s:||calc\\r\\nremoteapplicationname:s:Calculator\\r\\n';
    const b = new Uint8Array(2 + t.length * 2); b[0] = 0xFF; b[1] = 0xFE; for (let i = 0; i < t.length; i++) { b[2 + i * 2] = t.charCodeAt(i) & 255; b[3 + i * 2] = t.charCodeAt(i) >> 8; }
    importRdp([new File([b], 'srv.rdp')]); })()`);
  await waitFor(`S.D.machines.some(m => m.host === 'imported.example')`, 10000, 'imported');
  const m = await ev(`S.D.machines.find(m => m.host === 'imported.example')`);
  check(m.port === 3390 && m.app === '||calc' && m.name === 'Calculator' && m.mon === 'all' && m.cred, 'fields: ' + JSON.stringify(m));
  await ev(`post({t:'exportRdp', id:'${m.id}'}); true`);
  await waitFor(`__dl.length > 0`);
  const dl = await ev(`__dl[0]`);
  check(dl.name === 'Calculator.rdp' && dl.text.includes('full address:s:imported.example:3390') && dl.text.includes('remoteapplicationprogram:s:||calc'), 'export: ' + JSON.stringify(dl));
});

// Windows server (lab domain controller, NLA): WIN_DC=<address>, WIN_USER (DOMAIN\\user), WIN_PASS
if (process.env.WIN_DC) {
  const winM = async (o, pass = process.env.WIN_PASS) => {
    const label = 'Win ' + Math.random().toString(36).slice(2, 7);
    await ev(`post({t:'addCred', label:'${label}', user:${JSON.stringify(process.env.WIN_USER)}, pass:${JSON.stringify(pass)}}); true`);
    await waitFor(`S.D.creds.some(c => c.label === '${label}')`);
    const id = await ev(`(() => { const m = {...DEF, id: uid(), profile: S.D.profile, cred: S.D.creds.find(c => c.label === '${label}').id, hue: 30, certCheck: false, host: '${process.env.WIN_DC}', ...${JSON.stringify(o)}}; post({t:'saveMachine', m, connect:false}); return m.id; })()`);
    await waitFor(`S.D.machines.some(m => m.id === '${id}')`);
    return id;
  };
  const closeTab = async id => { await ev(`post({t:'close', sid:${sessionOf(id)}.id}); true`); await waitFor(`!${sessionOf(id)}`, 10000, 'tab gone'); };

  await test('Windows server: FreeRDP NLA logon with the vault password', async () => {
    const id = await winM({name: 'Windows NLA'});
    await ev(`connect('${id}')`);
    await connected(id, 45000);
    await sleep(4000);
    sh(`xwd -root -display $DISPLAY | magick xwd:- /out/linux-rdp-windows.png`);
    await closeTab(id);
  });

  await test('Windows server: wrong vault password ends with a logon error', async () => {
    const id = await winM({name: 'Windows wrong password'}, 'wrong-password');
    await ev(`__toasts = []; connect('${id}')`);
    await waitFor(`${sessionOf(id)}?.state === 'disconnected'`, 45000, 'disconnected');
    const e = await ev(`${sessionOf(id)}.error`);
    console.log('       error: ' + e.split('\n')[0]);
    check(/Logon failed|Credentials rejected|Security negotiation/.test(e), 'error: ' + e);
    await closeTab(id);
  });

  await test('Windows server: Restricted Admin logon', async () => {
    const id = await winM({name: 'Windows RA', auth: 'restricted'});
    await ev(`connect('${id}')`);
    await connected(id, 45000);
    await closeTab(id);
  });

  await test('Windows server: RemoteApp (notepad) through FreeRDP /app', async () => {
    const id = await winM({name: 'Notepad', app: 'notepad.exe'});
    await ev(`connect('${id}')`);
    await connected(id, 45000);
    let w = '';
    for (let i = 0; i < 20 && !/Notepad/i.test(w); i++) { await sleep(1000); w = shq(`xdotool search --name Notepad getwindowname %@`); }
    check(/Notepad/i.test(w), 'no Notepad window: ' + w);
    sh(`xwd -root -display $DISPLAY | magick xwd:- /out/linux-remoteapp.png`);
    await closeTab(id);
  });
}

await test('RDP: closing the tab ends FreeRDP', async () => {
  await ev(`post({t:'close', sid:${sessionOf(rdpId)}.id}); true`);
  await waitFor(`!${sessionOf(rdpId)}`, 10000, 'tab gone');
  await sleep(500);
  check(shq(`pgrep -c freerdp`) === '0' || shq(`pgrep -c freerdp`) === '', 'freerdp still running');
});

await test('single instance: second launch reuses the running backend', async () => {
  const out = sh(`${APP} --no-browser`);
  check(out.startsWith(`http://127.0.0.1:${port}/#k=`), 'second launch printed ' + out);
});

await test('closing the app window ends the backend', async () => {
  await p.call('Browser.close').catch(() => {});
  for (let i = 0; i < 50 && appExited === null; i++) await sleep(200);
  check(appExited === 0, 'backend exit code ' + appExited);
  check(!fs.existsSync(`${DATA}/instance.url`), 'instance.url left behind');
});

process.exit(summary());
