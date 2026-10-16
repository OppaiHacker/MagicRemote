// Linux: per-user install, then auto-update from a fake GitHub release feed (served here), restart into the new version.
import {execSync, spawn} from 'node:child_process';
import http from 'node:http';
import fs from 'node:fs';
import crypto from 'node:crypto';
import {attach, sleep, test, check, summary} from './cdp.mjs';

const sh = c => execSync(c, {encoding: 'utf8'}).trim();
const HOME = process.env.HOME;
const INST = `${HOME}/.local/share/magicremote/app`;

await test('install: --install copies the app, adds launcher, icon and .desktop entry', async () => {
  sh('/tmp/app/MagicRemote --install');
  check(fs.existsSync(`${INST}/MagicRemote`), 'app not copied');
  check(fs.readlinkSync(`${HOME}/.local/bin/magicremote`) === `${INST}/MagicRemote`, 'launcher symlink');
  const d = fs.readFileSync(`${HOME}/.local/share/applications/magicremote.desktop`, 'utf8');
  check(d.includes(`Exec="${INST}/MagicRemote"`) && d.includes('StartupWMClass=MagicRemote'), 'desktop entry');
  check(fs.existsSync(`${HOME}/.local/share/icons/hicolor/scalable/apps/magicremote.svg`), 'icon');
  check(sh(`${HOME}/.local/bin/magicremote --version`) === '1.1.0', 'installed version');
});

// fake release feed: tag v9.9.9 with the linux tarball and its digest
const tgz = fs.readFileSync('/out/update.tar.gz');
const digest = 'sha256:' + crypto.createHash('sha256').update(tgz).digest('hex');
let tamper = false;
const srv = http.createServer((q, r) => {
  if (q.url === '/latest') return r.end(JSON.stringify({tag_name: 'v9.9.9', assets: [{name: 'MagicRemote-9.9.9-linux-x64.tar.gz', browser_download_url: 'http://127.0.0.1:8099/pkg.tar.gz', digest: tamper ? 'sha256:' + '0'.repeat(64) : digest}]}));
  if (q.url === '/pkg.tar.gz') return r.end(tgz);
  r.statusCode = 404; r.end();
}).listen(8099);

process.env.MAGICREMOTE_UPDATE_URL = 'http://127.0.0.1:8099/latest';
delete process.env.MAGICREMOTE_NOUPDATE;

async function launch() {
  const app = spawn(`${HOME}/.local/bin/magicremote`, [], {stdio: 'ignore', detached: true});
  const p = await attach(9222, t => t.url.startsWith('http://127.0.0.1:'));
  await p.waitFor(`S.D.platform === 'linux'`);
  await p.ev(`(() => { window.__toasts = []; const t = H.toast; H.toast = m => { __toasts.push(m.msg); t(m); }; window.__upd = null; const u = H.update; H.update = m => { __upd = m; u(m); }; post({t:'ready'}); return true; })()`);
  return {app, p};
}

await test('update: corrupted download (digest mismatch) is rejected, app keeps running', async () => {
  tamper = true;
  const {app, p} = await launch();
  try {
    await p.waitFor(`__upd && __upd.version === '9.9.9'`, 20000, 'update offer');
    await p.ev(`post({t:'doUpdate'}); true`);
    await p.waitFor(`__toasts.some(t => t.includes('SHA-256'))`, 20000, 'digest refusal');
    check(sh(`${INST}/MagicRemote --version`) === '1.1.0', 'files replaced anyway');
  } finally {
    await p.call('Browser.close').catch(() => {});
    for (let i = 0; i < 50 && app.exitCode === null; i++) await sleep(200);
    tamper = false;
  }
});

await test('update: offer shown, download verified, app restarts as 9.9.9 with data kept', async () => {
  const {p} = await launch();
  await p.ev(`post({t:'addProfile', name:'Stays after update', color:'#2fb67c'}); true`);
  await p.waitFor(`S.D.profiles.some(x => x.name === 'Stays after update')`);
  await p.waitFor(`__upd && __upd.version === '9.9.9' && __upd.current === '1.1.0'`, 20000, 'update offer');
  const before = fs.readFileSync(`${process.env.MAGICREMOTE_DATA}/instance.url`, 'utf8');
  await p.ev(`post({t:'doUpdate'}); true`);
  for (let i = 0; i < 100; i++) {
    await sleep(300);
    const now = fs.existsSync(`${process.env.MAGICREMOTE_DATA}/instance.url`) ? fs.readFileSync(`${process.env.MAGICREMOTE_DATA}/instance.url`, 'utf8') : '';
    if (now && now !== before) break;
  }
  check(sh(`${INST}/MagicRemote --version`) === '9.9.9', 'not updated: ' + sh(`${INST}/MagicRemote --version`));
  check(!fs.existsSync(`${INST}.new`) && !fs.existsSync(`${INST}.old`), 'staging folders left behind');
  const q = await attach(9222, t => t.url.startsWith('http://127.0.0.1:') && !before.startsWith(t.url.split('#')[0]));
  await q.waitFor(`S.D.profiles.some(x => x.name === 'Stays after update')`, 20000, 'data after restart');
  await q.call('Browser.close').catch(() => {});
});

await test('uninstall: removes app, launcher and menu entry, keeps data', async () => {
  for (let i = 0; i < 50 && fs.existsSync(`${process.env.MAGICREMOTE_DATA}/instance.url`); i++) await sleep(200);
  sh(`${INST}/MagicRemote --uninstall`);
  check(!fs.existsSync(INST) && !fs.existsSync(`${HOME}/.local/bin/magicremote`) && !fs.existsSync(`${HOME}/.local/share/applications/magicremote.desktop`), 'leftovers');
  check(fs.existsSync(`${process.env.MAGICREMOTE_DATA}/data.json`), 'data removed');
});

srv.close();
process.exit(summary());
