// Phase 13 HTTP checks through the gateway (the UI's nginx.conf on nginx, gateway.ps1) for verify-p13.ps1, which starts the
// processes and passes the settings in the environment: GATEWAY_URL, ADMIN_TOKEN, LOGIN_USER / LOGIN_PASSWORD (a synthetic
// account's personal number and phone, typed like on the login page), LISTED_ORIGIN.
// Usage: node p13-http.mjs <routes|sse|cors-closed|cors-listed|agent-down>
// Prints one JSON check per line on stdout ({ result, check, expected, actual }); diagnostics go to stderr.
import { createHash } from 'node:crypto';

const env = process.env;
const G = env.GATEWAY_URL ?? 'http://127.0.0.1:5230';
const ADMIN = env.ADMIN_TOKEN;
const phase = process.argv[2];

function report(result, name, expected, actual) {
    process.stdout.write(`${JSON.stringify({ result, check: name, expected: `${expected}`, actual: `${actual}` })}\n`);
}
const check = (name, expected, actual) => report(`${expected}` === `${actual}` ? 'PASS' : 'FAIL', name, expected, actual);
const ok = (name, condition, detail = '') => check(name, true, condition ? true : `false${detail ? ` (${String(detail).slice(0, 240)})` : ''}`);
const sha512 = text => createHash('sha512').update(text, 'utf8').digest('hex');
const bearer = token => ({ Authorization: `Bearer ${token}` });

async function get(path, headers = {}) {
    const response = await fetch(`${G}${path}`, { headers, redirect: 'manual' });
    return { status: response.status, headers: response.headers, text: await response.text() };
}

async function postJson(path, body, headers = {}) {
    const response = await fetch(`${G}${path}`, { method: 'POST', headers: { 'Content-Type': 'application/json', ...headers }, body: typeof body === 'string' ? body : JSON.stringify(body), redirect: 'manual' });
    return { status: response.status, headers: response.headers, text: await response.text() };
}

const json = text => {
    try {
        return JSON.parse(text);
    } catch {
        return null;
    }
};

/**
 * Streams a chat request and records when each SSE event (and each ": ping" comment) arrived, in ms after the request.
 * abortAfter: abort the request that many ms after the thread event. Returns { status, headers, events, pings, error }.
 */
async function stream(path, body, token, { abortAfter, headers = {} } = {}) {
    const controller = new AbortController();
    const start = Date.now();
    const result = { status: 0, headers: null, events: [], pings: [], error: null, threadId: null };
    try {
        const response = await fetch(`${G}${path}`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', ...bearer(token), ...headers },
            body: JSON.stringify(body),
            signal: controller.signal,
        });
        result.status = response.status;
        result.headers = response.headers;
        if (!(response.headers.get('content-type') ?? '').startsWith('text/event-stream')) {
            result.body = await response.text();
            return result;
        }
        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';
        for (;;) {
            const { value, done } = await reader.read();
            if (done) break;
            buffer += decoder.decode(value, { stream: true });
            let cut;
            while ((cut = buffer.indexOf('\n\n')) >= 0) {
                const block = buffer.slice(0, cut);
                buffer = buffer.slice(cut + 2);
                const at = Date.now() - start;
                if (block.startsWith(':')) {
                    result.pings.push(at);
                    continue;
                }
                const name = /^event: (.*)$/m.exec(block)?.[1] ?? 'message';
                const data = json(block.split('\n').filter(l => l.startsWith('data:')).map(l => l.slice(5).trimStart()).join('\n'));
                result.events.push({ name, data, at });
                if (name === 'thread') {
                    result.threadId = data?.threadId;
                    if (abortAfter !== undefined) setTimeout(() => controller.abort(), abortAfter);
                }
            }
        }
    } catch (error) {
        result.error = error?.name ?? String(error);
    }
    return result;
}

const event = (r, name) => r.events.find(e => e.name === name);

async function phaseRoutes() {
    const root = await get('/');
    check('Static: / is the UI (index.html)', '200/true', `${root.status}/${root.text.includes('<app-root')}`);
    const login = await get('/login');
    ok('Static: /login is the prerendered login page', login.status === 200 && (login.headers.get('content-type') ?? '').startsWith('text/html') && login.text.includes('התחברות למערכת'), `${login.status} ${login.headers.get('content-type')}`);
    const deep = await get('/assignments-management');
    check('Static: a prerendered route (/assignments-management)', '200/true', `${deep.status}/${deep.text.includes('<app-root')}`);
    const unknown = await get('/no/such/page');
    check('Static: an unknown path falls back to index.html (SPA routing)', '200/true', `${unknown.status}/${unknown.text.includes('<app-root')}`);
    const script = /<script src="(main-[A-Z0-9]+\.js)" type="module">/.exec(login.text)?.[1];
    const js = script ? await get(`/${script}`) : { status: 0, headers: new Headers() };
    ok('Static: the main bundle is served as JavaScript', js.status === 200 && /^(application|text)\/javascript/.test(js.headers.get('content-type') ?? ''), `${script} ${js.status} ${js.headers.get('content-type')}`);
    ok('Static: the main bundle has the production URLs', !!js.text?.includes('serverURL:"/api",agentURL:"/agent/api"'));
    const style = /<link rel="stylesheet" href="(styles-[A-Z0-9]+\.css)"/.exec(login.text)?.[1];
    const css = style ? await get(`/${style}`) : { status: 0, headers: new Headers() };
    check('Static: the stylesheet is served as CSS', '200/text/css', `${css.status}/${css.headers.get('content-type')}`);

    const noToken = await get('/api/Mission/GetMissionsSummary');
    check('/api/: reaches ShabzakAPI (401 with its Bearer challenge without a token)', '401/true', `${noToken.status}/${(noToken.headers.get('www-authenticate') ?? '').startsWith('Bearer')}`);
    const missions = await get('/api/Mission/GetMissionsSummary', bearer(ADMIN));
    const list = json(missions.text);
    ok('/api/: an authenticated read returns the API data (Hebrew names intact)', missions.status === 200 && Array.isArray(list) && list.some(m => m.name === 'סיור'), `${missions.status} ${missions.text.slice(0, 120)}`);
    const loginResponse = await postJson('/api/User/Login', { username: sha512(env.LOGIN_USER), password: sha512(env.LOGIN_PASSWORD) });
    const loginBody = json(loginResponse.text);
    const token = loginBody?.value?.token;
    ok('/api/: POST User/Login with a synthetic account returns a JWT', loginResponse.status === 200 && loginBody?.success === true && /^eyJ[\w-]+\.[\w-]+\.[\w-]+$/.test(token ?? ''), `${loginResponse.status} success=${loginBody?.success} ${loginBody?.errorMessage ?? ''}`);
    check('/api/: the login answer carries no password or salt', '|', `${loginBody?.value?.user?.password ?? ''}|${loginBody?.value?.user?.salt ?? ''}`);

    const health = await get('/agent/health');
    check('/agent/: /agent/health reaches the agent (prefix stripped)', '200/ok', `${health.status}/${json(health.text)?.status}`);
    const meNoToken = await get('/agent/api/me');
    check('/agent/: no token -> the agent answers 401 unauthorized (JSON)', '401/unauthorized', `${meNoToken.status}/${json(meNoToken.text)?.code}`);
    const me = await get('/agent/api/me', { ...bearer(token ?? ''), Origin: G });
    const meBody = json(me.text);
    check('/agent/: the token from the real login works with the agent', `200/${env.LOGIN_ROLE}`, `${me.status}/${meBody?.role}`);
    check('/agent/: same origin needs no CORS header (CORS_ORIGINS empty, the compose default)', 'null', `${me.headers.get('access-control-allow-origin')}`);
    const unknownAgent = await get('/agent/no-such-route');
    check("/agent/: an unknown agent path is the agent's own 404 JSON", '404/not_found', `${unknownAgent.status}/${json(unknownAgent.text)?.code}`);
}

async function phaseSse() {
    const fast = await stream('/agent/api/chat/stream', { message: 'SLOW 3000 שלום' }, ADMIN);
    check('SSE: 200 text/event-stream through the gateway', '200/text/event-stream; charset=utf-8', `${fast.status}/${fast.headers?.get('content-type')}`);
    check('SSE: no-cache, no-transform reach the browser', 'no-cache, no-transform', fast.headers?.get('cache-control'));
    const thread = event(fast, 'thread');
    const final = event(fast, 'final');
    check('SSE: thread first, done last', 'thread/done', `${fast.events[0]?.name}/${fast.events.at(-1)?.name}`);
    ok('SSE: not buffered: the thread event arrives at once (< 1.5 s), the answer only after the 3 s the model takes', !!thread && !!final && thread.at < 1500 && final.at - thread.at >= 2500, `thread ${thread?.at} ms, final ${final?.at} ms`);
    check('SSE: Hebrew text intact', 'echo: שלום', final?.data?.message);

    const slow = await stream('/agent/api/chat/stream', { message: 'SLOW 16500 פינג' }, ADMIN);
    const slowFinal = event(slow, 'final');
    ok('SSE: the 15 s ping reaches the client before the answer (the stream stays alive through the gateway)', slow.pings.length >= 1 && slow.pings[0] >= 14000 && slow.pings[0] < (slowFinal?.at ?? 0), `pings ${slow.pings.join(',')} ms, final ${slowFinal?.at} ms`);

    const cut = await stream('/agent/api/chat/stream', { message: 'SLOW 8000 bye' }, ADMIN, { abortAfter: 1000 });
    check('Disconnect: the client aborts 1 s into the answer', 'AbortError', cut.error);
    const freedAt = Date.now();
    let again;
    for (let i = 0; i < 40; i++) {
        again = await stream('/agent/api/chat/stream', { threadId: cut.threadId, message: 'שוב' }, ADMIN);
        if (again.status !== 409) break;
        await new Promise(resolve => setTimeout(resolve, 100));
    }
    const waited = Date.now() - freedAt;
    ok('Disconnect: the gateway closes the agent connection, so the run stops and the thread is free at once (< 3 s, the answer had 7 s left)', again?.status === 200 && event(again, 'final')?.data?.message === 'echo: שוב' && waited < 3000, `status ${again?.status}, waited ${waited} ms`);

    const big = await postJson('/agent/api/chat/stream', JSON.stringify({ message: 'א'.repeat(40000) }), bearer(ADMIN));
    check("Body over the agent's 64 KB limit: the agent's own 413 JSON passes through", '413/bad_request', `${big.status}/${json(big.text)?.code}`);
    const long = await postJson('/agent/api/chat/stream', { message: 'א'.repeat(2001) }, bearer(ADMIN));
    check('A message over MAX_MESSAGE_CHARS: 400 bad_request from the agent', '400/bad_request', `${long.status}/${json(long.text)?.code}`);
}

async function preflight(origin) {
    const response = await fetch(`${G}/agent/api/chat/stream`, {
        method: 'OPTIONS',
        headers: { Origin: origin, 'Access-Control-Request-Method': 'POST', 'Access-Control-Request-Headers': 'authorization,content-type' },
    });
    return { status: response.status, headers: response.headers };
}

async function phaseCorsClosed() {
    const pre = await preflight('http://localhost:5555');
    check('CORS (compose default, CORS_ORIGINS empty): a preflight from another origin gets no CORS grant', '204/null', `${pre.status}/${pre.headers.get('access-control-allow-origin')}`);
    const same = await stream('/agent/api/chat/stream', { message: 'שלום' }, ADMIN, { headers: { Origin: G } });
    check("CORS: the gateway's own origin streams without any CORS header (same origin)", '200/null/echo: שלום', `${same.status}/${same.headers?.get('access-control-allow-origin')}/${event(same, 'final')?.data?.message}`);
}

async function phaseCorsListed() {
    const origin = env.LISTED_ORIGIN;
    const pre = await preflight(origin);
    check(`CORS (AGENT_CORS_ORIGINS=${origin}): the preflight through the gateway grants that origin`, `204/${origin}`, `${pre.status}/${pre.headers.get('access-control-allow-origin')}`);
    check('CORS: the preflight allows POST with Authorization and Content-Type', 'true/Authorization, Content-Type', `${(pre.headers.get('access-control-allow-methods') ?? '').includes('POST')}/${pre.headers.get('access-control-allow-headers')}`);
    const real = await stream('/agent/api/chat/stream', { message: 'שלום' }, ADMIN, { headers: { Origin: origin } });
    check('CORS: the stream answer grants the origin and exposes Retry-After', `200/${origin}/Retry-After`, `${real.status}/${real.headers?.get('access-control-allow-origin')}/${real.headers?.get('access-control-expose-headers')}`);
    ok('CORS: Vary: Origin on the answer', (real.headers?.get('vary') ?? '').includes('Origin'), real.headers?.get('vary'));
    const evil = await preflight('http://evil.example');
    check('CORS: an unlisted origin still gets no grant', '204/null', `${evil.status}/${evil.headers.get('access-control-allow-origin')}`);
}

async function phaseAgentDown() {
    const health = await get('/agent/health');
    const body = json(health.text);
    check('Agent down: the gateway answers 502 JSON like the agent (upstream_error)', '502/application/json/upstream_error', `${health.status}/${health.headers.get('content-type')}/${body?.code}`);
    check('Agent down: the message', 'The Shabzak assistant service is not reachable.', body?.message);
    const chat = await postJson('/agent/api/chat/stream', { message: 'שלום' }, bearer(ADMIN));
    check('Agent down: a chat request gets the same 502 JSON', '502/upstream_error', `${chat.status}/${json(chat.text)?.code}`);
    const ui = await get('/login');
    const apiRead = await get('/api/Mission/GetMissionsSummary', bearer(ADMIN));
    check('Agent down: the UI and /api/ keep working', '200/200', `${ui.status}/${apiRead.status}`);
}

const phases = { routes: phaseRoutes, sse: phaseSse, 'cors-closed': phaseCorsClosed, 'cors-listed': phaseCorsListed, 'agent-down': phaseAgentDown };
try {
    if (!phases[phase]) throw new Error(`Unknown phase "${phase}"`);
    await phases[phase]();
} catch (error) {
    report('FAIL', `Phase ${phase} crashed`, 'no error', error?.stack ?? error);
}
