// A scripted, OpenAI-compatible Chat Completions endpoint for the agent's live checks (verify-p7 / verify-p8).
// The dev network blocks api.openai.com, so the agent runs with LLM_BASE_URL=http://127.0.0.1:<port>/v1 and this stub plays
// the model. It does not reason: the last user message scripts it (the same mini-language as the agent's unit tests,
// ShabzakAgent/src/testing/scripted-model.ts directiveTurn):
//   CALL <tool> <json> [&& CALL <tool> <json>]... [THEN CALL ...]...   tool calls, one step per model turn
//   SLOW <ms> <rest>                                                  wait before answering (busy-thread / abort checks)
//   anything else                                                     answered "echo: <message>"
// After the last step it answers "done: <tool>=<result> | ..." with the tool results of the turn, so the scripts can read
// what the tools returned. Every request is appended to <logFile> as one JSON line: { tools, params, lastUser, system }
// (params: the parameter names of each offered tool).
// Usage: node fake-openai.mjs <port> [logFile]
import { appendFileSync } from 'node:fs';
import http from 'node:http';

const port = Number(process.argv[2] ?? 3902);
const logFile = process.argv[3];
let counter = 0;

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

function readBody(req) {
    return new Promise((resolve, reject) => {
        const parts = [];
        req.on('data', part => parts.push(part));
        req.on('end', () => resolve(Buffer.concat(parts).toString('utf8')));
        req.on('error', reject);
    });
}

const textOf = content => (typeof content === 'string' ? content : Array.isArray(content) ? content.map(p => p?.text ?? '').join('') : '');

/** Decides the reply from the conversation: { delayMs, text } or { delayMs, toolCalls: [{ name, args }] }. */
function decide(messages) {
    let lastUser = -1;
    messages.forEach((m, i) => {
        if (m.role === 'user') lastUser = i;
    });
    let text = lastUser >= 0 ? textOf(messages[lastUser].content) : '';
    let delayMs = 0;
    const slow = /^\s*SLOW\s+(\d+)\s*/.exec(text);
    if (slow) {
        delayMs = Number(slow[1]);
        text = text.slice(slow[0].length);
    }
    const steps = text
        .split(/\s+THEN\s+/)
        .map(step => step.split(/\s*&&\s*/).map(part => /^\s*CALL\s+([a-z_]+)\s+(\{.*\})\s*$/s.exec(part)))
        .filter(step => step.every(Boolean))
        .map(step => step.map(m => ({ name: m[1], args: JSON.parse(m[2]) })));
    if (steps.length === 0) return { delayMs, text: `echo: ${text}` };
    const after = messages.slice(lastUser + 1);
    const done = after.filter(m => m.role === 'assistant' && m.tool_calls?.length).length;
    if (done < steps.length) return { delayMs, toolCalls: steps[done] };
    const names = new Map(after.flatMap(m => (m.tool_calls ?? []).map(c => [c.id, c.function?.name])));
    const results = after.filter(m => m.role === 'tool').map(m => `${names.get(m.tool_call_id) ?? '?'}=${textOf(m.content)}`);
    return { delayMs, text: `done: ${results.join(' | ')}` };
}

function chunk(id, model, delta, finish = null) {
    return `data: ${JSON.stringify({ id, object: 'chat.completion.chunk', created: Math.floor(Date.now() / 1000), model, choices: [{ index: 0, delta, logprobs: null, finish_reason: finish }] })}\n\n`;
}

function streamReply(res, body, reply) {
    const id = `chatcmpl-fake-${++counter}`;
    const model = body.model ?? 'fake-model';
    res.writeHead(200, { 'Content-Type': 'text/event-stream', 'Cache-Control': 'no-cache', Connection: 'keep-alive' });
    res.write(chunk(id, model, { role: 'assistant', content: '' }));
    if (reply.toolCalls) {
        reply.toolCalls.forEach((call, index) => {
            const callId = `call_${counter}_${index}`;
            res.write(chunk(id, model, { tool_calls: [{ index, id: callId, type: 'function', function: { name: call.name, arguments: '' } }] }));
            res.write(chunk(id, model, { tool_calls: [{ index, function: { arguments: JSON.stringify(call.args) } }] }));
        });
        res.write(chunk(id, model, {}, 'tool_calls'));
    } else {
        for (const word of reply.text.match(/\S+\s*/g) ?? ['']) res.write(chunk(id, model, { content: word }));
        res.write(chunk(id, model, {}, 'stop'));
    }
    if (body.stream_options?.include_usage) {
        res.write(`data: ${JSON.stringify({ id, object: 'chat.completion.chunk', created: Math.floor(Date.now() / 1000), model, choices: [], usage: { prompt_tokens: 1, completion_tokens: 1, total_tokens: 2 } })}\n\n`);
    }
    res.end('data: [DONE]\n\n');
}

function jsonReply(res, body, reply) {
    const message = reply.toolCalls
        ? { role: 'assistant', content: null, tool_calls: reply.toolCalls.map((c, i) => ({ id: `call_${++counter}_${i}`, type: 'function', function: { name: c.name, arguments: JSON.stringify(c.args) } })) }
        : { role: 'assistant', content: reply.text };
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({
        id: `chatcmpl-fake-${++counter}`,
        object: 'chat.completion',
        created: Math.floor(Date.now() / 1000),
        model: body.model ?? 'fake-model',
        choices: [{ index: 0, message, finish_reason: reply.toolCalls ? 'tool_calls' : 'stop' }],
        usage: { prompt_tokens: 1, completion_tokens: 1, total_tokens: 2 },
    }));
}

http.createServer(async (req, res) => {
    try {
        if (req.method === 'GET' && req.url.endsWith('/models')) {
            res.writeHead(200, { 'Content-Type': 'application/json' });
            return res.end(JSON.stringify({ object: 'list', data: [{ id: 'fake-model', object: 'model' }] }));
        }
        if (req.method !== 'POST' || !req.url.endsWith('/chat/completions')) {
            res.writeHead(404, { 'Content-Type': 'application/json' });
            return res.end(JSON.stringify({ error: { message: 'not found' } }));
        }
        const body = JSON.parse(await readBody(req));
        const messages = body.messages ?? [];
        if (logFile) {
            const lastUser = [...messages].reverse().find(m => m.role === 'user');
            const system = messages.find(m => m.role === 'system' || m.role === 'developer');
            const tools = body.tools ?? [];
            const params = Object.fromEntries(tools.map(t => [t.function?.name, Object.keys(t.function?.parameters?.properties ?? {})]));
            appendFileSync(logFile, `${JSON.stringify({ tools: tools.map(t => t.function?.name), params, lastUser: textOf(lastUser?.content), system: textOf(system?.content) })}\n`);
        }
        const reply = decide(messages);
        if (reply.delayMs) await sleep(reply.delayMs);
        if (body.stream) streamReply(res, body, reply);
        else jsonReply(res, body, reply);
    } catch (error) {
        res.writeHead(500, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ error: { message: String(error?.message ?? error) } }));
    }
}).listen(port, '127.0.0.1', () => console.log(`fake-openai listening on ${port}`));
