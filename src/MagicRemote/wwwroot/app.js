'use strict';
/* MagicRemote UI. State lives in the C# host; this file renders it and sends intents back over chrome.webview. */

const ICONS = {
  plus:'<path d="M12 5v14M5 12h14"/>',
  x:'<path d="M18 6 6 18M6 6l12 12"/>',
  search:'<circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/>',
  grid:'<rect x="3" y="3" width="7" height="7" rx="1.5"/><rect x="14" y="3" width="7" height="7" rx="1.5"/><rect x="3" y="14" width="7" height="7" rx="1.5"/><rect x="14" y="14" width="7" height="7" rx="1.5"/>',
  folder:'<path d="M3 7.5A2.5 2.5 0 0 1 5.5 5H9l2 2h7.5A2.5 2.5 0 0 1 21 9.5v8a2.5 2.5 0 0 1-2.5 2.5h-13A2.5 2.5 0 0 1 3 17.5z"/>',
  clip:'<rect x="8" y="2.5" width="8" height="4" rx="1"/><path d="M16 4.5h1.5A2.5 2.5 0 0 1 20 7v12.5a2.5 2.5 0 0 1-2.5 2.5h-11A2.5 2.5 0 0 1 4 19.5V7a2.5 2.5 0 0 1 2.5-2.5H8"/>',
  moon:'<path d="M20.5 13.5A8.5 8.5 0 1 1 10.5 3.5a6.5 6.5 0 0 0 10 10z"/>',
  sun:'<circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/>',
  sidebar:'<rect x="3" y="4" width="18" height="16" rx="2.5"/><path d="M9 4v16"/>',
  key:'<circle cx="8" cy="15" r="4.5"/><path d="m11.5 11.5 8-8M16.5 6.5l2.5 2.5M14 9l2 2"/>',
  eye:'<path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12z"/><circle cx="12" cy="12" r="3"/>',
  eyeoff:'<path d="M3 3l18 18M10.6 5.1A10 10 0 0 1 12 5c6.4 0 10 7 10 7a17 17 0 0 1-3.2 4.1M6.6 6.6C3.7 8.4 2 12 2 12s3.6 7 10 7a9.6 9.6 0 0 0 5.4-1.6M9.9 9.9a3 3 0 0 0 4.2 4.2"/>',
  copy:'<rect x="9" y="9" width="12" height="12" rx="2"/><path d="M5 15h-.5A1.5 1.5 0 0 1 3 13.5v-9A1.5 1.5 0 0 1 4.5 3h9A1.5 1.5 0 0 1 15 4.5V5"/>',
  trash:'<path d="M4 7h16M9 7V4.5h6V7M6.5 7l.8 12.2A2 2 0 0 0 9.3 21h5.4a2 2 0 0 0 2-1.8L17.5 7"/>',
  pin:'<path d="M12 16v5M8.5 3h7l-1 6.5L18 13H6l3.5-3.5z"/>',
  more:'<circle cx="5" cy="12" r="1.2"/><circle cx="12" cy="12" r="1.2"/><circle cx="19" cy="12" r="1.2"/>',
  max:'<path d="M8 3H5a2 2 0 0 0-2 2v3M21 8V5a2 2 0 0 0-2-2h-3M3 16v3a2 2 0 0 0 2 2h3M16 21h3a2 2 0 0 0 2-2v-3"/>',
  power:'<path d="M12 3v9"/><path d="M18.4 6.6a9 9 0 1 1-12.8 0"/>',
  down:'<path d="m6 9 6 6 6-6"/>',
  arrowdown:'<path d="M12 5v14M6 13l6 6 6-6"/>',
  arrowup:'<path d="M12 19V5M6 11l6-6 6 6"/>',
  arrowright:'<path d="M5 12h14M13 6l6 6-6 6"/>',
  arrowleft:'<path d="M19 12H5M11 6l-6 6 6 6"/>',
  up:'<path d="M12 19V5M6 11l6-6 6 6"/>',
  refresh:'<path d="M20 11a8 8 0 1 0-2.3 5.7M20 4v7h-7"/>',
  newfolder:'<path d="M3 7.5A2.5 2.5 0 0 1 5.5 5H9l2 2h7.5A2.5 2.5 0 0 1 21 9.5v8a2.5 2.5 0 0 1-2.5 2.5h-13A2.5 2.5 0 0 1 3 17.5zM12 11v5M9.5 13.5h5"/>',
  swap:'<path d="M4 7h16M16 3l4 4-4 4M20 17H4M8 13l-4 4 4 4"/>',
  lock:'<rect x="4" y="11" width="16" height="10" rx="2"/><path d="M8 11V7.5a4 4 0 0 1 8 0V11"/>',
  shield:'<path d="M12 21.5s7.5-3.5 7.5-9.5V5.5L12 2.5l-7.5 3V12c0 6 7.5 9.5 7.5 9.5z"/>',
  check:'<path d="m5 12.5 4.5 4.5L19 7.5"/>',
  star:'<path d="m12 3 2.7 5.6 6.1.9-4.4 4.3 1 6.1L12 17l-5.4 2.9 1-6.1-4.4-4.3 6.1-.9z"/>',
  win:'<rect x="4" y="4" width="7" height="7" rx=".5"/><rect x="13" y="4" width="7" height="7" rx=".5"/><rect x="4" y="13" width="7" height="7" rx=".5"/><rect x="13" y="13" width="7" height="7" rx=".5"/>',
  term:'<path d="m5 7 5 5-5 5M12 17h7"/>',
  monitor:'<rect x="2.5" y="3.5" width="19" height="13" rx="2"/><path d="M8 20.5h8M12 16.5v4"/>',
  transfer:'<path d="M12 3v12M7 10l5 5 5-5M5 21h14"/>',
  bolt:'<path d="M13 2 4 14h7l-1 8 9-12h-7z"/>',
  info:'<circle cx="12" cy="12" r="9"/><path d="M12 11v5M12 8h.01"/>',
  plug:'<path d="M9 2v6M15 2v6M6 8h12v3a6 6 0 0 1-12 0zM12 17v5"/>',
};
const ic = (n, cls = '') => `<svg class="i ${cls}" viewBox="0 0 24 24" aria-hidden="true">${ICONS[n] || ''}</svg>`;
const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => [...r.querySelectorAll(s)];
const esc = v => String(v ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const fmt = b => b < 1024 ? Math.round(b) + ' B' : b < 1048576 ? (b/1024).toFixed(b < 10240 ? 1 : 0) + ' KB' : b < 1073741824 ? (b/1048576).toFixed(1) + ' MB' : (b/1073741824).toFixed(2) + ' GB';
const plural = (n, one, few, many) => n === 1 ? one : (n%10 >= 2 && n%10 <= 4 && (n%100 < 12 || n%100 > 14)) ? few : many;
const ago = t => { const s = (Date.now() - t) / 1000; return s < 50 ? 'now' : s < 3600 ? Math.round(s/60) + ' min ago' : s < 86400 ? Math.round(s/3600) + ' h ago' : Math.round(s/86400) + ' days ago'; };
const extOf = n => n.includes('.') ? n.split('.').pop().toLowerCase() : '';
const KIND = {xlsx:'sheet',csv:'sheet',docx:'doc',doc:'doc',md:'doc',txt:'doc',pdf:'pdf',zip:'zip',gz:'zip','7z':'zip',rar:'zip',tar:'zip',png:'img',jpg:'img',jpeg:'img',gif:'img',webp:'img',ps1:'code',sh:'code',conf:'code',sql:'code',ts:'code',js:'code',json:'code',yaml:'code',yml:'code',py:'code',cs:'code',exe:'code',msi:'code'};
const glyph = n => `<span class="fg ${KIND[extOf(n)] || ''}">${esc(extOf(n).slice(0, 4))}</span>`;
const PROTO = {rdp:'RDP', ssh:'SSH', sftp:'SFTP', ftp:'FTP'};
const DEF_PORT = {rdp:3389, ssh:22, sftp:22, ftp:21};
const mIcon = m => m.proto === 'ssh' ? 'term' : m.proto === 'rdp' ? (m.os === 'lnx' ? 'monitor' : 'win') : 'folder';

/* Bridge: WebView2 on Windows; on Linux a WebSocket to the local backend (token in the URL fragment). */
const WV = window.chrome?.webview;
const TOKEN = new URLSearchParams(location.hash.slice(1)).get('k') || '';
const hasHost = !!WV || !!TOKEN;
let sock = null, sockQ = [];
function onHost(fn) {
  if (WV) return WV.addEventListener('message', e => fn(e.data));
  const open = () => {
    sock = new WebSocket(`ws://${location.host}/ws?k=${TOKEN}`);
    sock.onopen = () => { for (const m of sockQ) sock.send(m); sockQ = []; };
    sock.onmessage = e => fn(JSON.parse(e.data));
    sock.onclose = () => { sock = null; if (!S.quit) setTimeout(() => { open(); post({t:'ready'}); }, 1000); };
  };
  open();
}
const post = o => {
  if (WV) return WV.postMessage(o);
  if (!TOKEN) return;
  const j = JSON.stringify(o);
  sock?.readyState === 1 ? sock.send(j) : sockQ.push(j);
};
const isLinux = () => S.D.platform === 'linux';

/* ---------- state ---------- */
const S = {
  D:{profiles:[], machines:[], creds:[], profile:'', theme:'dark', sidebar:true, platform:'win'}, quit:false,
  sessions:[], active:null, view:'session', panel:null, occ:null,
  reach:{}, clip:[], transfers:new Map(), thumbs:{}, reveal:{},
  search:'', collapsed:new Set(), moveMode:false, palIdx:0, palItems:[], reqN:0,
  panes:{}, // A/B = side panel, X/Y = file manager tab
};
const mach = id => S.D.machines.find(m => m.id === id);
const sess = id => S.sessions.find(s => s.id === id);
const act = () => sess(S.active);
const machOf = s => s && mach(s.mid);
const live = () => S.sessions.filter(s => s.state === 'connected');
const prof = (id = S.D.profile) => S.D.profiles.find(p => p.id === id) || S.D.profiles[0] || {name:'—', color:'#6b8afd'};
const locName = loc => loc === 'host' ? 'This computer' : machOf(sess(loc))?.name || '—';
const srcName = src => src === 'host' ? 'This computer' : mach(src)?.name || 'session';
const uid = () => crypto.randomUUID().replace(/-/g, '');

/* ---------- toasts & layers ---------- */
function toast(msg, o = {}) {
  const el = document.createElement('div');
  el.className = 'toast ' + (o.kind || '');
  el.title = msg;
  el.innerHTML = `${ic(o.icon || (o.kind === 'err' ? 'info' : 'check'))}<span>${esc(msg)}</span>${o.action ? `<button class="tbtn">${esc(o.action.label)}</button>` : ''}`;
  if (o.action) el.querySelector('button').onclick = () => { el.remove(); o.action.fn(); };
  const box = $('#toasts'); box.append(el);
  while (box.children.length > 3) box.firstChild.remove();
  setTimeout(() => { el.classList.add('out'); setTimeout(() => el.remove(), 220); }, o.ms || (o.kind === 'err' ? 6000 : 3200));
}
function openLayer(id, html, cls = '') {
  closeLayer(id);
  const el = document.createElement('div');
  el.className = 'layer ' + cls; el.dataset.layer = id; el.innerHTML = html;
  el.addEventListener('mousedown', e => { if (e.target === el) { el.remove(); syncOcc(); } });
  $('#layers').append(el);
  syncOcc();
  return el;
}
const closeLayer = id => { $(`[data-layer="${id}"]`)?.remove(); syncOcc(); };
function closeTop() {
  const l = $$('.layer').pop();
  if (l) { l.remove(); syncOcc(); return true; }
  if (!$('#profMenu').hidden) { $('#profMenu').hidden = true; return true; }
  return false;
}

/* The RDP picture is a native window above this page: hide it whenever HTML must be on top. */
function syncOcc() {
  const s = act();
  const on = S.view !== 'session' || !s || $('#layers').children.length > 0;
  document.body.classList.toggle('live', !on && s?.kind === 'rdp' && s.state === 'connected');
  if (on !== S.occ) { S.occ = on; post({t:'occlude', on}); }
}
function sendStage() {
  const r = $('#stage').getBoundingClientRect();
  post({t:'stage', x:r.left, y:r.top, w:r.width, h:r.height, dpr:devicePixelRatio});
}

function applyTheme() {
  document.documentElement.dataset.theme = S.D.theme;
  $('#themeBtn').innerHTML = ic(S.D.theme === 'dark' ? 'sun' : 'moon');
  $('#app').classList.toggle('nosb', !S.D.sidebar);
  for (const t of Object.values(TERMS)) t.term.options.theme = termTheme();
}

/* ---------- tabs ---------- */
function renderTabs() {
  $('#tabs').innerHTML = S.sessions.map((s, i) => {
    const m = machOf(s);
    return `<div class="tab ${s.id === S.active && S.view === 'session' ? 'on' : ''} ${s.state}" data-act="tab" data-id="${s.id}" data-dropto="${s.id}" tabindex="0"
      title="${esc(m?.name)} — ${esc(m?.host)} (${PROTO[m?.proto]})  ·  Alt+${i + 1}&#10;Drop a file on the tab to send it to this session">
      <span class="dot"></span><span class="nm">${esc(m?.name)}</span>
      ${s.kind === 'rdp' ? `<span class="rtt" data-rtt="${s.id}">${s.rtt > 0 ? s.rtt + ' ms' : ''}</span>` : `<span class="kind">${PROTO[m?.proto]}</span>`}
      <button class="x" data-act="close" data-id="${s.id}" title="Disconnect">${ic('x')}</button>
    </div>`;
  }).join('') + `<button class="icon-btn" data-act="palette" title="New connection (Ctrl+K)">${ic('plus')}</button>`;
  $('#ovBtn').classList.toggle('on', S.view === 'overview');
}

/* ---------- sidebar ---------- */
function renderSidebar() {
  const p = prof(), n = S.D.machines.filter(m => m.profile === p.id).length;
  $('#profBtn').innerHTML = `<span class="pav" style="--c:${esc(p.color)}">${esc(p.name[0] || '?')}</span><span class="pn"><b>${esc(p.name)}</b><small>Profile · ${n} ${plural(n, 'machine', 'machines', 'machines')}</small></span>${ic('down')}`;
  renderList();
}
function renderList() {
  const q = S.search.trim().toLowerCase();
  const ms = S.D.machines.filter(m => m.profile === S.D.profile && (!q || `${m.name} ${m.host} ${m.group} ${m.proto}`.toLowerCase().includes(q)))
    .sort((a, b) => (b.fav - a.fav) || a.name.localeCompare(b.name));
  const groups = {};
  ms.forEach(m => (groups[m.group] ||= []).push(m));
  $('#mlist').innerHTML = Object.entries(groups).map(([g, list]) => `
    <div class="grp ${S.collapsed.has(g) ? 'col' : ''}">
      <button class="gh" data-act="group" data-g="${esc(g)}">${ic('down')}<span>${esc(g)}</span><em>${list.length}</em></button>
      <div class="gl">${list.map(mrow).join('')}</div>
    </div>`).join('') || `<div class="empty">${q ? `No results for “${esc(S.search)}”` : 'No machines in this profile'}</div>`;
}
function mrow(m) {
  const s = S.sessions.find(x => x.mid === m.id);
  const st = s ? 'live' : S.reach[m.id] === false ? 'off' : S.reach[m.id] ? 'on' : '';
  const tip = s ? 'Session open — click to switch' : S.reach[m.id] === false ? `${m.host}:${m.port} does not respond` : 'Click to connect';
  return `<div class="mrow ${s && s.id === S.active && S.view === 'session' ? 'act' : ''}" data-act="connect" data-id="${m.id}" tabindex="0" title="${esc(tip)}">
    <span class="os">${ic(mIcon(m))}</span>
    <span class="mt"><b>${esc(m.name)}</b><small>${m.proto !== 'rdp' ? PROTO[m.proto].toLowerCase() + '://' : ''}${esc(m.host)}${m.port !== DEF_PORT[m.proto] ? ':' + m.port : ''}</small></span>
    ${m.fav ? `<span class="fav">${ic('star')}</span>` : ''}<span class="st ${st}"></span>
    <button class="icon-btn sm more" data-act="edit" data-id="${m.id}" title="Edit">${ic('more')}</button>
  </div>`;
}
function toggleProfMenu() {
  const menu = $('#profMenu');
  if (!menu.hidden) { menu.hidden = true; return; }
  menu.innerHTML = S.D.profiles.map(p => {
    const n = S.D.machines.filter(m => m.profile === p.id).length;
    return `<button data-act="profile" data-id="${p.id}"><span class="pav sm" style="--c:${esc(p.color)}">${esc(p.name[0] || '?')}</span><span class="pn"><b>${esc(p.name)}</b><small>${n} ${plural(n, 'machine', 'machines', 'machines')}</small></span>${p.id === S.D.profile ? ic('check') : ''}</button>`;
  }).join('') + `<div class="mdiv"></div><button data-act="profnew">${ic('plus')}<span>New profile…</span></button>`;
  menu.hidden = false;
}

/* ---------- session bar ---------- */
function renderSbar() {
  const s = act(), bar = $('#sbar');
  if (!s || S.view !== 'session') { bar.hidden = true; return; }
  const m = machOf(s);
  bar.hidden = false;
  const res = s.kind === 'rdp' && s.w ? `${s.w}×${s.h}` : '';
  bar.innerHTML = `<span class="dot" style="background:${s.state === 'connected' ? 'var(--ok)' : s.state === 'disconnected' ? 'var(--err)' : 'var(--warn)'}"></span>
    <span class="nm">${esc(m?.name)}</span><span class="badge">${PROTO[m?.proto]}</span><span class="host">${esc(m?.host)}:${m?.port}</span>
    ${s.kind === 'rdp' && s.state === 'connected' ? `${s.rtt > 0 ? `<span class="chipm" title="Network latency measured by the RDP engine. Cursor drawn locally (0 ms), input sent without queuing.">RTT <b data-rtt="${s.id}">${s.rtt} ms</b></span>` : ''}${res ? `<span class="chipm" title="Session resolution">${res}</span>` : ''}` : ''}
    <span class="grow"></span>
    ${s.kind === 'rdp' && s.state === 'connected' && !(s.external && m?.app) ? `<div class="seg">${[['dynamic','Fit','Dynamic resolution — host adapts to the window'],['fit','Scale','Fixed 1920×1080 scaled to the window'],['native','1:1','No scaling, with scrolling']].map(([v, l, t]) => `<button class="${s.scale === v ? 'on' : ''}" data-act="scale" data-v="${v}" title="${t}">${l}</button>`).join('')}</div>
      ${s.external ? '' : `<button class="icon-btn" data-act="cad" title="Send Ctrl+Alt+Del (lock, change password, task manager)">${ic('lock')}</button>
      <button class="icon-btn" data-act="fullscreen" title="Full screen (exit: Ctrl+Alt+Break or the top bar)">${ic('max')}</button>`}<span class="sep"></span>` : ''}
    <button class="icon-btn ${S.panel === 'files' ? 'on' : ''}" data-act="panel" data-v="files" title="Files">${ic('folder')}</button>
    <button class="icon-btn ${S.panel === 'clip' ? 'on' : ''}" data-act="panel" data-v="clip" title="Clipboard">${ic('clip')}</button>
    <button class="icon-btn" data-act="overview" title="Session overview">${ic('grid')}</button>
    <button class="icon-btn" data-act="edit" data-id="${m?.id}" title="Machine settings">${ic('more')}</button>
    <button class="icon-btn danger" data-act="close" data-id="${s.id}" title="Disconnect">${ic('power')}</button>`;
}

/* ---------- stage ---------- */
function renderStage() {
  const st = $('#stageMain'), th = $('#termHost');
  renderSbar();
  th.hidden = true;
  if (S.view === 'overview') { st.innerHTML = overviewHTML(); post({t:'thumbs'}); return syncOcc(); }
  const s = act(), m = machOf(s);
  if (!s) { st.innerHTML = homeHTML(); syncOcc(); return; }
  if (s.state === 'connecting' || (s.state === 'reconnecting' && s.kind !== 'rdp')) {
    st.innerHTML = `<div class="center"><div class="card2"><h3><span class="sp"></span>Connecting to ${esc(m?.name)}</h3><p class="mono">${PROTO[m?.proto]} · ${esc(m?.host)}:${m?.port}</p></div></div>`;
  } else if (s.state === 'disconnected') {
    st.innerHTML = `<div class="center"><div class="card2"><h3>${ic('plug')}Disconnected — ${esc(m?.name)}</h3><p>${esc(s.error || '')}</p>
      <div class="row"><button class="btn pri" data-act="reconnect" data-id="${s.id}">Reconnect</button><button class="btn" data-act="edit" data-id="${m?.id}">Settings</button><button class="btn" data-act="close" data-id="${s.id}">Close tab</button></div></div></div>`;
  } else if (s.kind === 'ssh') {
    st.innerHTML = '';
    th.hidden = false;
    const t = termFor(s.id);
    $$('.term', th).forEach(el => el.hidden = el !== t.el);
    requestAnimationFrame(() => { t.fit.fit(); t.term.focus(); });
  } else if (s.kind === 'files') {
    S.panes.X ||= newPane('host'); S.panes.Y ||= newPane(s.id);
    if (S.panes.Y.loc !== s.id) Object.assign(S.panes.Y, newPane(s.id));
    st.innerHTML = `<div class="fm">${paneHTML('X')}<div class="mid">${midButtons('X', 'Y', true)}</div>${paneHTML('Y')}${queueHTML()}</div>`;
    for (const k of ['X', 'Y']) if (!S.panes[k].items && !S.panes[k].loading) listPane(k);
  } else if (s.external) {
    st.innerHTML = `<div class="center"><div class="card2"><h3><span class="dot" style="background:var(--ok)"></span>${esc(m?.name)} — ${m?.app ? 'RemoteApp' : 'desktop'} in a separate window</h3>
      <p>${m?.app ? `Windows of <span class="mono">${esc(m.app)}</span> run like local apps.` : 'The RDP session runs in a FreeRDP window (Ctrl+Alt+Enter = full screen).'} Files dropped on this tab will go to the session.</p>
      <div class="row"><button class="btn" data-act="edit" data-id="${m?.id}">Settings</button><button class="btn danger" data-act="close" data-id="${s.id}">Disconnect</button></div></div></div>`;
  } else {
    st.innerHTML = '<div class="rdp-stage"></div>';
  }
  syncOcc();
}
function homeHTML() {
  const ms = S.D.machines.filter(m => m.profile === S.D.profile).sort((a, b) => (b.fav - a.fav) || (b.last || 0) - (a.last || 0)).slice(0, 6);
  return `<div class="home"><div class="hc">
    <h1>Connect</h1>
    <form class="qc" data-form="quick">${ic('search')}<input name="q" placeholder="host, IP, host:port, ssh://user@host, ftp://host" autocomplete="off" autofocus><span class="kbd">Enter</span></form>
    <div class="sec">${esc(prof().name)} · recent and favorites</div>
    ${ms.length ? `<div class="cards">${ms.map(m => `<button class="card" data-act="connect" data-id="${m.id}"><span class="cv" style="--h:${m.hue}">${ic(mIcon(m))}</span><b>${esc(m.name)}</b><small>${PROTO[m.proto]} · ${esc(m.host)}</small><span class="st ${S.reach[m.id] === false ? 'off' : S.reach[m.id] ? 'on' : ''}"></span></button>`).join('')}</div>`
      : `<div class="empty-cta"><b>No machines in profile “${esc(prof().name)}”</b><span>Add a remote desktop (RDP), SSH server, or file server (SFTP/FTP).</span><button class="btn pri" data-act="newm">${ic('plus')}Add machine</button></div>`}
    <div class="hints"><span><span class="kbd">Ctrl K</span>palette</span><span><span class="kbd">Alt 1–9</span>session</span><span><span class="kbd">Ctrl Alt ← →</span>switch from inside RDP</span><span><span class="kbd">Ctrl Alt Break</span>full screen</span></div>
  </div></div>`;
}
function overviewHTML() {
  return `<div class="ov"><div class="ov-h"><h2>Sessions <em>${S.sessions.length}</em></h2><span class="muted">Drop a file on a tile to send it to that session.</span></div>
    <div class="ov-g">${S.sessions.map(s => {
      const m = machOf(s), img = S.thumbs[s.id];
      return `<div class="tile ${s.id === S.active ? 'cur' : ''}" data-act="tab" data-id="${s.id}" data-dropto="${s.id}">
        <div class="thumb" style="aspect-ratio:16/9">${img ? `<img class="thumbimg" src="${img}" alt="">` : `<div class="thumbph" style="--h:${m?.hue ?? 220}">${ic(m ? mIcon(m) : 'monitor')}</div>`}</div>
        <div class="cap"><span class="dot" style="background:${s.state === 'connected' ? 'var(--ok)' : s.state === 'disconnected' ? 'var(--err)' : 'var(--warn)'}"></span><b>${esc(m?.name)}</b><small>${PROTO[m?.proto]} · ${esc(m?.host)}</small><span class="rtt" data-rtt="${s.id}">${s.rtt > 0 ? s.rtt + ' ms' : ''}</span>
        <button class="icon-btn sm" data-act="close" data-id="${s.id}" title="Disconnect">${ic('x')}</button></div>
      </div>`; }).join('')}
      <button class="tile add" data-act="palette">${ic('plus')}<span>New connection</span></button>
    </div></div>`;
}

/* ---------- SSH terminals (kept alive across renders) ---------- */
const TERMS = {}, PENDING = {}; // output that arrived before the terminal was first shown
const termTheme = () => S.D.theme === 'light'
  ? {background:'#fbfbfc', foreground:'#1d1f24', cursor:'#4a6cf7', selectionBackground:'#4a6cf744'}
  : {background:'#0b0c0e', foreground:'#d7dae0', cursor:'#6b8afd', selectionBackground:'#6b8afd55'};
function termFor(sid) {
  if (TERMS[sid]) return TERMS[sid];
  const el = document.createElement('div');
  el.className = 'term';
  $('#termHost').append(el);
  const term = new Terminal({fontFamily:'"Cascadia Code", "Cascadia Mono", Consolas, monospace', fontSize:14, cursorBlink:true, scrollback:20000, theme:termTheme()});
  const fit = new FitAddon.FitAddon();
  term.loadAddon(fit);
  term.open(el);
  if (PENDING[sid]) { term.write(PENDING[sid]); delete PENDING[sid]; }
  term.onData(d => post({t:'termIn', sid, d}));
  term.onResize(({cols, rows}) => post({t:'termSize', sid, cols, rows}));
  // Ctrl+C stays ^C for the shell; copy/paste use Ctrl+Shift+C/V (Windows Terminal convention)
  term.attachCustomKeyEventHandler(e => {
    if (e.type !== 'keydown' || !e.ctrlKey || !e.shiftKey) return true;
    if (e.code === 'KeyC') { const t = term.getSelection(); if (t) navigator.clipboard.writeText(t); return false; }
    if (e.code === 'KeyV') { navigator.clipboard.readText().then(t => term.paste(t)); return false; }
    return true;
  });
  return (TERMS[sid] = {term, fit, el});
}
new ResizeObserver(() => { const s = act(); if (s?.kind === 'ssh' && TERMS[s.id] && !$('#termHost').hidden) TERMS[s.id].fit.fit(); }).observe($('#termHost'));

/* ---------- file panes ---------- */
const newPane = loc => ({loc, path:null, items:null, err:null, loading:false, req:0, sel:new Set()});
function listPane(k, cd) {
  const p = S.panes[k];
  p.loading = true; p.req = ++S.reqN;
  post({t:'list', req:p.req, k, loc:p.loc, path:p.path, cd: cd ?? null});
}
function paneLocs() { return [['host', 'This computer'], ...live().map(s => [s.id, `${machOf(s)?.name} (${PROTO[machOf(s)?.proto]})`])]; }
function paneHTML(k) {
  const p = S.panes[k], locs = paneLocs();
  const loc = p.loc, osI = loc === 'host' ? 'monitor' : mIcon(machOf(sess(loc)) || {proto:'rdp'});
  const items = p.items || [];
  const total = items.reduce((a, f) => a + f.size, 0);
  return `<div class="pane"><div class="pane-h"><span class="os">${ic(osI)}</span>
    <select class="in sm" data-pane="${k}">${locs.map(([v, l]) => `<option value="${v}" ${v === loc ? 'selected' : ''}>${esc(l)}</option>`).join('')}</select>
    <small>${items.length} · ${fmt(total)}</small></div>
    <div class="pane-p"><button class="icon-btn sm" data-act="pane-up" data-k="${k}" title="Up">${ic('up')}</button><span class="path" title="${esc(p.path)}">&lrm;${esc(p.path ?? '…')}</span>
      <button class="icon-btn sm" data-act="pane-mkdir" data-k="${k}" title="New folder">${ic('newfolder')}</button>
      <button class="icon-btn sm" data-act="pane-del" data-k="${k}" title="Delete selected" ${p.sel.size ? '' : 'disabled'}>${ic('trash')}</button>
      <button class="icon-btn sm" data-act="pane-refresh" data-k="${k}" title="Refresh">${ic('refresh')}</button></div>
    <div class="plist" data-dropto="${loc}" data-k="${k}">${p.err ? `<div class="perr">${esc(p.err)}</div>` : p.loading && !p.items ? '<div class="pempty"><span class="sp"></span></div>'
      : items.map(f => `<div class="frow ${p.sel.has(f.name) ? 'sel' : ''}" draggable="true" data-drag="${k}" data-name="${esc(f.name)}" data-act="sel" data-k="${k}" ${f.dir ? `data-dir="1"` : ''} title="${esc(f.name)}">${f.dir ? `<span class="fi">${ic('folder')}</span>` : glyph(f.name)}<span class="fn">${esc(f.name)}</span><small>${f.dir ? '' : fmt(f.size)}</small></div>`).join('')
      || '<div class="pempty">Empty — drop files here<br>(also from Windows Explorer)</div>'}</div></div>`;
}
function midButtons(a, b, vertical) {
  const verb = S.moveMode ? 'Move' : 'Copy';
  const na = S.panes[a]?.sel.size || 0, nb = S.panes[b]?.sel.size || 0;
  return `<div class="seg"><button class="${S.moveMode ? '' : 'on'}" data-act="movemode" data-v="0">Copy</button><button class="${S.moveMode ? 'on' : ''}" data-act="movemode" data-v="1">Move</button></div>
    <button class="btn sm" data-act="xfer" data-v="${a}${b}" ${na ? '' : 'disabled'}>${ic(vertical ? 'arrowright' : 'arrowdown')}${verb}${na ? ` (${na})` : ''}</button>
    <button class="icon-btn" data-act="swap" data-v="${a}${b}" title="Swap panes">${ic('swap')}</button>
    <button class="btn sm" data-act="xfer" data-v="${b}${a}" ${nb ? '' : 'disabled'}>${ic(vertical ? 'arrowleft' : 'arrowup')}${verb}${nb ? ` (${nb})` : ''}</button>`;
}
function queueHTML() {
  const list = [...S.transfers.values()].sort((a, b) => b.id - a.id);
  if (!list.length) return '<div class="phint">Drag files between panes, onto a session tab, a tile in the overview, or straight from Explorer onto the remote desktop. <span class="kbd">Shift</span> while dropping = move.</div>';
  return `<div class="queue"><div class="qh"><span>Transfers</span>${list.some(t => t.state !== 'run') ? '<button data-act="qclear">Clear finished</button>' : ''}</div>
    ${list.map(t => `<div class="qi ${t.state}" data-tp="${t.id}"><div class="qt">${glyph(t.name)}<div class="qm"><b>${esc(t.name)}</b><span>${esc(t.from)} ${ic('arrowright')} ${esc(t.to)}${t.move ? ' · move' : ''}</span></div>
      ${t.state === 'run' ? `<button class="icon-btn sm x" data-act="cancel" data-id="${t.id}" title="Cancel">${ic('x')}</button>` : ''}</div>
      <div class="qbar"><i style="width:${t.total ? Math.min(100, t.done / t.total * 100) : 0}%"></i></div><small>${xferText(t)}</small></div>`).join('')}</div>`;
}
function xferText(t) {
  if (t.state === 'done') return 'Done · ' + fmt(t.total);
  if (t.state === 'fail') return 'Error: ' + (t.error || '');
  if (t.state === 'cancel') return 'Cancelled';
  if (!t.total) return 'Preparing…';
  const sp = t.done / Math.max(.2, t.ms / 1000), eta = (t.total - t.done) / Math.max(1, sp);
  return `${fmt(t.done)} of ${fmt(t.total)} · ${fmt(sp)}/s · ${eta < 1 ? '<1' : Math.ceil(eta)} s`;
}
function paintTransfers() {
  const run = [...S.transfers.values()].filter(t => t.state === 'run'), ind = $('#xferInd');
  ind.hidden = !run.length;
  if (run.length) {
    const all = run.reduce((a, t) => a + t.total, 0), dn = run.reduce((a, t) => a + t.done, 0);
    ind.style.setProperty('--p', (all ? dn / all * 100 : 0) + '%');
    ind.querySelector('b').textContent = run.length;
  }
  for (const t of run) {
    for (const el of $$(`[data-tp="${t.id}"]`)) {
      el.querySelector('.qbar i').style.width = (t.total ? Math.min(100, t.done / t.total * 100) : 0) + '%';
      el.querySelector('small').textContent = xferText(t);
    }
  }
}
function xferPanes(fk, tk) {
  const f = S.panes[fk], t = S.panes[tk];
  if (f.loc === t.loc && f.path === t.path) return toast('Source and target are the same folder', {kind:'err'});
  const names = [...f.sel];
  if (!names.length) return;
  f.sel.clear();
  post({t:'xfer', from:{loc:f.loc, path:f.path}, to:{loc:t.loc, path:t.path}, names, move:S.moveMode});
  rerenderFiles();
}
function rerenderFiles() {
  if (S.panel === 'files') renderPanel();
  if (act()?.kind === 'files' && S.view === 'session') renderStage();
}

/* ---------- side panel ---------- */
function renderPanel() {
  const p = $('#panel');
  $$('[data-act=panel]').forEach(b => b.classList.toggle('on', b.dataset.v === S.panel));
  if (!S.panel) { p.hidden = true; p.innerHTML = ''; return; }
  p.hidden = false;
  if (S.panel === 'files') {
    const locs = paneLocs().map(([v]) => v);
    S.panes.A ||= newPane('host');
    const s = act();
    if (!S.panes.B || !locs.includes(S.panes.B.loc)) S.panes.B = newPane(s?.state === 'connected' ? s.id : locs.find(l => l !== S.panes.A.loc) || 'host');
    if (!locs.includes(S.panes.A.loc)) S.panes.A = newPane('host');
    p.innerHTML = `<div class="ph"><h3>Files</h3><button class="icon-btn" data-act="panel" data-v="files" title="Close">${ic('x')}</button></div>
      ${paneHTML('A')}<div class="pmid">${midButtons('A', 'B', false)}</div>${paneHTML('B')}${queueHTML()}`;
    for (const k of ['A', 'B']) if (!S.panes[k].items && !S.panes[k].loading) listPane(k);
  } else p.innerHTML = clipHTML();
  paintTransfers();
}
function clipHTML() {
  const list = [...S.clip].sort((a, b) => (!!b.pinned - !!a.pinned) || b.t - a.t);
  const ent = c => {
    const m = c.src === 'host' ? null : mach(c.src);
    return `<div class="cent ${c.pinned ? 'pinned' : ''}" data-act="clip-use" data-id="${c.id}" title="${c.reusable ? 'Click to set as the current clipboard (all sessions)' : 'Files from a session — copy again in the source session'}">
      <div class="ch"><span class="src ${m ? '' : 'host'}" style="--h:${m?.hue ?? 0}"><span class="dot"></span>${esc(srcName(c.src))}</span><small>${ago(c.t)}</small>
        <span class="acts"><button class="icon-btn sm" data-act="clip-pin" data-id="${c.id}" title="${c.pinned ? 'Unpin' : 'Pin'}">${ic('pin')}</button><button class="icon-btn sm" data-act="clip-del" data-id="${c.id}" title="Remove from history">${ic('trash')}</button></span></div>
      ${c.type === 'img' ? `${c.img ? `<img src="${c.img}" alt="">` : ''}<small>${esc(c.text)}</small>` : c.type === 'files' ? `<div class="names">${(c.names || []).slice(0, 6).map(esc).join('<br>')}${(c.names || []).length > 6 ? `<br>… +${c.names.length - 6}` : ''}</div>` : `<pre>${esc(c.text)}</pre>`}</div>`;
  };
  return `<div class="ph"><h3>Clipboard</h3><button class="icon-btn" data-act="panel" data-v="clip" title="Close">${ic('x')}</button></div>
    <div class="clist"><div class="sec">History · shared by the host and all sessions</div>${list.map(ent).join('') || '<div class="empty">Copy something on this computer or in any session.</div>'}</div>
    <div class="phint">The RDP clipboard covers text, images and files in both directions: <span class="kbd">Ctrl C</span> in one session, <span class="kbd">Ctrl V</span> in another. Vault passwords are not added to history. You can turn off syncing in machine settings → Resources.</div>`;
}

/* ---------- vault ---------- */
function vaultHTML() {
  return `<div class="modal vault"><div class="mh"><div class="row"><span class="vic">${ic('shield')}</span><div style="flex:1"><h2>Password vault</h2><small class="muted">${isLinux() ? 'Encrypted with AES-256-GCM, key in the desktop keyring · view and copy after polkit authentication' : 'Encrypted with DPAPI (your Windows account only) · view and copy after Windows Hello verification or your Windows password'}</small></div><button class="icon-btn" data-act="x">${ic('x')}</button></div></div>
    <div class="mb">${S.D.creds.map(c => {
      const used = S.D.machines.filter(m => m.cred === c.id).length, shown = c.id in S.reveal;
      return `<div class="vr"><span class="os">${ic('key')}</span><div class="vm"><b>${esc(c.label || c.user)}</b><small><span class="mono">${esc(c.user)}</span> · ${used} ${plural(used, 'machine', 'machines', 'machines')}</small></div>
        <div class="vpw"><span class="mono pw">${shown ? esc(S.reveal[c.id]) : '••••••••••••'}</span>
        <button class="icon-btn sm" data-act="v-reveal" data-id="${c.id}" title="${shown ? 'Hide' : 'Show'}">${ic(shown ? 'eyeoff' : 'eye')}</button>
        <button class="icon-btn sm" data-act="v-copy" data-id="${c.id}" title="Copy password (clears after 30 s)">${ic('copy')}</button>
        <button class="icon-btn sm danger" data-act="v-del" data-id="${c.id}" title="Delete">${ic('trash')}</button></div></div>`; }).join('') || '<div class="empty">The vault is empty — add the first credentials below.</div>'}</div>
    <form class="mf vadd" data-form="vadd"><input class="in" name="label" placeholder="Name (e.g. Admin prod)"><input class="in" name="user" placeholder="DOMAIN\\user" required><input class="in" name="pass" type="password" placeholder="Password / passphrase" autocomplete="new-password"><button class="btn pri">${ic('plus')}Add</button></form></div>`;
}
const openVault = () => openLayer('vault', vaultHTML());
const refreshVault = () => { const l = $('[data-layer=vault]'); if (l) l.innerHTML = vaultHTML(); };

/* ---------- machine editor ---------- */
const DEF = {app:'', appArgs:'', relMouse:false, proto:'rdp', port:3389, sshPort:22, ftps:true, os:'win', scale:'dynamic', mon:'1', depth:32, hidpi:true, clip:true, drives:true, audio:true, mic:false, print:false, smart:false, cam:false, usb:false, admin:false, auth:'', lowlat:true, rawin:false, certCheck:true, gateway:'', mac:'', share:'', keyFile:'', fav:false, cred:null, group:'No group'};
function openEditor(id, preset) {
  const m = id ? mach(id) : {...DEF, id:'', name:'', host:'', profile:S.D.profile, cred:S.D.creds[0]?.id ?? null, hue:Math.floor(Math.random() * 360), ...preset};
  if (!m) return;
  const groups = [...new Set(S.D.machines.map(x => x.group))];
  const sel = (n, opts, v) => `<select class="in" name="${n}">${opts.map(([o, l]) => `<option value="${esc(o)}" ${String(o) === String(v ?? '') ? 'selected' : ''}>${esc(l)}</option>`).join('')}</select>`;
  const sw = (n, l, d) => `<label class="sw"><input type="checkbox" name="${n}" ${m[n] ? 'checked' : ''}><i></i><span>${l}<small>${d}</small></span></label>`;
  const el = openLayer('ed', `<form class="modal ed" data-form="ed" data-id="${esc(m.id)}" data-proto="${m.proto}" data-os="${m.os}" novalidate>
    <div class="mh"><div class="row"><h2>${id ? 'Edit · ' + esc(m.name) : 'New machine'}</h2><button type="button" class="icon-btn" data-act="x">${ic('x')}</button></div>
      <div class="seg">${[['conn','Connection'],['disp','Display'],['res','Resources'],['perf','Performance']].map(([k, l], i) => `<button type="button" class="${i ? '' : 'on'}" data-act="ed-tab" data-v="${k}">${l}</button>`).join('')}</div></div>
    <div class="mb">
      <section class="fgrid" data-sec="conn">
        <div class="fld c6"><span>Connection type</span><div class="radios" style="grid-template-columns:repeat(4,1fr)">${[['rdp','Desktop (RDP)','Windows, xrdp'],['ssh','SSH','Terminal + SFTP'],['sftp','SFTP','Files over SSH'],['ftp','FTP / FTPS','File server']].map(([v, l, d]) => `<label class="rc"><input type="radio" name="proto" value="${v}" ${m.proto === v ? 'checked' : ''}><b>${l}</b><small>${d}</small></label>`).join('')}</div></div>
        <div class="fld c4"><span>Name</span><input class="in" name="name" value="${esc(m.name)}" placeholder="e.g. srv-app-01"></div>
        <div class="fld c2" data-p="rdp"><span>System</span>${sel('os', [['win','Windows'],['lnx','Linux (xrdp)']], m.os)}</div>
        <div class="fld c2" data-p="ssh sftp ftp"><span>Group</span><input class="in" name="group2" list="grps" value="${esc(m.group)}"></div>
        <div class="fld c4" style="display:flex"><span>Host / IP</span><input class="in mono" name="host" value="${esc(m.host)}" placeholder="10.0.0.5 or host.domain"></div>
        <div class="fld c2" style="display:flex"><span>Port</span><input class="in mono" name="port" type="number" min="1" max="65535" value="${m.port}"></div>
        <div class="fld c3" style="display:flex"><span>Profile</span>${sel('profile', S.D.profiles.map(p => [p.id, p.name]), m.profile)}</div>
        <div class="fld c3" data-p="rdp"><span>Group</span><input class="in" name="group" list="grps" value="${esc(m.group)}" placeholder="e.g. Production"><datalist id="grps">${groups.map(g => `<option value="${esc(g)}">`).join('')}</datalist></div>
        <div class="fld c3" data-p="ssh sftp ftp"></div>
        <div class="fld c6" style="display:flex"><span>Credentials from vault</span>${sel('cred', [['', m.proto === 'ftp' ? 'Anonymous' : 'Ask when connecting (RDP only)'], ...S.D.creds.map(c => [c.id, `${c.label || c.user} — ${c.user}`])], m.cred)}</div>
        <div class="fld c6" data-p="ssh sftp"><span>Private key (optional; vault password = passphrase)</span><input class="in mono" name="keyFile" value="${esc(m.keyFile)}" placeholder="C:\\Users\\alice\\.ssh\\id_ed25519"></div>
        <div class="fld c6" data-p="ftp"><label class="sw"><input type="checkbox" name="ftps" ${m.ftps ? 'checked' : ''}><i></i><span>Explicit TLS (FTPS)<small>Encrypts password and data. Disable only for old servers.</small></span></label></div>
        <div class="fld c3" data-p="rdp"><span>RemoteApp (optional)</span><input class="in mono" name="app" value="${esc(m.app)}" placeholder="empty = desktop · e.g. notepad.exe, ||Excel"></div>
        <div class="fld c3" data-p="rdp"><span>RemoteApp arguments</span><input class="in mono" name="appArgs" value="${esc(m.appArgs)}" placeholder="e.g. C:\\report.txt"></div>
        <div class="fld c3" data-p="rdp"><span>RD Gateway (optional)</span><input class="in mono" name="gateway" value="${esc(m.gateway)}" placeholder="gw.company.com"></div>
        <div class="fld c3" data-p="rdp" data-os-only="win"><span>SMB share for files</span><input class="in mono" name="share" value="${esc(m.share)}" placeholder="auto: \\\\host\\C$\\Users\\…\\Desktop"></div>
        <div class="fld c3" data-p="rdp" data-os-only="lnx"><span>SFTP port for files</span><input class="in mono" name="sshPort" type="number" min="1" max="65535" value="${m.sshPort}"></div>
        <div class="fld c3" style="display:flex"><span>MAC (Wake-on-LAN)</span><input class="in mono" name="mac" value="${esc(m.mac)}" placeholder="AA:BB:CC:DD:EE:FF"></div>
        <div class="c3" style="align-self:end">${sw('fav', 'Favorite', 'At the top of the list')}</div>
      </section>
      <section class="fgrid" data-sec="disp" hidden>
        <div class="fld c6"><span>Scaling</span><div class="radios">${[['dynamic','Fit','The host changes its resolution to fit the window — sharp at any size, including outside full screen.'],['fit','Scale','Fixed 1920×1080, image scaled to the window.'],['native','1:1','No scaling, with scrolling.']].map(([v, l, d]) => `<label class="rc"><input type="radio" name="scale" value="${v}" ${m.scale === v ? 'checked' : ''}><b>${l}</b><small>${d}</small></label>`).join('')}</div></div>
        <div class="fld c3"><span>Monitors (full screen)</span>${sel('mon', [['1','Single monitor'],['all','All monitors']], m.mon)}</div>
        <div class="fld c3"><span>Color depth</span>${sel('depth', [['32','32-bit'],['24','24-bit'],['16','16-bit']], m.depth)}</div>
        <div class="c6">${sw('hidpi', 'Pass through DPI scaling', 'The session gets this screen scale (e.g. 125%), so text is the right size')}</div>
      </section>
      <section class="sws" data-sec="res" hidden>
        ${sw('clip', 'Clipboard', 'Text, images and files — shared between the host and sessions')}${sw('drives', 'Host drives', 'Drives of this computer visible in the session (\\tsclient)')}
        ${sw('audio', 'Audio', 'Play on this computer')}${sw('mic', 'Microphone', 'Redirect to the session')}
        ${sw('print', 'Printers', 'Local printers in the session')}${sw('smart', 'Smart cards', 'Smart card / keys')}
        ${sw('cam', 'Cameras', 'Cameras of this computer in the session (H.264, high quality)')}${sw('usb', 'Devices and ports', 'Phones, cameras (MTP), COM/LPT ports, POS — including devices plugged in later')}
      </section>
      <section class="fgrid" data-sec="perf" hidden>
        <div class="c6">${sw('lowlat', 'Low-latency mode', 'LAN profile: maximum frame rate, no adaptive throttling. Disable on weak links.')}</div>
        <div class="c6">${sw('rawin', 'Windows shortcuts always to session', 'Win, Alt+Tab, etc. go to the session even in a window (by default only in full screen)')}</div>
        <div class="c6">${sw('relMouse', 'Relative mouse (games, 3D)', 'The session gets mouse movement instead of position — the camera in games and 3D apps turns endlessly. Requires Windows 11 24H2 / Server 2025 on the host.')}</div>
        <div class="c6">${sw('admin', 'Administrative session', 'Server console (/admin), bypasses the RDS session limit')}</div>
        <div class="fld c6"><span>Credential protection</span>${sel('auth', [['','Standard logon (NLA)'],['restricted','Restricted Admin — password is not sent to the host'],['rcg','Remote Credential Guard — Kerberos stays here']], m.auth)}</div>
        <div class="c6">${sw('certCheck', 'Verify server certificate', 'Warns about untrusted certificates. Disable only for trusted hosts with a self-signed certificate (e.g. xrdp in a homelab).')}</div>
        <div class="note">${ic('info')}<span>The cursor is drawn locally (0 ms), input is sent without queuing, the image is decoded by the GPU (AVC444/AVC420 negotiated automatically), UDP transport when the host allows it.</span></div>
      </section>
    </div>
    <div class="mf">${id ? `<button type="button" class="btn danger" data-act="ed-del" data-id="${id}">${ic('trash')}<span>Delete</span></button>` : ''}
      ${id && m.proto === 'rdp' ? `<button type="button" class="btn" data-act="rdp-export" data-id="${id}" title="Save as .rdp file (mstsc, Microsoft Remote Desktop)">Export .rdp</button>` : ''}
      ${id ? '' : `<button type="button" class="btn" data-act="rdp-import" title="Load a .rdp file from mstsc or RD Web">Import .rdp</button>`}<span class="grow"></span>
      <button type="button" class="btn" data-act="x">Cancel</button><button class="btn pri" type="submit">${id ? 'Save' : 'Add and connect'}</button></div>
  </form>`);
  $('[name=name]', el).focus();
}
function edTab(v) {
  const l = $('[data-layer=ed]'); if (!l) return;
  $$('[data-sec]', l).forEach(s => s.hidden = s.dataset.sec !== v);
  $$('[data-act=ed-tab]', l).forEach(b => b.classList.toggle('on', b.dataset.v === v));
}
function saveEditor(f, fd) {
  const host = String(fd.get('host')).trim();
  if (!host) { edTab('conn'); const h = $('[name=host]', f); h.classList.add('bad'); h.focus(); return; }
  const id = f.dataset.id;
  const m = {...DEF, ...(id ? mach(id) : {hue:Math.floor(Math.random() * 360)})};
  m.id = id || uid();
  m.proto = fd.get('proto') || 'rdp';
  for (const k of ['name','os','profile','cred','gateway','scale','mon','share','mac','keyFile','auth','app','appArgs']) if (fd.has(k)) m[k] = String(fd.get(k)).trim();
  for (const k of ['fav','hidpi','clip','drives','audio','mic','print','smart','cam','usb','admin','lowlat','rawin','ftps','certCheck','relMouse']) m[k] = fd.has(k);
  m.group = String(fd.get(m.proto === 'rdp' ? 'group' : 'group2') || '').trim();
  m.host = host; m.port = +fd.get('port') || DEF_PORT[m.proto]; m.sshPort = +fd.get('sshPort') || 22; m.depth = +fd.get('depth') || 32;
  m.cred = m.cred || null;
  closeLayer('ed');
  post({t:'saveMachine', m, connect:!id});
  if (id) toast(`Saved ${m.name || host}${S.sessions.some(s => s.mid === id) ? ' · changes take effect after reconnecting' : ''}`);
}

/* .rdp files: read here (UTF-16 from mstsc or UTF-8), parsed by the host */
function importRdp(files) {
  const go = list => [...list].forEach(f => f.arrayBuffer().then(b => {
    const u = new Uint8Array(b), utf16 = u[0] === 0xFF && u[1] === 0xFE || u[1] === 0;
    post({t:'importRdp', name:f.name, text:new TextDecoder(utf16 ? 'utf-16le' : 'utf-8').decode(b)});
  }));
  if (files) return go(files);
  const inp = document.createElement('input');
  inp.type = 'file'; inp.accept = '.rdp'; inp.multiple = true;
  inp.onchange = () => { closeLayer('ed'); go(inp.files); };
  inp.click();
}

/* ---------- palette ---------- */
function openPalette() {
  S.palIdx = 0;
  openLayer('pal', `<div class="modal pal"><div class="pal-in">${ic('search')}<input id="palq" placeholder="Connect to a machine, type host / ssh://user@host / ftp://host or a command…" autocomplete="off"><span class="kbd">Esc</span></div><div class="pal-l" id="pall"></div></div>`, 'top');
  $('#palq').focus(); palRender();
}
function parseQuick(raw) {
  const q = raw.trim();
  const m = q.match(/^(?:(rdp|ssh|sftp|ftp):\/\/)?(?:([^@\s/]+)@)?([\w.-]+|\[[0-9a-f:]+\])(?::(\d+))?\/?$/i);
  if (!m) return null;
  const proto = (m[1] || 'rdp').toLowerCase();
  return {proto, user:m[2] || '', host:m[3], port:+m[4] || DEF_PORT[proto]};
}
function palItems(raw) {
  const q = raw.trim().toLowerCase(), items = [];
  const pq = q && parseQuick(raw);
  if (pq && /[.:\d]|:\/\//.test(q) && !S.D.machines.some(m => m.host.toLowerCase() === pq.host.toLowerCase() && m.proto === pq.proto))
    items.push({h:'Quick connection', icon:'bolt', label:`Connect: ${raw.trim()}`, sub:`${PROTO[pq.proto]} · save to profile`, run:() => quickConnect(raw)});
  S.D.machines.filter(m => !q || `${m.name} ${m.host} ${m.group} ${m.proto}`.toLowerCase().includes(q))
    .sort((a, b) => (b.profile === S.D.profile) - (a.profile === S.D.profile))
    .forEach(m => items.push({h:'Machines', icon:mIcon(m), label:m.name, sub:`${PROTO[m.proto]} · ${m.host} · ${prof(m.profile).name}`, live:S.sessions.some(s => s.mid === m.id), run:() => connect(m.id)}));
  [['New machine','plus',() => openEditor(),''],['Import .rdp file','plus',() => importRdp(),''],['Session overview','grid',() => ACT.overview(),'Ctrl Shift O'],['Files and transfer','folder',() => { S.panel = 'files'; renderPanel(); },''],
   ['Shared clipboard','clip',() => { S.panel = 'clip'; renderPanel(); },''],['Password vault','key',openVault,''],['Toggle theme','moon',() => ACT.theme(),''],['Sidebar','sidebar',() => ACT.sidebar(),'Ctrl B']]
    .filter(([l]) => !q || l.toLowerCase().includes(q)).forEach(([label, icon, run, sub]) => items.push({h:'Actions', icon, label, sub, run}));
  return items;
}
function palRender() {
  const q = $('#palq'); if (!q) return;
  S.palItems = palItems(q.value);
  S.palIdx = Math.min(S.palIdx, Math.max(0, S.palItems.length - 1));
  let h = '';
  $('#pall').innerHTML = S.palItems.map((it, i) => (it.h !== h ? `<div class="ph2">${(h = it.h)}</div>` : '') +
    `<div class="pi ${i === S.palIdx ? 'on' : ''}" data-act="pal" data-id="${i}">${ic(it.icon)}<span>${esc(it.label)}</span>${it.live ? '<span class="st live"></span>' : ''}<small>${esc(it.sub)}</small></div>`).join('')
    || '<div class="empty">Nothing found</div>';
  $('#pall .pi.on')?.scrollIntoView({block:'nearest'});
}
function runPal(i) { const it = S.palItems[i]; if (!it) return; closeLayer('pal'); it.run(); }

/* ---------- actions ---------- */
function connect(id) {
  const m = mach(id); if (!m) return;
  if (S.reach[id] === false && !S.sessions.some(s => s.mid === id) && m.mac)
    toast(`${m.name} does not respond`, {kind:'err', action:{label:'Wake (WoL)', fn:() => post({t:'wake', id})}});
  S.view = 'session';
  post({t:'connect', id});
}
function quickConnect(raw) {
  const pq = parseQuick(String(raw || ''));
  if (!pq) return toast('Address not recognized. Examples: 10.0.0.5, host:3390, ssh://root@host, ftp://host', {kind:'err'});
  const hit = S.D.machines.find(m => m.proto === pq.proto && (m.host.toLowerCase() === pq.host.toLowerCase() || m.name.toLowerCase() === pq.host.toLowerCase()) && m.port === pq.port);
  if (hit) return connect(hit.id);
  if (pq.proto !== 'rdp' && pq.proto !== 'ftp' && !S.D.creds.length)
    return openEditor(null, {proto:pq.proto, host:pq.host, port:pq.port, name:pq.host, group:'Quick connections'});
  const cred = pq.user ? S.D.creds.find(c => c.user.toLowerCase().endsWith(pq.user.toLowerCase())) : null;
  if (pq.user && !cred) return openEditor(null, {proto:pq.proto, host:pq.host, port:pq.port, name:pq.host, group:'Quick connections'}), toast(`Add account “${pq.user}” to the vault, then connect`, {kind:'err'});
  const m = {...DEF, id:uid(), name:pq.host, host:pq.host, port:pq.port, proto:pq.proto, profile:S.D.profile, group:'Quick connections', cred:cred?.id ?? (pq.proto === 'rdp' || pq.proto === 'ftp' ? null : S.D.creds[0]?.id ?? null), hue:Math.floor(Math.random() * 360)};
  S.view = 'session';
  post({t:'saveMachine', m, connect:true});
  toast(`Saved ${pq.host} to profile ${prof().name}`, {action:{label:'Edit', fn:() => openEditor(m.id)}});
}
const ACT = {
  connect: id => connect(id),
  tab: id => { S.view = 'session'; post({t:'activate', sid:id}); },
  close: id => post({t:'close', sid:id}),
  reconnect: id => post({t:'reconnect', sid:id}),
  edit: id => openEditor(id),
  newm: () => openEditor(),
  overview: () => { if (!S.sessions.length) return toast('No open sessions', {icon:'info'}); S.view = S.view === 'overview' ? 'session' : 'overview'; renderAll(); },
  panel: (id, v) => { S.panel = S.panel === v ? null : v; renderPanel(); renderSbar(); },
  panelopen: (id, v) => { S.panel = v; renderPanel(); renderSbar(); },
  theme: () => { S.D.theme = S.D.theme === 'dark' ? 'light' : 'dark'; applyTheme(); post({t:'theme', v:S.D.theme}); },
  sidebar: () => { S.D.sidebar = !S.D.sidebar; applyTheme(); post({t:'sidebar', v:S.D.sidebar}); },
  vault: () => openVault(),
  palette: () => openPalette(),
  scale: (id, v) => post({t:'scale', sid:S.active, v}),
  fullscreen: () => post({t:'fullscreen', sid:S.active}),
  cad: () => post({t:'cad', sid:S.active}),
  profmenu: () => toggleProfMenu(),
  profile: id => { $('#profMenu').hidden = true; S.D.profile = id; post({t:'selectProfile', id}); renderSidebar(); if (!act()) renderStage(); },
  profnew: () => {
    $('#profMenu').hidden = true;
    const el = openLayer('prompt', `<form class="modal pm" data-form="prompt"><div class="mh"><div class="row"><h2>New profile</h2></div></div><div class="mb"><input class="in" name="v" placeholder="e.g. Client ACME" required></div><div class="mf"><span class="grow"></span><button type="button" class="btn" data-act="x">Cancel</button><button class="btn pri">Create</button></div></form>`);
    $('input', el).focus();
  },
  group: (id, v, a) => { const g = a.dataset.g; S.collapsed.has(g) ? S.collapsed.delete(g) : S.collapsed.add(g); renderList(); },
  sel: (id, v, a, e) => {
    const p = S.panes[a.dataset.k], n = a.dataset.name;
    if (e.detail === 2 && a.dataset.dir) { p.sel.clear(); p.items = null; listPane(a.dataset.k, n); return rerenderFiles(); }
    p.sel.has(n) ? p.sel.delete(n) : p.sel.add(n);
    rerenderFiles();
  },
  'pane-up': (id, v, a) => { const k = a.dataset.k; S.panes[k].sel.clear(); listPane(k, '..'); },
  'pane-refresh': (id, v, a) => listPane(a.dataset.k),
  'pane-mkdir': (id, v, a) => {
    const k = a.dataset.k;
    const el = openLayer('prompt', `<form class="modal pm" data-form="mkdir" data-k="${k}"><div class="mh"><div class="row"><h2>New folder</h2></div></div><div class="mb"><input class="in" name="v" placeholder="Folder name" required></div><div class="mf"><span class="grow"></span><button type="button" class="btn" data-act="x">Cancel</button><button class="btn pri">Create</button></div></form>`);
    $('input', el).focus();
  },
  'pane-del': (id, v, a) => {
    const p = S.panes[a.dataset.k];
    if (!a.classList.contains('armed')) { a.classList.add('armed'); toast(`Click again to permanently delete ${p.sel.size} ${plural(p.sel.size, 'item', 'items', 'items')}`, {kind:'err', icon:'trash'}); setTimeout(() => a.classList.remove('armed'), 3000); return; }
    post({t:'delete', loc:p.loc, path:p.path, names:[...p.sel]}); p.sel.clear();
  },
  xfer: (id, v) => xferPanes(v[0], v[1]),
  swap: (id, v) => { const [a, b] = v; [S.panes[a], S.panes[b]] = [S.panes[b], S.panes[a]]; rerenderFiles(); },
  movemode: (id, v) => { S.moveMode = v === '1'; rerenderFiles(); },
  qclear: () => { for (const [k, t] of S.transfers) if (t.state !== 'run') S.transfers.delete(k); rerenderFiles(); },
  cancel: id => post({t:'cancel', id:+id}),
  'clip-use': id => post({t:'clipUse', id:+id}),
  'clip-pin': id => { const c = S.clip.find(x => x.id === +id); if (c) c.pinned = !c.pinned; renderPanel(); },
  'clip-del': id => { S.clip = S.clip.filter(x => x.id !== +id); renderPanel(); },
  'v-reveal': id => { if (id in S.reveal) { delete S.reveal[id]; return refreshVault(); } post({t:'revealCred', id}); },
  'v-copy': id => post({t:'copyCred', id}),
  'v-del': (id, v, a) => {
    if (!a.classList.contains('armed')) { a.classList.add('armed'); a.title = 'Click again to delete'; setTimeout(() => a.classList.remove('armed'), 3000); return; }
    post({t:'deleteCred', id});
  },
  'ed-tab': (id, v) => edTab(v),
  'ed-del': (id, v, a) => {
    if (!a.classList.contains('armed')) { a.classList.add('armed'); a.lastChild.textContent = 'Really delete?'; return; }
    closeLayer('ed'); post({t:'deleteMachine', id});
  },
  x: (id, v, a) => { a.closest('.layer')?.remove(); syncOcc(); },
  'rdp-export': id => post({t:'exportRdp', id}),
  'rdp-import': () => importRdp(),
  pal: id => runPal(+id),
};

/* ---------- host → UI ---------- */
const H = {
  data: m => {
    S.D = {profiles:m.profiles, machines:m.machines, creds:m.creds, profile:m.profile, theme:m.theme, sidebar:m.sidebar, platform:m.platform || 'win'};
    applyTheme(); renderSidebar(); renderTabs(); renderStage(); refreshVault();
  },
  sessions: m => {
    const prev = S.active;
    S.sessions = m.list; S.active = m.active;
    if (prev !== S.active && S.active) S.view = 'session';
    if (!S.sessions.length) S.view = 'session';
    for (const sid of Object.keys(TERMS)) if (!sess(sid)) { TERMS[sid].term.dispose(); TERMS[sid].el.remove(); delete TERMS[sid]; }
    for (const sid of Object.keys(PENDING)) if (!sess(sid)) delete PENDING[sid];
    for (const k of Object.keys(S.panes)) if (S.panes[k].loc !== 'host' && !live().some(s => s.id === S.panes[k].loc)) { if (k === 'Y') delete S.panes.Y; else S.panes[k] = newPane('host'); }
    renderTabs(); renderList(); renderStage(); if (S.panel === 'files') renderPanel();
  },
  net: m => {
    const s = sess(m.sid); if (!s) return;
    const first = !(s.rtt > 0) && m.rtt > 0;
    s.rtt = m.rtt;
    if (first) { renderSbar(); renderTabs(); } // the RTT chip only appears once the server reports it
    $$(`[data-rtt="${m.sid}"]`).forEach(el => el.textContent = m.rtt > 0 ? m.rtt + ' ms' : '');
  },
  reach: m => { S.reach = m.map; renderList(); if (!act() && S.view === 'session') renderStage(); },
  toast: m => toast(m.msg, {kind:m.kind}),
  clip: m => { S.clip.unshift(m.e); S.clip = S.clip.filter((c, i) => c.pinned || i < 40); if (S.panel === 'clip') renderPanel(); },
  cred: m => { S.reveal[m.id] = m.pass; refreshVault(); setTimeout(() => { delete S.reveal[m.id]; refreshVault(); }, 15000); },
  thumbs: m => { S.thumbs = m.map; if (S.view === 'overview') $('#stageMain').innerHTML = overviewHTML(); },
  list: m => {
    const p = S.panes[m.k];
    if (!p || p.req !== m.req) return;
    p.loading = false;
    if (m.error) { p.err = m.error; p.items = []; } else { p.err = null; p.items = m.items; p.path = m.path; }
    rerenderFiles();
  },
  changed: m => { for (const [k, p] of Object.entries(S.panes)) if (p.loc === m.loc && !p.loading) listPane(k); },
  xfers: m => {
    let fresh = false;
    for (const t of m.list) { if (!S.transfers.has(t.id) || S.transfers.get(t.id).state !== t.state) fresh = true; S.transfers.set(t.id, t); }
    if (fresh) rerenderFiles();
    paintTransfers();
  },
  update: m => toast(`Version ${m.version} available (you have ${m.current})`, {icon:'info', ms:20000, action:{label:'Update and restart', fn:() => post({t:'doUpdate'})}}),
  download: m => {
    const a = document.createElement('a');
    a.href = URL.createObjectURL(new Blob([m.text], {type:'application/x-rdp'}));
    a.download = m.name; a.click();
    setTimeout(() => URL.revokeObjectURL(a.href), 5000);
  },
  quit: () => { S.quit = true; window.close(); },
  term: m => { if (TERMS[m.sid]) TERMS[m.sid].term.write(m.d); else PENDING[m.sid] = (PENDING[m.sid] || '') + m.d; },
};
if (hasHost) onHost(d => H[d?.t]?.(d));

/* ---------- DOM events ---------- */
document.addEventListener('click', e => {
  if (!e.target.closest('.profwrap')) $('#profMenu').hidden = true;
  const a = e.target.closest('[data-act]');
  if (a && !a.disabled) ACT[a.dataset.act]?.(a.dataset.id, a.dataset.v, a, e);
});
document.addEventListener('auxclick', e => {
  const t = e.button === 1 && e.target.closest('.tab');
  if (t) { e.preventDefault(); post({t:'close', sid:t.dataset.id}); }
});
document.addEventListener('submit', e => {
  e.preventDefault();
  const f = e.target, fd = new FormData(f), v = String(fd.get('v') || '').trim();
  switch (f.dataset.form) {
    case 'quick': quickConnect(fd.get('q')); break;
    case 'prompt': if (v) { closeLayer('prompt'); post({t:'addProfile', name:v, color:['#6b8afd','#2fb67c','#e0a030','#e5484d','#8e5bd8','#26a0da'][S.D.profiles.length % 6]}); } break;
    case 'mkdir': if (v) { closeLayer('prompt'); const k = f.dataset.k, p = S.panes[k]; p.req = ++S.reqN; post({t:'mkdir', req:p.req, k, loc:p.loc, path:p.path, name:v}); } break;
    case 'vadd': post({t:'addCred', label:fd.get('label'), user:fd.get('user'), pass:fd.get('pass')}); f.reset(); toast('Added to vault', {icon:'lock'}); break;
    case 'ed': saveEditor(f, fd); break;
  }
});
document.addEventListener('change', e => {
  const t = e.target;
  if (t.dataset.pane) { S.panes[t.dataset.pane] = newPane(t.value); listPane(t.dataset.pane); rerenderFiles(); }
  const ed = t.closest('.ed');
  if (ed && t.name === 'proto') {
    const port = $('[name=port]', ed);
    if (Object.values(DEF_PORT).includes(+port.value)) port.value = DEF_PORT[t.value];
    ed.dataset.proto = t.value;
  }
  if (ed && t.name === 'os') ed.dataset.os = t.value;
});
document.addEventListener('input', e => {
  if (e.target.id === 'msearch') { S.search = e.target.value; renderList(); }
  else if (e.target.id === 'palq') { S.palIdx = 0; palRender(); }
  else if (e.target.classList.contains('bad')) e.target.classList.remove('bad');
});
document.addEventListener('keydown', e => {
  const k = e.key.toLowerCase();
  if (e.target.id === 'palq' && ['arrowdown', 'arrowup', 'enter'].includes(k)) {
    e.preventDefault();
    if (k === 'enter') return runPal(S.palIdx);
    S.palIdx = Math.max(0, Math.min(S.palItems.length - 1, S.palIdx + (k === 'arrowdown' ? 1 : -1))); return palRender();
  }
  if (e.target.closest?.('.xterm')) return; // the terminal owns its keys
  if (k === 'escape') { if (closeTop()) e.preventDefault(); return; }
  if (e.ctrlKey && !e.shiftKey && k === 'k') { e.preventDefault(); openPalette(); }
  else if (e.ctrlKey && e.shiftKey && k === 'o') { e.preventDefault(); ACT.overview(); }
  else if (e.ctrlKey && !e.shiftKey && k === 'b') { e.preventDefault(); ACT.sidebar(); }
  else if (e.altKey && /^[1-9]$/.test(e.key)) { const s = S.sessions[+e.key - 1]; if (s) { e.preventDefault(); ACT.tab(s.id); } }
  else if (k === 'enter' && e.target.matches('[data-act]:not(button)')) e.target.click();
});

/* drag & drop — targets carry data-dropto="host|<session id>"; files from Explorer go to the host with their real paths */
let dropEl = null;
const setDrop = t => { if (dropEl !== t) { dropEl?.classList.remove('drop'); t?.classList.add('drop'); dropEl = t; } };
const endDrag = () => { setDrop(null); document.body.classList.remove('dragging'); };
document.addEventListener('dragstart', e => {
  const el = e.target.closest?.('[data-drag]'); if (!el) return;
  const k = el.dataset.drag, p = S.panes[k], name = el.dataset.name;
  const names = p.sel.has(name) && p.sel.size > 1 ? [...p.sel] : [name];
  e.dataTransfer.setData('application/x-mr', JSON.stringify({loc:p.loc, path:p.path, names}));
  e.dataTransfer.effectAllowed = 'copyMove';
  document.body.classList.add('dragging');
});
document.addEventListener('dragenter', () => document.body.classList.add('dragging'));
document.addEventListener('dragover', e => {
  e.preventDefault();
  const t = e.target.closest?.('[data-dropto]');
  setDrop(t);
  e.dataTransfer.dropEffect = t ? (S.moveMode || e.shiftKey ? 'move' : 'copy') : 'none';
});
document.addEventListener('dragleave', e => { if (!e.relatedTarget) endDrag(); });
document.addEventListener('dragend', endDrag);
document.addEventListener('drop', e => {
  e.preventDefault();
  const t = e.target.closest?.('[data-dropto]');
  endDrag();
  if (!t) return;
  const loc = t.dataset.dropto, pane = t.dataset.k ? S.panes[t.dataset.k] : null;
  const to = {loc, path:pane ? pane.path : null}, move = S.moveMode || e.shiftKey;
  const raw = e.dataTransfer.getData('application/x-mr');
  if (raw) {
    const d = JSON.parse(raw);
    if (d.loc === loc && (!pane || d.path === pane.path)) return;
    for (const p of Object.values(S.panes)) if (p.loc === d.loc) p.sel.clear();
    post({t:'xfer', from:{loc:d.loc, path:d.path}, to, names:d.names, move});
    return;
  }
  if (!e.dataTransfer.files.length || !hasHost) return;
  const rdps = [...e.dataTransfer.files].filter(f => /\.rdp$/i.test(f.name));
  if (rdps.length && rdps.length === e.dataTransfer.files.length && !pane) return importRdp(rdps);
  if (WV) return WV.postMessageWithAdditionalObjects({t:'drop', to, move}, e.dataTransfer.files);
  // browser drop = file contents only: upload, the backend then copies them like any transfer
  const dirs = [...e.dataTransfer.items].filter(i => i.webkitGetAsEntry?.()?.isDirectory).length;
  if (dirs) toast('Drag folders through the file panel (This computer → session)', {kind:'err'});
  for (const f of e.dataTransfer.files) {
    if (!f.size && !f.type && dirs) continue;
    const q = new URLSearchParams({k:TOKEN, to:loc, path:to.path ?? '', name:f.name});
    fetch(`/upload?${q}`, {method:'POST', body:f}).then(r => { if (!r.ok) toast(`${f.name}: upload failed (${r.status})`, {kind:'err'}); }, err => toast(`${f.name}: ${err.message}`, {kind:'err'}));
  }
});

/* ---------- boot ---------- */
function renderAll() { renderTabs(); renderSidebar(); renderStage(); renderPanel(); paintTransfers(); requestAnimationFrame(sendStage); }
$$('[data-i]').forEach(el => el.insertAdjacentHTML('afterbegin', ic(el.dataset.i)));
new ResizeObserver(sendStage).observe($('#stage'));
addEventListener('resize', sendStage);
(function watchDpr() { matchMedia(`(resolution: ${devicePixelRatio}dppx)`).addEventListener('change', () => { sendStage(); watchDpr(); }, {once:true}); })();
setInterval(() => { if (S.panel === 'clip') $$('.cent .ch small').forEach((el, i) => { const c = [...S.clip].sort((a, b) => (!!b.pinned - !!a.pinned) || b.t - a.t)[i]; if (c) el.textContent = ago(c.t); }); }, 30000);
/* Linux: no clipboard events reach the backend, so read the clipboard whenever this window gets focus back */
let lastSeen = '';
async function seeClipboard() {
  if (WV || !isLinux() || !document.hasFocus() || !navigator.clipboard?.read) return;
  try {
    for (const item of await navigator.clipboard.read()) {
      const img = item.types.find(t => t.startsWith('image/'));
      if (img) {
        const blob = await item.getType(img);
        if (blob.size > 4e6) return;
        const url = await new Promise(r => { const fr = new FileReader(); fr.onload = () => r(fr.result); fr.readAsDataURL(blob); });
        if (url !== lastSeen) { lastSeen = url; post({t:'clipSeen', type:'img', img:url, text:`Image ${img.slice(6).toUpperCase()}`}); }
        return;
      }
      if (item.types.includes('text/plain')) {
        const text = await (await item.getType('text/plain')).text();
        if (text && text !== lastSeen) { lastSeen = text; post({t:'clipSeen', type:'text', text}); }
        return;
      }
    }
  } catch { /* permission not granted yet or clipboard empty */ }
}
addEventListener('focus', () => setTimeout(seeClipboard, 150));
document.addEventListener('copy', () => setTimeout(seeClipboard, 150));
setInterval(seeClipboard, 2000);

renderAll();
post({t:'ready'});
