# AI Chat Agent: implementation report, Phases 0–13

**Date:** 2026-11-12 (P0–P7), 2026-11-13 (P8–P12), 2026-11-15 (P13) · **Plan:** `Plan.md` (this folder), mirrored to `<workspace>/CHAT_AGENT_PLAN.md`; the progress tracker and deviations log are updated in both (the two files are identical).

| Phase | Status | Result |
|---|---|---|
| P0 Preparation and baseline | ✅ Done | Both repos are on `feature/ai-chat`, and both builds are green. JWT login was verified end to end on the API. The docs are corrected. |
| P1 Identity, role policies, account management | ✅ Done | Role policies, self-scope checks, PII redaction, and account management are in place. The UI hides edit controls from Regular users. 58/58 live checks pass. |
| P2 Query endpoints and constraint evaluator | ✅ Done | Four compact query endpoints, the pure constraint evaluator, and the xUnit project are in place. 58/58 unit tests and 53/53 live checks pass. |
| P3 Schedule mutation endpoints | ✅ Done | `Schedule/AssignSoldier`, `UnassignSoldier`, `ReplaceSoldier` (with swap) and `MoveSoldier`: validated through the evaluator, blocked on violations unless overridden, one `SaveChanges`, correct `IsFilled`. 63/63 live checks pass. |
| P4 Mission and instance mutation endpoints | ✅ Done | `CreateMission`, `UpdateMissionDetails` (never touches instances), `GenerateMissionInstances` (UI-equivalent, with dry run), `AddMissionInstances`, `UpdateMissionInstance`, `DeleteMissionInstance`, `Soldiers/SetSoldierActive`. 146/146 unit tests and 92/92 live checks pass. |
| P5 Agent service scaffold and infrastructure | ✅ Done | New repo `ShabzakAgent/` (Node 22, TypeScript, Express 5, LangChain 1.2.25): validated config, the OpenAI model factory, a typed client for every §4.3/§4.4 endpoint, the auth middleware, the utilities, the server (`/health`, `/api/me`) and the Docker/project files. 109 unit tests and 23/23 live checks pass. |
| P6 Agent core, read-only tools, prompt, dev CLI | ✅ Done, LLM check blocked | `defineTool`, nine role-scoped read tools, the registry, the system prompt (Shabzak topics only, §4.11), `createShabzakAgent` and the dev CLI. 170/170 unit tests (including the agent loop with a fake model) and 30/30 live tool checks pass. The LLM-answered acceptance questions are scripted (`verify-p6-llm.ps1`), but this network blocks `api.openai.com` (action item 6). |
| P7 SSE chat API, sessions, approvals, vacation tools | ✅ Done, LLM check blocked | `POST /api/chat/stream`, `/resume` and `/reset` with the §4.6 events, in-memory sessions (owner, TTL, one run per thread, pending approval), an approval middleware (our own: langchain 1.2.25's has a mixed-decision bug), server-built Hebrew approval cards, `request_vacation` / `respond_to_vacation`, `[audit]` lines. 52/52 live checks with `curl -N` pass, with a scripted OpenAI-compatible stub in place of the blocked OpenAI. |
| P8 Soldier and user-account tools | ✅ Done, LLM check blocked | `create_soldier` / `update_soldier` / `delete_soldier`, `create_user_accounts` / `update_user_account` (role field for SuperAdmins only) / `delete_user_account` / `reset_user_password` (default only), `list_user_accounts` and the audited `get_soldier_contact_details`; prompt workflows. After your decisions: deleting a soldier also deletes their login account (an API change, guarded), and the edit card masks the phone. 267/267 agent and 151/151 API unit tests and 52/52 live checks pass (same stub). |
| P9 Mission and instance tools | ✅ Done, LLM check blocked | `create_mission` / `update_mission` / `delete_mission`, `generate_mission_instances` (continuous like the missions screen, or day by day within the daily hours) / `add_mission_instance` / `update_mission_instance` (dry run on the card) / `delete_mission_instance`, `get_mission_instances`; the mission-creation workflow. One API addition: `Schedule/GetInstances` (shifts by ID for the cards). 39/39 live checks pass (same stub). |
| P10 Schedule tools and workflows | ✅ Done, LLM check blocked | `resolve_soldier_pool`, `get_schedule_coverage`, `run_auto_assign` (heartbeat, proposals summarized, cached per conversation without contact details), `list_schedule_candidates`, `get_candidate_details`, `accept_schedule_candidate`, `assign_soldier` / `unassign_soldier` / `replace_soldier` (with swap) / `move_soldier` (rules checked before asking), `suggest_replacements` (with swaps); the CREATE / FIX-CHANGE / SUGGEST workflows. **API bug fixed:** accepting an auto-assign proposal duplicated existing assignments (action item 13). `AutoAssign` during an interactive session → 409. 328/328 agent and 156/156 API unit tests and 41/41 live checks pass (same stub). |
| P11 UI chat widget and streaming client | ✅ Done, LLM check blocked | `agentURL` in every environment; the launcher (bottom-right, approval badge, hidden when logged out or on `/login`), the RTL panel (400×600, full screen on mobile; status line, Stop, counter, Enter / Shift+Enter), message bubbles with **hardened** markdown (`marked` 18.1.3: no raw HTML, images or links), a `fetch` + SSE client (pure SSE parser, abortable, `EMPTY` under SSR) and the chat state service. After your decision, the UI's dev `serverURL` uses the API's `http` profile, like the agent. `npm run build` passes with the SSR prerender. |
| P12 UI approval cards, refresh, persistence, polish | ✅ Done, LLM check blocked | Approval cards (per-action approve / reject with an optional reason, "אשר הכל" / "דחה הכל", risk chip, warnings), NgRx reloads from the refresh hints + "הנתונים עודכנו", per-user `sessionStorage` history (survives a reload, also with a pending approval; cleared on logout, user change and New chat), transparent new thread on `thread_not_found`, the 401 / network / 429 / `approval_pending` UX, `aria-live`, Escape, unread dot, suggestion chips per role. After your decision: a toolbar **logout** button and action. 81/81 Karma specs and **107/107 live checks** in headless Chrome against the real agent (same stub) pass. |
| P13 Integration, deployment, end-to-end acceptance | ✅ Done, Docker run and LLM check open | `docker-compose.yml` runs the agent as `shabzak-agent` (`5232:3000`) and finally gives the API its connection string and JWT key, all from a root `.env` (new `.env.example`). After your decisions, the UI container's nginx proxies `/api/` and `/agent/` on its own origin (the production `agentURL` stays `/agent/api`, no CORS needed), and the images use current pinned versions (`node:22.23.4`, `nginx:1.30.6`). Two deployment bugs found and fixed (a 301 on every prerendered route; no `.dockerignore`). Docker isn't installed here, so the real `nginx.conf` ran on nginx 1.30.6 for Windows in front of the local stack: **234/234 checks**, including the P7–P10 chat suites (184) with every request through it. The real-LLM checklist is scripted (`verify-p13-llm.ps1`). |

> **P0–P4 committed, not pushed.** At your request, all P0–P4 work is one commit per repo, made on `feature/ai-chat` and fast-forwarded into `opus-refactor` (both branches point at it). The `ShabzakAPI` commit includes `AI Plans/`; `.github/` stays untracked.
>
> **P5–P13 are not committed** (the plan commits only with your approval):
> - `ShabzakAgent/`: a new repository (`git init`, branch `main`, no commit yet). `.env` holds your OpenAI key and is gitignored; `node_modules/` is ignored. P11–P12 changed only its `AI_CONTEXT.md`.
> - `ShabzakAPI`: `AI Plans/` (plan, this report, `start-api-localdb.ps1`, new `verify-p5.ps1`, `verify-p6.ps1`, `verify-p6-llm.ps1`, `verify-p7.ps1`, `verify-p8.ps1`, `verify-p7-p8-llm.ps1`, `verify-p9.ps1`, `verify-p10.ps1`, `verify-p11-p12.ps1`, `ui-e2e.mjs`, `agent-http.ps1` and `fake-openai.mjs`) and `AI_CONTEXT.md`. Code: the soldier-delete cascade (deviation 31) in `BL/Services/SoldierService.cs`, `BL/Services/UserAccountGuard.cs`, `Controllers/SoldiersController.cs`, `Controllers/UserController.cs`, the new `Controllers/AccountResponseExtensions.cs`, and `ShabzakAPI.Tests/UserAccountGuardTests.cs`; P9–P10 (deviations 32 and 35): `Controllers/ScheduleController.cs`, `Controllers/MissionController.cs`, `BL/Services/ScheduleService.cs`, `BL/Services/AutoAssignService.cs`, and the new `ViewModels/GetScheduleInstancesRequest.cs`, `BL/Services/CandidateAcceptanceFilter.cs`, `BL/Models/AutoAssignConflictException.cs`, `ShabzakAPI.Tests/CandidateAcceptanceFilterTests.cs`.
> - `Shabzak` (P11–P12, on `opus-refactor`): `package.json` / `package-lock.json` (`marked` 18.1.3), `src/environments/*.ts`, `app.component.{ts,html,scss}` (chat hosts, logout button), `state-management/actions/user.actions.ts` (`logout`), `effects/user.effects.ts` (`logout$`), `reducers/user.reducer.ts`, `reducers/index.ts` (`resetOnLogout`), `reducers/missions.reducer.ts` (candidates prune), `services/user.service.ts` (`logout()`), the new `components/chat/**`, `services/chat/**`, `models/chat/chat.model.ts`, `utils/sse-parser.ts`, `utils/markdown.ts` and their specs (incl. `reducers/reset-on-logout.spec.ts`), `README.md`, `AI_CONTEXT.md`.
> - Workspace root (not a repo): `CHAT_AGENT_PLAN.md`, `UNIFIED_AI_CONTEXT.md`.
> - **P13 added:**
>   - `ShabzakAPI`: `README.md`, `AI_CONTEXT.md`, the plan and this report; in `verification/`: `agent-http.ps1` (gateway mode), `ui-e2e.mjs` (two production phases, failed-response diagnostics), and the new `get-nginx.ps1`, `gateway.ps1`, `compose-check.mjs`, `agent-config-check.ts`, `p13-http.mjs`, `verify-p13.ps1`, `verify-p13-llm.ps1`. No API code changed.
>   - `Shabzak`: `nginx.conf`, `Dockerfile`, the new `.dockerignore`, `README.md`, `AI_CONTEXT.md`. No source change.
>   - `ShabzakAgent`: `Dockerfile` (pinned image), `.env.example` (one comment line), `README.md`, `AI_CONTEXT.md`. No source change.
>   - Workspace root: `docker-compose.yml`, the new `.env.example`, `CHAT_AGENT_PLAN.md`, `UNIFIED_AI_CONTEXT.md`.

---

## ⚠ Action items for you

1. **Rotate the Azure SQL password now.** The password literal is in **pushed** git history of `github.com/ErezKon/ShabzakAPI`: commit `52dd597` (on `origin/main` and `origin/opus-refactor`) and commit `ab09ca8` (on `origin/opus-refactor`), both in `DataLayer/ShabzakDB.cs`. Removing it from the working tree doesn't revoke it. After rotating:
   - Update the local secret: `dotnet user-secrets set "ConnectionStrings:ShabzakDB" "<new>" --project 04.ShabzakAPI`.
   - Optionally scrub history with `git filter-repo` or BFG. That rewrites history, so it needs your explicit decision.
2. **Review and push** the P0–P4 commit on `opus-refactor` in both repos when you're ready. Nothing is pushed.
3. **Run a smoke test with real accounts.** The production DB was unreachable from this network (TCP 1433 connects, but the TDS pre-login handshake times out, which looks like a corporate firewall). On a network that can reach Azure SQL:
   - Log in as an Admin and a Regular user (UI `npm start`, API `dotnet run --launch-profile http`: since P11 the UI's dev `serverURL` is `http://localhost:5165/api`, deviation 39).
   - Check that `/assignments` is read-only for the Regular user, and that the Admin's "הסר חייל" now works (it always failed before P3, see deviation 9).
   - Check that the personal page works (summary, vacations, password change).
   - Missions page (P4 verify step): create, edit and delete a mission in the UI. The API side of these legacy calls passed on the dev DB, but no browser test was possible here.
   - The toolbar's new logout button (P12): it should return to `/login` and stay there.
4. **Check the DB compatibility level.** EF Core 8 turns `list.Contains()` into `OPENJSON`, which needs compatibility level ≥ 130. Azure SQL defaults to 150+. Check with `SELECT compatibility_level FROM sys.databases WHERE name = DB_NAME()`.
5. **Stash `stash@{0}`** on `opus-refactor` (`pre-ai-chat local dev changes …`) holds your three uncommitted files. Its LoadCSV changes write **decrypted soldier PII** to `DataLayer/soldiers2.txt`. Decide whether to keep or drop it; I did not drop it.
6. **Run the LLM acceptance (P6, P7, P8) from a network that allows OpenAI.** Your OpenAI key is in `ShabzakAgent/.env`, but this machine's corporate firewall blocks `api.openai.com`. Every request, with or without a key, gets a `302` to a URL-filter block page (`http://172.66.0.243:6080/php/urlblock.php`); PowerShell gets the same answer. The agent now says so clearly instead of timing out (in the chat stream too: an `upstream_error` event "…proxy or firewall…").
   - Run `verification/verify-p6-llm.ps1`, with the API on `start-api-localdb.ps1 -LaunchProfile http`. It asks the plan's questions (tomorrow-morning guard, "my next mission", the hours of אבי פרץ this month, who is available for the tomorrow-morning patrol) plus a Regular user asking an admin question, four off-topic requests (market advice, general knowledge, coding, a jailbreak) and a mixed request. It checks the tools used, the facts and the §4.11 fixed replies, and writes `p6-llm-transcript.log`.
   - Then run `verification/verify-p7-p8-llm.ps1` (same prerequisites). Through the chat API it checks that a vacation request goes straight to an approval card (no "should I?" question first) and is created only after approval, that "add a soldier" with missing details gets a question instead of a tool call, that an Admin asking for a role change is told a SuperAdmin must do it while a SuperAdmin gets the card (rejected by the script), that a phone number is given only when asked and never in a soldier table, that account listings have no hashes, and that a Regular user can't approve anything. It writes `p7-p8-llm-transcript.log` and deletes its test vacation.
   - **P9/P10 have no real-LLM script yet** (it couldn't be run or debugged here). Once OpenAI is reachable, try these in `npm run chat:cli` as an Admin: "צור משימה חדשה בשם מארב" (it should ask for the slots, shift length and hours in one question), "תאריך את הסיור לשבוע הבא" (it should look at the existing shifts and pick day-by-day for a 06:00–22:00 patrol), "תבנה שבצ"ק לשבוע הבא למחלקה 1" (resolve → coverage → one auto-assign card → the best proposal → accept card), "תעביר את אבי מהשמירה לסיור" (a move card, or a Hebrew explanation of the broken rule), "מי יכול להחליף את אבי בשמירה מחר?" (suggestions with reasons). It must never set `allowViolations` unless you explicitly ask to override.
   - **The same questions in the UI (P11–P12).** The widget is verified end to end with the scripted stub (107/107), but never with the real model. With OpenAI reachable, run the API (`start-api-localdb.ps1 -LaunchProfile http`), the agent (`npm run dev` in `ShabzakAgent/`) and the UI (`npm start`), log in, and repeat a few of the questions above in the chat panel: the answers should stream, an approval card should appear for a change, and the page behind the panel should reload after you approve.
   - **The P13 checklist: `verification/verify-p13-llm.ps1`** (same prerequisites, plus ports 5230 and 3901 free). It runs the plan's end-to-end checklist through the production path (the UI's `nginx.conf` on nginx, `/agent/api`; `-Direct` skips it), in Hebrew and English with Admin, SuperAdmin and Regular: schedule, own-data and fairness questions; six off-topic requests and two mixed ones; generate shifts, auto-assign then accept, "move X from shift A to B", suggest a replacement then apply it; create (after asking for the missing details), update and delete a soldier; the Admin vs SuperAdmin role change (rejected); a new mission with missing details; deleting a shift (rejected); and the security checks (no card for a Regular user, API 403, no hashes, a phone only when asked, another user's thread → 403). It builds its own synthetic mission for tomorrow, approves a card only when it is the expected tool, and removes everything at the end. Its mechanics were checked here with `-Stub` (setup, the API and security checks and the cleanup pass; the model-behavior checks fail by design). Read `p13-llm-transcript.log` for the wording.
   - Alternatives: ask IT to allow `api.openai.com` for this machine, or set `LLM_BASE_URL` to an OpenAI-compatible endpoint that is allowed.
   - `LLM_MODEL` is empty in your `.env`, so the default `gpt-4.1` applies. Whether your key can use it couldn't be checked from here.
7. **Confirm two P4 semantics** (both implemented as the plan says; easy to change):
   - `UpdateMissionInstance` blocks only on `OVERLAP`. A move into an assigned soldier's **vacation** or below the **rest** minimum is reported in `validations` but not blocked. If vacations should block too, it's a one-line change (`BlockingPolicy.Reschedule`).
   - In `GenerateMissionInstances`, a date-only `to` means "that date at the mission's `ToTime`", as in the UI dialog. For a round-the-clock mission (`00:00`→`00:00`), "to 2026-11-21" therefore stops at the start of the 21st. ✅ Handled in the agent (P9): its tools take the **last day, included**, and send exact datetimes, so a round-the-clock or overnight mission covers its last day; the card shows the resolved period and the last shift. The API keeps the UI's semantics.
8. **Approve the P5–P13 commits** (or tell me how you want them split, e.g. one per phase): the first `ShabzakAgent` commit (and which branch it should live on; it is `main` now, renameable before the first commit), the API and docs/verification changes in `ShabzakAPI`, and the P11–P13 changes in `Shabzak` (on `opus-refactor`, list in the box above). `docker-compose.yml` and the root `.env.example` live in the workspace root, which is not a repository: copy them to the deploy host's `/Shabzak` yourself.
9. **Run the containerized stack once** (P13). Docker isn't installed on this machine (neither on Windows nor in its WSL), so no image has been built yet. On a machine with Docker:
   - In the workspace root: `cp .env.example .env`, then fill in `SHABZAK_DB_CONNECTION_STRING`, `LLM_API_KEY` and `JWT_KEY` (`openssl rand -base64 32`). Compose refuses to start while either required value is empty.
   - `docker compose up --build`. Expect four containers. `docker compose ps` should show `shabzak-agent` as healthy (image HEALTHCHECK), and `docker logs shabzak-agent` should start with `ShabzakAgent listening on port 3000 (… api=http://shabzak-api:8080/api cors=(none) …)`.
   - Open `http://<host>:5230`, log in as an Admin, and ask the assistant something. The UI calls only `:5230/api/…` and `:5230/agent/api/…`. Approve one change and check that the page behind the panel reloads.
   - `docker compose stop shabzak-agent`: the chat should say "שירות העוזר לא זמין כרגע" with "The Shabzak assistant service is not reachable.", while the rest of the UI keeps working. `docker compose start shabzak-agent`, then "נסה שוב" should answer on a new thread.
   - What was verified here instead: the compose file and the environment it produces (including through the agent's real config validation), the image settings, and the UI's real `nginx.conf` on the same nginx version, in front of the local API and agent (`verify-p13.ps1`, 234/234). Not verified: the image builds themselves, and Docker's DNS name resolution (`127.0.0.11`), which this setup can't exercise.
10. ✅ **Decided: mask the phone on the edit card.** `update_soldier` now masks the phone on both sides (deviation 28). Still shown in full, as before: the new personal number on the edit card, and the typed phone and personal number on the `create_soldier` card. Say if you want those masked too.
11. ✅ **Decided: deleting a soldier deletes their login account.** This is done in the API (`Soldiers/DeleteSoldier`), so the UI's delete cascades too (deviation 31). It follows the account guard: when the user may not delete that account (a SuperAdmin's account for an Admin, or their own), nothing is deleted and the API answers 403 / 400 (409 for the last enabled SuperAdmin). The UI then shows its generic "שגיאה בהסרת חייל", and the agent refuses before the card.
12. **Re-seed the dev database when you next run the date-dependent suites.** The synthetic seed is relative to its day (2026-11-12). On 2026-11-13 `verify-p6.ps1` gives 29/30: "Large schedule result is capped" needs ≥ 60 instances in the 7 days from today. Dropping and re-seeding (`sqlcmd … DROP DATABASE ShabzakAgentDev`, then `seed-localdb.ps1`) restores it; I didn't drop the database without asking. The P7/P8 suites compute their expectations from the API and don't depend on the seed day; the P9/P10 suites use their own synthetic missions 12–20 days ahead. On 2026-11-13 `verify-p2.ps1` (3), `verify-p3.ps1` (14) and `verify-p4.ps1` (6) also fail only their checks that need "tomorrow's guard 08–12 shift" to be staffed (the seed staffed 11-13's, instance 21; "tomorrow" is now 11-14, instance 27, empty). `verify-p1.ps1` passes 58/58.
13. **Check the production data for duplicate assignments (P10 fix, deviation 35).** Before P10, accepting an auto-assign proposal (in the UI's assignments-management screen too) inserted the existing assignments of every shift the run had skipped **a second time**. If proposals were accepted in production over shifts that already had soldiers, the same soldier may appear twice in a shift. Find them with `SELECT SoldierId, MissionInstanceId, COUNT(*) FROM SoldierMission GROUP BY SoldierId, MissionInstanceId HAVING COUNT(*) > 1`. Removing the extra rows is a data change for you to decide; I did not touch production. New acceptances are safe, including of proposals saved before the fix.
14. **The UI's auto-assign error (P10, deviation 35).** `Mission/AutoAssign` now answers **409** `{ message }` (was 500) while an interactive session is open. The UI behaves the same for both: `autoAssign$` logs the error to the browser console and `autoAssignFailure` only clears the loading state, so the user sees no message. Showing one (e.g. a snackbar with the 409's `message`) would be a small UI change, if you want it.
15. ✅ **Decided (P11): local dev runs on the API's `http` profile.** The UI's dev `serverURL` is now `http://localhost:5165/api` (deviation 39), so the UI and the agent share one API process. Start the API with `dotnet run --launch-profile http` (or plain `dotnet run`, whose default is the same profile). The `https` profile now leaves the UI without an API in dev.
16. ✅ **Decided (P12): a logout button.** The toolbar has a logout button (icon `logout`, title "התנתק") that clears the token, the user, the store data and the chat (deviation 43). If you'd rather have it elsewhere (e.g. in the sidenav), that's a template-only change.
17. ✅ **Decided (P13): the UI container's nginx proxies `/api/` and `/agent/`.** The production `agentURL` stays `/agent/api`. There was no proxy for `/api` anywhere either (deviation 49), so the containerized UI couldn't log in before. If you also run a proxy of your own in front of port 5230 (e.g. for TLS), it only needs to forward everything to 5230 without buffering `/agent/` (nginx-based proxies honor the agent's `X-Accel-Buffering: no`) and with reads of at least 60 s.
18. **The assignments-management proposals dialog (optional follow-up).** "הצג הצעות שיבוץ" lists only the proposals of the UI's own auto-assign run in this browser session: `getAllCandidates` fetches just the IDs. So proposals the agent created don't show up there (accept them in the chat), and after a reload the dialog is empty. P12 made the `candidates` refresh drop proposals that are no longer pending from that list (deviation 44), so it can't offer a discarded one. Loading the missing proposals with `GetCandidate` (about 3 MB each) would be a separate UI change, if you want it.
19. **Deployment hardening to consider (P13; nothing here was changed without your decision):**
   - The SQL container's `sa` password is still written in `docker-compose.yml` (`MSSQL_SA_PASSWORD=P@ssw0rd`, pre-existing). If the API uses that container, move it to the root `.env`. Note that the SQL image applies it only when the data folder is first created: an existing volume keeps its old password.
   - The agent's `[audit]` lines go to the container's stdout, and Docker deletes them when compose recreates the container (every `docker compose up --build` after a change). If you need the audit trail, configure a logging driver that ships them elsewhere, or ask for a file sink in the agent (`utils/audit.ts` already has a pluggable writer).
   - The stack serves plain HTTP (5230), and the API (5231) and the agent (5232) are also published on the host, although the UI reaches both through 5230. On an exposed host, put TLS in front of 5230 and consider not publishing 5231 / 5232.
20. **Set `JWT_KEY` in the root `.env` (P13).** Without it, the API generates a new signing key at every container start, so each restart or redeploy logs every user out (the chat agent then answers 401 and the UI sends them to the login page).

---

## Phase 0: Preparation and baseline

**Git and secrets (ShabzakAPI):**
- I moved the hardcoded connection string from your uncommitted `ShabzakDB.cs` into **user-secrets** (`ConnectionStrings:ShabzakDB`; the csproj already has a `UserSecretsId`).
  - The value was piped as JSON on stdin, so it never appeared in output, logs, or the process command line.
- I stashed the three modified files (`SoldiersController.cs`, `SoldierService.cs`, `ShabzakDB.cs`) as `stash@{0}`. Untracked `AI Plans/` and `.github/` were left alone.
- I created `feature/ai-chat` from `opus-refactor` (`a68dc79`).

**Git (Shabzak UI):** `git switch -c opus-refactor --track origin/opus-refactor`, then I created `feature/ai-chat` (`15ab88b`). The working tree was clean.

**Baseline:**
- `dotnet build ShabzakAPI.sln`: 0 errors (128 pre-existing nullable warnings).
- `npm ci` followed by `npm run build`: passes, including SSR prerender of 7 routes.
- Live checks against isolated LocalDB with synthetic data (see "Verification environment"):
  - `/health` returns 200.
  - Requests without a token, or with a bad token, get 401.
  - A valid JWT gets 200.
  - **A real login** (`User/Login` with SHA-512 hashed synthetic credentials) returned a JWT with the expected claims (`sub, jti, name, role, enabled, activated, soldierId, exp, iss, aud`), and that token worked on protected endpoints.

**Docs:** `UNIFIED_AI_CONTEXT.md`, `ShabzakAPI/AI_CONTEXT.md`, and `Shabzak/AI_CONTEXT.md` were corrected for:
- JWT: `JwtHelper`, `LoginResponse`, `[Authorize]`, `/health`, and the config keys table
- PBKDF2 hashing and the legacy upgrade, plus AES v2 random IVs
- The UI interceptor and token storage, `super-admin.guard`, and `/assignments-management` → `adminGuard`
- The plan pointer and the branch note

**Plan:** copied to `<workspace>/CHAT_AGENT_PLAN.md`.

---

## Phase 1: API identity, role policies, account management

### New files (ShabzakAPI)
| File | Purpose |
|---|---|
| `04.ShabzakAPI/Auth/Policies.cs` | `AdminOrAbove` / `SuperAdminOnly` (`RequireRole`), registered with `AddAuthorization(Policies.Configure)` |
| `04.ShabzakAPI/Auth/ClaimsPrincipalExtensions.cs` | `GetUserId` (NameIdentifier/`sub`), `GetSoldierId`, `GetHashedUsername`, `IsAdminOrAbove`, `IsSuperAdmin`, `GetRole` |
| `04.ShabzakAPI/Auth/TokenRevalidation.cs` | JwtBearer `OnTokenValidated`: deleted or disabled account → 401; the role claim is replaced with the current role *(addition, see deviations)* |
| `04.ShabzakAPI/ViewModels/UpdateUserRequest.cs` | `{ userId, role?, enabled?, activated? }` |
| `BL/Models/Users/MeDto.cs`, `UserAccountDto.cs` | Whitelisted DTOs that never include the hashed username, password, or salt |
| `BL/Services/UserAccountGuard.cs` | Pure guard rules, `AccountChange`, `AccountGuardResult`, and `UserAccountException` |

### Changed files
- **`Program.cs`:**
  - Policies, plus a Swagger **Bearer** security scheme (the "Authorize" button)
  - `OnTokenValidated` hook
  - `UserService(usersCache, soldiersCache)`
- **Controllers:** `[Authorize(Policy = Policies.AdminOrAbove)]` is set explicitly on every admin action: 17 in Mission, 6 in Soldiers, 5 in User, and 3 in Metadata.
- **Self-scope (403 otherwise):**
  - `GetSummary`
  - `RequestVacation`
  - `GetVacations`: forced to the caller's own soldier, or an empty list if they have none
  - `ResetPassword`: own account only for non-admins. An unknown username also returns 403, so usernames can't be probed.
- **`GetSoldiers`:** non-admins get **copies** with phone and personal number blanked. Vacations are kept only on their own soldier. Cache objects are never mutated.
- **New endpoints** `User/Me`, `GetUsers`, `UpdateUser`, `DeleteUser`, and `ResetPasswordToDefault`, backed by `UserService`:
  - `GetMe`, `GetAllUsers`, `UpdateUser`, `DeleteUser`, `ResetPasswordToDefault`
  - `EnsureCanResetPassword`, `HasAccount`
  - Guard failures return `GeneralResponse { success:false }` with **403** (permission), **400** (self change), **409** (last SuperAdmin), or **404**.
- **`UsersCache`:**
  - One lock object and a by-ID index
  - `GetAllUsers`, `GetUserById` (DB fallback), `AddOrReplaceUser` (replace-on-update), `RemoveUser`
  - The DB lookup in `GetUser` was moved out of the lock.
- **`CreateUsersForSoldiers`:** now AdminOrAbove, and it skips soldiers that already have an account.
- **UI (`Shabzak`):**
  - `AssignmentsByDayComponent` shows the `+` trigger and the add/remove/replace menu items only when `user.role >= UserRole.Admin`. The handlers also do nothing for non-admins.
  - The password-change bug fix is listed under deviations.

### Verification: `verification/verify-p1.ps1`, **58/58 PASS**
- A Regular token gets **403 on all 31 admin endpoints** and 200 on the reads.
  - `GetSoldiers` returns no phone or personal number, and no vacations for other soldiers.
  - `GetSummary`/`RequestVacation` for another soldier return 403.
  - `GetVacations` is forced to the caller's own rows. Own-password change and login with the new password work.
- An Admin gets 403 for:
  - a role change
  - disabling, deleting, or `ResetPasswordToDefault` on a SuperAdmin
  - `ResetPassword` on a SuperAdmin's username
- An Admin gets 400 for self-disable and self-delete. An unknown user gets 404, and an empty update gets 400.
- A SuperAdmin role change succeeds and can be reverted. A SuperAdmin gets 400 for self-demote, self-disable, and self-delete, and an invalid role value gets 400.
- **Revalidation:**
  - A forged or stale `Admin` claim on a Regular account gets 403.
  - A token for a non-existent user gets 401.
  - Disabling an account gives 401 immediately, and re-enabling gives 200 again.
- `CreateUsersForSoldiers` is idempotent: one account created, then 0 on the second call. `DeleteUser` works, and `ResetPasswordToDefault` followed by a default-credential login works.
- Admin flows still work: `GetAvailableSoldiers`, Metadata, candidates.
- **Last enabled SuperAdmin:** live, this is unreachable by design. An enabled SuperAdmin acting on another SuperAdmin means at least two exist, and self-changes are blocked first. So it's covered by unit tests (`UserAccountGuardTests`).
- `dotnet build` passes with no new warnings, and `npm run build` passes.

---

## Phase 2: query endpoints and constraint evaluator

### New files (ShabzakAPI)
| File | Purpose |
|---|---|
| `BL/Models/Schedule/ScheduleInstanceDto.cs` | `ScheduleInstanceDto`, `ScheduleAssignmentDto`, `MissingPositionDto` |
| `BL/Models/Schedule/AssignmentValidationDto.cs` | `AssignmentValidationDto`, `OverlapInfoDto`, `VacationInfoDto`, `PositionFitKind`, `AssignmentConstraintCodes` |
| `BL/Models/Schedule/MissionSummaryDto.cs` | `MissionSummaryDto`, `MissionPositionDto` |
| `BL/Models/Schedule/SoldierDtos.cs` | `SoldierLiteDto`, `SoldierDetailsDto` (with `From` mappers) |
| `BL/Models/Schedule/ScheduleDateFormat.cs` | ISO local format `yyyy-MM-ddTHH:mm:ss` |
| `BL/Services/AssignmentConstraintEvaluator.cs` | **Pure** evaluator implementing §4.5 (input records `Constraint*`) |
| `BL/Services/ScheduleInstanceMapper.cs` | Pure mapping from a DB instance to `ScheduleInstanceDto` |
| `BL/Services/ScheduleService.cs` | Singleton: `GetAssignments`, `ValidateAssignments`, `GetMissionsSummary(ids?)` |
| `04.ShabzakAPI/Controllers/ScheduleController.cs` | `POST Schedule/GetAssignments` (any authenticated user), `POST Schedule/ValidateAssignments` (Admin+) |
| `04.ShabzakAPI/ViewModels/GetScheduleAssignmentsRequest.cs`, `ValidateAssignmentsRequest.cs` | Request bodies as defined in §4.3 |
| `ShabzakAPI.Tests/` (in the sln) | xUnit net8.0 → BL. Template packages from 2023: xunit 2.5.3, Test.Sdk 17.8.0, coverlet 6.0.0 |

### Endpoints added
- **`GET Mission/GetMissionsSummary`** (any authenticated user): one Include query for missions and positions, plus **grouped SQL** for instance stats and assignment counts. No instance graph is loaded.
- **`GET Soldiers/GetSoldiersLite?includeInactive=false`** (any authenticated user): reads from the cache; no contact details.
- **`GET Soldiers/GetSoldierById`** (self, or Admin+): includes phone, personal number, and `hasUserAccount`. Returns 404 if unknown. Each access is logged with IDs only.
- **`POST Schedule/GetAssignments`:**
  - **One SQL-filtered query** (`FromTime < to && ToTime > from`, plus the mission and soldier filters) that includes `Mission.MissionPositions` and `Soldiers.MissionPosition`.
  - Mission names are decrypted, soldier names come from the cache, and results are sorted by `from`.
  - `isFilled` is computed **live**, because the stored flag can be stale.
  - Returns 400 when the range is over 62 days or `to <= from`.
- **`POST Schedule/ValidateAssignments`:**
  - Loads the instance, then the soldiers' assignments in a window of max(7 days, the largest `RequiredRestAfter` + 24h), plus overlapping non-Denied vacations, and passes them to the evaluator.
  - Soldier name, positions, and active flag come from the cache.
  - Errors: more than 1000 soldiers or a slot from another mission → 400; unknown instance → 404; unknown soldier → a `NOT_FOUND` entry.

### Evaluator rules (§4.5)
- **Codes:** `INACTIVE`, `ALREADY_ASSIGNED`, `OVERLAP`, `ON_VACATION`, `INSTANCE_FULL`, `POSITION_MISMATCH`, `REST_BEFORE`, `REST_AFTER`, and `NOT_FOUND`, in a deterministic order. Warning: `SIMILAR_POSITION`.
- **Overlap** uses half-open intervals, so back-to-back assignments don't overlap (`AutoAssignService` semantics).
- **Rest:** only the nearest assignment on each side counts. `RequiredRestAfter` defaults to 8h when null, and 0 allows back-to-back.
- **Position fit:** based on `PositionHelper`. An exact match among free slots wins over a similar one. A slot is suggested only if it has free capacity.
- `ignoreSoldierId` frees that soldier's slot, and `excludeInstanceIds` are ignored for overlap and rest.

### Verification
- **`dotnet test`: 58/58 pass.**
  - `AssignmentConstraintEvaluatorTests` covers:
    - overlap and back-to-back
    - rest before and after, including the 8h defaults, nearest-only, and zero rest
    - vacations: Pending, Approved, Denied, and touching
    - position fit: exact over similar, similar, none, and the commander similarity matrix
    - full instance, `ignoreSoldierId` replacement, already assigned
    - `excludeInstanceIds` move, swap, inactive
    - requested slot, unknown slot, ordering, `NOT_FOUND`
  - `UserAccountGuardTests` covers every P1 guard rule, including the last-SuperAdmin cases.
  - `ScheduleInstanceMapperTests` covers counts, missing slots, live `isFilled`, and ISO dates.
- **Live: `verification/verify-p2.ps1`, 53/53 PASS.**
  - Response shapes match §4.3, with the exact key sets checked.
  - **Cross-checks against the existing `GetMissions`:** the per-instance soldier sets, the overlap-filtered instance count, and the mission summary counts and names all match.
  - Filters (`missionIds`, `soldierIds` keeping all assignments, `onlyUnfilled`) and range errors behave as specified.
  - ValidateAssignments, on real seeded scenarios:
    - Pending vacation → `ON_VACATION` / `Pending`; a Denied one is ignored.
    - A commander gets `none` for a Simple slot.
    - A 4h gap gives `REST_BEFORE` (4 < 8).
    - An overlap is reported with details, and `excludeInstanceIds` removes it.
    - Already assigned gives `ALREADY_ASSIGNED` plus `INSTANCE_FULL`, and `ignoreSoldierId` frees the slot.
    - Error cases return 403, 404, 400, or `[]` as specified.
  - Existing endpoints still behave the same.
- Phase 1 suite re-run on the Phase 2 build: 58/58 PASS. `dotnet build` passes with no new warnings.

---

## Phase 3: schedule mutation endpoints

### New files (ShabzakAPI)
| File | Purpose |
|---|---|
| `BL/Services/AssignmentSlotSelector.cs` | **Pure** slot ranking (exact > similar fit, then most missing, then lowest slot ID) and `SelectTargetSlot`. The evaluator now uses the same ranking, so a write lands exactly where `ValidateAssignments` would suggest. |
| `BL/Services/ScheduleChangePlanner.cs` | **Pure** planning per write: `Assign`, `Replace`, `Swap`, `Move` and `Reschedule` produce the target slot and the evaluator options (`ignoreSoldierId`, `excludeInstanceIds`). `BlockingPolicy` + `BlockReason` decide whether validations stop the write. |
| `BL/Models/Schedule/ScheduleWriteDtos.cs` | `ScheduleWriteResult<T>` (applied or blocked + reason), `ScheduleConflictException` (409), `AssignSoldierResultDto`, `ScheduleChangeResultDto` |
| `04.ShabzakAPI/Controllers/WriteResponseExtensions.cs` | `ExecuteWrite`: maps a write result or exception to `GeneralResponse<T>` + 200 / 400 / 404 / 409, and logs one ID-only line per call |
| `04.ShabzakAPI/ViewModels/ScheduleWriteRequests.cs` | `AssignSoldierRequest`, `UnassignSoldierRequest`, `ScheduleReplaceSoldierRequest` (the legacy `ReplaceSoldierRequest` name is taken), `MoveSoldierRequest` |
| `ShabzakAPI.Tests/AssignmentSlotSelectorTests.cs`, `ScheduleChangePlannerTests.cs` | 39 tests |

### Changed files
- **`ScheduleService`:** the P2 validation loading is split into reusable helpers (`FindInstance`, `ToConstraintInstance`, `FindSoldier`, `Evaluate`, `LoadInstanceDtos`, `ToDto`, `RefreshIsFilled`, `ReloadCaches`, and the lock `WriteSync`). The P2 queries behave the same (the P2 suite still passes 53/53). New methods: `AssignSoldier`, `UnassignSoldier`, `ReplaceSoldier` (with swap) and `MoveSoldier`.
- **`AssignmentConstraintEvaluator`:** takes capacity and ranking from `AssignmentSlotSelector`. Behavior is unchanged; all 58 earlier tests pass.
- **`ScheduleController`:** four Admin+ write endpoints.
- **`AutoAssignService.RemoveSoldierFromMissionInstance`:** one-line fix of the legacy UI endpoint (deviation 9).

### How a schedule write works
1. Under `ScheduleService.WriteSync`, load the tracked instance(s) with slots and assignments, and the soldier from the cache (404 if unknown).
2. Plan the placement(s): target slot + options. Replace frees the old soldier's seat and prefers it; swap is two replacements, each excluding the instance that soldier leaves; move excludes the source instance and prefers the same slot within one mission.
3. Evaluate against the soldiers' current assignments and vacations (the P2 loading).
4. **Blocked** (any violation, unless `allowViolations`; `ALREADY_ASSIGNED` / `NOT_FOUND` always) → HTTP 409, nothing written, `value` carries the validations.
5. Otherwise remove/add the `SoldierMission` rows, recompute `IsFilled` in memory for every affected instance (`assigned ≥ required`, from the tracked non-deleted rows), one `SaveChanges`, reload both caches, and return the instances re-read from the DB.

### Verification
- **`dotnet test`: 97/97 pass** after P3 (39 new): slot ranking and tie-breaks, the vacated-slot and same-slot preferences, fallbacks (no fit, all full, no slots), `ignoreSoldierId`, agreement with the evaluator's suggestion; the planner's options for every operation, end-to-end with the evaluator (replace into the vacated slot, swap between overlapping instances valid only with the exclusions, move into an overlapping instance, reschedule); the blocking policy and messages.
- **Live: `verification/verify-p3.ps1`, 63/63 PASS** (it reverts every change it makes):
  - Regular → 403 on all four writes; no token → 401.
  - Assign: the automatic slot equals the suggestion; the stored `IsFilled` is false at 1 of 2 and true at 2 of 2 (single save, recomputed); the legacy `GetMissions` (the UI's data source) and `GetSoldiers?reloadCache=true` show the change.
  - Blocking: a full instance → 409 with `INSTANCE_FULL` and nothing written; `allowViolations` over-fills it; `ALREADY_ASSIGNED` can't be overridden; a 4h gap → 409 `REST_BEFORE` (4/8), then applied with the override; unknown soldier/instance → 404; a slot of another mission → 400.
  - Slots: a requested Medic slot is used; replacing that medic keeps the vacated Medic slot, where the plain suggestion would have picked Simple.
  - Swap of two back-to-back guard slots: valid only because each soldier's own instance is excluded (without it: `REST_AFTER`); both stay filled; swapped back.
  - Move from a guard slot to an overlapping standby slot: valid thanks to the exclusion; stored `IsFilled` of both updated; moved back. A move right after a 06–14 patrol → 409 `REST_BEFORE`.
  - **Concurrency:** two parallel requests for the last seat → one 200 and one 409; the slot is never over-filled.
  - Legacy UI endpoints: `RemoveSoldierFromMissionInstance` now returns 200 (it was 500); `ReplaceSoldierInMissionInstance` still works.
  - Audit log: every write is logged with status and user ID, with no Hebrew text (no names).
- The P2 suite re-run right after: 53/53.

---

## Phase 4: mission and instance mutation endpoints

### New files (ShabzakAPI)
| File | Purpose |
|---|---|
| `BL/Services/MissionEditService.cs` | Singleton: `CreateMission`, `UpdateMissionDetails`, `GenerateMissionInstances`, `AddMissionInstances`, `UpdateMissionInstance`, `DeleteMissionInstance` |
| `BL/Services/MissionInstanceGenerator.cs` | **Pure** generator mirroring the UI dialog's `autoCreateInstances`: continuous back-to-back slots, the last one truncated; skips slots that overlap existing instances; limits (duration ≥ 1, ≤ 62 days, ≤ 2000 slots). `ResolveRange` turns dates into the mission's FromTime/ToTime. |
| `BL/Services/MissionSlotPlanner.cs` | **Pure** position-slot upsert plan (update / add / remove) with validation (enum, count ≥ 1, unknown or repeated IDs, repeated positions) |
| `BL/Models/Schedule/MissionEditDtos.cs` | Inputs (`MissionCreateInput`, `MissionUpdateInput`, `MissionSlotInput`, `GenerateRangeInput`) and results (`CreateMissionResultDto`, `GenerateInstancesResultDto`, `TimeSlotDto`, `UpdateInstanceResultDto`, `DeleteInstanceResultDto`) |
| `04.ShabzakAPI/ViewModels/MissionEditRequests.cs` | Request VMs (with `ToInput()` mappers), including `SetSoldierActiveRequest` |
| `ShabzakAPI.Tests/MissionInstanceGeneratorTests.cs`, `MissionSlotPlannerTests.cs`, `ScheduleDateFormatTests.cs` | 49 tests |

### Changed files
- **`ScheduleDateFormat`:** `ParseDateOrDateTime` (ISO datetime, or a date + default time; offsets rejected), `TryParseTimeOfDay` / `FormatTimeOfDay` (`HH:mm`).
- **`SoldierService.SetActive`** and **`Soldiers/SetSoldierActive`**. `UpdateSoldier` still ignores `Active`, as the plan requires.
- **`MissionController`:** six Admin+ endpoints. **`Program.cs`:** registers `MissionEditService`.

### Design notes
- **Generator = UI:** the mission dialog generates one continuous run of `duration`-hour slots from the start date at FromTime to the end date at ToTime; the daily window is not repeated per day. The API does the same, so a date-only range gives the same instances as the UI.
- **Encryption:** `CreateMission` uses the existing `Mission.Encrypt()` extension. `UpdateMissionDetails` re-encrypts only the changed fields with the same `AESEncryptor` ("N/A" for blanks), instead of a `Decrypt()`/`Encrypt()` round trip of the tracked entity. That avoids rewriting unchanged columns and the pre-existing `Decrypt()` bug (pre-existing issue 11).
- **Slots:** removing a slot that assignments or pending auto-assign candidates reference → 409 (the DB FK is `NoAction`, and a dangling candidate would break `AcceptAssignCandidate`). When slots change, `IsFilled` of every instance of the mission is recomputed.
- **EF pitfall:** `MissionPositions` initializes `Mission = new Mission()`. New slots get `Mission` set to the tracked mission explicitly, so EF can't insert a phantom mission.
- **`UpdateMissionInstance`:** each assigned soldier is re-validated at the new times in their own slot with their own seat freed; per the plan only `OVERLAP` blocks (action item 7). `dryRun` was added so the P9 approval card can show the effect before approval.
- **`DeleteMissionInstance`** also removes the instance's pending candidate rows (deviation 10).

### Verification
- **`dotnet test`: 146/146 pass** (49 new): contiguous slots, truncation, crossing midnight, duration over 24h, the UI scenario (06:00–22:00 window, 8h shifts), round-the-clock missions, skipping existing (and not skipping touching ones), idempotent regeneration, every limit, range resolution and invalid boundaries; the slot planner; date and time parsing.
- **Live: `verification/verify-p4.ps1`, 92/92 PASS** (it creates its own missions and deletes them at the end):
  - Regular → 403 on all seven writes.
  - `CreateMission` with `generate` over two dates → 6 contiguous 6h slots from 06:00 on the first date to 18:00 on the last. Name, description, times and flags round-trip, and the legacy `GetMissions` (UI) decrypts them. A special mission gets one 3h instance. 13 invalid bodies → 400 `GeneralResponse`, and nothing is created.
  - `GenerateMissionInstances`: the dry run returns count, resolved range, preview and `lastSlot` and saves nothing; the real run creates 6; the same range again creates 0 (6 skipped); a range across both series fills only the 2 missing slots (4 skipped), leaving 14 contiguous instances; explicit datetimes are used as is; over 62 days, `to` before `from`, an offset or a special mission → 400; unknown mission → 404.
  - `AddMissionInstances`: returns the new instance; overlapping an existing one → 409; overlapping each other → 400; invalid or empty → 400; unknown mission → 404.
  - `UpdateMissionInstance`: an empty instance is moved; onto a sibling → 409; onto an assigned soldier's guard slot → 409 `OVERLAP` with the conflicting instance (also as a dry run), then applied with `allowViolations`; a 2h-rest move → 200 with `REST_BEFORE` (2/8) reported; a dry run returns the would-be instance and saves nothing.
  - `DeleteMissionInstance`: 1 assignment deleted, the instance's pending candidate row removed, and the soldier's UI data updated; unknown → 404.
  - `UpdateMissionDetails`: fields updated (`"7:00"` normalized to `"07:00"`), instance IDs unchanged throughout; slot upsert with `IsFilled` recomputed (3 of 5 → false, then back to true); removing a slot used by 2 assignments → 409, by a pending candidate → 409; `""` clears the description; nothing to update / unknown mission / invalid time / foreign slot / empty list → 400 or 404.
  - `SetSoldierActive`: the soldier disappears from `GetSoldiersLite`, validation reports `INACTIVE` immediately, the legacy `UpdateSoldier` with `active:true` still doesn't change it, and reactivation works.
  - Legacy UI mission endpoints: `UpdateMission` still works on a P4 mission (and visibly recreates every instance, dropping the assignments), and `AddMission` and `DeleteMission` work.
  - Cleanup returns to the 4 seed missions, and the audit log has IDs only.
- Swagger generates the document with all 11 new endpoints.
- **Regression on the final build:** `verify-p1.ps1` 58/58, `verify-p2.ps1` 53/53, `verify-p3.ps1` 63/63. `dotnet build`: 0 errors; the only warning in a touched file is the pre-existing one in `SoldiersController.LoadCSV`.

---

## Phase 5: agent service scaffold and infrastructure

New repository `ShabzakAgent/` in the workspace (`git init`, branch `main`, nothing committed). Full reference: `ShabzakAgent/AI_CONTEXT.md`; setup and troubleshooting: `ShabzakAgent/README.md`.

### Packages
- Direct dependencies pinned exactly to `agents/package-lock.json`: `langchain` 1.2.25, `@langchain/core` 1.1.26, `@langchain/langgraph` 1.1.5, `@langchain/openai` 1.2.8, `zod` 4.3.6, `express` 5.2.1, `dotenv` 17.4.2. Added `undici` 8.12.3 (8.12.4 was published exactly 7 days ago). Dev: `typescript` 5.9.3, `tsx` 4.21.0, `@types/node` 22.20.5, `@types/express` 5.0.6.
- A fresh resolve pulled `@langchain/langgraph-checkpoint` 1.2.2, whose peer is `@langchain/core ^1.1.48`. `overrides` pin it, `langsmith` and `openai` to the agents' known-good versions (1.0.0, 0.5.4, 6.22.0). The lockfile was resolved with `npm install --before=2026-11-05`, so every package is at least 7 days old.
- Scripts: `dev` (tsx watch), `start`, `typecheck` (`tsc --noEmit`), `test` (`tsx --test "src/**/*.test.ts"`), `chat:cli`. `tsconfig` follows the agents style (ES2022 target, ESNext/bundler, strict, `isolatedModules`).

### New files (ShabzakAgent)
| File | Purpose |
|---|---|
| `src/config.ts` | The §4.8 variables validated with zod. Empty values count as unset; `ConfigError` lists every problem without echoing values |
| `src/llm/model.factory.ts`, `llm-errors.ts` | `ChatOpenAI` for the OpenAI API: API key, optional `LLM_BASE_URL`, redirects refused, no `temperature` for reasoning models. `describeLlmError` gives clear messages for LLM failures (both after the OpenAI-only change, deviation 16) |
| `src/api/types.ts`, `shabzak-api.client.ts` | Wire types and `ShabzakApiClient`: typed methods for every §4.3/§4.4 endpoint (plus `GetSoldiers`), `GeneralResponse` unwrapped, `ApiError {status, code}`, per-call timeouts, a long-timeout undici dispatcher for AutoAssign, redirects refused |
| `src/auth/roles.ts`, `auth.middleware.ts` | Bearer → `User/Me` → `req.auth = {token, user}`; 60 s identity cache keyed by SHA-256(token), bounded, deduped; 401 / 403 / 502 |
| `src/utils/dates.ts`, `hebrew.ts`, `positions.ts`, `sanitize.ts`, `log.ts`, `rate-limit.ts`, `cors.ts`, `http-error.ts` | Israel wall-clock helpers and Hebrew formatting; Hebrew normalization and fuzzy matching with ambiguity detection; positions mirroring the UI translator and `PositionHelper`; PII masking and whitelist mappers; masked colored-tag logging; per-user token bucket; minimal CORS; `{ code, message }` errors |
| `src/server.ts` | Express: 64 KB JSON limit, CORS from `CORS_ORIGINS`, `GET /health`, `GET /api/me`, JSON 404 / 400 / 413 / 500; `main()` loads `.env`, fails fast on config errors, handles SIGTERM |
| `Dockerfile`, `.dockerignore`, `.gitignore`, `.env.example`, `README.md`, `AI_CONTEXT.md` | `node:22-slim`, non-root, HEALTHCHECK, runs tsx's CLI as PID 1; no secrets anywhere |

### How the API client behaves
- **One client per request.** It forwards that user's JWT, so the API's role policies decide every call. There is no method for `Mission/UpdateMission`.
- **Errors.** Each failure becomes `ApiError` with one of these codes:
  - `blocked`: a 409 whose `GeneralResponse.value` is an object (the validations). Nothing was written.
  - `conflict`: any other 409, including guard failures whose `value` is the `0` of `GeneralResponse<int>`.
  - `failed`: HTTP 2xx with `success: false`.
  - `unauthorized` (with the `WWW-Authenticate` description), `forbidden`, `not_found`, `bad_request` (also reads ProblemDetails field errors), `upstream_error` (the 5xx body is never surfaced), `redirect`, `timeout`, `aborted`, `network`.
- **Transport.** undici `fetch` with the client's own dispatchers, never undici's global one, so the npm undici can't mix with Node's bundled copy. Per-call `AbortSignal.timeout` (30 s by default, 15 min for AutoAssign), combined with the request's abort signal.
- **Redirects** are refused with an actionable message. On the dev API, the `https` launch profile redirects `http://localhost:5165` to https, and following that redirect would drop the token. `start-api-localdb.ps1` therefore gained `-LaunchProfile http`.

### Verification
- `npm run typecheck`: clean. `npm test`: **109/109** for P5. Covered:
  - config: defaults, empty values, every missing variable at once, auth modes, no secret values in errors
  - Hebrew normalization and matching, dates (winter/summer offsets, naive arithmetic across DST, Sunday weeks, Hebrew ranges, tool ranges), positions (UI names, aliases, the PositionHelper map), sanitizing, rate limit
  - **the API client:** method, path, query and body of all 39 endpoint methods, plus envelope unwrapping, blocked vs conflict, ProblemDetails, 401 description, hidden 5xx, redirects, malformed JSON, network errors, timeouts, cancellation, and AutoAssign's separate dispatcher
  - the auth middleware, on a real Express app: missing or invalid tokens, API 401 / 403 / outage, disabled, not activated, unknown role, caching, deduped lookups, TTL and JWT-exp caps, bounded cache
  - the server: health, `/api/me`, CORS, 404 / 400 / 413
- **Live: `verification/verify-p5.ps1`, 23/23 PASS.** It starts the agent against the synthetic dev API (`start-api-localdb.ps1 -LaunchProfile http`), checks it and stops it:
  - `/health` answers.
  - A missing token, Basic auth, a garbage token, an expired token, or a token of a non-existent user → 401.
  - SuperAdmin, Admin and Regular JWTs get through with the right role. `/api/me` exposes only `id, role, soldierId, soldierName`.
  - A forged Admin claim on a Regular account comes out as Regular.
  - A not-activated account → 403.
  - Disabling an account: a new token → 401, while a token used in the last minute still passes (the documented 60 s cache; its API calls fail). Re-enabling → 200.
  - CORS preflight from the UI origin works, and other origins get no CORS header. An unknown route → 404.
  - The agent log has the `User/Me` debug line and **no JWT**.

---

## Phase 6: agent core, read-only tools, prompt, dev CLI

### New files (ShabzakAgent)
| File | Purpose |
|---|---|
| `src/agent/define-tool.ts` | `defineTool({ name, labelHe, roles, kind, refresh?, description, schema, run })` → a `ToolSpec` whose `create(ctx)` builds the LangChain tool for one request. Per-turn `RunContext` (tool budget, refresh hints, memoized lookups), `{ error, code }` results instead of throwing, output cap at 16 000 chars (largest lists trimmed, `truncated` / `omitted` / `hint`), refresh hints recorded only on success |
| `src/tools/common.ts` | Role sets, memoized soldier and mission lists, the compact schedule row, the per-soldier shift row (status and counted hours), Hebrew vacation-status and constraint labels |
| `src/tools/read/*.tool.ts` | The nine §4.7 Phase 6 read tools (below) |
| `src/agent/tool-registry.ts` | `getToolsForRole(role, ctx)`, `toolSpecsForRole`, Hebrew labels, refresh hints, `interruptOnFor(role)` (empty: no write tools yet) |
| `src/agent/shabzak.prompt.ts`, `scope.ts` | `buildSystemPrompt({ now, user, maxToolCalls, toolNames, canWrite })`: `<identity>`, `<scope>` (§4.11, texts from `scope.ts`), `<context>`, `<domain_knowledge>`, `<capabilities_by_role>`, `<rules>`, `<workflows>` (read-only recipes for now), `<response_style>`, `<tool_budget>` |
| `src/agent/shabzak.agent.ts` | `createShabzakAgent({ user, client, model, checkpointer?, runCtx, now? })` → `createAgent` with the role's tools and the per-request prompt; no `responseFormat` |
| `src/scripts/chat-cli.ts` | Dev REPL with a fixed thread per session that prints tool calls; `--tool` mode runs one tool without an LLM |
| `src/testing/*` | Test doubles and synthetic fixtures |

### The read tools
| Tool | Roles | Notes |
|---|---|---|
| `get_my_profile` | All | Own profile, the next 1–14 days of shifts (`current`, `next`), the all-time summary, current and future vacations |
| `search_soldiers` | All | Fuzzy Hebrew name search with `ambiguous` and per-row `match`. Regular users: name query only, rows `{id, name}`. Admins: also platoon, company and positions, with unit and Hebrew positions in the rows |
| `list_missions` | All | Regular users: name, description, type, shift length, daily window. Admins: also position slots, rest after (8 h when unset), counted hours, instance and assignment counts |
| `get_schedule` | All | At most 14 days. Rows exactly as the plan specifies: `{instanceId, mission, from, to, weekday, filled:"3/4", soldiers:["name (תפקיד)"], missing:["חובש×1"]}`, at most 60, then `truncated` with a hint |
| `get_soldier_schedule` | All | One soldier, at most 62 days. Status past / current / upcoming, counted hours (the mission's actualHours, like the fairness page), totals per mission, the next shift |
| `get_soldier_summary` | Regular: self; Admin+: any | All-time totals; the Regular schema has no `soldierId` |
| `get_vacations` | Regular: self; Admin+: any | Status and period filters. Admins without a soldier filter get vacations attributed through `GetSoldiers` (the `Vacation` DTO has no soldier ID) |
| `get_fairness_stats` | Admin+ | Assignments and hours per soldier, the average, the range and each soldier versus the average. `to` is sent as the last day at 00:00 because the API adds a day |
| `get_available_soldiers` | Admin+ | Validates the (filtered) active pool in batches of 1000 and ranks: exact fit, then more rest, then name. Exclusions are counted by each soldier's first violation, with Hebrew reasons |

### Design notes
- **Role enforcement inside the tools:**
  - Per-role schemas; LangChain strips unknown keys, so a Regular user's `soldierId` or `platoon` is dropped before the tool runs.
  - Own-soldier forcing in the summary and vacation tools.
  - Admin-only tools aren't registered for Regular users. If the model calls one anyway, LangChain answers with an error tool message and no API call is made (tested).
- **Dates:** tools take `yyyy-MM-dd` or `yyyy-MM-ddTHH:mm` in Israel time. A date-only `to` means the whole day.
  - All arithmetic is on naive wall-clock values, as the API does. Only "now" goes through `Intl` with `Asia/Jerusalem`.
  - The prompt's `<context>` pre-computes today, tomorrow, this and next week (Sunday to Saturday), and this month, so the model never does calendar math.
- **PII:**
  - Outputs are built from explicit field picks only. API objects that carry phone or personal number (metadata soldiers, `GetSoldiers`) are never spread into an output.
  - Every log line is masked.
- **Budget:** the per-turn budget (`AGENT_MAX_TOOL_CALLS`) is enforced in `defineTool`'s wrapper. Over the budget, a tool returns `budget_exhausted`, and the model is told to answer with what it has.
- **Scope (§4.11):** the assistant serves only Shabzak.
  - `<scope>` lists what is in and out. General knowledge, market or investment advice, medical or legal advice, coding, unrelated writing and jailbreak attempts are out.
  - Out-of-scope requests get a fixed Hebrew or English reply (`agent/scope.ts`) with no tool calls. A mixed request gets only its Shabzak part answered, plus a fixed sentence.
  - The agent has no general-purpose tools (no web search, browsing or code), so the model's own knowledge is the only way it could answer other topics, and the prompt forbids that.

### Verification
- `npm run typecheck`: clean. **`npm test`: 170/170** (including the model-factory and LLM-error tests of the OpenAI-only change, deviation 16). Covered:
  - `defineTool`: metadata, error mapping, budget, refresh only on success, per-context schemas, output trimming, memoization
  - each read tool with a fake client: scoping, the exact row formats, range limits, truncation, the metadata date semantics, the 1000-soldier batches, ranking, exclusions, and no contact details in any output
  - the registry: Regular users get exactly the 7 shared tools; Admins and SuperAdmins get all 9; no interrupts
  - the prompt: sections in order, the Israel date and relative dates for a fixed "now", the glossary, capabilities by role, the safety rules, the budget, and the `<scope>` section with the exact fixed replies, identical for every role
  - **the agent loop with LangChain's `FakeToolCallingModel`:**
    - the model calls `get_schedule`, the tool reads through the user's client, and the answer contains the tool data
    - Regular users are offered only their 7 tools, and a hallucinated admin call never reaches the API
    - conversation memory works per thread through the shared checkpointer
    - the budget stops the third call
- **Live: `verification/verify-p6.ps1`, 30/30 PASS.** It runs every read tool through `chat-cli.ts --tool` against the synthetic dev API, as SuperAdmin, Admin and Regular, and cross-checks with the API:
  - Regular users don't have the two admin tools, and a not-activated account is refused.
  - `get_schedule` for tomorrow returns the same instance count as `Schedule/GetAssignments`, in the exact row shape, and the patrol's `1/4` matches the API.
  - `get_available_soldiers` considers every active soldier (16). Its ranked soldiers are exactly the API's valid soldiers (7, 8, 15), with exact fits first. An unknown instance → `not_found`.
  - `get_fairness_stats` hours for soldier 5 equal `Metadata/GetHoursPerSoldiers` (44), and the soldier count matches (12).
  - `get_soldier_schedule` for the week matches the API (8 shifts).
  - The admin vacation list attributes the pending vacation to the right soldier.
  - A 7-day schedule is capped at 60 rows with a hint.
  - Regular users' scoping holds (own summary and vacations only, no staffing fields).
  - **No phone number, personal number or JWT in any tool output.**
- **Not run here: the LLM-answered CLI questions.** With your OpenAI key the CLI gets as far as the model call, but the corporate firewall answers `api.openai.com` with a 302 to its block page (action item 6). `verification/verify-p6-llm.ps1` runs them, plus the §4.11 off-topic and mixed-request checks, on a network that allows OpenAI; here it stops at the first question with the firewall message.

---

## Phase 7: SSE chat API, sessions, approvals, vacation tools

### Spike findings (the plan's step 1)
- `humanInTheLoopMiddleware` exists in langchain 1.2.25 with the planned `interruptOn` shape, but it has a bug with mixed decisions: when one call of a batch is rejected, it drops the approved calls and leaves auto-approved calls without a ToolMessage, so the next OpenAI request fails. langchain 1.6 fixed it. I wrote our own `approvalMiddleware` with the same protocol instead of moving the pinned LangChain tree (deviation 21).
- `agent.stream(input, { configurable: { thread_id }, streamMode: ['updates','messages'], recursionLimit, signal })` yields `[mode, payload]` tuples. The model node is `model_request`, tools run in `tools`, and an interrupt arrives as `__interrupt__: [{ id, value }]`. Message chunks carry `metadata.langgraph_node`.
- All write calls of one model turn arrive in **one** interrupt. You resume it with `new Command({ resume: { decisions } })` on the same thread.
- `agent.getState(config)` lists pending interrupts under `tasks[].interrupts` (used as a fallback).
- Aborting the signal stops the run with an `AbortError`. The checkpoint can keep an AI message whose tool calls never got answers, which OpenAI then refuses (hence the repair middleware). The route must still free the thread.

### New files (ShabzakAgent)
| File | Purpose |
|---|---|
| `src/chat/chat.routes.ts` | `POST /api/chat/stream`, `/resume`, `/reset`: zod bodies, ownership (403), unknown or expired thread (404), `approval_pending` / `thread_busy` (409), per-user rate limit, abort when the client disconnects, `thread` first and `done` last |
| `src/chat/session.store.ts` | `SessionStore`: one shared `MemorySaver`; threads with owner, last activity, the active run's `AbortController` and the pending approval; TTL sweep (checkpoints deleted), ≤ 10 threads per user, reset of running threads after their run; `expandDecisions` (the user's decisions + the automatic rejections) |
| `src/chat/sse.ts` | `SseStream` (headers `no-cache, no-transform`, `X-Accel-Buffering: no`, a `: ping` every 15 s, disconnect tracking) and `formatSseEvent` |
| `src/chat/stream-runner.ts` | `runTurn`: model calls → `status` thinking; tools → `tool_start` / `tool_end` (Hebrew labels); model text → `token`; interrupt → cards → `approval_required` (the turn ends); completion → `final` with refresh hints; failures → `error` (`classifyRunError`) |
| `src/agent/approval.ts` | `approvalMiddleware`, `applyDecisions`, the `REJECTED:` / `NOT RUN:` texts |
| `src/agent/action-describer.ts` | `describeAction(s)`: a card from the tool's describer; a describer error or an API refusal → the action is refused without asking; API down → a generic card with the redacted arguments |
| `src/agent/middleware.ts` | Tool-call repair for stopped runs, and the "thinking" progress events |
| `src/tools/vacations.ts` | Vacation owners (the DTO has no soldier ID), `findVacation`, Hebrew period labels (inclusive days), overlaps |
| `src/tools/write/request-vacation.tool.ts`, `respond-to-vacation.tool.ts`, `helpers.ts` | The two vacation write tools and the shared write helpers |
| `src/utils/audit.ts` | `[audit]` lines |
| `src/testing/scripted-model.ts`, `sse.ts` | A deterministic streaming chat model (and the `CALL` directive language of the live stub); SSE parsing and posting helpers |

Changed: `define-tool.ts` (describers, audit, progress events, writes outside the budget, thread ID), `tool-registry.ts` (`isWriteTool`), `shabzak.agent.ts` (the three middlewares), `shabzak.prompt.ts` (the general write workflow and the vacation workflows, rule 8), `server.ts` (the chat router, the session sweeper), `chat-cli.ts` (approval prompts, `--yes`, `/tool!`), `utils/sanitize.ts` (UUID-aware masking).

### How a change is approved
1. The model calls a write tool. The approval middleware pauses the run before any tool of that model turn runs.
2. The runner describes every write call with its tool's `describe`. This runs server code with the user's token: it validates the arguments, looks up the soldier, the vacations and the shifts, and builds a Hebrew card with title, details, warnings and risk.
3. If a describer refuses (invalid arguments, unknown soldier, period in the wrong order…), that call is answered `NOT RUN: …` without asking. The model can then fix the call or ask the user.
4. The remaining cards go out as `approval_required`, and the turn ends. The thread stores the pending approval, and new messages get 409 `approval_pending` until it is answered.
5. `resume` takes one decision per card. Approved calls run (each writes an `[audit]` line and its refresh hints); rejected ones get `REJECTED: …` with the user's reason, and no API call is made. The model then summarizes from the tool results.

### Verification
- `npm test`: **237/237** at the end of P7, covering the SSE writer, the stream runner against a scripted stream (tokens, tools, interrupts, invalid actions, a fallback from the saved state, error classes), sessions (ownership, TTL, 409, reset, the per-user cap), resume validation, the describers, the approval middleware with real `createAgent` (approve, reject, mixed decisions), the repair middleware, both vacation tools, and the chat routes end to end (real Express and a scripted streaming model).
- **Live: `verification/verify-p7.ps1`, 52/52 PASS** (`curl -N`, real agent, synthetic dev API, the scripted LLM stub):
  - events stream in order (`thread` first, `done` last; status → tokens → final; the tokens add up to the final message; Hebrew labels)
  - a vacation request ends with `approval_required` and **no API call** until approve; approve creates exactly one pending vacation covering both days and sends `soldiers, vacations`; approving and denying through `respond_to_vacation` change the API state
  - reject → no API call and no refresh, and the model reads the reason; an invalid soldier → no card, `NOT RUN`
  - a message while a card waits → 409 `approval_pending`; a second message while one is answered → 409 `thread_busy`; a client that leaves stops the run and frees the thread; wrong decision counts and resume without a pending approval → 400; another user's thread → 403; unknown → 404; reset → 204, then 404
  - Regular users are never offered write tools (checked in the model requests), and a write call from them never reaches the API
  - the rate limit (per user, shared by stream and resume) → 429 with `Retry-After`
  - one `[audit]` line per executed write, with user, role and thread, and none for rejected or refused actions; no JWT, phone or personal number in the log
- Regression: `verify-p5.ps1` 23/23. `verify-p6.ps1` showed 28/30 because two error-path checks couldn't parse the CLI output. `.env` sets `LOG_LEVEL=info`, so an info log line came before the JSON. This was fixed in P8 (deviation 26), and the suite is back to 30/30 minus the seed-day check (action item 12).
- **Not run here:** the LLM-driven checks (action item 6).

---

## Phase 8: soldier and user-account tools

### New files (ShabzakAgent)
| File | Purpose |
|---|---|
| `src/tools/write/create-soldier.tool.ts` | All six fields required; phone and personal number normalized; positions from Hebrew / English names and aliases; חפ"ש for non-commanders (like `AddSoldier`); unique personal number; warnings for a namesake in the platoon, a phone already used and an unknown unit |
| `src/tools/write/update-soldier.tool.ts` | Reads the soldier, merges only the given fields (`positions`, or `addPositions` / `removePositions`), writes details through `UpdateSoldier` and `active` only through `SetSoldierActive`; per-field diff card with warnings (account username after a personal-number change, password after a phone change, future shifts on deactivation, future assignments in a position the soldier loses) |
| `src/tools/write/delete-soldier.tool.ts` | High-risk card with all-time and upcoming assignments, vacations and the login account deleted with the soldier; checks the account guard first; suggests deactivating |
| `src/tools/write/soldier-fields.ts` | `cleanText`, `normalizePhone` (+972 → 0, digits only), `normalizePersonalNumber`, `parsePositionList`, `withRequiredPositions` |
| `src/tools/accounts.ts` | Account views and labels, `findAccount`, `accountChangeProblem` (the `UserAccountGuard` rules), `namesList` |
| `src/tools/write/create-user-accounts.tool.ts`, `update-user-account.tool.ts`, `delete-user-account.tool.ts`, `reset-user-password.tool.ts` | The account tools |
| `src/tools/read/list-user-accounts.tool.ts`, `get-soldier-contact-details.tool.ts` | The two admin read tools |

Changed: the registry (20 tools), the prompt (the P8 workflows, a role-aware line about roles, the contact-details rule), `tools/write/helpers.ts` (`soldiersWithContacts`, `phoneDigits`, `soldierView`, `upcomingShifts`), `llm/llm-errors.ts` (follows `cause`), `utils/log.ts` + `chat-cli.ts` (logs on stderr in `--tool` mode), `testing/fake-client.ts` (synthetic contact details and accounts).

ShabzakAPI (your decision, deviation 31):
| File | Change |
|---|---|
| `BL/Services/SoldierService.cs` | `DeleteSoldier(id, callerId, callerRole)` deletes the soldier's login accounts in the same `SaveChanges`, after the guard check, removes them from `UsersCache`, and logs IDs only |
| `BL/Services/UserAccountGuard.cs` | New pure `CheckDeleteAll` (each account deletable by the caller; SuperAdmins deleted earlier in the same call no longer count) |
| `04.ShabzakAPI/Controllers/SoldiersController.cs` | `DeleteSoldier` passes the caller and maps a guard refusal to 403 / 400 / 409 `GeneralResponse` (success still returns the soldier ID) |
| `04.ShabzakAPI/Controllers/AccountResponseExtensions.cs` (new), `UserController.cs` | `AccountError` moved out of `UserController` so both controllers share it (same behavior) |
| `ShabzakAPI.Tests/UserAccountGuardTests.cs` | 5 tests for `CheckDeleteAll` |

### Design notes
- **Missing fields:** the `create_soldier` schema requires every field, so a call without the phone is refused before asking, with the missing field named. The prompt tells the model to ask for all missing fields in one question and never invent them.
- **Roles:** an Admin's `update_user_account` schema has no `role` field (checked in the live model requests). An Admin who asks anyway ends with "Nothing to change. Only a SuperAdmin can change roles." Changes to SuperAdmin accounts, self-disable and the last SuperAdmin are refused before the card with the API's own wording, and the API checks again.
- **Passwords:** there is no password argument anywhere. `reset_user_password` resets to the default, which is the linked soldier's phone, digits only. The card shows the phone masked (••••••0199), and the result never contains it. New accounts are Regular and not activated. The prompt offers to activate them and explains the username and default password.
- **PII:** tool outputs never carry phone or personal number, except `get_soldier_contact_details`. That tool takes one soldier per call, only on an explicit request, and is audit-logged. The full soldier list (with contact details) is used only for the uniqueness checks. Account listings are whitelisted: no username hash, password or salt.
- **Phones:** stored as local digits, because the default password is the phone without dashes and users type it that way. A phone change doesn't change the password, so the card suggests a reset. The edit card masks the phone on both sides (your decision). A change with the same last four digits, such as a typo fix in the middle, is marked "(מספר אחר)" so the card still shows that something changed.
- **Soldier delete cascade:** the API deletes the login account with the soldier, so the agent's card says so and checks the same guard rules first. A refused account gives `NOT RUN` before any card; the API checks again.

### Verification
- `npm test`: **267/267** (merge logic, Hebrew position parsing, per-role schemas, the guard pre-checks, every describer and run, no secrets in any output, the prompt workflows). `dotnet test`: **151/151** (146 + 5 for `CheckDeleteAll`).
- **Live: `verification/verify-p8.ps1`, 52/52 PASS** (real agent and API, the scripted LLM stub):
  - Regular users don't have the P8 tools. `list_user_accounts` has one row per account, whitelisted fields only, no hash, password or salt; its `soldiersWithoutAccount` matches the API. `get_soldier_contact_details` returns the API's values and writes an audit line without them.
  - create: a missing phone and a duplicate personal number are refused without a card. The card shows the normalized input and the added חפ"ש. Nothing is written before approval. After approval the soldier exists with positions 0,3, a normalized phone and personal number, and the result has no contact details.
  - update: the diff card; rename + `addPositions` applied; a rejected change isn't applied; deactivate and reactivate go through `SetSoldierActive`.
  - accounts: the card lists who gets an account and who is skipped. The new account is Regular, not activated, with refresh `users`. The Admin's schema has no `role`, and an Admin's role change, an Admin disabling the SuperAdmin and an Admin disabling themselves are all refused. Activation works. A SuperAdmin's schema has `role`, and the role change card (medium risk) works.
  - reset: the card names the default with the phone masked. After it, **logging in with the personal number and the phone works** (`User/Login`), and the result never contains the phone.
  - delete account: the card (an Admin account → high risk), approved, gone.
  - **soldier delete cascade:** the soldier gets a new account, made SuperAdmin. An Admin's `delete_soldier` is refused before asking ("also deletes their login account … Only a SuperAdmin …"). The API answers 403 to an Admin's `DeleteSoldier` and 400 to that SuperAdmin deleting their own soldier, and nothing is deleted either time. The SuperAdmin's card names the account "#… (מנהל-על) — יימחק יחד עם החייל"; after approval the soldier **and** the account are gone, with refresh `missions, soldiers, users`. Soldiers and accounts are back to the start.
  - The tool results contain no stored phone, personal number, password, salt or hash. There are 10 audit lines, one per executed write, all `ok`, with contact details masked, and none for refused or rejected actions. The log has no JWT, phone or personal number.
- Regression on the final build (API rebuilt with the cascade): `verify-p5.ps1` 23/23, `verify-p7.ps1` 52/52; `verify-p6.ps1` 29/30 before the API change (the seed-day check, action item 12). The refactored account error mapping was spot-checked: an Admin's role change → 403, an unknown account → 404, both with the `GeneralResponse` body.
- **Not run here:** the LLM-driven checks in `verify-p7-p8-llm.ps1` (action item 6). On this network the script stops at once with "The LLM endpoint answered with a redirect … proxy or firewall …".

---

## Phase 9: mission and instance tools

### New files (ShabzakAgent)
| File | Purpose |
|---|---|
| `src/tools/missions.ts` | Mission views (shared with `list_missions`), slots (`parseSlots`, `seatCounts`, `staffingWarnings`), shifts by ID (`loadInstances`: one `Schedule/GetInstances` batch per turn), and shift planning: `planSlots` (a pure mirror of `MissionInstanceGenerator`), `resolveShiftPeriod` (first and last day, included), `eachDayWindows`, `shiftsOutsideWindow` |
| `src/tools/read/get-mission-instances.tool.ts` | One mission's shifts (≤ 62 days) with a per-day count and where its shifts end |
| `src/tools/write/create-mission.tool.ts` | Recurring (optionally with its shifts, continuous or `eachDay`) or special missions; requirements derived from the slots; staffing / namesake / off-window / short-shift warnings |
| `src/tools/write/update-mission.tool.ts` | `UpdateMissionDetails` only; per-field diff; `positions` or `setPositions` / `removePositions`; requirements follow consistent slots; removing a used slot refused before asking |
| `src/tools/write/delete-mission.tool.ts` | High-risk card with shifts, assignments (all-time and the next 62 days), pending proposals |
| `src/tools/write/generate-mission-instances.tool.ts`, `add-mission-instance.tool.ts`, `update-mission-instance.tool.ts`, `delete-mission-instance.tool.ts` | The instance tools (dry runs on the cards, overlap refusals, the soldiers removed) |
| `src/tools/assignments.ts` | Hebrew violation texts with details, refusal / block explanations, the API's seat preference (shared with P10) |

Changed: the client and types (`getInstances`), `list_missions` (views moved to `missions.ts`), the registry (28 tools after P9), the prompt (mission workflows, how shifts are generated). ShabzakAPI: `Schedule/GetInstances` (`ScheduleController`, `ScheduleService.GetInstances`, `ViewModels/GetScheduleInstancesRequest.cs`).

### Design notes
- **Continuous vs day by day:** the API generator (like the missions screen) fills a period with back-to-back shifts, nights included. For a mission with daily hours, `eachDay` creates only each day's window; the agent plans those shifts with the same algorithm and writes them with `AddMissionInstances`. The continuous card counts and warns about the shifts outside the daily hours, and the prompt tells the model to match the existing pattern (it reads `get_mission_instances` first).
- **Periods:** "from the 22nd to the 28th" means the 28th is included: a round-the-clock or overnight mission ends on the 29th at its end time (the agent sends exact datetimes; the API's date-only `to` would stop at 00:00 of the 28th).
- **Refused before asking:** unknown missions (`DeleteMission` answers 500), a special mission without a start, half a period, a period whose shifts all exist, an extra shift over an existing one, a reschedule that gives an assigned soldier overlapping shifts (the API's dry run), removing a slot that assignments use. The model gets the reason (`NOT RUN: …`) and asks or explains.

### Verification
- `npm test`: part of the 328 (the generator mirror, periods, windows, slots, staffing warnings, one-batch lookups, every describer and run).
- **Live: `verification/verify-p9.ps1`, 39/39 PASS**: `GetInstances` (order, unknown IDs, 400s, 401); Regular users lack the tools; `get_mission_instances` matches the API; create with day-by-day shifts (nothing before approval; 2+1 required, shifts 06–10, 10–14); a continuous extension shows "6 new, 2 existing, 4 outside 06:00–14:00" and is rejected; day by day adds 2; the same period again is refused; add a shift, and one over an existing shift is refused; moving a shift onto its soldier's other shift is refused with "נועם פרידמן: חפיפה עם שיבוץ אחר (משימת עזר P9 …)", the override gives a high-risk card (rejected), a valid move keeps the soldier; update (rename, a new slot, requirements 2 → 3 "(לפי התפקידים)"); removing the used slot is refused; deleting the shift lists and removes its soldier; deleting the mission removes its shifts; 7 audit lines, all `ok`; no JWT in the log.

---

## Phase 10: schedule tools and workflows

### New files (ShabzakAgent)
| File | Purpose |
|---|---|
| `src/tools/candidates.ts` | `compactCandidate` (IDs, names, positions, times, status, missing seats; no contact details, no ranking data), `summarizeCandidate`, the conversation cache (≤ 8 proposals), `requirePendingCandidate` |
| `src/tools/read/resolve-soldier-pool.tool.ts`, `get-schedule-coverage.tool.ts` | The pool (filters, names to add / leave out, ambiguous / unknown names) and the coverage (as auto-assign counts it) |
| `src/tools/read/list-schedule-candidates.tool.ts`, `get-candidate-details.tool.ts` | Pending proposals, summarized; one proposal's shifts with filters |
| `src/tools/read/suggest-replacements.tool.ts` | The plan's filter → validate → rank pipeline, plus swaps validated like the API's swap |
| `src/tools/write/run-auto-assign.tool.ts`, `accept-schedule-candidate.tool.ts` | Auto-assign (heartbeat, 409 explained) and acceptance (high risk) |
| `src/tools/write/assign-soldier.tool.ts`, `unassign-soldier.tool.ts`, `replace-soldier.tool.ts`, `move-soldier.tool.ts` | The `Schedule/*` writes, validated before the card |
| `src/testing/candidate-fixture.ts` | A synthetic proposal with full (synthetic) soldier objects, to prove nothing leaks |

Changed: `define-tool.ts` (`tool_start` labels, `RunContext.threadCache`), `session.store.ts` (`ThreadRecord.cache`), `chat.routes.ts` and `chat-cli.ts` (pass the cache), `stream-runner.ts` (sends a tool's own label), the registry (39 tools), the prompt (the three workflows and the Hebrew rule names).

ShabzakAPI (deviation 35):
| File | Change |
|---|---|
| `BL/Services/AutoAssignService.cs` | `FinalizeScheduleResult` stores only the proposal's own assignments (not those of the shifts the run skipped); `AcceptAutoAssignCandidate` filters the rows (`CandidateAcceptanceFilter`) and logs what it skipped; batch runs throw `AutoAssignConflictException` during interactive sessions |
| `BL/Services/CandidateAcceptanceFilter.cs` (new) | Pure: dangling rows (deleted instance / soldier / slot) and duplicates (existing assignment, or a repeated row) are left out |
| `BL/Models/AutoAssignConflictException.cs` (new) | → 409 |
| `04.ShabzakAPI/Controllers/MissionController.cs` | `AutoAssign` maps it to 409 `{ message }` |
| `ShabzakAPI.Tests/CandidateAcceptanceFilterTests.cs` (new) | 5 tests |

### Design notes
- **Rules before the card:** every assignment change runs `ValidateAssignments` with exactly the options the write will use (`preferredSeat` mirrors the API's seat choice: the vacated seat, the same seat within a mission; the shift a soldier leaves excluded; both sides of a swap). A violation is refused before asking, with Hebrew reasons for the model; `allowViolations` exists only for an explicit override and gives a high-risk card.
- **Proposals:** the API returns them with full soldier objects (contact details) and ranking data, megabytes per run. The agent compacts them at once, keeps only the compact form for the conversation, and summarizes: fully staffed / faulty / kept shifts, the faulty shifts' missing seats in Hebrew, load per soldier (min / average / max / top 5, counting the run's pool), evenness.
- **What auto-assign fills:** only shifts without any soldier; partly staffed shifts are kept as they are. The coverage tool and the run's card say so, and the CREATE workflow offers to complete faulty shifts with `get_available_soldiers` and `assign_soldier` after accepting.
- **The acceptance bug** (found while building `accept_schedule_candidate`): a run keeps the existing assignments of the shifts it skips in memory, and `FinalizeScheduleResult` saved all of them as candidate rows, so accepting inserted them again (one soldier twice in a shift). Reproduced on the dev DB, fixed in the API, and checked live: the kept shift's soldier appears once after acceptance, and legacy rows (a duplicate and a deleted shift) are skipped.
- **Heartbeat:** `run_auto_assign` repeats its status every 15 s with the elapsed time ("מריץ שיבוץ אוטומטי… (עברו 30 שניות)"), so the UI can show progress during a long run.

### Verification
- `npm test`: **328/328** (P9 + P10: `summarizeCandidate` on the synthetic proposal, no contact details in any output, `suggest_replacements` filtering, ranking and swap validation, the exact validation requests of every assignment tool, the heartbeat with mock timers, the 409). `dotnet test`: **156/156** (151 + 5).
- **Live: `verification/verify-p10.ps1`, 41/41 PASS**: Regular users lack the tools; the pool and the coverage match the API; `run_auto_assign` card ("3 to fill of 4", 1 kept, the pool by unit, the partly-staffed warning), nothing before approval, one proposal after it (status label of the run, refresh `candidates`, no contact details); `list_schedule_candidates` and `get_candidate_details` (the kept shift); acceptance adds exactly the proposal's new assignments, with soldier 4 once in the kept shift and no duplicates; legacy duplicate / dangling rows are skipped by the API; a rest violation is refused with "מנוחה לא מספקת", the override gives a high-risk card (rejected); a move within the mission keeps the seat and warns about the shift left short; `suggest_replacements` gives reasons, and every suggestion is valid per `ValidateAssignments`; replace with the first one; a swap; a **move to another mission** ("move X from mission A to mission B"); unassign; `AutoAssign` answers 409 while an interactive session is open and the agent explains it; cleanup (missions, the proposal file); 8 audit lines (7 `ok`, the blocked run `invalid_input`); no JWT, phone or personal number in the log.
- Regression: `verify-p1.ps1` 58/58 (incl. the auto-assign role gates), `verify-p5.ps1` 23/23, `verify-p7.ps1` 52/52, `verify-p8.ps1` 52/52, `verify-p9.ps1` 39/39; `verify-p6.ps1` 29/30 and `verify-p2–p4` as in action item 12 (seed day).
- **Not run here:** real-LLM behavior of the new workflows (action item 6).

---

## Phase 11: UI chat widget and streaming client

Repo `Shabzak`, branch `opus-refactor` (not committed). Full reference: `Shabzak/AI_CONTEXT.md` §2.7.

### Your decision before coding
The agent needs the API's `http` profile (the `https` profile redirects `http://localhost:5165` to https, and the agent refuses redirects). The UI's dev `serverURL` pointed at `https://localhost:7170`, which only the `https` profile serves, so the UI and the chat couldn't both work on one local API. You chose to move the UI's dev `serverURL` to `http://localhost:5165/api` (deviation 39).

### New files (Shabzak)
| File | Purpose |
|---|---|
| `src/app/models/chat/chat.model.ts` | Wire types (events, cards, decisions, request errors), message and state types, limits (2000 / 500), `INITIAL_CHAT_STATE` |
| `src/app/utils/sse-parser.ts` | Pure SSE parser: chunks split anywhere (also inside a CRLF), LF / CR / CRLF, multi-line `data`, `: ping` comments, BOM, default event name, an unterminated last event dropped |
| `src/app/utils/markdown.ts`, `components/chat/chat-markdown.pipe.ts` | `marked` with GFM and line breaks. Raw HTML is escaped, images become their alt text and links their text (deviation 41); task-list boxes → ☑/☐ |
| `src/app/services/chat/chat-api.service.ts` | `stream` / `resume` / `reset` with native `fetch` (`CHAT_FETCH` token), the user's JWT from `UserService.getToken()`, the `ReadableStream` body through a streaming `TextDecoder` and the parser; unsubscribe aborts; outside the Angular zone; `EMPTY` under SSR; typed request errors (`Retry-After` included) |
| `src/app/services/chat/chat-protocol.ts` | Pure event parsing with safe defaults, error-code mapping, validated restore of a stored chat |
| `src/app/services/chat/chat-state.service.ts` | The chat state (`BehaviorSubject`): turns (tokens accumulate, the `final` message replaces them), status labels, errors mapped to Hebrew, user changes |
| `src/app/components/chat/chat-launcher/` | `mat-fab` bottom-right (z-index 900: over the sidenav, under dialogs), badge while an approval is pending, hidden when logged out or on `/login` |
| `src/app/components/chat/chat-panel/` | 400×600 (full screen below 768px), `dir="rtl"`; header (title, new chat, close), auto-scrolling list, status line with spinner and the agent's Hebrew label, empty state with the §4.11 topics, textarea (Enter / Shift+Enter, counter, Stop while streaming) |
| `src/app/components/chat/chat-message/` | User, assistant (markdown) and note bubbles, `dir="auto"`, RTL table styles |
| `src/app/components/chat/chat-visibility.ts`, `chat-theme.scss` | Visibility rule; palette variables (the global theme is not imported into the components) |

Changed: `src/environments/*.ts` (`agentURL` everywhere; dev `serverURL`), `app.component.{ts,html}` (the two hosts after `mat-sidenav-container`, with `ngSkipHydration`), `package.json` / `package-lock.json` (`marked` 18.1.3, deviation 40).

### Design notes
- **Not `HttpClient`.** Streaming needs the response body as it arrives, so the client uses `fetch` and sets the `Authorization` header itself. The read loop runs outside the Angular zone and re-enters it per event, so the 15 s pings cost no change detection.
- **Hebrew in chunks.** A multi-byte character can be split between two network chunks; the streaming `TextDecoder` keeps the partial bytes (tested with chunks of 1–7 bytes).
- **SSR.** The two hosts render empty on the server (no user there), the client returns `EMPTY` and storage isn't touched; `ngSkipHydration` makes the client render them fresh. The prerendered pages contain only the empty hosts.
- **Long runs.** There is no client timeout; the latest `status` label is shown, so the auto-assign heartbeat ("…(עברו 30 שניות)") updates the status line.
- **Layout.** Bottom-right, so it never covers the pages' left `.floating-button`. The assistant's bubbles sit on the start (right) side and the user's on the end (left), mirroring LTR chat apps.

### Verification
- `npm run build` (production, SSR, prerender of the 7 routes) passes; the only warnings are the pre-existing NG8107 ones in other components.
- Karma: `sse-parser.spec.ts` (every split point, CRLF splits, CR, multi-line data, defaults, BOM, the end of the stream), `chat-state.service.spec.ts` (with a scriptable fake API and a mock store), `chat-api.service.spec.ts` (real `ReadableStream`s, Hebrew split at 1–7 byte chunks, error JSON and `Retry-After`, non-SSE answers, network failures, abort on unsubscribe, SSR), `markdown.spec.ts`, `chat-protocol.spec.ts`, and the panel and launcher component specs. Totals in Phase 12.
- Live (with Phase 12): `verify-p11-p12.ps1`.

---

## Phase 12: UI approval cards, store refresh, persistence, polish

### Your decision before coding
The UI had no logout, and the login page bounced a stored session back to the personal page, so "401 → send the user to login" and "logout clears the conversation" couldn't work. You chose a logout button plus a logout action (deviation 43).

### New files (Shabzak)
| File | Purpose |
|---|---|
| `src/app/components/chat/chat-approval-card/` | The cards of one approval: title, details, warnings, risk chip; per action אשר / דחה and an optional reason; "אשר הכל" / "דחה הכל"; "שלח"; the outcome once decided (deviation 45) |
| `src/app/services/chat/chat-decisions.ts` | Pure: choices → decisions in order (reason trimmed and capped, empty reason omitted) |
| `src/app/services/chat/chat-refresh.ts` | Pure: refresh hints → NgRx actions (deviation 44) |
| `src/app/services/chat/chat-storage.service.ts` | `sessionStorage['shabzak.chat.<userId>']` with size caps, SSR-safe |
| `src/app/state-management/reducers/reset-on-logout.spec.ts` and the other P12 specs | See Verification |

Changed:
- `chat-state.service.ts`: approvals and resume, refresh, persistence, the error UX, unread.
- `chat-panel`: the cards, the disabled input with its hint, the error banner with "נסה שוב" / "שיחה חדשה", Escape, chips per role, `aria-live` / `aria-busy`, focus on open.
- `chat-launcher`: the unread dot.
- Logout: `user.actions.ts` (`logout`), `user.reducer.ts`, `user.effects.ts` (`logout$`), `user.service.ts` (`logout()`), `reducers/index.ts` (`resetOnLogout`), and `app.component` (the toolbar button).
- `missions.reducer.ts`: `getAllCandidatesSuccess` also drops proposals that are no longer pending.

### How an approval works in the UI
1. The agent ends a turn with `approval_required`. The panel shows the cards, the input is disabled ("יש לאשר או לדחות את הפעולה לפני שממשיכים"), and the launcher counts the pending actions while the panel is closed.
2. The admin picks אשר or דחה per action (with "אשר הכל" / "דחה הכל" for several), optionally types a reason, and presses "שלח". The card turns into "ההחלטות נשלחו", and the UI calls `resume` with the decisions in card order.
3. The agent runs the approved writes and streams its summary. The `final` refresh hints reload the store (e.g. `soldiers` + `vacations` → `GetSoldiers?reloadCache=true` and the user's own `GetVacations`), and a snackbar says "הנתונים עודכנו".
4. If something fails in between, the card says what happened. Not delivered: the card is pending again, with "נסה שוב". Thread gone: "לא בוצע", with a note. Unknown outcome after an approval: everything is reloaded.

### Verification
- **Karma: 81/81** (`npx ng test --watch=false --browsers=ChromeHeadless --include=…`, see `Shabzak/README.md`): the SSE parser (8), markdown (5), protocol (6), refresh mapping (6), decision mapping (5), API client (10), state service (22), approval card (6), panel (8), launcher (3), logout reset (2). They passed in three separate runs (Jasmine randomizes the order). The full `ng test` gives 96 passing and 34 failing: the 34 are the generated component stubs that fail without providers, exactly as before these phases (49 specs, 34 failing).
- **Live: `verification/verify-p11-p12.ps1`, 107/107 PASS.** It starts the agent on port 3001 with the scripted LLM stub, launches headless Chrome, and `ui-e2e.mjs` drives the real UI (`ng serve`, the dev environment) over the DevTools protocol:
  - SSR: both hosts are rendered empty, with no panel or launcher markup.
  - Layout: no launcher when logged out or on `/login`. After an Admin login it sits fixed at the bottom right (24 px, z-index 900), on the other side from the page's left floating button. The panel is RTL, 400×600, with its title, the admin chips and the empty-state line, and the input is focused when it opens. Below 768 px it fills the screen and the launcher hides.
  - Streaming: the answer streams from the agent, and the request carries the `Authorization: Bearer` token from `localStorage`. Assistant bubbles are on the right, user bubbles on the left, with `dir="auto"`. A markdown table renders as an RTL table.
  - **Injection:** `<img onerror>`, `![…](url)`, `[…](url)` and `<script>` in an answer produce no `img`, `a` or `script` elements, run no script, and send no request to those URLs. The raw HTML shows as text, and Angular reports no sanitizer warnings.
  - Status and Stop: the status line shows "חושב…" with Stop in place of Send; a tool answer arrives; Stop aborts and the next message is answered.
  - Approvals: the card has the server's title ("בקשת חופשה עבור נועם פרידמן"), the risk chip and the details. The input is disabled with its hint, the closed launcher shows the badge "1", and "שלח" stays disabled until a choice is made.
    - Approve: the write runs, the card shows "אושר", the "הנתונים עודכנו" snackbar appears, and the UI requests `chat/resume`, `GetSoldiers?reloadCache=true` and the user's own `GetVacations`.
    - Reject with a reason: the agent gets `REJECTED` with the reason, the card shows "נדחה: לא עכשיו", and nothing is reloaded.
    - Two writes in one turn: one approval with two cards and the bulk buttons. Approving one and rejecting the other runs exactly one write; the outcomes are "אושר|נדחה".
  - Persistence: a reload restores 20 messages and the same thread. After a reload with a **pending** card, the badge is back, the card is still pending, the input is disabled, and approving resumes the same thread. An unknown thread (simulating an expired thread or an agent restart) is answered transparently, with the divider note right before the message and a new thread stored.
  - Unread, Escape, New chat: closing the panel during an answer shows the unread dot, and opening clears it. Escape closes the panel. New chat empties the list, removes the stored conversation and calls `chat/reset`.
  - Logout: back on `/login` and it stays there. The launcher is gone, the token, user and conversation are cleared, and the thread is reset.
  - Regular user: the self-service chips and no "changes" line. A scripted write call gets no card (the tool isn't registered for the role), and the API shows no vacation for them.
  - **401** (valid `exp`, bad signature): the snackbar "פג תוקף ההתחברות. יש להתחבר מחדש.", then `/login` with the token cleared.
  - **LLM down** (the stub killed): "שירות העוזר לא זמין כרגע" with the agent's English detail and "נסה שוב".
  - **Agent down**: "אין חיבור לשירות העוזר", and the message is marked "לא נשלחה". **Agent back** (a new process): "נסה שוב" answers the same message on a new thread, with the note.
  - **429** (the agent restarted with 2 requests a minute): "שלחת הרבה הודעות בזמן קצר. אפשר לנסות שוב בעוד N שניות." and "נסה שוב".
  - No uncaught exception in any phase.
  - API state afterwards: exactly the 3 approved vacations exist, the rejected ones don't, and none for the Regular user. Cleanup returns soldiers 8 and 3 to their starting vacations. The agent log has 3 `[audit]` lines (one per approved write) and no JWT.
- I also looked at screenshots of the widget (empty state, two cards, after the decisions, mobile) taken by the same driver: the Hebrew RTL layout, cards, chips and tables render as intended.
- **Not run here:** the widget with the real model (action item 6); `npm run build` was run, but no real-account smoke test (action item 3).

---

## Phase 13: integration, deployment, end-to-end acceptance

Workspace root (not a repo), `Shabzak` and `ShabzakAgent` (none committed). No application source changed in any repo. Docker isn't installed on this machine, so the stack was verified without it (see "How it was verified").

### Your decisions before coding
- **Production routing.** Nothing proxied the production URLs. `environment.prod.ts` uses the relative `/api` (since a July 2026 refactor; on `master` it is `https://localhost:7170/api`) and `/agent/api`, but the UI's `nginx.conf` only served files, so the containerized UI couldn't even log in. You chose to have the UI container's nginx proxy both (deviation 49).
- **Base images.** The UI image built on Node 18 (EOL; `marked` 18.1.3 needs Node ≥ 20) and served with nginx 1.17.1 (2019). You chose current pinned versions (deviation 50).
- **nginx for the local checks.** You approved downloading the official nginx 1.30.6 for Windows, to run the real `nginx.conf` here.

### Changed and new files
| File | Change |
|---|---|
| `docker-compose.yml` (workspace root) | New `shabzak-agent` service: `build: /Shabzak/ShabzakAgent`, `5232:3000`, network `ui_layer`, `depends_on: shabzak-api`, `restart: always`, `TZ`, `SHABZAK_API_URL=http://shabzak-api:8080/api`, `CORS_ORIGINS`, `LLM_*`, `LOG_LEVEL`. The API service now also gets `ConnectionStrings__ShabzakDB` and `Jwt__Key`. Every value comes from the root `.env` |
| `.env.example` (workspace root, new) | Every variable compose reads, documented, with no values, and how to generate `JWT_KEY` |
| `Shabzak/nginx.conf` | The `/api/` and `/agent/` proxy, the prerendered-route fix, `absolute_redirect off`, the JSON 502 |
| `Shabzak/Dockerfile` | `node:22.23.4-alpine` build stage, `nginx:1.30.6-alpine` runtime |
| `Shabzak/.dockerignore` (new) | Keeps `node_modules`, `dist`, `.angular`, `.git` and `.env*` out of the build context |
| `ShabzakAgent/Dockerfile` | `node:22.23.4-slim` (was the floating `node:22-slim`) |
| `ShabzakAgent/.env.example` | A comment line: under compose, the root `.env` applies |
| Docs | `UNIFIED_AI_CONTEXT.md` (§1, §2.4.1, §3.7, §5, §7 rewritten, §8, §10), the three `AI_CONTEXT.md` files, and the three READMEs. The API README's Docker and configuration sections were wrong: they named a root `.env.example` that didn't exist, `Aes:*` keys and a Development-only Swagger |
| `verification/` | New `get-nginx.ps1`, `gateway.ps1`, `compose-check.mjs`, `agent-config-check.ts`, `p13-http.mjs`, `verify-p13.ps1`, `verify-p13-llm.ps1`; `agent-http.ps1` gains a gateway mode; `ui-e2e.mjs` gains the `prod-login` and `gateway-agent-down` phases |

### A request in the containerized stack
```
browser ── http://<host>:5230 ──► shabzak-ui (nginx 1.30.6)
                                   ├─ /          → the build: a file, a prerendered route's index.html, or the app shell
                                   ├─ /api/…     → shabzak-api:8080/api/…   (900 s reads, 30 MB bodies)
                                   └─ /agent/…   → shabzak-agent:3000/…     (no buffering, 300 s reads, JSON 502 when down)
shabzak-agent ── http://shabzak-api:8080/api (the user's JWT) ──► shabzak-api ──► SQL Server (Azure SQL or shabzak-sql)
```

### Design notes
- **One origin.** The browser never leaves port 5230, so the API's and the agent's CORS settings don't matter for the containerized UI. `AGENT_CORS_ORIGINS` is empty by default; it only matters for a UI on another origin that calls port 5232 directly.
- **Streaming through nginx.** `proxy_buffering off` (the agent's `X-Accel-Buffering: no` says the same). The read timeout is 300 s, and the agent pings every 15 s. When the browser leaves, nginx closes its connection to the agent, which stops the run, as before.
- **Start order and restarts.** The upstreams are variables, resolved per request through Docker's DNS (`127.0.0.11`, cached 10 s). A plain `proxy_pass` with a host name would make nginx fail at startup while the agent is down, and would keep a recreated container's old address until nginx restarts.
- **Agent down.** The gateway answers `502 {"code":"upstream_error","message":"The Shabzak assistant service is not reachable."}`, in the agent's own error format. The widget shows "שירות העוזר לא זמין כרגע" with that detail, marks the message "לא נשלחה" and offers "נסה שוב". The rest of the UI keeps working.
- **Required values fail fast.** `${SHABZAK_DB_CONNECTION_STRING:?…}` and `${LLM_API_KEY:?…}` make compose refuse to start and name the missing value, instead of starting a container that crash-loops. `JWT_KEY` stays optional, as before, but is strongly recommended (action item 20).
- **No secret in an image or in the compose file.** The agent's local `.env` (your OpenAI key) and any `.env` are excluded from every build context, and the compose file holds only `${…}` references for the API and the agent.

### Bugs found and fixed
1. **Every prerendered route answered 301** (e.g. `/login` → `/login/`). The prerendered pages are folders, and `try_files $uri $uri/` adds the slash. The `Location` was absolute and named nginx's own port 80, which compose publishes as 5230, so in the container the redirect would have pointed at the wrong port. Now `try_files $uri $uri/index.html /index.html`, plus `absolute_redirect off`. Found by the first gateway run.
2. **The UI had no `.dockerignore`,** so `COPY . .` would copy a dev checkout's `node_modules` (e.g. the Windows esbuild binaries) over the container's `npm ci` result.
3. **The API couldn't start under compose** since P0 removed the hardcoded connection string (`appsettings.json` holds a placeholder). Now it comes from the root `.env`.
4. **The UI image's Node 18 is rejected by `marked` 18.1.3** (`engines: >= 20`), the only package in the lockfile that rejects Node 18.

### How it was verified
Docker isn't installed here: not on Windows, and not in WSL (Ubuntu, where `sudo` needs a password). So `docker compose up --build` couldn't run (action item 9). Instead, `verification/verify-p13.ps1` runs the compose topology on this machine, with one origin like the UI container:
- The production UI build sits behind the UI's **real `nginx.conf`** on **nginx 1.30.6 for Windows**, the same version as the image. It is the official zip from nginx.org: `get-nginx.ps1` checks its PGP signature (Sergey Kandaurov's release key from nginx.org/keys) before extracting it, and I deleted it after the runs.
- `gateway.ps1` replaces only the Docker-specific values (port 80, the `mime.types` path, the root, the two service host names), and each must match exactly once.
- Behind it run the local API (LocalDB, http profile) and the agent on port 3901 with the scripted LLM stub.

**234/234 PASS:**

- **Static checks (36, `compose-check.mjs`):**
  - The compose structure, with compose's `${…}` substitution emulated: a `.env` copied unchanged from `.env.example` makes compose refuse to start, naming the two required values.
  - The agent environment that compose produces, run through **the agent's real `loadConfig`** (`agent-config-check.ts`): port 3000, `http://shabzak-api:8080/api`, no CORS, `gpt-4.1`, `info`; every optional value set in `.env` arrives as well.
  - `.env.example` documents every variable compose references and holds no values; secrets appear only as references.
  - The Dockerfiles, the three `.dockerignore` files, and the Node engines of both lockfiles: nothing rejects 22.23.4, and `marked` rejects 18.
  - The production URLs match the nginx locations.
- **The build:** `npm run build` passes. The bundle has `serverURL:"/api",agentURL:"/agent/api"` and no dev URL, and the 7 routes are prerendered.
- **HTTP through the gateway (37, `p13-http.mjs`):**
  - `nginx -t` passes. `/`, `/login` and `/assignments-management` serve their pages without a redirect, an unknown path gets the app shell, and JavaScript and CSS have the right MIME types.
  - `/api/` reaches the API: its Bearer challenge, Hebrew data, and a **real `User/Login`** with a synthetic account. `/agent/` reaches the agent with the prefix stripped: `/health`, its 401 and 404 JSON, and the real login's token accepted.
  - **SSE isn't buffered:** the `thread` event arrives at once and the answer about 3 s later, the **15 s ping** reaches the client, and Hebrew arrives intact.
  - **A client that leaves** frees its thread at once: the gateway closes its connection to the agent, which stops the run (and logs it). The agent's own 413 and 400 answers pass through unchanged.
  - CORS is closed by default. With `AGENT_CORS_ORIGINS`, the preflight and the stream grant only that origin, with `Retry-After` exposed and `Vary: Origin`.
  - With the agent down: the 502 JSON, while the UI and `/api/` keep working.
- **The UI in headless Chrome through the gateway (134, `ui-e2e.mjs`):**
  - The whole P11–P12 `main` phase (69) on the production build: layout, streaming, RTL, markdown injection, approvals (approve, reject with a reason, mixed), the refresh requests through `/api/`, persistence across reloads (also with a pending card), an unknown thread, the unread dot, Escape, New chat, mobile, logout. Then the `regular`, `unauthorized` (401 → login) and `llm-down` phases.
  - **Real logins through the login form** for Admin, SuperAdmin and Regular. Each reaches the personal page, gets a chat answer through `/agent/api` with that login's token, and logs out. The SuperAdmin's role-change card was rejected, and the Regular user has no admin tool. Every API and agent request stayed on the gateway's origin.
  - **The agent down behind the gateway** (the banner with the gateway's detail, "לא נשלחה", "נסה שוב"), then the agent back (the retry answers on a new thread with the note), and a 429 with its wait time.
- **The chat suites again, through the gateway:** `verify-p7` (52), `verify-p8` (52), `verify-p9` (39) and `verify-p10` (41) pass with every chat request through nginx (`agent-http.ps1` gateway mode). That covers everything from the SSE protocol, the approvals and the client disconnect to the soldier, account, mission and schedule flows. nginx's access log shows their requests, including the 403, 404 and 400 answers that only those suites trigger.
- **The data and the logs:** after the run, exactly the 3 approved vacations exist, and user 3 is still Regular after the rejected role change; the cleanup returned everything to the start. The agent wrote 3 audit lines (one per approved write), and its log has no JWT, phone or personal number. nginx's error log has no `emerg`, `alert` or `crit` line, and its access log has no token.
- **Regression of the unchanged code:** the agent's `npm run typecheck` and `npm test` (328/328), and `dotnet test` (156/156). The UI's sources didn't change (its Karma specs weren't re-run); its production build passes.

### The checklist with the real model (not run here)
`verify-p13-llm.ps1` scripts the plan's P13 checklist for the real model (action item 6). This network still blocks `api.openai.com` (a 302 to the URL-filter block page, checked again on 2026-11-15). I ran it with `-Stub` to check its own mechanics: the setup, the gateway path, the API-side security checks (a Regular user's 403, another user's thread → 403, no contact details in a soldier table) and the full cleanup pass, while the 38 model-behavior checks fail, as they must without a model.

### Not verified here
- Building the images and `docker compose up` (action item 9), including Docker's DNS name resolution.
- The model's behavior, in the containerized stack or anywhere else (action item 6).
- A smoke test with real accounts on the production database (action item 3).

---

## Deviations and contract changes
These are recorded in the plan's deviations log, and §4 was updated to match.

1. **Verification database (P0–P4).**
   - The production Azure SQL was unreachable, so live checks used an isolated **SQL LocalDB** database `ShabzakAgentDev` with **synthetic** data seeded through the API.
   - Tokens were minted with a process-only dev signing key.
   - This is safer than the approved "no writes on prod", but the UI login with real accounts is still open (action item 3).
2. **Token revalidation (P1, addition).** Without it, disabling an account or changing a role through `UpdateUser` would only take effect when the token expires, up to 8h later. A consequence is that tokens must reference an existing user.
3. **Contract tightening (P1):**
   - Non-admins don't see other soldiers' vacations in `GetSoldiers`.
   - Admins can't `ResetPassword` a SuperAdmin.
   - The guard also blocks self-deactivation and deactivating the last enabled SuperAdmin.
   - `CreateUsersForSoldiers` skips existing accounts. Duplicate usernames would make `UsersCache.ReloadCache` throw inside its timer, which crashes the process.
4. **UI bug fix (P1).** The personal-page password change was already broken: the username was hashed twice and the password was always empty, which would have set an empty password. This fix makes the P1 acceptance item "change password still works" true.
5. **Contract additions (P2):**
   - the `NOT_FOUND` code, the 1000-soldier cap, and 404/400 with `{ message }`
   - `INSTANCE_FULL` semantics for a requested slot
   - `vacation.status` as a string
   - live `isFilled`
   - `futureInstanceCount` = instances with `from >= now`
   - the `"N/A"` placeholders become null
6. **Extra tests (P2):** the guard and the mapper also have unit tests, beyond the evaluator.
7. **Write responses (P3/P4).** The plan only said "`Success=false` when blocked". Every agent-facing write now returns `GeneralResponse<T>` with HTTP 200 (applied), **409 when blocked** (`value` still carries the validations), 409 for conflicting data, 404 for unknown entities and 400 for invalid input, like the P1 account endpoints. A non-200 status makes "nothing was written" explicit for the P5 client. All writes are serialized in-process (`WriteSync`) so validation and save can't interleave.
8. **Blocking and slot rules (P3):** `ALREADY_ASSIGNED` (and `NOT_FOUND`) can't be overridden, because they would create a duplicate row. `ReplaceSoldier` puts the new soldier in the vacated seat when they fit it, and `MoveSoldier` keeps the slot within one mission. The plan only said "the slot from the request, or `suggestedMissionPositionId`", which could move a replacement to a different role when the instance had other gaps.
9. **Legacy bug fix (P3).** `Mission/RemoveSoldierFromMissionInstance` (the UI's "הסר חייל" on `/assignments`) failed with 500 on **every** call: it summed `Mission.MissionPositions` without loading them. The plan's P3 "read first" pointed at this line. I added the missing `ThenInclude`; verified live (500 before, 200 after).
10. **Contract additions (P4):**
    - `GenerateMissionInstances` also accepts dates (the UI semantics), returns the resolved `from`/`to` and `lastSlot` (P9 wants the first and last slot, and the preview stops at 50), and rejects special missions.
    - `UpdateMissionInstance` gains `dryRun` (for the P9 approval card), returns `instance: null` when blocked, and rejects overlapping another instance of the mission (409).
    - `UpdateMissionDetails`: `""` clears a text field; removing a slot that pending auto-assign candidates reference is also refused.
    - `DeleteMissionInstance` deletes the instance's pending candidate rows, which have no FK.
    - `AddMissionInstances`: overlaps within the request → 400, with existing instances → 409.
    - Created missions get `RequiredInstances = true` (the UI default).
11. **Dependencies (P5).** The direct dependencies match the agents lockfile exactly, but a fresh resolve would have mixed in an incompatible `@langchain/langgraph-checkpoint` (1.2.2, peer `@langchain/core ^1.1.48`). `overrides` pin it, `langsmith` and `openai` to the agents' tree. `undici` is 8.12.3, because 8.12.4 was only 7 days old. Node ≥ 22.19 (undici 8).
12. **Agent HTTP additions (P5, §4.6 updated):**
    - `GET /api/me` returns the identity the agent resolved. P5's acceptance needs a protected route ("a valid JWT gets through"), and the UI can use it too.
    - Pre-stream errors have the body `{ code, message }`.
    - Besides the listed statuses, there is 404 `not_found` (unknown route), 413 (body over 64 KB) and **502 `upstream_error`** when ShabzakAPI is down during authentication. The plan listed only 400/401/403/409/429.
13. **The 60 s identity cache (P5)** means a token used in the last minute still passes the agent's middleware after its account is disabled. Every tool call forwards the token, and the API rejects it at once (verified live). Lowering `cacheTtlMs` is a one-line change if you prefer.
14. **Local dev uses the API's `http` launch profile (P5, §4.8 updated).** The `https` profile redirects `http://localhost:5165` to https. Following that redirect would drop the bearer token, so the client refuses it with an actionable message. `start-api-localdb.ps1` gained `-LaunchProfile http|https` (the default stays `https`, so `verify-p1..p4` are unchanged). `.env.example` uses `PORT=3001` (the UI's planned dev `agentURL`; the container keeps 3000).
15. **Client additions (P5, §4.4 updated):** `Soldiers/GetSoldiers` (see 17); `acceptAssignCandidate` discards its response, which is every mission. There is deliberately no method for `Mission/UpdateMission`.
16. **LLM auth: OpenAI API key only (user decision, after P6).**
    - The client-credentials OAuth mode is removed: `llm/dell-auth.ts`, `LLM_AUTH_MODE`, `DELL_CLIENT_ID` and `DELL_CLIENT_SECRET` are gone.
    - `LLM_API_KEY` is required, `LLM_MODEL` defaults to `gpt-4.1`, and `LLM_BASE_URL` is optional (unset = the OpenAI API).
    - `createChatModel` is synchronous and omits `temperature` for reasoning models.
    - Two hardening changes came from testing against the firewall. The OpenAI client refuses redirects; a block page used to surface as a timeout or a parser crash. `describeLlmError` explains blocked, key, model, quota, timeout and network failures.
    - Your `.env` lost the two lines of the removed mode; the key line is unchanged.
17. **Tool behaviors the plan didn't spell out (P6):**
    - Limits: `get_schedule` ≤ 14 days and 60 rows; `get_soldier_schedule` ≤ 62 days.
    - Counted hours use the mission's `actualHours`, matching the fairness page. `GetSummary` is all-time and uses raw durations.
    - `search_soldiers` for Regular users is name-only.
    - `get_fairness_stats` sends `to` as the last day at 00:00, because the API adds a day. It doesn't use the breakdown endpoint, which clears cached soldiers' `missions` (issue 15).
    - Exclusions are counted once per soldier, under the first violation.
    - The API's `Vacation` DTO has no soldier ID, so the admin "all soldiers" vacation view attributes vacations through `GetSoldiers`, reading only IDs, names and vacations.
18. **Layout and budget (P6, §4.10 updated):** added `tools/common.ts`, `utils/cors.ts` (no `cors` dependency), `utils/http-error.ts` and `src/testing/`. The per-turn tool budget is enforced in `defineTool`'s wrapper through the per-turn `RunContext`, rather than by re-wrapping tool functions in the agent factory.
19. **Dev CLI addition (P6):** `--tool <name> --args '<json>'` and `/tool` run one tool for the token's role without an LLM. `verify-p6.ps1` relies on it.
20. **P6 acceptance not fully run:** the dev network blocks OpenAI, so the LLM-answered questions are pending (action item 6; scripted in `verify-p6-llm.ps1`). The agent loop is covered with `FakeToolCallingModel`, and every tool live.
21. **Own approval middleware (P7, task 6 replaced).** langchain 1.2.25's `humanInTheLoopMiddleware` mishandles a batch with mixed decisions (see the P7 spike findings). `agent/approval.ts` keeps its protocol (one interrupt per model turn, `Command({ resume: { decisions } })`), runs every approved and non-gated call, and answers each rejected call with an error ToolMessage that carries the user's reason. Upgrading to langchain 1.6 would also have moved the pinned LangChain tree.
22. **Refused before asking (P7, §4.6 updated).** When a describer fails (invalid arguments, unknown entity, a guard rule, an API 4xx), the action isn't shown. The model reads `NOT RUN: …` and can fix the call or ask. `approval_required` lists only the shown cards, and `resume` takes one decision per shown card. If the API is down while describing, a generic card is shown instead of refusing.
23. **Chat API additions (P7, §4.6 updated):**
    - 409 `thread_busy` (one run per thread)
    - `resume` without a pending approval → 400
    - `reset` → 204 also for unknown threads, and 403 for someone else's
    - the rate limit is shared by `stream` and `resume`
    - ≤ 10 threads per user
    - a disconnect aborts the run
    - the read-tool budget doesn't block approved writes, and it continues across a resume
24. **Tool-call repair (P7, addition).** A run stopped between a tool call and its result leaves a history that OpenAI rejects, and every later message on that thread would fail. `agent/middleware.ts` repairs the copy sent to the model: reads get `CANCELLED`, and writes get `OUTCOME UNKNOWN … check with a read tool`.
25. **Vacation periods (P7).** `request_vacation`'s date-only `to` is the last day, **included**: it ends at 23:59:59, so the UI shows that day as the last one. The limit is 90 days. `respond_to_vacation` finds the vacation through `soldierId` when the model has it, else through the soldier list.
26. **Fixes found by the live suites (P7–P8):**
    - The log masker turned 7–9-digit runs inside UUIDs (the thread IDs in audit lines) into `[number]`. It is now UUID-aware.
    - `chat-cli --tool` printed info log lines before its JSON when `LOG_LEVEL` is set. In tool mode, logs now go to stderr, and `verify-p6.ps1` parses stdout only.
    - LLM failures reached the stream wrapped in langchain's `MiddlewareError` and were reported as `internal`. `describeLlmError` now follows `cause`, so a blocked network gives the clear `upstream_error`.
27. **Live verification with a scripted LLM (P7–P8).** OpenAI is blocked here, so `verify-p7.ps1` and `verify-p8.ps1` run the real agent with `fake-openai.mjs`, a local OpenAI-compatible stub. The model's choices come from `CALL <tool> <json>` directives in the user message, so the tests are deterministic. Streaming, approvals, tools and API calls are all real. The LLM's own behavior is checked by `verify-p7-p8-llm.ps1` (action item 6).
28. **Contact details on approval cards (P8, §4.9 updated; action item 10).** Stored values are masked. `create_soldier` reads back the typed phone and personal number in full so they can be checked before saving. `update_soldier` masks the phone on both sides (your decision) and shows the new personal number in full.
29. **Soldier tool rules beyond the plan (P8):**
    - The personal number must be unique (it becomes the username).
    - Phones are stored as local digits.
    - Non-commanders keep חפ"ש on update too.
    - `positions` / `addPositions` / `removePositions` change positions.
    - A failed `SetSoldierActive` after a successful `UpdateSoldier` returns `code: "partial"`.
    - Deleting a soldier deletes their login account too (deviation 31).
30. **Account tool rules beyond the plan (P8):**
    - The `UserAccountGuard` rules are checked before the card.
    - `update_user_account` sends only changed fields.
    - `create_user_accounts` refuses unknown soldier IDs after one soldier-cache reload.
    - `reset_user_password` refuses accounts without a linked soldier or phone.
    - Addition: `list_user_accounts` `soldiersWithoutAccount`.
    - `get_soldier_contact_details` calls are audit-logged like writes (§4.9 updated).
31. **Soldier delete cascades to the login account (P8, your decision; API change, §4.3 updated; action item 11).** `Users.SoldierId` has no FK, so `SoldierService.DeleteSoldier` now deletes the linked accounts itself, in the same `SaveChanges`, and removes them from `UsersCache`. This covers every client, including the UI's soldiers page.
    - **The cascade follows the account guard.** Otherwise an Admin could remove a SuperAdmin's account, or their own, just by deleting the soldier. The new pure `UserAccountGuard.CheckDeleteAll` checks every account. If one may not be deleted, nothing is deleted, and the endpoint answers like the account endpoints (403 / 400 / 409 `GeneralResponse`, via the shared `AccountError`).
    - **The agent side:** `delete_soldier` checks the same rules before the card (`NOT RUN` when refused), says on the card that the account goes too, refreshes `users` as well, and reports `deletedAccounts`.
    - **Not changed:** the UI. It shows its generic error snackbar on a refusal. `delete_user_account` still exists for removing just an account.
32. **`Schedule/GetInstances` (P9, API addition, §4.2/§4.3 updated).** `{ instanceIds }` (1–200) → the `GetAssignments` rows in the requested order, unknown IDs left out, for any authenticated user (the same data as `GetAssignments`). The instance-based cards need a shift by ID; there was no such endpoint (the gap noted after P8).
33. **Day-by-day shifts and inclusive periods (P9).** The API's generator makes one continuous run (like the missions screen), which puts night shifts into a mission with daily hours. The agent's `eachDay` option plans each day's window with a mirror of the generator and writes the shifts with `AddMissionInstances`; continuous stays the default, and its card counts the shifts outside the daily hours. Periods in the tools are the first and the last day, included, so a round-the-clock or overnight mission also covers its last day (action item 7, second bullet).
34. **Mission tool rules beyond the plan (P9):** `update_mission` keeps slot IDs, accepts `setPositions` / `removePositions`, lets the requirements follow the slots when they matched them before, and refuses removing a slot that assignments use (with a warning when pending proposals might); `delete_mission` refuses unknown missions (the API answers 500); `update_mission_instance` puts the API's dry run on the card and refuses a soldier overlap before asking; the instance update / delete tools also refresh `soldiers` (§4.7 updated).
35. **Auto-assign API fixes (P10, §4.4 updated; action items 13 and 14).** Accepting a proposal inserted the existing assignments of the shifts the run had skipped a second time (reproduced on the dev DB: soldier 8 twice in one shift). `FinalizeScheduleResult` now stores only the proposal's own assignments, and `AcceptAssignCandidate` skips rows that duplicate an assignment or point at a deleted instance / soldier / slot (the pure `CandidateAcceptanceFilter`, 5 xUnit tests), which also makes older proposals and the dangling rows of issues 13 and 19 safe. `AutoAssign` during an interactive session answered 500; it now answers 409 `{ message }`.
36. **Schedule tool rules beyond the plan (P10):** `run_auto_assign` is capped at 31 days, leaves inactive soldiers out of the pool (auto-assign doesn't check the flag) and refuses when no shift is empty (it fills only shifts without any soldier). Proposals are cached per conversation in a compact, contact-free form (the full ones carry every soldier's contact details and are megabytes each), at most 8. `get_candidate_details` also filters by soldier and status. The assignment tools validate exactly what the API's write will check and refuse violations before asking. `suggest_replacements` reports the API's inclusive overlap flag as `BUSY` (issue 8) and validates swaps like `ReplaceSoldier(swap)`.
37. **Runtime additions (P9/P10):** a `tool_start` event may carry the tool's own label (the auto-assign heartbeat); `ThreadRecord.cache` / `RunContext.threadCache` hold conversation-wide tool data (cleared with the thread; the CLI REPL keeps one per session).
38. **Live verification with the scripted LLM (P9/P10).** `verify-p9.ps1` and `verify-p10.ps1` use the stub like P7/P8; the real-LLM behavior of the new workflows is listed in action item 6.
39. **The UI's dev `serverURL` uses the API's `http` profile (P11, your decision; §4.8 updated; action item 15).** `environment.development.ts` (used by `ng serve`) and the base `environment.ts` now point at `http://localhost:5165/api`, so the UI and the agent share one local API. `agentURL` is `http://localhost:3001/api` in dev and `/agent/api` in production, a placeholder for the P13 reverse proxy (action item 17).
40. **`marked` 18.1.3 (P11).** The plan asks for an exact version at least 7 days old: 18.2.0 was 4 days old, so 18.1.3 (11 days) it is. It is ESM-only with no dependencies, and works with the application builder (incl. SSR) and with Karma. `npm install` added exactly one package.
41. **Hardened markdown (P11, §4.9 updated).** The plan relied on Angular's sanitizer, which keeps `<img src="https://…">` and links. A reply influenced by prompt injection (e.g. a soldier's name or a mission description crafted to steer the model) could then make the browser load a URL carrying data, without a click. The renderer therefore escapes raw HTML and renders images and links as their text; the sanitizer stays as a second layer.
42. **Client details (P11).** `fetch` instead of `HttpClient` (the body must be read while it streams; the token comes from `UserService.getToken()`), an injectable `CHAT_FETCH` for tests, the read loop outside the Angular zone, `ngSkipHydration` on the two hosts. The plan's state fields are kept, plus `unread` and `highlightApproval` for P12.
43. **Logout (P12, your decision; action item 16).** A toolbar button and `userActions.logout`: `UserService.logout()` removes the token, the user and the cached user, then the app goes to `/login`. A new `resetOnLogout` meta-reducer resets every store slice except `router`, so the previous user's data (e.g. the soldiers with contact details an admin loaded) doesn't stay in memory for the next login. The chat clears itself whenever the store user changes or disappears, and an agent 401 dispatches the same logout.
44. **Refresh mapping beyond the plan (P12; action item 18):**
    - `vacations` also reloads the user's own `getVacations`, so the personal page's list updates too.
    - `users` reloads nothing, because the UI keeps no account list.
    - `candidates` → `getAllCandidates()`. Its reducer now also drops proposals that are no longer pending from `candidatesList` and the selected `candidateAssignments`. Otherwise the assignments-management dialog could still offer a proposal the agent's acceptance had discarded.
    - After an **approved** resume that fails or is stopped once the agent has started it, the UI reloads missions, soldiers, vacations and candidates, because the write may have run but no `final` arrived.
45. **The approval card (P12).** The plan lists per-action choices, bulk buttons and a submit. Implemented as an אשר / דחה toggle per action (with an optional reason for דחה) and one "שלח", enabled once every action is decided. "אשר הכל" / "דחה הכל" set every choice and appear only for two or more actions. Nothing is sent on a single click, so a high-risk action always takes an explicit choice plus "שלח".
46. **Error and thread handling details (P12):**
    - Retryable: `thread_busy`, `rate_limited`, `upstream_error`, `internal` and network errors.
    - A message retry re-sends the same text without a new bubble.
    - A resume that never reached the agent puts its card back to pending.
    - 400 on a resume (nothing pending) and `thread_not_found` on a resume expire the card with a note. Nothing ran.
    - `approval_pending` re-shows and highlights the last card; with none, the banner offers "שיחה חדשה".
    - `forbidden` drops the thread ID.
    - The banner adds the agent's own text for `forbidden`, `bad_request`, `upstream_error` and `internal`. It is English, but it says why, e.g. that a firewall blocks the LLM.
47. **Persistence details (P12).** Saved at turn boundaries, not per token (≤ 200 messages, ≤ 500 000 characters), and validated on restore. On a reload a streaming answer becomes "התשובה נקטעה", and only the last pending approval stays pending, so its card can still be approved on the same thread. `open` / `unread` aren't stored. New chat, logout and a user change also reset the agent thread (best effort).
48. **UI verification in a real browser (P11–P12).** The plan's acceptance is interactive, so `verify-p11-p12.ps1` drives the real UI in headless Chrome over the DevTools protocol (`ui-e2e.mjs`, Node 22's built-in WebSocket, no new npm dependency), against the real agent with the scripted LLM stub, like P7–P10. It also covers failure modes the plan lists under "Error UX" (401, network, 429, `approval_pending`, the LLM or the agent down) and markdown injection.
49. **Production routing in the UI container (P13, your decision; §4.6 and §4.8 updated; action item 17).** The plan's P13 said "set the production UI `agentURL`" and "confirm CORS works for every UI origin", assuming a reverse proxy that already served `/api`. There was none: the UI's `nginx.conf` served only files. The UI container's nginx now proxies `/api/` → `shabzak-api:8080` and `/agent/` → `shabzak-agent:3000` (prefix stripped, unbuffered, Docker DNS per request, a JSON 502 when the agent is down). So the production `agentURL` stays `/agent/api`, every UI origin is the gateway's own origin (no CORS involved), and compose's `CORS_ORIGINS` is empty by default (`AGENT_CORS_ORIGINS` for a UI on another origin).
50. **Current pinned base images (P13, your decision).** UI build: `node:hydrogen-alpine` (Node 18, EOL, rejected by `marked` 18.1.3) → `node:22.23.4-alpine`. UI runtime: `nginx:1.17.1-alpine` (2019; its resolver, which the proxy now uses, has CVE-2021-23017) → `nginx:1.30.6-alpine` (stable, released 2026-10-14; also fixes CVEs in variable expressions and rewrite, both used by the proxy). Agent: the floating `node:22-slim` → `node:22.23.4-slim`. 22.23.4 is the Node version all verification ran on; 22.23.5 was 4 days old.
51. **The API's secrets in compose (P13, addition).** The plan's P13 listed only the agent's variables and "document a persistent `Jwt__Key`". But since P0 moved the connection string to user-secrets, the API container had no connection string under compose and couldn't start. The `shabzak-api` service now gets `ConnectionStrings__ShabzakDB` (`SHABZAK_DB_CONNECTION_STRING`, required) and `Jwt__Key` (`JWT_KEY`) from the same root `.env`, and the new root `.env.example` documents every variable with no values. The SQL container's hardcoded `sa` password is unchanged (action item 19).
52. **nginx and build-context fixes (P13).** The prerendered routes answered 301 with an absolute `Location` on port 80; now they are served directly (`try_files $uri $uri/index.html /index.html`, `absolute_redirect off`). The UI got a `.dockerignore`.
53. **Verification without Docker (P13).** Docker isn't installed here, so instead of `docker compose up --build`:
    - The compose file and the environment it produces are checked statically, including through the agent's real config validation.
    - The UI's real `nginx.conf` runs on nginx 1.30.6 for Windows (downloaded with your approval and PGP-verified) in front of the local API and agent, and the P11–P12 browser phases, real logins and the P7–P10 chat suites run through it (`verify-p13.ps1`, 234/234).
    - The plan's checklist "in Hebrew and English with Admin, SuperAdmin and Regular" needs the real model, which this network blocks. With the stub, the three roles and every mechanical item of the checklist passed (security, approvals, reject = no change, refresh hints, reload, logout, thread hijacking). The language-dependent items are scripted in `verify-p13-llm.ps1` (action item 6).

---

## Verification environment (reusable for later phases)
The scripts are in `verification/` in this folder. They contain no secrets: `jwt.key` is generated per machine and gitignored, and all data is synthetic.

| Script | Purpose |
|---|---|
| `dev.ps1` | `New-DevJwt -UserId -Role -SoldierId` (HS256, 30 min) and `Invoke-Api` (dates kept as strings) |
| `start-api-localdb.ps1` | Runs the API with process-scoped `ConnectionStrings__ShabzakDB=(localdb)\MSSQLLocalDB;Database=ShabzakAgentDev` and `Jwt__Key` (`--no-build`: build first). `-LaunchProfile https` (default, for `verify-p1..p4`) or `http` (for the agent: `verify-p5/p6`, the chat CLI) |
| `seed-localdb.ps1` | Seeds through the API: 16 soldiers, 4 missions / 64 instances (guard 4h 24/7, patrol, standby, special), assignments, vacations (Pending, Approved, Denied), 5 accounts |
| `verify-p1.ps1`, `verify-p2.ps1` | The P1 and P2 acceptance suites |
| `verify-p3.ps1`, `verify-p4.ps1` | The P3 and P4 acceptance suites. They revert every change they make (P4 uses `sqlcmd` to add and remove a synthetic pending-candidate row), so all four suites can be re-run in any order. |
| `verify-p5.ps1` | The P5 suite (API on the `http` profile). It starts the agent from `ShabzakAgent/` on port 3901 with dummy LLM settings, runs the checks, stops the process tree, and scans the agent log for JWTs. Its only data change (disabling user 3) is reverted |
| `verify-p6.ps1` | The P6 suite (API on the `http` profile, read-only). It runs every read tool through `chat-cli.ts --tool` with dev JWTs, cross-checks with the API endpoints, and scans all outputs for phone numbers, personal numbers and JWTs |
| `verify-p6-llm.ps1` | The P6 LLM acceptance. Needs the API on the `http` profile, `LLM_API_KEY`, and a network that allows OpenAI. It pipes the plan's questions into the CLI as Regular and Admin, and as a Regular user with shifts (user 4, activated for the question and deactivated again). It checks the tools called and the key facts against the API, checks that off-topic requests (market advice, general knowledge, coding, a jailbreak) get the §4.11 reply with no tool calls and that a mixed request gets only its Shabzak part answered, and writes `p6-llm-transcript.log` |
| `agent-http.ps1` | Helpers for the chat suites: `Start-AgentStack` (the agent on port 3901 plus the LLM stub on 3902, or `-RealLlm` for the `.env` model), `Stop-AgentStack`, `Invoke-Chat` (`curl -N`, parses the SSE events), `Invoke-CliTool` (stdout JSON + stderr), `Get-AgentLog`, `Get-Sha512Hex`. **Gateway mode (P13):** with `$env:AGENT_GATEWAY_URL` (e.g. `http://127.0.0.1:5230/agent`), the chat requests to the agent on `$env:AGENT_GATEWAY_PORT` (default 3901) go through the gateway; agents on other ports are still called directly |
| `fake-openai.mjs` | A local OpenAI-compatible Chat Completions stub (streaming and not) for the agent: `CALL <tool> <json> [&& …] [THEN …]` in the user message makes it call tools; it then answers `done: <tool>=<result> \| …`. It logs each request's tool names, their parameters and the system prompt |
| `verify-p7.ps1` | The P7 suite (API on the `http` profile): the SSE chat API end to end with `curl -N` (events, approvals, reject, refused actions, 409s, resume validation, Regular users, busy + client disconnect, reset, rate limit, audit, log hygiene). Its test vacations of soldier 8 are deleted at the end (`sqlcmd`) |
| `verify-p8.ps1` | The P8 suite (API on the `http` profile): the P8 read tools via the CLI, then every soldier and account write through the chat API with approve and reject, the guard refusals, the per-role schemas (from the stub's request log), the default-password login, the soldier-delete cascade and its guard refusals (agent and API, with a synthetic SuperAdmin account), PII and audit. It creates one synthetic soldier and account and deletes both (API cleanup if it fails midway) |
| `verify-p7-p8-llm.ps1` | The P7–P8 LLM acceptance through the chat API with the real model (see action item 6); writes `p7-p8-llm-transcript.log` |
| `verify-p9.ps1` | The P9 suite (API on the `http` profile): `Schedule/GetInstances`, then the mission and shift tools through the chat API with approve and reject (create with day-by-day shifts, continuous vs day-by-day extension, add / reschedule / delete a shift, update, delete), the refusals before asking, Regular users, audit. Its two synthetic missions (12–13 days ahead) are deleted at the end, also when it fails midway |
| `verify-p10.ps1` | The P10 suite (API on the `http` profile): pool and coverage via the CLI, then auto-assign → proposal → accept, the API's handling of legacy candidate rows (`sqlcmd`), rule refusals, move (within and across missions) / replace / swap / unassign, `suggest_replacements` re-validated through the API, the interactive-session 409, audit, PII. It refuses to start while proposals are pending (accepting would discard them); its two synthetic missions (16–19 days ahead) and the proposal file it creates are removed at the end |
| `ui-e2e.mjs` | The browser driver of the UI suites: a small Chrome DevTools Protocol client (Node 22's built-in `WebSocket`, no npm dependencies) with one phase per scenario (`main`, `regular`, `unauthorized`, `llm-down`, `agent-down`, `agent-back`, `ratelimit`, plus `screenshots`; P13: `prod-login` (real logins through the login form, `LOGINS`) and `gateway-agent-down`). It records the page's network requests, console warnings, failed responses (on stderr) and uncaught exceptions, and prints one JSON check per line. `UI_URL` / `AGENT_URL` point it at the dev server or at the gateway |
| `verify-p11-p12.ps1` | The P11–P12 suite. Needs the API on the `http` profile and the UI dev server (`npx ng serve --port 5555` in `Shabzak/`). It starts the agent on port 3001 (the UI's dev `agentURL`) with the stub on 3902, and headless Chrome on 9333 with a temporary profile. It runs the phases, kills the stub, then the agent, and restarts it (also with a 2-per-minute rate limit) for the failure phases. Then it checks the API state and the agent log, and deletes the test vacations of soldiers 8 and 3 (`sqlcmd`) |
| `get-nginx.ps1` | P13: downloads the official nginx for Windows (default 1.30.6, the UI image's version) into `%TEMP%\shabzak-nginx`, verifies the zip's PGP signature against the keys on nginx.org with Git for Windows' `gpg`, extracts it and prints the path of `nginx.exe` (reused when present). `-Remove` deletes the folder |
| `gateway.ps1` | P13: `Start-Gateway` / `Stop-Gateway` run the UI's real `nginx.conf` with only the Docker values replaced (port 80 → `127.0.0.1:5230`, the `mime.types` path, the root → a copy of `dist/shabzak/browser`, `shabzak-api:8080` → `127.0.0.1:5165`, `shabzak-agent:3000` → `127.0.0.1:3901`), each required to match exactly once; `nginx -t` first |
| `compose-check.mjs` + `agent-config-check.ts` | P13: the static deployment checks (compose structure and emulated `${…}` substitution, the root `.env.example`, Dockerfiles, `.dockerignore` files, lockfile Node engines via semver, the production URLs vs the nginx locations). The agent environment compose produces is run through the agent's real `loadConfig` by `agent-config-check.ts` (tsx). Uses js-yaml and semver from `Shabzak/node_modules` |
| `p13-http.mjs` | P13: the HTTP checks through the gateway, by phase: `routes`, `sse` (timing of the events, the 15 s ping, a client that leaves, 413 / 400), `cors-closed`, `cors-listed`, `agent-down` |
| `verify-p13.ps1` | The P13 suite (about 20 minutes; `-SkipChatSuites` about 6, `-SkipBuild` reuses the build). Needs the API on the `http` profile, Chrome, and ports 5230, 3901–3904 and 9333 free. It gets nginx (`get-nginx.ps1`), runs the static checks and the production build, starts the agent on 3901 with the stub and the gateway, runs the HTTP phases and the browser phases through the gateway (restarting the agent for the failure and CORS phases), checks the API state and the logs, then runs `verify-p7` … `verify-p10` in gateway mode (their output goes to `p13-verify-p*.log`). Users 1–3 get their default password back only if their default login fails. Soldiers 8 and 3's test vacations are deleted |
| `verify-p13-llm.ps1` | The P13 checklist with the real model (action item 6), through the gateway (`-Direct`: straight to the agent). It creates the synthetic mission "תצפית P13" and the soldier "דני P13" and removes them, and refuses to start while proposals are pending. `-Stub` runs it with the scripted stub to check the script itself. Writes `p13-llm-transcript.log` |

- **Bootstrap:** after seeding, run `sqlcmd -S "(localdb)\MSSQLLocalDB" -d ShabzakAgentDev -E -Q "UPDATE Users SET Role=2, Activated=1 WHERE Id=1"` to create the first SuperAdmin. Because of revalidation, a SuperAdmin can't be created through the API from nothing.
- The seed's instance times are relative to the seed day, and `verify-p2/p3/p6` look for "today"/"tomorrow" slots. On a later day, drop the DB and re-seed.
- **Agent tools without an LLM:** `node node_modules/tsx/dist/cli.mjs src/scripts/chat-cli.ts --tool <name> --args '<json>'` in `ShabzakAgent/`, with `SHABZAK_TOKEN` set (from `New-DevJwt`). In PowerShell, call `node` directly (`npx` mangles the JSON quoting), set `[Console]::OutputEncoding = [Text.Encoding]::UTF8` for Hebrew, and don't name a function parameter `$args`. The JSON is on stdout; logs and audit lines are on stderr. A write tool prints only its approval card unless `--yes` is added.
- **UI suite:** `verify-p11-p12.ps1` needs ports 3001, 3902 and 9333 free, Chrome installed, and the dev server already compiled (`ng serve` prints its URL when ready). It takes about 4 minutes. To look at the widget, run `ui-e2e.mjs screenshots` with `SHOT_DIR` set (it writes five PNGs). For the UI's own unit tests run Karma with `--include` (`Shabzak/README.md`).
- **P13 suite:** run `verify-p13.ps1` from PowerShell 7 with the API already up (`start-api-localdb.ps1 -LaunchProfile http`). The first run downloads nginx (about 3 MB, signature-checked); `get-nginx.ps1 -Remove` deletes it again. File names on Windows can't contain `:`: a phase tag like `ui:main` once became an NTFS alternate data stream and stopped the run silently, so the suite now sanitizes the names and fails if any phase didn't report.
- **Chat suites:** `verify-p7.ps1` to `verify-p10.ps1` need ports 3901–3902 free. In PowerShell, variables are case-insensitive (`$m` and `$M` are one variable), which bit the first `verify-p10` run. They stop their processes in `finally` and delete their logs (`*.log` is gitignored anyway).
- **Cleanup:** `sqlcmd -S "(localdb)\MSSQLLocalDB" -E -Q "DROP DATABASE ShabzakAgentDev"`.

---

## Pre-existing issues found (not fixed; outside the phase scope)
1. `UserService.Login`:
   - It replaces the **cached** soldier's `Missions` with future missions only, which affects `GetSoldiers` until the next reload.
   - It throws a NullReferenceException for accounts that have no linked soldier.
2. `MissionService.GetAvailableSoldiers` and `GetReplacementCandidates` mutate cached soldiers (`Missions = []`, reordered `Positions`).
3. `AssignSoldiersToMission` computes `IsFilled` before `SaveChanges`, so the stored flag is stale. The new endpoints compute it live, and the P3/P4 writes store it correctly for every instance they touch (the seeded full instances still had `IsFilled = false`).
4. `Login` reveals whether a username exists ("User not found." vs "Invalid username or password.").
5. `UsersCache.ReloadCache` uses `.Single()` per username inside a timer callback, so a duplicate username crashes the process. New duplicates are now prevented by fix 3 above.
6. Swagger is enabled in every environment, although the README says Development only. `appsettings.json` contains the AES key and IV (as already noted in the plan).
7. The UI has no logout action. New accounts are `Activated=false`, so the UI guards block them until an admin activates them, which `UpdateUser` now supports.
8. `MissionService.OverlappingTimes` is inclusive, so touching intervals count as overlapping. `AutoAssignService` and the new evaluator are strict.
9. `PositionHelper` leaves `Hamal` out of the simple positions, while the docs list it as Simple. The evaluator follows the code.
10. The legacy `Mission/UnassignSoldier` (`MissionService.UnassignSoldiersToMission`, not used by the UI) always fails with "Soldier Mission not found": it never loads `instance.Soldiers`.
11. `Translators.Extensions.MissionExtension.Decrypt` tests `FromTime` twice, so `ToTime` stays encrypted when `FromTime` is empty.
12. `MissionService.UpdateMission` (the UI edit dialog) recreates every instance and ignores position changes, although its XML comment says it preserves assignments "where possible".
13. The legacy `UpdateMission`/`DeleteMission` leave pending auto-assign candidate rows that point at deleted instances, which made `AcceptAssignCandidate` fail for that candidate. Only the new `DeleteMissionInstance` cleans them up. **Since P10 acceptance skips such rows** (deviation 35); the rows themselves still remain until the next acceptance.
14. The legacy write endpoints don't take the new write lock, so a UI edit and an agent write can still interleave.
15. `MetadataService.GetAssignmentsBreakdownPerSoldiers` sets `Missions = []` on the **cached** soldiers while building its response (`hours.Soldier.Missions = []`), so `GetSoldiers` returns soldiers without missions until the next cache reload. The `/justice` page triggers it; the agent avoids that endpoint.
16. The metadata endpoints return the full cached `Soldier` per row, including phone, personal number, vacations and every assignment. That is heavy, and admin-only, but the agent strips it. A compact variant (ID, name, platoon, totals) would cut the payload.
17. `Translators.Models.Vacation` has no `SoldierId`, so `GetVacations` without a soldier filter can't say whose vacation each row is. Adding it to the DTO and `VacationTranslator.ToBL` would be backward compatible.
18. `Soldiers/GetSummary` is all-time (not date filtered) and sums raw durations, while the metadata hours count a mission's `ActualHours`. The two disagree for missions like the seed's standby (12 h shifts counted as 4 h).
19. `Users.SoldierId` and `SoldierMissionCandidate.SoldierId` have no FK (`ShabzakDB` configures no relationship), so `DeleteSoldier` left the soldier's login account and pending auto-assign candidate rows behind. **The account part is fixed** (deviation 31: the account is now deleted with the soldier). The candidate rows still remain; since P10 `AcceptAssignCandidate` skips them (like issue 13).
20. `AddSoldier` / `UpdateSoldier` don't validate the fields: empty values are stored (encrypted as "N/A"), and a duplicate personal number is accepted even though it would become a duplicate username for `CreateUsersForSoldiers`. The agent validates both.
21. `Soldiers/DeleteSoldier` answers 500 for an unknown soldier ID (its `ArgumentException` isn't mapped to 404). The agent checks the soldier first.
22. ✅ **Fixed in P10 (deviation 35):** accepting an auto-assign proposal duplicated the existing assignments of the shifts the run had skipped; `AutoAssign` during an interactive session answered 500. Production data may already hold such duplicates (action item 13).
23. `Mission/DeleteMission` and `Mission/GetCandidate` answer 500 for an unknown ID (not mapped to 404). The agent checks `GetMissionsSummary` / `GetAllCandidates` first.
24. Auto-assign doesn't look at the soldiers' `Active` flag: an inactive soldier in the pool (or every soldier when no pool is given) can be scheduled. The agent leaves inactive soldiers out of the pool it sends; the UI's dialog doesn't.
25. Auto-assign fills only shifts with **no** soldier: a partly staffed shift is skipped as "already assigned" and stays short. Both the agent's coverage tool and its card say so; completing them takes `assign_soldier` (or the UI's manual assignment).
26. Auto-assign fills a shift up to the mission's required soldiers + commanders and counts it valid when it has exactly that many, while the schedule's fill (`isFilled`, the UI) counts the position slots. When the two differ, the views disagree: with more slots than required, auto-assign leaves seats empty and calls the shift valid; with fewer, a fully staffed shift counts as faulty. The agent's mission cards warn about such a mismatch.
27. ✅ **Fixed in P12 (your decision):** the UI had no logout, and the NgRx store kept the previous user's data (e.g. soldiers with contact details) when another user logged in on the same tab. Logout now clears both (deviation 43). A new login without logging out first (e.g. after the token expired) still keeps the old store data until the pages reload it.
28. The assignments-management "הצג הצעות שיבוץ" dialog lists only the proposals of this browser session's own auto-assign run (`getAllCandidates` fetches IDs, which no component uses), so it is empty after a reload and never shows the agent's proposals (action item 18).
29. 34 of the UI's 49 pre-existing spec files are generated stubs that fail (they create components without the store or dialog providers, and `app.component.spec.ts` still expects a title "Shabzak" and an `h1`). `npm test` therefore fails as a whole; the chat specs run green with `--include`.
30. `environment.development.ts` (used by `ng serve`) still holds the old AES `key`, although the docs said the key was removed from the environments; nothing reads it (the docs now say so).
31. ✅ **Fixed in P13 (your decision, deviation 49):** the UI's `nginx.conf` served only the static files, so the production `serverURL` `/api` (and `agentURL` `/agent/api`) reached nothing. It now proxies both, and serves the prerendered routes without the 301 it used to send.
32. `docker-compose.yml` still holds the SQL container's `sa` password in plain text (`MSSQL_SA_PASSWORD`), and the workspace root that holds it is not version-controlled (action item 19).
33. `docker-compose.yml` starts with `version: '3'`, which Compose v2 ignores with a warning ("the attribute `version` is obsolete"). It is harmless, and left in place because the legacy `docker-compose` v1 still needs it.

## Notes for future work (all 14 phases are done)
- **Agent status:** the service, the typed client, auth, 39 tools (17 read, 22 write), the prompt with the P7–P10 workflows, the chat API, sessions and approvals are in place (`ShabzakAgent/AI_CONTEXT.md`). The agent and the `RunContext` are created **per turn** (the routes do it). LLM failures go through `describeLlmError` as `upstream_error`.
- **Adding a write tool:** `defineTool({ kind: 'write', describe, run, refresh, … })` and add it to `TOOL_SPECS`. Everything else is automatic: the approval gate, the card, `NOT RUN` on a describer `ToolError`, the audit line and the refresh hints. Share one `prepare()` between `describe` and `run` so the run re-validates. Add a recipe to `WRITE_WORKFLOWS` in the prompt (it appears only when the tool is registered). For the live suite, reuse `agent-http.ps1` and `fake-openai.mjs` (`CALL` directives; `&&` batches calls into one approval, `THEN` chains steps); `verify-p9.ps1` / `verify-p10.ps1` show how to read a tool's result from the stub's `done:` answer.
- **The UI widget (P11–P12, done):** `Shabzak/AI_CONTEXT.md` §2.7. It needs the agent's `CORS_ORIGINS` to list the UI origin, and reads `Retry-After` through the exposed header. An agent restart costs the open conversations a transparent new thread (with a note) and expires their pending cards. Changing the SSE contract (§4.6) means updating `services/chat/chat-protocol.ts` and its spec.
- **P13 (integration, done):** the compose stack, its root `.env` and the UI container's gateway are described in `UNIFIED_AI_CONTEXT.md` §7. Before changing `nginx.conf`, the compose file or the images, run `verify-p13.ps1`: it exercises the real `nginx.conf` and re-runs the chat suites through it. `gateway.ps1` expects the Docker-specific lines of `nginx.conf` (`listen 80;`, `include /etc/nginx/mime.types;`, `root /usr/share/nginx/html;`, the two `set $… http://shabzak-…;` lines) exactly once each, so keep them, or update its replacement table with them. Open items: the first Docker run (action item 9) and the real-LLM checklist (action item 6).
- **Future enhancements** (plan §8): a persistent checkpointer (conversations survive restarts), auto-assign as a background job, pseudonymized names, and the audit trail outside the container log (action item 19).
- **Blocked writes:** `ApiError.code === 'blocked'` (409 + validations, nothing written) is turned into a ToolError with the Hebrew reasons (`explainBlocked`); the tools never retry with `allowViolations` unless the admin explicitly asks.
