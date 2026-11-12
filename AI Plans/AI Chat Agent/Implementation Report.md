# AI Chat Agent: implementation report, Phases 0–4

**Date:** 2026-11-12 · **Plan:** `Plan.md` (this folder), mirrored to `<workspace>/CHAT_AGENT_PLAN.md`; the progress tracker and deviations log are updated in both.

| Phase | Status | Result |
|---|---|---|
| P0 Preparation and baseline | ✅ Done | Both repos are on `feature/ai-chat`, and both builds are green. JWT login was verified end to end on the API. The docs are corrected. |
| P1 Identity, role policies, account management | ✅ Done | Role policies, self-scope checks, PII redaction, and account management are in place. The UI hides edit controls from Regular users. 58/58 live checks pass. |
| P2 Query endpoints and constraint evaluator | ✅ Done | Four compact query endpoints, the pure constraint evaluator, and the xUnit project are in place. 58/58 unit tests and 53/53 live checks pass. |
| P3 Schedule mutation endpoints | ✅ Done | `Schedule/AssignSoldier`, `UnassignSoldier`, `ReplaceSoldier` (with swap) and `MoveSoldier`: validated through the evaluator, blocked on violations unless overridden, one `SaveChanges`, correct `IsFilled`. 63/63 live checks pass. |
| P4 Mission and instance mutation endpoints | ✅ Done | `CreateMission`, `UpdateMissionDetails` (never touches instances), `GenerateMissionInstances` (UI-equivalent, with dry run), `AddMissionInstances`, `UpdateMissionInstance`, `DeleteMissionInstance`, `Soldiers/SetSoldierActive`. 146/146 unit tests and 92/92 live checks pass. |

> **Committed, not pushed.** At your request, all P0–P4 work is one commit per repo, made on `feature/ai-chat` and fast-forwarded into `opus-refactor` (both branches point at it). The `ShabzakAPI` commit includes `AI Plans/`; `.github/` stays untracked. P3 and P4 changed only `ShabzakAPI` code; the `Shabzak` repo got a docs-only change (`AI_CONTEXT.md`).

---

## ⚠ Action items for you

1. **Rotate the Azure SQL password now.** The password literal is in **pushed** git history of `github.com/ErezKon/ShabzakAPI`: commit `52dd597` (on `origin/main` and `origin/opus-refactor`) and commit `ab09ca8` (on `origin/opus-refactor`), both in `DataLayer/ShabzakDB.cs`. Removing it from the working tree doesn't revoke it. After rotating:
   - Update the local secret: `dotnet user-secrets set "ConnectionStrings:ShabzakDB" "<new>" --project 04.ShabzakAPI`.
   - Optionally scrub history with `git filter-repo` or BFG. That rewrites history, so it needs your explicit decision.
2. **Review and push** the P0–P4 commit on `opus-refactor` in both repos when you're ready. Nothing is pushed.
3. **Run a smoke test with real accounts.** The production DB was unreachable from this network (TCP 1433 connects, but the TDS pre-login handshake times out, which looks like a corporate firewall). On a network that can reach Azure SQL:
   - Log in as an Admin and a Regular user (UI `npm start`, API `dotnet run --launch-profile https`).
   - Check that `/assignments` is read-only for the Regular user, and that the Admin's "הסר חייל" now works (it always failed before P3, see deviation 9).
   - Check that the personal page works (summary, vacations, password change).
   - Missions page (P4 verify step): create, edit and delete a mission in the UI. The API side of these legacy calls passed on the dev DB, but no browser test was possible here.
4. **Check the DB compatibility level.** EF Core 8 turns `list.Contains()` into `OPENJSON`, which needs compatibility level ≥ 130. Azure SQL defaults to 150+. Check with `SELECT compatibility_level FROM sys.databases WHERE name = DB_NAME()`.
5. **Stash `stash@{0}`** on `opus-refactor` (`pre-ai-chat local dev changes …`) holds your three uncommitted files. Its LoadCSV changes write **decrypted soldier PII** to `DataLayer/soldiers2.txt`. Decide whether to keep or drop it; I did not drop it.
6. **LLM endpoint credentials** aren't needed until P5/P6. When they are, put them only in `ShabzakAgent/.env` (gitignored).
7. **Confirm two P4 semantics** (both implemented as the plan says; easy to change):
   - `UpdateMissionInstance` blocks only on `OVERLAP`. A move into an assigned soldier's **vacation** or below the **rest** minimum is reported in `validations` but not blocked. If vacations should block too, it's a one-line change (`BlockingPolicy.Reschedule`).
   - In `GenerateMissionInstances`, a date-only `to` means "that date at the mission's `ToTime`", as in the UI dialog. For a round-the-clock mission (`00:00`→`00:00`), "to 2026-11-21" therefore stops at the start of the 21st. The dry run shows the resolved range and the last slot, so the P9 approval card will make this visible.

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

---

## Verification environment (reusable for later phases)
The scripts are in `verification/` in this folder. They contain no secrets: `jwt.key` is generated per machine and gitignored, and all data is synthetic.

| Script | Purpose |
|---|---|
| `dev.ps1` | `New-DevJwt -UserId -Role -SoldierId` (HS256, 30 min) and `Invoke-Api` (dates kept as strings) |
| `start-api-localdb.ps1` | Runs the API with process-scoped `ConnectionStrings__ShabzakDB=(localdb)\MSSQLLocalDB;Database=ShabzakAgentDev` and `Jwt__Key` (`--no-build`: build first) |
| `seed-localdb.ps1` | Seeds through the API: 16 soldiers, 4 missions / 64 instances (guard 4h 24/7, patrol, standby, special), assignments, vacations (Pending, Approved, Denied), 5 accounts |
| `verify-p1.ps1`, `verify-p2.ps1` | The P1 and P2 acceptance suites |
| `verify-p3.ps1`, `verify-p4.ps1` | The P3 and P4 acceptance suites. They revert every change they make (P4 uses `sqlcmd` to add and remove a synthetic pending-candidate row), so all four suites can be re-run in any order. |

- **Bootstrap:** after seeding, run `sqlcmd -S "(localdb)\MSSQLLocalDB" -d ShabzakAgentDev -E -Q "UPDATE Users SET Role=2, Activated=1 WHERE Id=1"` to create the first SuperAdmin. Because of revalidation, a SuperAdmin can't be created through the API from nothing.
- The seed's instance times are relative to the seed day, and `verify-p2/p3` look for "today"/"tomorrow" slots. On a later day, drop the DB and re-seed.
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
13. The legacy `UpdateMission`/`DeleteMission` leave pending auto-assign candidate rows that point at deleted instances, which makes `AcceptAssignCandidate` fail for that candidate. Only the new `DeleteMissionInstance` cleans them up.
14. The legacy write endpoints don't take the new write lock, so a UI edit and an agent write can still interleave.

## Notes for the next phases
- **P5 (API client):** a blocked write is HTTP 409 with `GeneralResponse { success:false, errorMessage, value }`. Keep `value` (the validations) in the `ApiError` so tools can explain why; data conflicts are 409 without `value`.
- **P5:** `User/Me` returns 401 for deleted or disabled accounts, thanks to revalidation. The agent middleware still has to reject `activated=false` with 403.
- **P8:** change `Active` only through `Soldiers/SetSoldierActive`; `UpdateSoldier` ignores it.
- **P9:** the describers can use `GenerateMissionInstances` `dryRun` (count, `skippedExisting`, resolved `from`/`to`, `preview[0]`, `lastSlot`) and `UpdateMissionInstance` `dryRun` (validations at the new times). For `delete_mission_instance`, list the soldiers with `Schedule/GetAssignments(missionIds)`. Never call `UpdateMission`.
- **P10:** to show the same validation the write will run, call `ValidateAssignments` with the planner's options: replace → `ignoreSoldierId = old`; swap → each side with the other instance in `excludeInstanceIds`; move → `excludeInstanceIds = [from]`. Use `allowViolations` only on an explicit admin request; it never overrides `ALREADY_ASSIGNED`.
