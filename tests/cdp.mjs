// Minimal Chrome DevTools Protocol client (Node 22+: global WebSocket/fetch). Used by the E2E scripts.
export async function attach(port, match = () => true, timeout = 30000) {
  const end = Date.now() + timeout;
  let t;
  while (!(t = (await fetch(`http://127.0.0.1:${port}/json`).then(r => r.json()).catch(() => [])).find(x => x.type === 'page' && match(x)))) {
    if (Date.now() > end) throw new Error('no page target');
    await sleep(300);
  }
  const ws = new WebSocket(t.webSocketDebuggerUrl);
  await new Promise((ok, bad) => { ws.onopen = ok; ws.onerror = bad; });
  let id = 0;
  const pending = new Map();
  ws.onmessage = e => { const m = JSON.parse(e.data); if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); } };
  // page gone (app quit or restarted): fail what is still waiting instead of hanging
  ws.onclose = () => { for (const f of pending.values()) f({result: {exceptionDetails: {text: 'page closed'}}}); pending.clear(); };
  const call = (method, params = {}) => new Promise(ok => { const i = ++id; pending.set(i, ok); ws.send(JSON.stringify({id:i, method, params})); });
  const ev = async expr => {
    const r = await call('Runtime.evaluate', {expression: expr, awaitPromise: true, returnByValue: true});
    if (r.result?.exceptionDetails) throw new Error(r.result.exceptionDetails.exception?.description || r.result.exceptionDetails.text);
    return r.result?.result?.value;
  };
  const waitFor = async (expr, ms = 20000, what = expr) => {
    const end = Date.now() + ms;
    for (;;) {
      const v = await ev(expr).catch(() => undefined);
      if (v) return v;
      if (Date.now() > end) throw new Error('timeout: ' + what);
      await sleep(200);
    }
  };
  return {call, ev, waitFor, close: () => ws.close()};
}
export const sleep = ms => new Promise(r => setTimeout(r, ms));

let pass = 0, fail = 0;
export async function test(name, fn) {
  if (process.env.ONLY && !new RegExp(process.env.ONLY).test(name)) return; // ONLY=regex: run a subset (keep the setup tests in it)
  const t0 = Date.now();
  try { await fn(); pass++; console.log(`  ok   ${name} (${Date.now() - t0} ms)`); }
  catch (e) { fail++; console.log(`  FAIL ${name}: ${e.message}`); }
}
export function check(cond, what) { if (!cond) throw new Error(what); }
export function summary() { console.log(`\n${pass}/${pass + fail} passed`); return fail ? 1 : 0; }
