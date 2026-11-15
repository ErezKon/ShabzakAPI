// Phase 13 static checks of the deployment files (no Docker needed): docker-compose.yml, the root .env.example, the
// Dockerfiles and .dockerignore files, the Node engines of both lockfiles and the UI's production URLs vs nginx.conf.
// Compose's ${VAR}, ${VAR:-default} and ${VAR:?error} substitution is emulated, and the agent environment it produces
// is fed to the agent's real loadConfig() (agent-config-check.ts, through tsx).
// Uses js-yaml and semver from Shabzak/node_modules (no new dependency). Prints one JSON check per line on stdout.
import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const workspace = path.resolve(here, '..', '..', '..', '..');
const ui = path.join(workspace, 'Shabzak');
const agent = path.join(workspace, 'ShabzakAgent');
const api = path.join(workspace, 'ShabzakAPI');
const uiRequire = createRequire(path.join(ui, 'package.json'));
const yaml = uiRequire('js-yaml');
const semver = uiRequire('semver');
const loadYaml = uiRequire('js-yaml/package.json').version.startsWith('3.') ? yaml.safeLoad : yaml.load;
const read = file => readFileSync(file, 'utf8');

function report(result, name, expected, actual) {
    process.stdout.write(`${JSON.stringify({ result, check: name, expected: `${expected}`, actual: `${actual}` })}\n`);
}
const check = (name, expected, actual) => report(`${expected}` === `${actual}` ? 'PASS' : 'FAIL', name, expected, actual);

/** Compose's substitution for the forms used here: $$, $VAR, ${VAR}, ${VAR:-d}, ${VAR-d}, ${VAR:?e}, ${VAR?e}. */
function interpolate(value, env, errors) {
    return value.replace(/\$\$|\$\{([A-Za-z_]\w*)(?:(:?[-?])([^}]*))?\}|\$([A-Za-z_]\w*)/g, (match, name, op, arg, bare) => {
        if (match === '$$') return '$';
        const key = name ?? bare;
        const v = env[key];
        const empty = v === undefined || v === '';
        if (op === ':-') return empty ? arg : v;
        if (op === '-') return v === undefined ? arg : v;
        if ((op === ':?' && empty) || (op === '?' && v === undefined)) {
            errors.push(`${key}: ${arg}`);
            return '';
        }
        return v ?? '';
    });
}

/** KEY=VALUE lines (comments and blank lines skipped), like compose's .env file. */
const parseEnvFile = text => Object.fromEntries(text.split(/\r?\n/).filter(l => /^\s*[A-Za-z_]\w*\s*=/.test(l)).map(l => {
    const i = l.indexOf('=');
    return [l.slice(0, i).trim(), l.slice(i + 1).trim()];
}));

const toMap = list => Object.fromEntries((list ?? []).map(entry => {
    const i = entry.indexOf('=');
    return [entry.slice(0, i), entry.slice(i + 1)];
}));

function effectiveEnvironments(compose, env) {
    const errors = [];
    const services = {};
    for (const [name, service] of Object.entries(compose.services)) {
        services[name] = Object.fromEntries(Object.entries(toMap(service.environment)).map(([k, v]) => [k, interpolate(v, env, errors)]));
    }
    return { services, errors };
}

const composeText = read(path.join(workspace, 'docker-compose.yml'));
let compose;
try {
    compose = loadYaml(composeText);
    check('docker-compose.yml parses as YAML', true, true);
} catch (error) {
    check('docker-compose.yml parses as YAML', true, error.message);
    process.exit(0);
}
check('Compose: the four services', 'shabzak-api,shabzak-ui,shabzak-agent,shabzak-sql', Object.keys(compose.services ?? {}).join(','));

const agentService = compose.services['shabzak-agent'] ?? {};
check('Agent service: build path (the absolute-path convention), name, restart', '/Shabzak/ShabzakAgent|shabzak-agent|always', `${agentService.build}|${agentService.container_name}|${agentService.restart}`);
check('Agent service: the other builds use the same /Shabzak/ parent', '/Shabzak/ShabzakAPI|/Shabzak/Shabzak', `${compose.services['shabzak-api']?.build}|${compose.services['shabzak-ui']?.build}`);
check('Agent service: ports 5232:3000, network ui_layer only (never the database network)', '5232:3000|ui_layer', `${(agentService.ports ?? []).join(',')}|${(agentService.networks ?? []).join(',')}`);
check('Agent service: depends_on shabzak-api', true, (Array.isArray(agentService.depends_on) ? agentService.depends_on : Object.keys(agentService.depends_on ?? {})).includes('shabzak-api'));
check('Agent service: runs as the image user (node), not root', 'undefined', `${agentService.user}`);
check('API and UI services keep their networks', 'db_layer,ui_layer|ui_layer', `${(compose.services['shabzak-api']?.networks ?? []).join(',')}|${(compose.services['shabzak-ui']?.networks ?? []).join(',')}`);

const envExampleText = read(path.join(workspace, '.env.example'));
const envExample = parseEnvFile(envExampleText);
const referenced = [...new Set([...composeText.matchAll(/\$\{([A-Za-z_]\w*)/g)].map(m => m[1]))].sort();
check('.env.example documents every variable docker-compose.yml references', referenced.join(','), referenced.filter(k => k in envExample).join(','));
check('.env.example has no variable that docker-compose.yml does not use', '', Object.keys(envExample).filter(k => !referenced.includes(k)).join(','));
check('.env.example holds no values (no secrets, no defaults to drift)', '', Object.entries(envExample).filter(([, v]) => v !== '').map(([k]) => k).join(','));

const bare = effectiveEnvironments(compose, envExample);
check('A .env copied from .env.example unchanged: compose refuses to start and names the two required values', 'SHABZAK_DB_CONNECTION_STRING,LLM_API_KEY', bare.errors.map(e => e.split(':')[0]).join(','));
check('The refusal tells what to do', true, bare.errors.every(e => e.includes('in .env (see .env.example)')));

const filled = { ...envExample, SHABZAK_DB_CONNECTION_STRING: 'Server=example;Database=ShabzakDB', LLM_API_KEY: 'test-key-not-real' };
const ok = effectiveEnvironments(compose, filled);
check('With the two required values: no substitution error', '', ok.errors.join(' | '));
const apiEnv = ok.services['shabzak-api'];
check('API env: TZ, the connection string from .env, an empty Jwt__Key (generated at start)', 'Asia/Jerusalem|Server=example;Database=ShabzakDB|', `${apiEnv.TZ}|${apiEnv.ConnectionStrings__ShabzakDB}|${apiEnv.Jwt__Key}`);
check('API env: JWT_KEY reaches Jwt__Key', 'k'.repeat(44), effectiveEnvironments(compose, { ...filled, JWT_KEY: 'k'.repeat(44) }).services['shabzak-api'].Jwt__Key);
const agentEnv = ok.services['shabzak-agent'];
check('Agent env (defaults)', JSON.stringify({ TZ: 'Asia/Jerusalem', SHABZAK_API_URL: 'http://shabzak-api:8080/api', CORS_ORIGINS: '', LLM_API_KEY: 'test-key-not-real', LLM_MODEL: '', LLM_BASE_URL: '', LLM_TEMPERATURE: '', LLM_TIMEOUT_MS: '', LOG_LEVEL: 'info' }), JSON.stringify(agentEnv));
const secretRefs = { 'shabzak-api': ['ConnectionStrings__ShabzakDB', 'Jwt__Key'], 'shabzak-agent': ['LLM_API_KEY'] };
check('Secrets appear only as ${...} references', true, Object.entries(secretRefs).every(([s, keys]) => keys.every(k => /^\$\{[A-Z_]+(:[-?][^}]*)?\}$/.test(toMap(compose.services[s].environment)[k]))));
const agentKnown = Object.keys(parseEnvFile(read(path.join(agent, '.env.example'))));
check('Agent env: only variables the agent knows (ShabzakAgent/.env.example)', '', Object.keys(agentEnv).filter(k => !agentKnown.includes(k)).join(','));

// The agent's real config validation on the environment compose produces (plus the image's ENV PORT=3000).
const dockerfileAgent = read(path.join(agent, 'Dockerfile'));
const imagePort = /PORT=(\d+)/.exec(dockerfileAgent)?.[1];
function agentConfig(env) {
    const base = Object.fromEntries(['SystemRoot', 'PATH', 'Path', 'TEMP', 'TMP', 'USERPROFILE'].filter(k => process.env[k]).map(k => [k, process.env[k]]));
    const run = spawnSync(process.execPath, [path.join(agent, 'node_modules', 'tsx', 'dist', 'cli.mjs'), path.join(here, 'agent-config-check.ts')], {
        cwd: agent,
        env: { ...base, PORT: imagePort, ...env },
        encoding: 'utf8',
    });
    try {
        return JSON.parse(run.stdout.trim().split('\n').pop());
    } catch {
        return { ok: false, error: `${run.stdout}${run.stderr}`.slice(0, 300) };
    }
}
const defaults = agentConfig(agentEnv);
check("The agent's loadConfig accepts the compose environment", true, defaults.ok ? true : defaults.error);
check('Agent config from compose (defaults): port, API, CORS, model, endpoint, temperature, timeout, TZ, log level',
    '3000|http://shabzak-api:8080/api||gpt-4.1|OpenAI API|0.2|60000|Asia/Jerusalem|info',
    `${defaults.port}|${defaults.api}|${(defaults.cors ?? []).join(',')}|${defaults.model}|${defaults.baseUrl ?? 'OpenAI API'}|${defaults.temperature}|${defaults.timeoutMs}|${defaults.timeZone}|${defaults.logLevel}`);
const overridden = agentConfig(effectiveEnvironments(compose, { ...filled, LLM_MODEL: 'gpt-4o', LLM_BASE_URL: 'https://llm-proxy.example/v1/', LLM_TEMPERATURE: '0.5', LLM_TIMEOUT_MS: '90000', AGENT_CORS_ORIGINS: 'http://localhost:5555, https://shabzak.example', AGENT_LOG_LEVEL: 'debug' }).services['shabzak-agent']);
check('Agent config from compose (every optional value set in .env)',
    'gpt-4o|https://llm-proxy.example/v1|0.5|90000|http://localhost:5555,https://shabzak.example|debug',
    `${overridden.model}|${overridden.baseUrl}|${overridden.temperature}|${overridden.timeoutMs}|${(overridden.cors ?? []).join(',')}|${overridden.logLevel}`);
check('The image port (ENV PORT) is the container port compose publishes', '3000', imagePort);

// Images.
const dockerfileUi = read(path.join(ui, 'Dockerfile'));
check('UI image: build stage node:22.23.4-alpine, runtime nginx:1.30.6-alpine', 'node:22.23.4-alpine AS build|nginx:1.30.6-alpine', [...dockerfileUi.matchAll(/^FROM\s+(.+)$/gm)].map(m => m[1].trim()).join('|'));
check('UI image: nginx.conf and the browser build are copied', true, dockerfileUi.includes('COPY nginx.conf /etc/nginx/nginx.conf') && dockerfileUi.includes('dist/shabzak/browser /usr/share/nginx/html'));
check('Agent image: node:22.23.4-slim, user node, EXPOSE 3000, a HEALTHCHECK', 'node:22.23.4-slim|node|3000|true',
    `${/^FROM\s+(\S+)/m.exec(dockerfileAgent)?.[1]}|${/^USER\s+(\S+)/m.exec(dockerfileAgent)?.[1]}|${/^EXPOSE\s+(\d+)/m.exec(dockerfileAgent)?.[1]}|${/^HEALTHCHECK /m.test(dockerfileAgent)}`);
const ignore = file => read(file).split(/\r?\n/).map(l => l.trim()).filter(l => l && !l.startsWith('#'));
const uiIgnore = ignore(path.join(ui, '.dockerignore'));
check('UI .dockerignore: node_modules, .angular, dist and .env stay out of the build context', true, ['node_modules/', '.angular/', 'dist/', '.env'].every(p => uiIgnore.includes(p)));
const agentIgnore = ignore(path.join(agent, '.dockerignore'));
check('Agent .dockerignore: node_modules and .env (the local OpenAI key) stay out of the image', true, ['node_modules/', '.env', '.env.*'].every(p => agentIgnore.includes(p)));
const apiIgnore = ignore(path.join(api, '.dockerignore'));
check('API .dockerignore: .env, bin and obj stay out of the image', true, ['**/.env', '**/bin', '**/obj'].every(p => apiIgnore.includes(p)));

function rejecting(lockFile, version) {
    const lock = JSON.parse(read(lockFile));
    return Object.entries(lock.packages ?? {})
        .filter(([key, pkg]) => key && pkg.engines?.node && !semver.satisfies(version, pkg.engines.node, { includePrerelease: true }))
        .map(([key, pkg]) => `${key.replace(/^.*node_modules\//, '')}@${pkg.version} (${pkg.engines.node})`)
        .filter((entry, i, all) => all.indexOf(entry) === i)
        .sort();
}
check('UI lockfile: no package rejects Node 22.23.4 (engines)', '', rejecting(path.join(ui, 'package-lock.json'), '22.23.4').join(', '));
check('UI lockfile: Node 18 (the old node:hydrogen image) is rejected by marked', true, rejecting(path.join(ui, 'package-lock.json'), '18.20.8').some(e => e.startsWith('marked@')));
check('Agent lockfile: no package rejects Node 22.23.4 (engines)', '', rejecting(path.join(agent, 'package-lock.json'), '22.23.4').join(', '));

// The production URLs of the UI match the nginx locations.
const prodEnv = read(path.join(ui, 'src', 'environments', 'environment.prod.ts'));
const nginx = read(path.join(ui, 'nginx.conf'));
check('environment.prod.ts: serverURL /api and agentURL /agent/api', '/api|/agent/api', `${/serverURL:\s*'([^']*)'/.exec(prodEnv)?.[1]}|${/agentURL:\s*'([^']*)'/.exec(prodEnv)?.[1]}`);
check('nginx.conf: a location for each production URL (and the SPA fallback)', '/,/api/,/agent/,@agent_unavailable', [...nginx.matchAll(/^\s*location\s+(\S+)/gm)].map(m => m[1]).join(','));
check('nginx.conf: upstreams are the compose service names and container ports', 'http://shabzak-api:8080|http://shabzak-agent:3000', [...nginx.matchAll(/^\s*set\s+\$\w+\s+(\S+);/gm)].map(m => m[1]).join('|'));
check('nginx.conf: Docker DNS resolver (per-request lookups)', true, /^\s*resolver 127\.0\.0\.11\b/m.test(nginx));
