// Drives the Shabzak UI in headless Chrome over the DevTools protocol (Node 22 WebSocket, no npm dependencies) for
// verify-p11-p12.ps1 (the dev server) and verify-p13.ps1 (the production build behind the gateway), which start the
// processes and pass the settings in the environment.
// Usage: node ui-e2e.mjs <main|regular|unauthorized|llm-down|agent-down|agent-back|ratelimit|prod-login|gateway-agent-down>
// Prints one JSON check per line on stdout ({ result, check, expected, actual }); diagnostics go to stderr.
import { setTimeout as sleep } from 'node:timers/promises';

const env = process.env;
const CDP = `http://127.0.0.1:${env.CDP_PORT ?? '9333'}`;
const UI = env.UI_URL ?? 'http://localhost:5555';
const AGENT = env.AGENT_URL ?? 'http://localhost:3001/api';
const STORE_KEY = id => `shabzak.chat.${id}`;
const ADMIN_USER = JSON.parse(env.ADMIN_USER ?? '{}');
const REGULAR_USER = JSON.parse(env.REGULAR_USER ?? '{}');
const phase = process.argv[2];

function report(result, name, expected, actual) {
    process.stdout.write(`${JSON.stringify({ result, check: name, expected: `${expected}`, actual: `${actual}` })}\n`);
}
const check = (name, expected, actual) => report(`${expected}` === `${actual}` ? 'PASS' : 'FAIL', name, expected, actual);
const ok = (name, condition, detail = '') => check(name, true, condition ? true : `false${detail ? ` (${String(detail).slice(0, 200)})` : ''}`);

class Cdp {
    static async connect() {
        const targets = await (await fetch(`${CDP}/json/list`)).json();
        const page = targets.find(t => t.type === 'page') ?? (await (await fetch(`${CDP}/json/new?about:blank`, { method: 'PUT' })).json());
        const cdp = new Cdp(page.webSocketDebuggerUrl);
        await cdp.opened;
        return cdp;
    }

    constructor(url) {
        this.ws = new WebSocket(url);
        this.id = 0;
        this.pending = new Map();
        this.handlers = new Map();
        this.opened = new Promise((resolve, reject) => {
            this.ws.onopen = resolve;
            this.ws.onerror = reject;
        });
        this.ws.onmessage = event => {
            const msg = JSON.parse(event.data);
            if (msg.id !== undefined) {
                const waiter = this.pending.get(msg.id);
                this.pending.delete(msg.id);
                if (waiter) msg.error ? waiter.reject(new Error(msg.error.message)) : waiter.resolve(msg.result);
            } else {
                for (const handler of this.handlers.get(msg.method) ?? []) handler(msg.params);
            }
        };
    }

    send(method, params = {}) {
        const id = ++this.id;
        this.ws.send(JSON.stringify({ id, method, params }));
        return new Promise((resolve, reject) => this.pending.set(id, { resolve, reject }));
    }

    on(method, handler) {
        this.handlers.set(method, [...(this.handlers.get(method) ?? []), handler]);
    }

    async eval(fn, ...args) {
        const result = await this.send('Runtime.evaluate', {
            expression: `(${fn.toString()})(...${JSON.stringify(args)})`,
            awaitPromise: true,
            returnByValue: true,
        });
        if (result.exceptionDetails) throw new Error(result.exceptionDetails.exception?.description ?? result.exceptionDetails.text);
        return result.result.value;
    }

    async waitFor(fn, { timeout = 20000, args = [] } = {}) {
        const end = Date.now() + timeout;
        let value;
        while (Date.now() < end) {
            try {
                value = await this.eval(fn, ...args);
                if (value) return value;
            } catch {
                value = undefined;
            }
            await sleep(100);
        }
        return value ?? false;
    }
}

const cdp = await Cdp.connect();
const requests = [];
const exceptions = [];
const warnings = [];
cdp.on('Network.requestWillBeSent', p => requests.push({ url: p.request.url, method: p.request.method, headers: p.request.headers }));
cdp.on('Network.responseReceived', p => {
    if (p.response.status >= 400) process.stderr.write(`[http ${p.response.status}] ${p.response.url}\n`);
});
cdp.on('Runtime.exceptionThrown', p => exceptions.push(p.exceptionDetails?.exception?.description ?? p.exceptionDetails?.text));
cdp.on('Runtime.consoleAPICalled', p => {
    const text = (p.args ?? []).map(a => a.value ?? a.description ?? '').join(' ');
    if (p.type === 'warning') warnings.push(text);
    if (p.type === 'error') process.stderr.write(`[console.error] ${text.slice(0, 300)}\n`);
});
await cdp.send('Network.enable');
await cdp.send('Runtime.enable');
await cdp.send('Page.enable');

async function desktop() {
    await cdp.send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 900, deviceScaleFactor: 1, mobile: false });
}

async function goto(url) {
    await cdp.send('Page.navigate', { url });
    await sleep(300);
    await cdp.waitFor(() => document.readyState === 'complete' && !!document.querySelector('app-root mat-toolbar'));
    await sleep(500);
}

async function reload() {
    await cdp.send('Page.reload', {});
    await sleep(300);
    await cdp.waitFor(() => document.readyState === 'complete' && !!document.querySelector('app-root mat-toolbar'));
    await sleep(800);
}

async function login(token, user) {
    await goto(`${UI}/login`);
    await cdp.eval((t, u) => {
        localStorage.clear();
        sessionStorage.clear();
        localStorage.setItem('token', t);
        localStorage.setItem('user', JSON.stringify(u));
    }, token, user);
    await goto(`${UI}/assignments`);
    return cdp.waitFor(() => !!document.querySelector('.chat-launcher-button'));
}

const click = selector => cdp.eval(s => {
    const element = document.querySelector(s);
    if (element) element.click();
    return !!element;
}, selector);

const clickLast = selector => cdp.eval(s => {
    const all = document.querySelectorAll(s);
    const element = all[all.length - 1];
    if (element) element.click();
    return !!element;
}, selector);

async function openPanel() {
    if (!(await cdp.eval(() => !!document.querySelector('.chat-panel')))) await click('.chat-launcher-button');
    return cdp.waitFor(() => !!document.querySelector('.chat-panel .chat-textarea'));
}

async function send(text) {
    await cdp.waitFor(() => !document.querySelector('.chat-panel .stop'), { timeout: 30000 });
    return cdp.eval(value => {
        const textarea = document.querySelector('.chat-textarea');
        textarea.focus();
        textarea.value = value;
        textarea.dispatchEvent(new Event('input', { bubbles: true }));
        textarea.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
        return true;
    }, text);
}

const assistantCount = () => cdp.eval(() => document.querySelectorAll('.bubble.assistant').length);
const lastAssistant = () => cdp.eval(() => ([...document.querySelectorAll('.bubble.assistant .markdown')].pop()?.textContent ?? '').trim());
const waitIdle = (timeout = 30000) => cdp.waitFor(() => !document.querySelector('.chat-panel .stop') && !!document.querySelector('.chat-panel'), { timeout });

async function waitAnswer(previousCount, timeout = 20000) {
    await cdp.waitFor(n => document.querySelectorAll('.bubble.assistant').length > n && !document.querySelector('.chat-panel .stop'), { timeout, args: [previousCount] });
    return lastAssistant();
}

async function waitAnswerOrError(previousCount, timeout = 20000) {
    await cdp.waitFor(n => (document.querySelectorAll('.bubble.assistant').length > n || !!document.querySelector('.chat-error')) && !document.querySelector('.chat-panel .stop'), { timeout, args: [previousCount] });
    const error = await cdp.eval(() => document.querySelector('.chat-error')?.textContent?.trim() ?? '');
    return { error, answer: error ? '' : await lastAssistant() };
}

const lastCard = fn => cdp.eval(source => {
    const card = [...document.querySelectorAll('app-chat-approval-card')].pop();
    return card ? new Function('card', `return (${source})(card)`)(card) : null;
}, fn.toString());

const outcomes = () => lastCard(card => [...card.querySelectorAll('.outcome')].map(e => e.textContent.trim()).join('|'));
const waitPendingCard = (actions = 1) => cdp.waitFor(n => {
    const card = [...document.querySelectorAll('app-chat-approval-card')].pop();
    return !!card && !card.querySelector('.resolved') && card.querySelectorAll('.action').length === n && !document.querySelector('.chat-panel .stop');
}, { args: [actions] });

async function setReason(text) {
    return cdp.eval(value => {
        const input = [...document.querySelectorAll('app-chat-approval-card input.reason')].pop();
        input.value = value;
        input.dispatchEvent(new Event('input', { bubbles: true }));
        return true;
    }, text);
}

const storedChat = id => cdp.eval(key => JSON.parse(sessionStorage.getItem(key) ?? 'null'), STORE_KEY(id));

async function phaseMain() {
    await desktop();
    await goto(`${UI}/login`);
    await cdp.eval(() => {
        localStorage.clear();
        sessionStorage.clear();
    });
    await goto(`${UI}/login`);
    check('Logged out on /login: no chat launcher', false, await cdp.eval(() => !!document.querySelector('.chat-launcher-button')));

    ok('Admin: the launcher appears after login (on /assignments)', await login(env.ADMIN_TOKEN, ADMIN_USER));
    const launcher = await cdp.eval(() => {
        const box = document.querySelector('.chat-launcher');
        const rect = document.querySelector('.chat-launcher-button').getBoundingClientRect();
        const style = getComputedStyle(box);
        return `${style.position}/${Math.round(innerWidth - rect.right)}/${Math.round(innerHeight - rect.bottom)}/${style.zIndex}`;
    });
    check('Launcher: fixed bottom-right (24px), z-index 900 (above the sidenav, below dialogs)', 'fixed/24/24/900', launcher);
    ok('Toolbar: logout button for a logged-in user', await cdp.eval(() => !!document.querySelector('mat-toolbar .logout')));
    ok("Launcher: no collision with the page's left floating button", await cdp.eval(() => {
        const rect = document.querySelector('.chat-launcher-button').getBoundingClientRect();
        return rect.left > innerWidth / 2;
    }));

    ok('Launcher click opens the panel', await openPanel());
    const panel = await cdp.eval(() => {
        const element = document.querySelector('.chat-panel');
        const rect = element.getBoundingClientRect();
        return {
            layout: `${element.getAttribute('dir')}/${Math.round(rect.width)}/${Math.round(rect.height)}`,
            title: document.querySelector('.chat-title').textContent.trim(),
            chips: [...document.querySelectorAll('.chat-suggestion')].map(e => e.textContent.trim()).join('|'),
            empty: document.querySelector('.chat-empty-text')?.textContent ?? '',
            focused: document.activeElement?.classList.contains('chat-textarea') ?? false,
        };
    });
    check('Panel: RTL, 400x600 on desktop', 'rtl/400/600', panel.layout);
    check('Panel: title', 'העוזר של שבצ"ק', panel.title);
    check('Empty state: admin suggestion chips', 'מי בשמירה היום?|צור שבצ"ק לשבוע הבא|הצע מחליף לחייל', panel.chips);
    ok('Empty state: one line on what the assistant helps with (and that changes need approval)', panel.empty.includes('לוח השיבוצים') && panel.empty.includes('חופשות') && panel.empty.includes('אחרי שתאשר'), panel.empty);
    ok('Panel: the input is focused on open', panel.focused);

    let mark = requests.length;
    let n = await assistantCount();
    await send('שלום');
    ok('Send (Enter): the user bubble shows the message', await cdp.waitFor(() => [...document.querySelectorAll('.bubble.user .text')].some(e => e.textContent === 'שלום')));
    check('Answer: streamed from the agent (scripted LLM echo)', 'echo: שלום', await waitAnswer(n));
    const streamRequest = requests.slice(mark).find(r => r.url === `${AGENT}/chat/stream` && r.method === 'POST');
    ok('Request: POST /chat/stream with the Bearer token of localStorage', streamRequest?.headers?.Authorization === `Bearer ${env.ADMIN_TOKEN}`, streamRequest ? 'header differs' : 'no request');
    const bubbles = await cdp.eval(() => {
        const user = document.querySelector('.bubble.user');
        const assistant = document.querySelector('.bubble.assistant');
        return `${user.getBoundingClientRect().left < assistant.getBoundingClientRect().left}/${assistant.querySelector('.markdown').getAttribute('dir')}`;
    });
    check('RTL layout: assistant bubbles on the right (start), user bubbles on the left; dir="auto" text', 'true/auto', bubbles);

    n = await assistantCount();
    await send('| א | ב |\n|---|---|---|\n| 1 | 2 | 3 |');
    await waitAnswer(n);
    const table = await cdp.eval(() => {
        const answer = [...document.querySelectorAll('.bubble.assistant .markdown')].pop();
        const element = answer.querySelector('table');
        if (!element) return 'no table';
        const th = [...element.querySelectorAll('th')].map(e => e.textContent.trim()).join('|');
        const td = [...element.querySelectorAll('td')].map(e => e.textContent.trim()).join('|');
        return `${getComputedStyle(element).direction}/${th}/${td}`;
    });
    check('Markdown: a table renders as an RTL table', 'rtl/echo:|א|ב/1|2|3', table);

    mark = requests.length;
    n = await assistantCount();
    await send('<img src=x onerror="window.__pwned=1"> ![a](http://127.0.0.1:9/leak.png) [לחץ](http://127.0.0.1:9/go) <script>window.__pwned=2</script>');
    await waitAnswer(n);
    const injected = await cdp.eval(() => {
        const answer = [...document.querySelectorAll('.bubble.assistant .markdown')].pop();
        return { tags: `${answer.querySelectorAll('img').length}/${answer.querySelectorAll('a').length}/${answer.querySelectorAll('script').length}`, pwned: window.__pwned ?? null, text: answer.textContent };
    });
    check('Injection: no img / a / script elements in the rendered answer', '0/0/0', injected.tags);
    check('Injection: no script ran', null, injected.pwned);
    ok('Injection: raw HTML is shown as text', injected.text.includes('<img src=x'), injected.text);
    ok('Injection: no request to the image or link URLs', !requests.slice(mark).some(r => r.url.startsWith('http://127.0.0.1:9/')));

    n = await assistantCount();
    await send(`SLOW 2500 CALL get_schedule {"from":"${env.TOMORROW}"}`);
    const label = await cdp.waitFor(() => document.querySelector('.chat-status span')?.textContent?.trim(), { timeout: 5000 });
    check('Status line: spinner + Hebrew label while the agent works', 'חושב…', label);
    ok('Streaming: Stop replaces Send', await cdp.eval(() => !!document.querySelector('.chat-panel .stop') && !document.querySelector('.chat-panel .send')));
    const toolAnswer = await waitAnswer(n, 30000);
    ok('Tool answer: the final message carries the get_schedule result', toolAnswer.startsWith('done: get_schedule='), toolAnswer.slice(0, 80));
    check('After the answer: no status line', false, await cdp.eval(() => !!document.querySelector('.chat-status')));

    await send('SLOW 6000 שאלה ארוכה');
    await cdp.waitFor(() => !!document.querySelector('.chat-panel .stop'));
    await sleep(500);
    await click('.chat-panel .stop');
    ok('Stop: the request is aborted and Send is back', await cdp.waitFor(() => !document.querySelector('.chat-panel .stop') && !!document.querySelector('.chat-panel .send'), { timeout: 3000 }));
    await sleep(1500);
    n = await assistantCount();
    await send('אחרי עצירה');
    let after = await waitAnswerOrError(n, 20000);
    if (after.error) {
        process.stderr.write(`after stop: ${after.error}\n`);
        await click('.chat-panel .retry');
        after = await waitAnswerOrError(n, 20000);
    }
    check('After Stop: the next message is answered', 'echo: אחרי עצירה', after.answer || after.error);

    n = await assistantCount();
    await send(`CALL request_vacation {"soldierId":8,"from":"${env.D1}","to":"${env.D2}"}`);
    ok('Approval: the card appears and the turn ends', await waitPendingCard(1));
    const card = await lastCard(c => ({
        title: c.querySelector('.action-title').textContent.trim(),
        risk: c.querySelector('.risk-chip').textContent.trim(),
        details: c.querySelectorAll('.details li').length,
        header: c.querySelector('.approval-header').textContent.trim(),
    }));
    check('Approval card: the server-built title', 'בקשת חופשה עבור נועם פרידמן', card?.title);
    check('Approval card: risk chip', 'סיכון נמוך', card?.risk);
    ok('Approval card: details and header', (card?.details ?? 0) >= 2 && card?.header.includes('נדרש אישור'), JSON.stringify(card));
    const input = await cdp.eval(() => {
        const textarea = document.querySelector('.chat-textarea');
        return { disabled: textarea.disabled, placeholder: textarea.placeholder };
    });
    ok('Approval pending: the input is disabled with a hint', input.disabled && input.placeholder.includes('לאשר או לדחות'), JSON.stringify(input));
    await click('.close-chat');
    const badge = await cdp.waitFor(() => {
        const content = document.querySelector('.chat-launcher .mat-badge-content');
        return content && !document.querySelector('.chat-launcher .mat-badge-hidden') ? content.textContent.trim() : '';
    }, { timeout: 3000 });
    check('Panel closed: the launcher badge counts the pending action', '1', badge);
    await openPanel();
    ok('Submit is disabled until a choice is made', await lastCard(c => c.querySelector('.submit-decisions').disabled));
    mark = requests.length;
    await clickLast('app-chat-approval-card .approve-toggle button');
    await sleep(150);
    await clickLast('app-chat-approval-card .submit-decisions');
    const approved = await waitAnswer(n, 30000);
    ok('Approve: the agent ran the write', approved.startsWith('done: request_vacation=') && !approved.includes('REJECTED'), approved.slice(0, 100));
    check('Approve: the card shows the decision', 'אושר', await outcomes());
    check('Approve: the refresh snackbar', 'הנתונים עודכנו', await cdp.waitFor(() => document.querySelector('mat-snack-bar-container')?.textContent?.trim(), { timeout: 5000 }));
    const after1 = requests.slice(mark);
    ok('Approve: POST /chat/resume', after1.some(r => r.url === `${AGENT}/chat/resume` && r.method === 'POST'));
    ok('Refresh hints soldiers + vacations: GetSoldiers?reloadCache=true', after1.some(r => r.url.includes('/Soldiers/GetSoldiers?reloadCache=true')));
    ok("Refresh hint vacations: the user's own GetVacations", after1.some(r => r.url.endsWith('/Soldiers/GetVacations')));
    check('Approve: the pending badge is gone', false, await cdp.eval(() => !!document.querySelector('.chat-launcher .mat-badge-content') && !document.querySelector('.chat-launcher .mat-badge-hidden')));

    n = await assistantCount();
    await send(`CALL request_vacation {"soldierId":8,"from":"${env.D3}","to":"${env.D3}"}`);
    await waitPendingCard(1);
    await clickLast('app-chat-approval-card .reject-toggle button');
    await sleep(150);
    await setReason('לא עכשיו');
    await sleep(150);
    mark = requests.length;
    await clickLast('app-chat-approval-card .submit-decisions');
    const rejected = await waitAnswer(n, 30000);
    ok('Reject: the agent got the rejection with the reason, nothing ran', rejected.includes('REJECTED') && rejected.includes('לא עכשיו'), rejected.slice(0, 160));
    check('Reject: the card shows the decision and the reason', 'נדחה: לא עכשיו', await outcomes());
    ok('Reject: no data reload', !requests.slice(mark).some(r => r.url.includes('/Soldiers/GetSoldiers')));

    n = await assistantCount();
    await send(`CALL request_vacation {"soldierId":8,"from":"${env.D4}","to":"${env.D4}"} && CALL request_vacation {"soldierId":8,"from":"${env.D5}","to":"${env.D5}"}`);
    ok('Two writes in one turn: one approval with two cards', await waitPendingCard(2));
    ok('Two cards: "אשר הכל" and "דחה הכל"', await lastCard(c => !!c.querySelector('.approve-all') && !!c.querySelector('.reject-all')));
    await clickLast('app-chat-approval-card .reject-all');
    await sleep(150);
    await lastCard(c => {
        c.querySelectorAll('.action')[0].querySelector('.approve-toggle button').click();
        return true;
    });
    await sleep(150);
    await clickLast('app-chat-approval-card .submit-decisions');
    const mixed = await waitAnswer(n, 30000);
    ok('Mixed decisions: one write ran and one was rejected', /request_vacation=\{/.test(mixed) && mixed.includes('REJECTED'), mixed.slice(0, 200));
    check('Mixed decisions: the outcomes in order', 'אושר|נדחה', await outcomes());

    const beforeReload = await cdp.eval(key => ({ count: document.querySelectorAll('app-chat-message, app-chat-approval-card').length, thread: JSON.parse(sessionStorage.getItem(key) ?? '{}').threadId }), STORE_KEY(ADMIN_USER.id));
    await reload();
    ok('Reload: the session survives (launcher visible)', await cdp.waitFor(() => !!document.querySelector('.chat-launcher-button')));
    await openPanel();
    const afterReload = await cdp.eval(key => ({ count: document.querySelectorAll('app-chat-message, app-chat-approval-card').length, thread: JSON.parse(sessionStorage.getItem(key) ?? '{}').threadId }), STORE_KEY(ADMIN_USER.id));
    check('Reload: the conversation is restored (messages / thread)', `${beforeReload.count}/${beforeReload.thread}`, `${afterReload.count}/${afterReload.thread}`);

    await send(`CALL request_vacation {"soldierId":8,"from":"${env.D6}","to":"${env.D6}"}`);
    await waitPendingCard(1);
    await reload();
    check('Reload with a pending approval: the badge is back', '1', await cdp.waitFor(() => document.querySelector('.chat-launcher .mat-badge-content')?.textContent?.trim() ?? '', { timeout: 5000 }));
    await openPanel();
    const restoredCard = await cdp.eval(() => ({
        pending: !!document.querySelector('app-chat-approval-card .approval:not(.resolved)'),
        disabled: document.querySelector('.chat-textarea').disabled,
    }));
    check('Reload with a pending approval: the card is pending, the input disabled', 'true/true', `${restoredCard.pending}/${restoredCard.disabled}`);
    n = await assistantCount();
    await clickLast('app-chat-approval-card .approve-toggle button');
    await sleep(150);
    await clickLast('app-chat-approval-card .submit-decisions');
    const afterReloadApproval = await waitAnswer(n, 30000);
    ok('Reload with a pending approval: approving resumes the same thread', afterReloadApproval.startsWith('done: request_vacation=') && !afterReloadApproval.includes('REJECTED'), afterReloadApproval.slice(0, 100));

    await cdp.eval(key => {
        const chat = JSON.parse(sessionStorage.getItem(key));
        chat.threadId = 'deadbeef-0000-4000-8000-000000000000';
        sessionStorage.setItem(key, JSON.stringify(chat));
    }, STORE_KEY(ADMIN_USER.id));
    await reload();
    await openPanel();
    n = await assistantCount();
    await send('שלום אחרי');
    check('Unknown thread (expired or agent restarted): answered transparently', 'echo: שלום אחרי', await waitAnswer(n));
    const divider = await cdp.eval(() => {
        const items = [...document.querySelectorAll('app-chat-message')];
        const index = items.findIndex(e => e.querySelector('.note')?.textContent.includes('השיחה הקודמת כבר לא זמינה'));
        return index >= 0 && items[index + 1]?.querySelector('.bubble.user')?.textContent.includes('שלום אחרי');
    });
    ok('Unknown thread: a divider note right before the message', divider);
    const newThread = (await storedChat(ADMIN_USER.id))?.threadId;
    ok('Unknown thread: a new thread is stored', !!newThread && newThread !== 'deadbeef-0000-4000-8000-000000000000', newThread);

    await send('SLOW 1500 שלום שוב');
    await sleep(300);
    await click('.close-chat');
    ok('Panel closed while answering: an unread dot when the answer arrives', await cdp.waitFor(() => !!document.querySelector('.chat-launcher .unread-dot'), { timeout: 15000 }));
    await openPanel();
    check('Opening the panel clears the unread dot', false, await cdp.eval(() => !!document.querySelector('.chat-launcher .unread-dot')));

    await cdp.eval(() => {
        const textarea = document.querySelector('.chat-textarea');
        textarea.focus();
        textarea.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
        return true;
    });
    ok('Escape closes the panel', await cdp.waitFor(() => !document.querySelector('.chat-panel'), { timeout: 3000 }));

    await openPanel();
    mark = requests.length;
    await click('.new-chat');
    ok('New chat: the messages are cleared, the empty state is back', await cdp.waitFor(() => !!document.querySelector('.chat-empty') && !document.querySelector('app-chat-message')));
    check('New chat: the stored conversation is removed', null, await storedChat(ADMIN_USER.id));
    ok('New chat: POST /chat/reset for the old thread', requests.slice(mark).some(r => r.url === `${AGENT}/chat/reset` && r.method === 'POST'));

    await cdp.send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 2, mobile: true });
    await sleep(500);
    const mobile = await cdp.eval(() => {
        const rect = document.querySelector('.chat-panel').getBoundingClientRect();
        return `${Math.round(rect.x)}/${Math.round(rect.y)}/${Math.round(rect.width) === innerWidth}/${Math.round(rect.height) === innerHeight}/${getComputedStyle(document.querySelector('.chat-launcher')).display}`;
    });
    check('Mobile (< 768px): full-screen panel, launcher hidden while open', '0/0/true/true/none', mobile);
    await desktop();
    await sleep(300);

    n = await assistantCount();
    await send('לפני יציאה');
    await waitAnswer(n);
    ok('Before logout: the conversation is stored', !!(await storedChat(ADMIN_USER.id)));
    mark = requests.length;
    await click('mat-toolbar .logout');
    ok('Logout: back on /login', await cdp.waitFor(() => location.pathname === '/login'));
    await sleep(500);
    const loggedOut = await cdp.eval(key => `${!!document.querySelector('.chat-launcher-button')}/${localStorage.getItem('token')}/${localStorage.getItem('user')}/${sessionStorage.getItem(key)}`, STORE_KEY(ADMIN_USER.id));
    check('Logout: no launcher; token, user and conversation cleared', 'false/null/null/null', loggedOut);
    ok('Logout: the agent thread is reset', requests.slice(mark).some(r => r.url === `${AGENT}/chat/reset` && r.method === 'POST'));
    await goto(`${UI}/login`);
    check('Logout: the login page stays (no bounce to the personal page)', '/login', await cdp.eval(() => location.pathname));
}

async function phaseRegular() {
    await desktop();
    ok('Regular user: the launcher appears', await login(env.REGULAR_TOKEN, REGULAR_USER));
    await openPanel();
    const empty = await cdp.eval(() => ({
        chips: [...document.querySelectorAll('.chat-suggestion')].map(e => e.textContent.trim()).join('|'),
        text: document.querySelector('.chat-empty-text')?.textContent ?? '',
    }));
    check('Regular user: self-service suggestion chips', 'מי בשמירה היום?|מתי המשימה הבאה שלי?|כמה שעות עשיתי החודש?', empty.chips);
    ok('Regular user: the empty state does not offer changes', !empty.text.includes('שינויים'), empty.text);
    const n = await assistantCount();
    await send(`CALL request_vacation {"soldierId":3,"from":"${env.D1}","to":"${env.D1}"}`);
    const answer = await waitAnswer(n, 30000);
    check('Regular user: a write call gets no approval card', 0, await cdp.eval(() => document.querySelectorAll('app-chat-approval-card').length));
    ok('Regular user: the write tool is not available to the model', answer.startsWith('done: request_vacation=') && !answer.includes('"vacation"'), answer.slice(0, 160));
    await click('mat-toolbar .logout');
    await cdp.waitFor(() => location.pathname === '/login');
}

async function phaseUnauthorized() {
    await desktop();
    ok('Bad token (valid exp, bad signature): the UI shows the launcher', await login(env.BAD_TOKEN, ADMIN_USER));
    await openPanel();
    await send('שלום');
    const expired = 'פג תוקף ההתחברות. יש להתחבר מחדש.';
    check('401: the session-expired snackbar', expired, await cdp.waitFor(text => {
        const shown = document.querySelector('mat-snack-bar-container')?.textContent?.trim();
        return shown === text ? shown : '';
    }, { timeout: 15000, args: [expired] }) || 'not shown');
    ok('401: sent to /login with the token cleared', await cdp.waitFor(() => location.pathname === '/login' && localStorage.getItem('token') === null, { timeout: 10000 }));
    check('401: no launcher on the login page', false, await cdp.eval(() => !!document.querySelector('.chat-launcher-button')));
}

async function phaseLlmDown() {
    await desktop();
    await login(env.ADMIN_TOKEN, ADMIN_USER);
    await openPanel();
    await send('שלום');
    const error = await cdp.waitFor(() => !document.querySelector('.chat-panel .stop') && document.querySelector('.chat-error')?.textContent?.trim(), { timeout: 60000 });
    ok('LLM unreachable: the Hebrew error with the agent detail', !!error && error.includes('שירות העוזר לא זמין כרגע') && /LLM/.test(error), error);
    ok('LLM unreachable: a retry button', await cdp.eval(() => !!document.querySelector('.chat-error .retry')));
}

async function phaseAgentDown() {
    await openPanel();
    await send('שלום');
    const error = await cdp.waitFor(() => !document.querySelector('.chat-panel .stop') && document.querySelector('.chat-error')?.textContent?.trim(), { timeout: 20000 });
    ok('Agent unreachable: the network error with a retry button', !!error && error.includes('אין חיבור לשירות העוזר') && (await cdp.eval(() => !!document.querySelector('.chat-error .retry'))), error);
    check('Agent unreachable: the message is marked as not sent', 'לא נשלחה', await cdp.eval(() => [...document.querySelectorAll('.bubble.user')].pop()?.querySelector('.meta')?.textContent?.trim()));
}

async function phaseAgentBack() {
    const n = await assistantCount();
    await click('.chat-error .retry');
    check('Agent back: retry answers the same message', 'echo: שלום', (await waitAnswerOrError(n, 30000)).answer);
    ok('Agent back: the failed mark is gone', await cdp.eval(() => ![...document.querySelectorAll('.bubble.user')].pop()?.querySelector('.meta')));
    ok('Agent back (new agent process): the new-thread note', await cdp.eval(() => [...document.querySelectorAll('.note')].some(e => e.textContent.includes('השיחה הקודמת כבר לא זמינה'))));
}

async function phaseRateLimit() {
    await openPanel();
    await click('.new-chat');
    for (const text of ['אחת', 'שתיים']) {
        const n = await assistantCount();
        await send(text);
        await waitAnswer(n);
    }
    await send('שלוש');
    const error = await cdp.waitFor(() => !document.querySelector('.chat-panel .stop') && document.querySelector('.chat-error')?.textContent?.trim(), { timeout: 15000 });
    ok('429: a friendly wait message with the seconds (Retry-After)', /שלחת הרבה הודעות בזמן קצר\. אפשר לנסות שוב בעוד (\d+ שניות|שנייה)/.test(error || ''), error);
    ok('429: a retry button', await cdp.eval(() => !!document.querySelector('.chat-error .retry')));
}

// Phase 13: real logins through the login form of the production build (LOGINS: [{ role, user, password, id }], the
// synthetic accounts' personal number and phone), each followed by a chat turn through the gateway and a logout.
async function phaseProdLogin() {
    await desktop();
    const sameOrigin = url => url.startsWith(`${UI}/`);
    for (const account of JSON.parse(env.LOGINS ?? '[]')) {
        const who = account.role;
        await goto(`${UI}/login`);
        await cdp.eval(() => {
            localStorage.clear();
            sessionStorage.clear();
        });
        await goto(`${UI}/login`);
        let mark = requests.length;
        await cdp.eval((user, password) => {
            for (const [name, value] of [['username', user], ['password', password]]) {
                const input = document.querySelector(`input[name="${name}"]`);
                input.value = value;
                input.dispatchEvent(new Event('input', { bubbles: true }));
            }
            return true;
        }, account.user, account.password);
        await sleep(200);
        await click('button.accept');
        ok(`${who}: the login form leads to the personal page with a stored token`, await cdp.waitFor(() => location.pathname === '/personal-page' && !!localStorage.getItem('token'), { timeout: 20000 }));
        const loginRequest = requests.slice(mark).find(r => r.url.endsWith('/User/Login'));
        check(`${who}: the login goes to the gateway's /api/ (same origin)`, `${UI}/api/User/Login`, loginRequest?.url);
        const stored = await cdp.eval(() => ({ token: localStorage.getItem('token'), user: JSON.parse(localStorage.getItem('user') ?? '{}') }));
        check(`${who}: the stored user is the account (id / role)`, `${account.id}/${account.roleNumber}`, `${stored.user.id}/${stored.user.role}`);
        ok(`${who}: the launcher appears after the real login`, await cdp.waitFor(() => !!document.querySelector('.chat-launcher-button')));
        await openPanel();
        mark = requests.length;
        let n = await assistantCount();
        await send('שלום');
        check(`${who}: the chat answers through the gateway`, 'echo: שלום', await waitAnswer(n));
        const streamRequest = requests.slice(mark).find(r => r.url.endsWith('/chat/stream'));
        check(`${who}: the chat request goes to the gateway's /agent/api (same origin)`, `${UI}/agent/api/chat/stream`, streamRequest?.url);
        ok(`${who}: the chat request carries the token of the real login`, streamRequest?.headers?.Authorization === `Bearer ${stored.token}`);
        if (account.role === 'SuperAdmin') {
            n = await assistantCount();
            await send(`CALL update_user_account {"userId":${env.ROLE_TARGET_USER},"role":"Admin"}`);
            ok('SuperAdmin: a role change gets an approval card', await waitPendingCard(1));
            const card = await lastCard(c => ({ title: c.querySelector('.action-title').textContent.trim(), details: [...c.querySelectorAll('.details li')].map(e => e.textContent.trim()) }));
            ok('SuperAdmin: the card shows the role change (Regular -> Admin)', card?.details?.includes('תפקיד: משתמש רגיל ← מנהל'), JSON.stringify(card));
            await clickLast('app-chat-approval-card .reject-toggle button');
            await sleep(150);
            await setReason('בדיקת P13');
            await sleep(150);
            await clickLast('app-chat-approval-card .submit-decisions');
            const rejected = await waitAnswer(n, 30000);
            ok('SuperAdmin: rejected, the agent got the reason and nothing ran', rejected.includes('REJECTED') && rejected.includes('בדיקת P13'), rejected.slice(0, 160));
            check('SuperAdmin: the card shows the rejection', 'נדחה: בדיקת P13', await outcomes());
        }
        if (account.role === 'Regular') {
            n = await assistantCount();
            await send('CALL list_user_accounts {}');
            const answer = await waitAnswer(n, 30000);
            ok('Regular: an admin tool is not available (no card, the model is told the tool does not exist)', answer.includes('list_user_accounts is not a valid tool') && (await cdp.eval(() => document.querySelectorAll('app-chat-approval-card').length)) === 0, answer.slice(0, 160));
        }
        const apiOrAgent = requests.filter(r => /\/(api|agent)\//.test(new URL(r.url).pathname));
        check(`${who}: every API and agent request of the page went to the gateway origin`, '', apiOrAgent.filter(r => !sameOrigin(r.url)).map(r => r.url).join(' '));
        await click('mat-toolbar .logout');
        ok(`${who}: logout returns to /login and clears the token`, await cdp.waitFor(() => location.pathname === '/login' && localStorage.getItem('token') === null));
    }
}

// Phase 13: the agent is down behind the gateway, which answers 502 JSON (nginx.conf @agent_unavailable).
async function phaseGatewayAgentDown() {
    await openPanel();
    await send('שלום');
    const error = await cdp.waitFor(() => !document.querySelector('.chat-panel .stop') && document.querySelector('.chat-error')?.textContent?.trim(), { timeout: 20000 });
    ok("Agent down behind the gateway: the Hebrew 'unavailable' message with the gateway's detail", !!error && error.includes('שירות העוזר לא זמין כרגע') && error.includes('The Shabzak assistant service is not reachable.'), error);
    ok('Agent down behind the gateway: a retry button', await cdp.eval(() => !!document.querySelector('.chat-error .retry')));
    check('Agent down behind the gateway: the message is marked as not sent', 'לא נשלחה', await cdp.eval(() => [...document.querySelectorAll('.bubble.user')].pop()?.querySelector('.meta')?.textContent?.trim()));
}

async function screenshot(name) {
    const { data } = await cdp.send('Page.captureScreenshot', { format: 'png' });
    const { writeFileSync } = await import('node:fs');
    writeFileSync(`${env.SHOT_DIR}/${name}.png`, Buffer.from(data, 'base64'));
}

async function phaseScreenshots() {
    await desktop();
    await login(env.ADMIN_TOKEN, ADMIN_USER);
    await openPanel();
    await sleep(500);
    await screenshot('1-empty-state');
    let n = await assistantCount();
    await send('מי בשמירה מחר?');
    await waitAnswer(n);
    n = await assistantCount();
    await send('| משמרת | חיילים |\n|---|---|---|\n| 08:00–12:00 | 2/2 | אבי, דנה |');
    await waitAnswer(n);
    await send(`CALL request_vacation {"soldierId":8,"from":"${env.D1}","to":"${env.D2}"} && CALL request_vacation {"soldierId":8,"from":"${env.D3}","to":"${env.D3}"}`);
    await waitPendingCard(2);
    await lastCard(c => {
        c.querySelectorAll('.action')[1].querySelector('.reject-toggle button').click();
        return true;
    });
    await sleep(500);
    await screenshot('2-approval-cards');
    await clickLast('app-chat-approval-card .reject-all');
    await sleep(150);
    n = await assistantCount();
    await clickLast('app-chat-approval-card .submit-decisions');
    await waitAnswer(n, 30000);
    await sleep(500);
    await screenshot('3-after-decisions');
    await cdp.send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: true });
    await sleep(800);
    await screenshot('4-mobile');
    await desktop();
    await click('.close-chat');
    await sleep(500);
    await screenshot('5-closed');
}

const phases = {
    screenshots: phaseScreenshots,
    main: phaseMain,
    regular: phaseRegular,
    unauthorized: phaseUnauthorized,
    'llm-down': phaseLlmDown,
    'agent-down': phaseAgentDown,
    'agent-back': phaseAgentBack,
    ratelimit: phaseRateLimit,
    'prod-login': phaseProdLogin,
    'gateway-agent-down': phaseGatewayAgentDown,
};

try {
    if (!phases[phase]) throw new Error(`Unknown phase "${phase}"`);
    await phases[phase]();
    check('No uncaught exceptions in the page', 0, exceptions.length);
    check('No Angular sanitizer warnings (the markdown renderer escapes everything itself)', 0, warnings.filter(w => /sanitiz/i.test(w)).length);
    for (const e of exceptions) process.stderr.write(`[exception] ${String(e).slice(0, 500)}\n`);
} catch (error) {
    report('FAIL', `Phase ${phase} crashed`, 'no error', error?.stack ?? error);
} finally {
    cdp.ws.close();
}
