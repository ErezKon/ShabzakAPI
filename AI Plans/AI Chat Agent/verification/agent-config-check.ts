// Phase 13: runs ShabzakAgent's real config validation on this process's environment (compose-check.mjs passes the
// environment that docker-compose.yml produces) and prints one JSON line. Never prints the API key, only its length.
import { describeConfig, loadConfig } from '../../../../ShabzakAgent/src/config';

try {
    const config = loadConfig(process.env);
    console.log(JSON.stringify({
        ok: true,
        summary: describeConfig(config),
        port: config.port,
        api: config.shabzakApiUrl,
        cors: config.corsOrigins,
        model: config.llm.model,
        baseUrl: config.llm.baseUrl ?? null,
        temperature: config.llm.temperature,
        timeoutMs: config.llm.timeoutMs,
        timeZone: config.timeZone,
        logLevel: config.logLevel,
        keyLength: config.llm.apiKey.length,
    }));
} catch (error) {
    console.log(JSON.stringify({ ok: false, error: error instanceof Error ? error.message : String(error) }));
}
