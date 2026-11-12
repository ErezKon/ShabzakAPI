# ShabzakAPI — Backend AI Context

> **AI Instruction:** Every time you make a change to any file in the ShabzakAPI project, you **MUST** update this document to reflect that change — including new files, renamed files, new methods, changed architectures, new models, deleted items, etc. This file must always be accurate and up-to-date so future AI sessions can rely on it without re-analyzing the codebase. Also apply sthe same updates to the unified md document at the root folder UNIFIED_AI_CONTEXT.md

> **Active initiative — AI Chat Assistant:** see `CHAT_AGENT_PLAN.md` in the workspace root (next to `UNIFIED_AI_CONTEXT.md`) for the phased plan, shared API contracts (§4) and progress tracker. Work happens on branch `feature/ai-chat` (from `opus-refactor`); P0–P4 are committed and merged into `opus-refactor` (both branches point at the same commit). JWT auth exists only on `opus-refactor` / `feature/ai-chat`; `main` is pre-JWT.

---

## 1. Project Overview

**Shabzak (שבצ"ק)** is a military duty-scheduling (assignment/rotation) system for an IDF (Israel Defense Forces) battalion (unit 9213). It manages soldiers, missions (guard duties, patrols, etc.), and the assignment of soldiers to mission time-slots ("instances"). The system supports both manual and automatic assignment with fairness scoring.

The monorepo contains three components orchestrated by `docker-compose.yml`:

| Component | Tech | Container Port | Host Port |
|-----------|------|---------------|-----------|
| **Shabzak** (UI) | Angular 17, SSR, Material, NgRx | 80 (nginx) | 5230 |
| **ShabzakAPI** (Backend) | ASP.NET Core 8 Web API | 8080 | 5231 |
| **ShabzakSQL** (DB) | SQL Server (Azure SQL in prod) | 1433 | 1433 |

**Language/locale:** Hebrew (RTL UI). The app title is שבצ"ק (short for שיבוץ צבאי קל — "easy military assignment").

---

## 2. ShabzakAPI — Backend (.NET)

### 2.1 Solution Structure

```
ShabzakAPI/
├── ShabzakAPI.sln
├── Dockerfile
├── ShabzakAPI.Tests/      # xUnit (net8.0) tests for pure BL logic — run: dotnet test ShabzakAPI.sln
│   ├── AssignmentConstraintEvaluatorTests.cs
│   ├── AssignmentSlotSelectorTests.cs
│   ├── ScheduleChangePlannerTests.cs
│   ├── ScheduleInstanceMapperTests.cs
│   ├── MissionInstanceGeneratorTests.cs
│   ├── MissionSlotPlannerTests.cs
│   ├── ScheduleDateFormatTests.cs
│   └── UserAccountGuardTests.cs
├── 04.ShabzakAPI/        # ASP.NET Core Web API (startup, controllers, view models)
│   ├── Program.cs         # Startup: config/secrets, JWT auth, DI, CORS, Swagger, caches, services, /health
│   ├── Auth/
│   │   ├── JwtHelper.cs                  # Issues signed HS256 JWTs (sub, name, role, enabled, activated, soldierId)
│   │   ├── Policies.cs                   # Policy names AdminOrAbove / SuperAdminOnly + registration (RequireRole)
│   │   ├── ClaimsPrincipalExtensions.cs  # GetUserId, GetSoldierId, GetHashedUsername, IsAdminOrAbove, IsSuperAdmin, GetRole
│   │   └── TokenRevalidation.cs          # JwtBearer OnTokenValidated: rejects deleted/disabled accounts, refreshes role claim
│   ├── Controllers/
│   │   ├── MissionController.cs
│   │   ├── SoldiersController.cs
│   │   ├── UserController.cs
│   │   ├── MetadataController.cs
│   │   ├── ScheduleController.cs  # Compact schedule queries, assignment validation and assignment writes (agent-facing)
│   │   └── WriteResponseExtensions.cs # ExecuteWrite: BL write result / exceptions → GeneralResponse + 200/400/404/409, ID-only log line
│   └── ViewModels/        # Request/response DTOs for the API layer (incl. UpdateUserRequest, GetScheduleAssignmentsRequest, ValidateAssignmentsRequest, ScheduleWriteRequests.cs, MissionEditRequests.cs)
├── BL/                    # Business Logic layer
│   ├── Services/
│   │   ├── AutoAssignService.cs   (~1700 lines — the core scheduling algorithm)
│   │   ├── MissionService.cs      (~620 lines)
│   │   ├── SoldierService.cs
│   │   ├── UserService.cs
│   │   ├── MetadataService.cs
│   │   ├── MissionInstanceService.cs
│   │   ├── MissionPositionService.cs
│   │   ├── SoldierMissionService.cs
│   │   ├── UserAccountGuard.cs    (pure account-management guard rules + UserAccountException)
│   │   ├── ScheduleService.cs     (singleton: GetAssignments, ValidateAssignments, GetMissionsSummary; writes AssignSoldier, UnassignSoldier, ReplaceSoldier (+swap), MoveSoldier)
│   │   ├── MissionEditService.cs  (singleton: CreateMission, UpdateMissionDetails, Generate/Add/Update/DeleteMissionInstance — never recreates instances)
│   │   ├── ScheduleInstanceMapper.cs (pure: DB instance → ScheduleInstanceDto)
│   │   ├── AssignmentConstraintEvaluator.cs (pure: assignment rules → AssignmentValidationDto)
│   │   ├── AssignmentSlotSelector.cs (pure: slot ranking + target slot, shared by the evaluator and the writes)
│   │   ├── ScheduleChangePlanner.cs (pure: slot + evaluator options per write, BlockingPolicy / BlockReason)
│   │   ├── MissionInstanceGenerator.cs (pure: back-to-back instance generation, range resolution, limits)
│   │   ├── MissionSlotPlanner.cs  (pure: position-slot upsert plan)
│   │   └── PositionHelper.cs      (position similarity map)
│   ├── Cache/
│   │   ├── SoldiersCache.cs       (singleton, auto-reload every 5 min)
│   │   ├── MissionsCache.cs       (singleton, auto-reload every 5 min)
│   │   └── UsersCache.cs          (singleton)
│   ├── Extensions/                (Encrypt/Decrypt, ToBL/ToDB, position helpers)
│   │   ├── SoldierExtension.cs
│   │   ├── MissionExtension.cs
│   │   ├── MissionInstanceExtension.cs
│   │   ├── MissionPositionExtension.cs
│   │   ├── SoldierMissionExtension.cs
│   │   ├── PositionExtension.cs
│   │   ├── UserExtension.cs
│   │   ├── DateTimeExtension.cs
│   │   └── EnumerableExtension.cs
│   ├── Models/                    (BL-level models: validation, scoring, summaries)
│   │   ├── Users/                 (MeDto, UserAccountDto — whitelisted account DTOs)
│   │   └── Schedule/              (ScheduleInstanceDto, AssignmentValidationDto, MissionSummaryDto, SoldierLiteDto/SoldierDetailsDto, ScheduleDateFormat, codes; ScheduleWriteDtos, MissionEditDtos)
│   └── Logging/
├── Translators/            # Mapping layer between DataLayer ↔ BL models
│   ├── Models/             (BL/API-facing DTOs: Soldier, Mission, etc.)
│   ├── Translators/        (ToBL / ToDB mappers for each entity)
│   ├── Encryption/
│   │   ├── AESEncryptor.cs (AES-256 — encrypts PII at rest; random IV per value, "v2:" prefix)
│   │   └── Sha512Encryptor.cs (SHA-512 hex + PBKDF2-HMAC-SHA512 password hashing/verification)
│   └── Extensions/         (Encrypt/Decrypt for DataLayer models)
└── DataLayer/              # EF Core data access
    ├── ShabzakDB.cs        (DbContext — SQL Server, connection string, Fluent API config)
    ├── LocalDB.cs          (DbContext — SQLite, inherits ShabzakDB, for local/offline dev)
    ├── DbFactory.cs        (Static factory: DbFactory.Create() returns ShabzakDB or LocalDB)
    ├── RemoteDB.cs         (secondary DB context — currently unused/commented out)
    ├── Models/             (EF entity classes)
    │   ├── Soldier.cs
    │   ├── Mission.cs
    │   ├── MissionInstance.cs
    │   ├── MissionPositions.cs
    │   ├── SoldierMission.cs
    │   ├── SoldierMissionCandidate.cs
    │   ├── Position.cs           (enum)
    │   ├── User.cs
    │   ├── UserRole.cs           (enum)
    │   ├── UserToken.cs          (currently unused)
    │   ├── Vacation.cs
    │   ├── VacationRequestStatus.cs (enum)
    │   ├── AutoAssignmentsMeta.cs
    │   └── InteractiveAutoAssignLog.cs
    └── soldiers.txt        (seed data file)
```

### 2.2 Layered Architecture

```
Controllers  →  BL Services  →  DataLayer (EF Core DbContext)
     ↕               ↕                ↕
  ViewModels    Translators/Models   DataLayer/Models
                 (Encrypt/Decrypt)
```

- **DataLayer** — EF Core entities stored encrypted (PII fields). Connection to Azure SQL (or SQLite via `DbFactory.UseLocal`).
- **Translators** — Bidirectional mappers (`ToBL` / `ToDB`). Also houses `AESEncryptor` and `Sha512Encryptor`.
- **BL** — Business logic services, caching (singleton pattern with periodic reload), scoring algorithms.
- **04.ShabzakAPI** — ASP.NET Core controllers, request/response view models, `Program.cs` startup.

### 2.3 Database Schema (EF Core Entities)

#### Core Entities

| Entity | Key Fields | Relationships |
|--------|-----------|---------------|
| `Soldier` | Id, Name*, PersonalNumber*, Phone*, Platoon*, Company*, Position (CSV of enum ints), Active | Has many `SoldierMission`, `Vacation` |
| `Mission` | Id, Name*, Description, SoldiersRequired, CommandersRequired, Duration, FromTime, ToTime, IsSpecial, ActualHours, RequiredRestAfter | Has many `MissionInstance`, `MissionPositions` |
| `MissionInstance` | Id, MissionId, FromTime, ToTime, IsFilled | Belongs to Mission; has many `SoldierMission` |
| `MissionPositions` | Id, MissionId, Position (enum), Count | Belongs to Mission; has many `SoldierMission` |
| `SoldierMission` | Id, SoldierId, MissionInstanceId, MissionPositionId | Junction: Soldier ↔ MissionInstance ↔ MissionPosition |
| `SoldierMissionCandidate` | Id, SoldierId, MissionInstanceId, MissionPositionId, CandidateId (GUID) | Temporary auto-assign candidates before acceptance |
| `Vacation` | Id, SoldierId, From, To, Approved (enum: Pending/Approved/Denied) | Belongs to Soldier |
| `User` | Id, Name, Password, Salt, Role (enum), Activated, Enabled, SoldierId? | Linked to Soldier (optional) |
| `InteractiveAutoAssignLog` | Id, SessionId, MissionInstanceId, Action, PicksJson, Timestamp | Audit log for interactive auto-assign |

> \* = stored AES-encrypted at rest

#### Position Enum (numeric, stored as CSV string in Soldier.Position)

| Value | Name | Category |
|-------|------|----------|
| 0 | Simple | Simple |
| 1 | Marksman | Simple |
| 2 | GrenadeLauncher | Simple |
| 3 | Medic | Simple |
| 4 | Negev | Simple |
| 5 | Hamal | Simple |
| 6 | Sniper | Simple |
| 7 | Translator | Simple |
| 8 | ShootingInstructor | Simple |
| 9 | KravMagaInstructor | Simple |
| 10 | DroneOperator | Simple |
| 11 | PlatoonCommanderComms | — |
| 12 | CompanyCommanderComms | — |
| 13 | ClassCommander | Commanding |
| 14 | Sergant | Commanding |
| 15 | PlatoonCommander | Commanding + Officer |
| 16 | CompanyDeputy | Commanding + Officer |
| 17 | CompanyCommander | Commanding + Officer |

#### Position Similarity Map (`PositionHelper`)
- `Simple` → all simple positions
- `Marksman` → Marksman, Sniper
- `ClassCommander` → ClassCommander, Sergant, PlatoonCommander
- `Sergant` → Sergant, PlatoonCommander
- `CompanyDeputy` → CompanyDeputy, CompanyCommander
- Other positions map to themselves only

### 2.4 Encryption Strategy

- **At-Rest Encryption:** Soldier PII (Name, Phone, PersonalNumber, Platoon, Company, and the Position CSV) and Mission Name/Description/FromTime/ToTime are AES-256 encrypted before DB writes and decrypted on reads. Encrypted columns cannot be filtered in SQL.
  - `AESEncryptor` — Key/IV come from config (`Encryption:AesKey`, `Encryption:AesIV`) via `AESEncryptor.Configure()` in `Program.cs`. New values use a random IV prepended to the ciphertext (`"v2:"` prefix); values without the prefix are decrypted with the legacy static IV. Encrypt/decrypt results are memoized in static dictionaries (so the same plaintext maps to the same ciphertext within a process).
  - Extension methods: `soldier.Encrypt()` / `soldier.Decrypt()` and same for missions. Blank values are stored as encrypted `"N/A"`.
- **Password Hashing (PBKDF2):** `Sha512Encryptor.HashWithPbkdf2(pwd)` → `pbkdf2:{iterations}:{saltB64}:{hashB64}` (PBKDF2-HMAC-SHA512, 100k iterations, 16-byte salt, 64-byte hash). `Sha512Encryptor.Verify(pwd, storedHash, legacySalt)` auto-detects the format (PBKDF2 or legacy `SHA-512(pwd + salt)`) and compares in constant time. `UserService.Login` transparently re-hashes legacy hashes to PBKDF2 after a successful login (`NeedsUpgrade`). The `User.Salt` column is only used by legacy hashes.
- **Login credentials:** the client sends Username = SHA-512(personalNumber) and Password = SHA-512(phone) (hex, computed in the browser). The server stores `PBKDF2(SHA-512(phone digits))`; `User.Name` holds the SHA-512 hashed username (never expose it).

### 2.4.1 Authentication, Authorization & Configuration (JWT)

- **JWT Bearer:** `Program.cs` registers `AddAuthentication().AddJwtBearer(...)` (validates issuer, audience, lifetime, HS256 signing key; 1-minute clock skew). `Auth/JwtHelper.GenerateToken(user)` issues tokens with claims `sub` (userId), `jti`, `ClaimTypes.Name` (hashed username), `ClaimTypes.Role` (`Regular`/`Admin`/`SuperAdmin`), `enabled`, `activated`, and optional `soldierId`. Default expiry 480 min (`Jwt:ExpiryMinutes`). Inbound claim mapping is on, so `sub` is read as `ClaimTypes.NameIdentifier`.
- **Endpoints:** every controller has `[Authorize]`; `User/Login` is the only `[AllowAnonymous]` endpoint and returns `GeneralResponse<LoginResponse>` = `{ user, token }` (password/salt blanked). `GET /health` (minimal API, anonymous) returns `"healthy"`.
- **Role policies** (`Auth/Policies.cs`, registered with `AddAuthorization(Policies.Configure)`): `AdminOrAbove` = `RequireRole("Admin","SuperAdmin")`, `SuperAdminOnly` = `RequireRole("SuperAdmin")`. Admin endpoints carry `[Authorize(Policy = Policies.AdminOrAbove)]` explicitly per action. Finer rules (self-scope, SuperAdmin-only account changes) are checked inside the actions/services.
- **Token revalidation** (`Auth/TokenRevalidation.cs`, JwtBearer `OnTokenValidated`): every authenticated request looks the `sub` user up in `UsersCache` (DB fallback). Deleted or disabled (`Enabled=false`) accounts get **401** immediately, and the role claim is **replaced with the account's current role**, so role changes and disabling take effect without waiting for token expiry. `Activated` is not checked by the API (the UI guards and the chat agent check it).
- **Claims helpers** (`Auth/ClaimsPrincipalExtensions.cs`): `User.GetUserId()` (NameIdentifier/`sub`), `GetSoldierId()`, `GetHashedUsername()`, `IsAdminOrAbove()`, `IsSuperAdmin()`, `GetRole()`.
- **Swagger:** `AddSwaggerGen` defines an HTTP `bearer` security scheme, so the Swagger UI "Authorize" button accepts the JWT from `/api/User/Login`.
- **Configuration keys** (appsettings.json → user-secrets in Development → environment variables, later wins):

| Key | Purpose | Notes |
|-----|---------|-------|
| `ConnectionStrings:ShabzakDB` | SQL Server connection string | Required. appsettings.json holds a placeholder; local dev uses `dotnet user-secrets` (the csproj has a `UserSecretsId`), containers use `ConnectionStrings__ShabzakDB`. **Never hardcode it in `ShabzakDB.cs`.** |
| `Jwt:Key` | HS256 signing key | If empty or starting with `CHANGE_ME`/`AUTO_GENERATED`, a random key is generated at startup (tokens die on restart). Set `Jwt__Key` for persistence. The Docker `entrypoint.sh` also generates one if missing. |
| `Jwt:Issuer` / `Jwt:Audience` | Token issuer / audience | Defaults `ShabzakAPI` / `ShabzakApp`. |
| `Jwt:ExpiryMinutes` | Token lifetime | Default 480. |
| `Encryption:AesKey` / `Encryption:AesIV` | AES key + legacy IV (Base64) | Required (currently committed in appsettings.json — pre-existing, rotate when possible). |
| `Cors:AllowedOrigins` | Comma-separated origins or `*` | Default `*`. |

- **Swagger** is currently enabled in every environment (the Development-only gate in `Program.cs` is commented out). `UseHttpsRedirection` runs in Development only.
- **Local dev:** `dotnet run --launch-profile https` (https://localhost:7170, http://localhost:5165). SQL Server LocalDB (`(localdb)\MSSQLLocalDB`) works for isolated testing: set `ConnectionStrings__ShabzakDB` for the process and `EnsureCreated()` builds the schema.

### 2.4.2 Authorization Matrix

Roles: `Regular=0`, `Admin=1`, `SuperAdmin=2` (JWT `ClaimTypes.Role` = enum name). "Admin+" = policy `AdminOrAbove`.

| Access | Endpoints |
|--------|-----------|
| **Anonymous** | `User/Login`, `GET /health` |
| **Any authenticated user** | `User/Me`; `Mission/GetMissions`, `GetMissionInstances`, `GetMissionInstancesInRange`, `GetMissionsSummary`; `Schedule/GetAssignments`; `Soldiers/GetSoldiersLite`; `Soldiers/GetSoldiers` (non-admins get **copies** with `phone`/`personalNumber` blanked and vacations only on their own soldier) |
| **Self, or Admin+** | `Soldiers/GetSoldierById` (403 for another soldier), `Soldiers/GetSummary` (403 for another soldier), `Soldiers/RequestVacation` (403 for another soldier), `Soldiers/GetVacations` (non-admins: soldier filter forced to their own soldier; empty list if none), `User/ResetPassword` (non-admins: own account only, 403 otherwise — also for unknown usernames) |
| **Admin+** | Everything else: soldier CRUD (`AddSoldier`, `UpdateSoldier`, `SetSoldierActive`, `DeleteSoldier`, `ReloadCache`, `LoadCSV`), `RespondToVacationRequest`, all mission/instance CRUD (incl. `CreateMission`, `UpdateMissionDetails`, `GenerateMissionInstances`, `AddMissionInstances`, `UpdateMissionInstance`, `DeleteMissionInstance`), all assignment edits (`AssignSoldiers`, `UnassignSoldier`, `AssignSoldiersToMissionInstance`, `RemoveSoldierFromMissionInstance`, `ReplaceSoldierInMissionInstance`, `Schedule/AssignSoldier`, `Schedule/UnassignSoldier`, `Schedule/ReplaceSoldier`, `Schedule/MoveSoldier`), `GetAvailableSoldiers`, `GetReplacementCandidates`, `Schedule/ValidateAssignments`, auto-assign (batch + interactive), candidates (`GetAllCandidates`, `GetCandidate`, `AcceptAssignCandidate`), all `Metadata/*`, `User/CreateUsersForSoldiers`, `User/GetUsers`, `User/UpdateUser`, `User/DeleteUser`, `User/ResetPasswordToDefault` |
| **SuperAdmin only (in-method)** | Setting a user's `role`; modifying, resetting or deleting a SuperAdmin account (also via `ResetPassword`) |

Account guard rules (`BL/Services/UserAccountGuard.cs`, pure): no self role change, self disable/deactivate or self delete (400); an Admin cannot touch SuperAdmin accounts or set roles (403); the last enabled SuperAdmin cannot be demoted, disabled, deactivated or deleted (409); unknown user (404). Failures are returned as `GeneralResponse { success:false, errorMessage }` with that HTTP status.

### 2.5 Database Factory Pattern

All database context instantiation goes through `DataLayer.DbFactory.Create()` instead of `new ShabzakDB()`. This enables runtime switching between SQL Server and SQLite:

- **`DbFactory.UseLocal`** — Static bool (default `false`). Set to `true` to use SQLite (`LocalDB`).
- **`DbFactory.Create()`** — Returns `new LocalDB()` if `UseLocal` is true, otherwise `new ShabzakDB()`.
- **`LocalDB`** — Inherits `ShabzakDB`, overrides `OnConfiguring` to use SQLite (`shabzak_local.db`).

### 2.6 Caching System

Three singleton caches, each auto-reloading from DB every 5 minutes:
- **`SoldiersCache`** — Holds both `DataLayer.Models.Soldier` and `Translators.Models.Soldier` dictionaries. Decrypts on load.
- **`MissionsCache`** — Holds missions with positions and instances. Decrypts on load.
- **`UsersCache`** — Holds DB user entities keyed by hashed username plus a by-ID index, guarded by one lock. `GetUser(username)` / `GetUserById(id)` fall back to the DB; `GetAllUsers()` returns a snapshot; `AddOrReplaceUser(user)` (replace-on-update; `AddUser` delegates to it) and `RemoveUser(id)` keep it in sync after account changes.

Cache invalidation: Services call `SoldiersCache.ReloadAsync()` or `MissionsCache.ReloadAsync()` after mutations; `UserService` updates `UsersCache` directly. The agent-facing schedule/instance writes reload both (`ScheduleService.ReloadCaches`, in the background); `SoldierService.SetActive` reloads `SoldiersCache` synchronously because validation reads `Active` from it. Never mutate cached objects when shaping responses — return copies (e.g. `GetSoldiers` redaction).

### 2.7 API Endpoints

Auth column: **Any** = any authenticated user, **Self** = own soldier/account unless Admin+, **Admin+** = policy `AdminOrAbove` (see §2.4.2).

#### MissionController (`api/Mission/`)

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | `GetMissions` | Any | Get all missions with instances and positions (very large payload) |
| GET | `GetMissionsSummary` | Any | `MissionSummaryDto[]`: definition (`id, name, description, isSpecial, requiredInstances, duration, fromTime, toTime`), staffing (`soldiersRequired, commandersRequired, actualHours, requiredRestAfter, positions[{id, position, count}]`) and counts (`instanceCount, futureInstanceCount, firstInstanceFrom, lastInstanceTo, assignmentCount`) — grouped SQL, no instance graph |
| POST | `AddMission` | Admin+ | Create a new mission |
| POST | `UpdateMission` | Admin+ | Update an existing mission (**destructive**: deletes and recreates all instances, cascading to their assignments; position slots are not updated). Used by the UI edit dialog. **The AI agent must never call it — use `UpdateMissionDetails`** |
| POST | `DeleteMission` | Admin+ | Delete a mission by ID |
| POST | `AssignSoldiers` | Admin+ | Assign soldiers to a mission instance |
| POST | `UnassignSoldier` | Admin+ | Remove a soldier from a mission instance |
| POST | `GetAvailableSoldiers` | Admin+ | Get soldiers available for a specific mission instance with rest-time scoring |
| POST | `GetMissionInstances` | Any | Get instances of a specific mission |
| POST | `GetMissionInstancesInRange` | Any | Get instances within a date range |
| POST | `AssignSoldiersToMissionInstance` | Admin+ | Sync the assignment list of one instance (add new, remove missing) |
| POST | `AutoAssign` | Admin+ | Run batch auto-assignment algorithm |
| POST | `AcceptAssignCandidate` | Admin+ | Accept an auto-assign candidate schedule |
| GET | `GetAllCandidates` | Admin+ | Get all pending candidate schedule GUIDs |
| POST | `GetCandidate` | Admin+ | Get details of a specific candidate schedule |
| POST | `RemoveSoldierFromMissionInstance` | Admin+ | Remove a soldier and return updated missions (the `/assignments` "הסר חייל" action; until P3 it always failed with 500 because `Mission.MissionPositions` was not loaded) |
| POST | `StartInteractiveAutoAssign` | Admin+ | Start interactive step-by-step auto-assign session |
| POST | `ContinueInteractiveAutoAssign` | Admin+ | Continue interactive session with user picks |
| POST | `CancelInteractiveAutoAssign` | Admin+ | Cancel an active interactive session |
| POST | `GetReplacementCandidates` | Admin+ | Get ranked replacement candidates for a soldier in an instance |
| POST | `ReplaceSoldierInMissionInstance` | Admin+ | Replace (or swap) a soldier in a mission instance |

Agent-facing, non-destructive mission/instance writes (`BL/Services/MissionEditService`; ISO local datetimes; responses and status codes as in "Schedule and mission write rules" below):

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| POST | `CreateMission` | Admin+ | Body `{ name, description?, soldiersRequired, commandersRequired, duration (≥ 1h), fromTime?/toTime? ("HH:mm" daily times), isSpecial, actualHours?, requiredRestAfter?, positions:[{position, count ≥ 1}] (≥ 1, no repeated position), specialStart? (special only: one instance [specialStart, +duration)), generate?:{from, to} (recurring only, see GenerateMissionInstances) }` → `GeneralResponse<{ mission: MissionSummaryDto, generatedInstances }>`. Text fields encrypted as usual; `RequiredInstances = true` |
| POST | `UpdateMissionDetails` | Admin+ | Body `{ id, name?, description?, soldiersRequired?, commandersRequired?, duration?, fromTime?, toTime?, actualHours?, requiredRestAfter?, positions?:[{id?, position, count}] }` → `GeneralResponse<MissionSummaryDto>`. Null = unchanged; `""` clears description/fromTime/toTime. `positions` is the complete desired list (id → update, no id → add, missing → remove); removing a slot referenced by assignments or pending auto-assign candidates → 409. **Never touches instances**; when slots change, IsFilled of every instance is recomputed. Nothing to update → 400 |
| POST | `GenerateMissionInstances` | Admin+ | Body `{ missionId, from, to, dryRun }`: ISO datetimes, or dates (`yyyy-MM-dd` → the mission's FromTime for `from`, ToTime for `to`, 00:00 if unset — like the UI dialog) → `GeneralResponse<{ count, skippedExisting, from, to (resolved), preview:[{from, to}] (≤ 50), lastSlot? }>`. Back-to-back slots of `duration` hours, last one truncated at `to`; slots overlapping existing instances are skipped (`skippedExisting`). Duration ≥ 1, range ≤ 62 days, ≤ 2000 slots; special mission → 400; dryRun saves nothing |
| POST | `AddMissionInstances` | Admin+ | Body `{ missionId, instances:[{from, to}] }` (each ≤ 62 days, ≤ 2000) → `GeneralResponse<ScheduleInstanceDto[]>`. Instances overlapping each other → 400; overlapping an existing instance of the mission → 409 |
| POST | `UpdateMissionInstance` | Admin+ | Body `{ missionInstanceId, from, to, allowViolations?=false, dryRun?=false }` → `GeneralResponse<{ instance (null when blocked), validations }>`. Overlapping another instance of the mission → 409. Every assigned soldier is re-validated at the new times in their own slot (own seat freed): **only OVERLAP blocks** (409) unless allowViolations; other violations (rest, vacation, …) are reported. dryRun returns the same result without saving |
| POST | `DeleteMissionInstance?missionInstanceId=` | Admin+ | → `GeneralResponse<{ deletedAssignments }>`; cascades to the assignments and also deletes the instance's pending auto-assign candidate rows (no FK; a dangling row would break `AcceptAssignCandidate`) |

#### SoldiersController (`api/Soldiers/`)

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | `GetSoldiers` | Any | Get all soldiers (optional cache reload). Non-admins get copies with phone/personal number blanked and vacations only on their own soldier |
| GET | `GetSoldiersLite?includeInactive=false` | Any | `SoldierLiteDto[] { id, name, platoon, company, positions:number[], active }` from the cache (no contact details) |
| GET | `GetSoldierById?soldierId=` | Self | `SoldierDetailsDto { ...lite, phone, personalNumber, hasUserAccount }`; 403 for another soldier, 404 if unknown; access logged (IDs only) |
| GET | `ReloadCache` | Admin+ | Force reload soldier cache |
| GET | `LoadCSV` | Admin+ | Dev utility to load soldiers from file/JSON (Development only) |
| POST | `AddSoldier` | Admin+ | Add a new soldier |
| POST | `UpdateSoldier` | Admin+ | Update soldier details (ignores `Active`) |
| POST | `SetSoldierActive` | Admin+ | Body `{ soldierId, active }` → `GeneralResponse<SoldierLiteDto>` (404 unknown). The only way to change `Active`; reloads `SoldiersCache` synchronously so validation sees `INACTIVE` immediately |
| POST | `DeleteSoldier` | Admin+ | Delete a soldier |
| POST | `RequestVacation` | Self | Request a vacation for a soldier (403 for another soldier) |
| POST | `RespondToVacationRequest` | Admin+ | Approve/deny a vacation request |
| POST | `GetVacations` | Self | Get vacations with optional filters (non-admins: forced to own soldier) |
| POST | `GetSummary` | Self | Get soldier assignment summary (total missions, hours, breakdown); 403 for another soldier |

#### UserController (`api/User/`)

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| POST | `Login` | Anonymous | Authenticate with SHA-512 hashed credentials → `GeneralResponse<LoginResponse>` `{ user, token }` (JWT) |
| GET | `Me` | Any | Caller identity → `MeDto { id, role, roleName, enabled, activated, soldierId?, soldierName? }` (401 if the account is gone) |
| POST | `ResetPassword` | Self | Reset a password (body: hashed username, new password, `encrypted` flag); stored as PBKDF2. Non-admins: own account only (403). Only a SuperAdmin may reset a SuperAdmin |
| POST | `CreateUsersForSoldiers` | Admin+ | Bulk create Regular accounts from soldier IDs (body `number[]`); skips personal number `N/A` and soldiers that already have an account; returns the created count |
| GET | `GetUsers` | Admin+ | `UserAccountDto[] { id, soldierId?, soldierName?, platoon?, company?, role, roleName, enabled, activated }` (no hashes/passwords) |
| POST | `UpdateUser` | Admin+ | Body `{ userId, role?, enabled?, activated? }` → `GeneralResponse<UserAccountDto>`; `role` requires SuperAdmin; guard rules in §2.4.2 |
| POST | `DeleteUser?userId=` | Admin+ | Delete a login account (soldier kept) → `GeneralResponse<int>` |
| POST | `ResetPasswordToDefault?userId=` | Admin+ | Password := PBKDF2(SHA-512(linked soldier's phone without dashes)) → `GeneralResponse<string>`; never accepts/returns a password |

#### MetadataController (`api/Metadata/`)

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| POST | `GetAssignmentsPerSoldiers` | Admin+ | Get assignment count per soldier in date range |
| POST | `GetHoursPerSoldiers` | Admin+ | Get total hours per soldier in date range |
| POST | `GetAssignmentsBreakdownPerSoldiers` | Admin+ | Get per-mission assignment breakdown per soldier |

#### ScheduleController (`api/Schedule/`) — compact, agent-facing

JSON is camelCase; **datetimes in requests and responses are ISO local wall-clock strings `yyyy-MM-ddTHH:mm:ss` without offset** (`BL/Models/Schedule/ScheduleDateFormat`); positions are enum numbers. Query errors: 400/404 with `{ message }`; writes return `GeneralResponse<T>` (see "Schedule and mission write rules").

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| POST | `GetAssignments` | Any | Body `{ from, to, missionIds?, soldierIds?, onlyUnfilled?=false }` (range ≤ 62 days, `to > from`, else 400) → `ScheduleInstanceDto[]` sorted by `from`: `{ instanceId, missionId, missionName, from, to, isFilled, requiredCount, assignedCount, assignments[{ soldierMissionId, soldierId, soldierName, missionPositionId, position }], missingPositions[{ missionPositionId, position, missing }] }`. Instances overlapping `[from, to)` via one SQL-filtered query (`FromTime < to && ToTime > from` + optional mission/soldier filters). With `soldierIds`, only instances where any of them is assigned, each with all its assignments. `isFilled` is computed live (`assignedCount >= requiredCount`), not read from the stored `IsFilled` flag |
| POST | `ValidateAssignments` | Admin+ | Body `{ missionInstanceId, soldierIds:number[] (≤ 1000), missionPositionId?, ignoreSoldierId?, excludeInstanceIds?:number[] }` → `AssignmentValidationDto[]` (one per distinct soldier): `{ soldierId, soldierName, isValid, violations[], warnings[], positionFit:"exact"\|"similar"\|"none", suggestedMissionPositionId?, suggestedPosition?, restBeforeHours?, requiredRestBeforeHours?, restAfterHours?, requiredRestAfterHours?, overlap?{instanceId, missionName, from, to}, vacation?{id, from, to, status:"Pending"\|"Approved"} }`. 404 unknown instance; 400 when `missionPositionId` is not a slot of the instance |
| POST | `AssignSoldier` | Admin+ | Body `{ missionInstanceId, soldierId, missionPositionId?, allowViolations?=false }` → `GeneralResponse<{ instance (null when blocked), validation }>`. Slot: the requested one, else the best free fit (= ValidateAssignments' `suggestedMissionPositionId`) |
| POST | `UnassignSoldier` | Admin+ | Body `{ missionInstanceId, soldierId }` → `GeneralResponse<ScheduleInstanceDto>`; 404 if the soldier is not assigned to the instance |
| POST | `ReplaceSoldier` | Admin+ | Body `{ missionInstanceId, oldSoldierId, newSoldierId, swap?=false, swapMissionInstanceId?, allowViolations?=false }` → `GeneralResponse<{ instances (empty when blocked), validations }>`. The new soldier prefers the vacated slot. Swap: the new soldier must be assigned to `swapMissionInstanceId`; each soldier takes the other's seat, validated with the instance they leave excluded (validations: [new soldier, old soldier]) |
| POST | `MoveSoldier` | Admin+ | Body `{ soldierId, fromMissionInstanceId, toMissionInstanceId, toMissionPositionId?, allowViolations?=false }` → same shape as ReplaceSoldier (instances [from, to]). The source instance is excluded from the overlap/rest checks; the same slot is preferred when both instances belong to one mission |

#### Schedule and mission write rules (`Schedule/*` writes, `Mission/*` P4 writes, `Soldiers/SetSoldierActive`)

- **Responses** (`Controllers/WriteResponseExtensions.ExecuteWrite`): `GeneralResponse<T>` with HTTP **200** applied; **409 blocked by violations** (`success:false`, `errorMessage` lists `soldier <id>: <codes>`, `value` carries the validations with `instance` null / `instances` empty); **409** conflicting data (`ScheduleConflictException`: overlapping instances, referenced slot); **404** unknown instance/soldier/mission/assignment (`KeyNotFoundException`); **400** invalid request (`ArgumentException`). Every call is logged as `"{operation} {ids} -> {status} by user ID={id}"` (IDs only, never names).
- **Validate, then write once:** placements are planned by `ScheduleChangePlanner` and checked by `AssignmentConstraintEvaluator` against the soldiers' current assignments and vacations; nothing is written when blocked; otherwise one `SaveChanges`.
- **Blocking** (`BlockingPolicy`): assignment writes block on any violation unless `allowViolations`; `ALREADY_ASSIGNED` and `NOT_FOUND` can never be overridden. `UpdateMissionInstance` blocks only on `OVERLAP` (other violations are reported).
- **Slot choice** (`AssignmentSlotSelector.SelectTargetSlot`): requested slot → preferred slot (the seat vacated by a replaced soldier, or the same slot when moving within one mission) when free and the soldier fits → best free fit (exact > similar, most missing, lowest id) → preferred slot anyway → free slot with most missing → best fitting full slot → lowest slot. The evaluator then validates exactly that slot (so POSITION_MISMATCH / INSTANCE_FULL describe it).
- **Exclusions:** replace frees the old soldier's seat (`ignoreSoldierId`); swap and move exclude the instance each soldier leaves (`excludeInstanceIds`); reschedule re-validates each assigned soldier in their own slot with their own seat freed.
- **IsFilled** of every affected instance is recomputed in memory before the save (`assigned ≥ required`, from the tracked non-deleted assignments).
- **Caches:** MissionsCache and SoldiersCache are reloaded (background) after each write.
- **Concurrency:** the writes are serialized in-process by `ScheduleService.WriteSync`, so validation and save are atomic with respect to each other (two parallel requests for the last seat → one 200, one 409). The legacy UI endpoints do not take this lock.

#### Assignment constraint rules (`BL/Services/AssignmentConstraintEvaluator.cs`, pure)

- **Violations:** `INACTIVE` (soldier not active), `ALREADY_ASSIGNED` (already in this instance), `OVERLAP` (another assignment overlaps; half-open intervals, so back-to-back is not an overlap), `ON_VACATION` (Pending or Approved vacation overlaps; Denied ignored), `INSTANCE_FULL` (no free capacity in the requested slot, or in the whole instance when no slot is requested), `POSITION_MISMATCH` (no slot the soldier can fill), `REST_BEFORE`, `REST_AFTER`, and `NOT_FOUND` (unknown soldier ID). **Warning:** `SIMILAR_POSITION`. Codes are emitted in this order.
- **Rest before:** gap from the end of the nearest previous assignment to this start ≥ that previous mission's `RequiredRestAfter` (null → 8h, as in `MissionService.ComputeRestScore`). **Rest after:** gap from this end to the start of the nearest next assignment ≥ this mission's `RequiredRestAfter` (null → 8h). `RequiredRestAfter = 0` allows back-to-back. Only assignments within a window of max(7 days, max RequiredRestAfter + 24h) around the instance are loaded.
- **Position fit:** `PositionHelper.GetSimilarPositions(slotPosition)` lists the soldier positions allowed to fill a slot. Among free slots (or the requested slot), an exact match wins over a similar one (ties: most missing, then lowest slot id; ranking in `AssignmentSlotSelector`, shared with the writes); the chosen slot is returned as `suggestedMissionPositionId/suggestedPosition` only when it has free capacity.
- **`ignoreSoldierId`** frees that soldier's slot in the instance (replacements); **`excludeInstanceIds`** are ignored for overlap and rest (moves, and both directions of a swap).
- Soldier name/positions/active come from `SoldiersCache`; assignments and vacations are read from the DB per call.

### 2.8 Auto-Assignment Algorithm (AutoAssignService)

This is the most complex part of the system (~1700 lines). Two modes:

#### Batch Mode (`AutoAssign`)
1. Reloads cache, selects soldiers and missions based on filters.
2. Determines which mission instances fall in the date range.
3. Selects multiple "starting missions" (up to `maxSchedules`, default 4) — prioritized by (soldiersRequired + commandersRequired) × instanceCount.
4. For each starting mission, runs a full schedule:
   - Orders instances: starting mission first, then by time.
   - Skips instances that already have accepted assignments.
   - For each instance: computes available soldiers, ranks them, fills positions.
5. Ranking factors (multiplicative):
   - **Rest Multiplier** — Based on hours since last/before next mission. Staircase: ≥1.5×threshold → 0.9, ≥1.25× → 0.8, ≥threshold → 0.7, below → 0.1, overlap → 0.0.
   - **Position Multiplier** — Exact match → 1.0, similar positions → 0.7–0.9 (count-based), no match → 0.0.
   - **Mission Avg Multiplier** — Fairness: how many times this soldier has been assigned to this specific mission vs. average.
   - **Total Mission Avg Multiplier** — Same across all missions.
   - **Mission Hours Avg Multiplier** — Based on hours worked on this mission vs. average.
   - **Total Hours Avg Multiplier** — Same across all missions.
   - **Over-Qualification Damping** — Optional: penalizes non-exact position matches.
   - **Deterministic Jitter** — Optional: adds deterministic pseudo-random noise for schedule diversity.
6. Position filling: matches soldiers to required positions (exact first, then similar). Two-pass: strict then relaxed.
7. Results stored as `SoldierMissionCandidate` in DB + JSON file. Best candidate marked by ValidInstancesCount, EvennessScore.
8. Evenness score = coefficient of variation of assignment counts across selected soldiers.

#### Interactive Mode (`StartInteractiveAutoAssign` / `ContinueInteractiveAutoAssign`)
- Step-by-step: pauses at each instance (or only faulty ones) to let the user pick soldiers.
- Session stored in `ConcurrentDictionary<string, InteractiveAutoAssignSession>`.
- Max 16 concurrent sessions, 60-minute idle timeout with cleanup timer.
- User can accept auto-picked soldiers, provide their own picks, or skip instances.
- Actions logged to `InteractiveAutoAssignLogs` table.

#### Replacement Feature (`GetReplacementCandidates` / `ReplaceSoldierInMissionInstance`)
- Scores candidates with position-match, rest-time, and fairness factors.
- Supports direct replacement or swap (old soldier takes new soldier's slot in another instance).

### 2.9 BL Models (Key)

| Model | Purpose |
|-------|---------|
| `AssignmentValidationModel` | Result of auto-assign: valid/faulty/skipped instances, evenness score |
| `CandidateMissionInstance` | Instance view with soldier assignments and missing positions |
| `CandidateSoldierAssignment` | Soldier ranked for an instance (rank, breakdown) |
| `GetAvailableSoldiersModel` | Soldier availability with rest times |
| `ReplacementCandidateModel` | Replacement candidate with scoring |
| `InteractiveAutoAssignSession` | Session state for interactive auto-assign |
| `InteractiveAutoAssignStep` | Step result returned to frontend |
| `RunContext` | Mutable context for an auto-assign run (aggregates, intervals, caches) |
| `AutoAssignScoringOptions` | Configurable scoring weights and flags |
| `SoldierSummary` | Total missions, hours, per-mission breakdown |
| `Users/MeDto`, `Users/UserAccountDto` | Whitelisted account views (no hashed username / password / salt) |
| `Schedule/ScheduleInstanceDto` (+ `ScheduleAssignmentDto`, `MissingPositionDto`) | Compact instance view for `Schedule/GetAssignments` |
| `Schedule/AssignmentValidationDto` (+ `OverlapInfoDto`, `VacationInfoDto`, `PositionFitKind`, `AssignmentConstraintCodes`) | Result of `Schedule/ValidateAssignments` |
| `Schedule/MissionSummaryDto` (+ `MissionPositionDto`) | Compact mission view for `Mission/GetMissionsSummary` |
| `Schedule/SoldierLiteDto`, `Schedule/SoldierDetailsDto` | Soldier views for `GetSoldiersLite` / `GetSoldierById` |
| `Schedule/ScheduleDateFormat` | ISO local datetime format (`yyyy-MM-ddTHH:mm:ss`) used by the agent-facing endpoints; `ParseDateOrDateTime` (datetime, or date + default time), `TryParseTimeOfDay`/`FormatTimeOfDay` ("HH:mm") |
| `Schedule/ScheduleWriteResult<T>`, `ScheduleConflictException` | Write outcome (`IsBlocked` + reason + value) and the 409 data-conflict exception |
| `Schedule/AssignSoldierResultDto`, `ScheduleChangeResultDto` | Values of `Schedule/AssignSoldier` and `ReplaceSoldier`/`MoveSoldier` |
| `Schedule/MissionCreateInput`, `MissionUpdateInput`, `MissionSlotInput`, `GenerateRangeInput` | `MissionEditService` inputs (mapped from the API request VMs) |
| `Schedule/CreateMissionResultDto`, `GenerateInstancesResultDto` (+ `TimeSlotDto`), `UpdateInstanceResultDto`, `DeleteInstanceResultDto` | Values of the P4 mission/instance writes |
| `Services: TimeSlot`, `InstanceGenerationResult`, `SlotCapacity`, `SlotPlan`, `PlannedPlacement`, `BlockingPolicy` | Records of the pure write helpers (generator, slot selector, slot planner, change planner) |

---

## 3. Conventions & Patterns

- **Naming:** Backend uses PascalCase (C# convention). Enum values match between front/back.
- **Encryption:** All PII is encrypted before DB writes and decrypted after reads. Extension methods handle this transparently.
- **Cache Pattern:** Singleton caches with `GetInstance()`, periodic auto-reload, manual `ReloadAsync()` after mutations.
- **Translation Pattern:** `DataLayer.Models` ↔ `Translators.Models` via static `Translator` classes and `.ToBL()` / `.ToDB()` extension methods.
- **Error Handling:** Services log errors via `Logger` before rethrowing. Controllers wrap in `GeneralResponse<T>` for auth/account endpoints; the schedule query endpoints map `ArgumentException` → 400 and `KeyNotFoundException` → 404 with `{ message }`; the agent-facing writes go through `ExecuteWrite` (`ArgumentException` → 400, `KeyNotFoundException` → 404, `ScheduleConflictException` or a blocked `ScheduleWriteResult` → 409, all as `GeneralResponse`).
- **Testing:** `ShabzakAPI.Tests` (xUnit, net8.0, references BL) covers pure logic only; run `dotnet test ShabzakAPI.sln`. Keep new rules pure (no DB/cache access) so they can be unit tested — the writes keep their decisions in pure helpers (`AssignmentSlotSelector`, `ScheduleChangePlanner`, `MissionInstanceGenerator`, `MissionSlotPlanner`).
- **IsFilled:** the stored `MissionInstance.IsFilled` flag can be stale (e.g. `AssignSoldiersToMission` recomputes it before saving), so `Schedule/GetAssignments` computes it live. The agent-facing writes recompute it correctly (`assigned ≥ required`) for every instance they touch.
- **EF tracked inserts:** `DataLayer.Models.MissionPositions` initializes `Mission = new Mission()`; when adding a new slot, set `Mission` to the tracked mission explicitly (as `MissionEditService` does).
- **EF Core collections:** `ids.Contains(x)` in EF queries is translated to `OPENJSON` (EF Core 8), which requires DB compatibility level ≥ 130 (Azure SQL default is 150+; check with `SELECT compatibility_level FROM sys.databases WHERE name = DB_NAME()`).

---

## 4. File Reference Quick-Lookup

### Most Important Files
| File | Purpose |
|------|---------|
| `04.ShabzakAPI/Program.cs` | Config/secrets, JWT auth, DI setup, singletons, CORS, Swagger, `/health` |
| `04.ShabzakAPI/Auth/JwtHelper.cs` | JWT issuing (claims, signing, expiry) |
| `04.ShabzakAPI/Auth/Policies.cs` | `AdminOrAbove` / `SuperAdminOnly` policies |
| `04.ShabzakAPI/Auth/ClaimsPrincipalExtensions.cs` | Claim helpers (`GetUserId`, `GetSoldierId`, `IsAdminOrAbove`, ...) |
| `04.ShabzakAPI/Auth/TokenRevalidation.cs` | Per-request account check + role refresh |
| `BL/Services/UserAccountGuard.cs` | Pure account-management guard rules |
| `04.ShabzakAPI/Controllers/ScheduleController.cs` | `Schedule/GetAssignments`, `ValidateAssignments`, `AssignSoldier`, `UnassignSoldier`, `ReplaceSoldier`, `MoveSoldier` |
| `04.ShabzakAPI/Controllers/WriteResponseExtensions.cs` | `ExecuteWrite`: write outcome → `GeneralResponse` + status, ID-only log line |
| `BL/Services/ScheduleService.cs` | Compact schedule queries, validation data loading, mission summaries, assignment writes, `WriteSync` |
| `BL/Services/MissionEditService.cs` | Non-destructive mission/instance writes (create, details/slot upsert, generate, add, reschedule, delete) |
| `BL/Services/AssignmentConstraintEvaluator.cs` | Pure assignment rule evaluation (§2.7 constraint rules) |
| `BL/Services/AssignmentSlotSelector.cs` | Pure slot ranking / target slot selection |
| `BL/Services/ScheduleChangePlanner.cs` | Pure planning of assign / replace / swap / move / reschedule + blocking policy |
| `BL/Services/MissionInstanceGenerator.cs` | Pure instance generation (UI-equivalent back-to-back slots) |
| `BL/Services/MissionSlotPlanner.cs` | Pure position-slot upsert planning |
| `BL/Services/ScheduleInstanceMapper.cs` | Pure instance → `ScheduleInstanceDto` mapping |
| `ShabzakAPI.Tests/` | xUnit tests (evaluator, slot selector, change planner, generator, slot planner, date format, mapper, account guard) |
| `04.ShabzakAPI/Controllers/MissionController.cs` | All mission & auto-assign endpoints, incl. the P4 mission/instance writes |
| `04.ShabzakAPI/Controllers/SoldiersController.cs` | Soldier CRUD, SetSoldierActive, vacations, summary |
| `04.ShabzakAPI/Controllers/UserController.cs` | Login, password reset, user creation |
| `04.ShabzakAPI/Controllers/MetadataController.cs` | Statistics endpoints |
| `BL/Services/AutoAssignService.cs` | Core scheduling algorithm (batch + interactive) |
| `BL/Services/MissionService.cs` | Mission CRUD, available soldiers, replacement |
| `BL/Services/SoldierService.cs` | Soldier CRUD, vacations, summary |
| `BL/Services/UserService.cs` | Authentication, account creation, Me, account management (GetAllUsers, UpdateUser, DeleteUser, ResetPasswordToDefault) |
| `BL/Services/MetadataService.cs` | Assignment/hours statistics |
| `BL/Services/PositionHelper.cs` | Position similarity mapping |
| `BL/Cache/SoldiersCache.cs` | Soldier in-memory cache |
| `BL/Cache/MissionsCache.cs` | Mission in-memory cache |
| `BL/Cache/UsersCache.cs` | User in-memory cache |
| `DataLayer/ShabzakDB.cs` | EF Core DbContext + Fluent API config |
| `DataLayer/LocalDB.cs` | SQLite DbContext for local/offline dev |
| `DataLayer/DbFactory.cs` | Static factory for DB context creation |
| `Translators/Encryption/AESEncryptor.cs` | AES encrypt/decrypt for PII |
| `Translators/Encryption/Sha512Encryptor.cs` | SHA-512 hex + PBKDF2 password hashing/verification |
| `Translators/Translators/SoldierTranslator.cs` | Soldier DB↔BL mapping |
| `Translators/Translators/MissionTranslator.cs` | Mission DB↔BL mapping |

---

## 5. Documentation Status

**XML Comments Added:**
- ✅ All Controllers (MissionController, SoldiersController, UserController, MetadataController, ScheduleController)
- ✅ All BL Services (AutoAssignService, MissionService, SoldierService, UserService, MetadataService, MissionInstanceService, MissionPositionService, SoldierMissionService, PositionHelper, ScheduleService, MissionEditService, AssignmentConstraintEvaluator, AssignmentSlotSelector, ScheduleChangePlanner, MissionInstanceGenerator, MissionSlotPlanner, ScheduleInstanceMapper, UserAccountGuard)
- ✅ Auth (JwtHelper, Policies, ClaimsPrincipalExtensions, TokenRevalidation), WriteResponseExtensions, and the new DTOs (`BL/Models/Users`, `BL/Models/Schedule`) / view models (UpdateUserRequest, GetScheduleAssignmentsRequest, ValidateAssignmentsRequest, ScheduleWriteRequests, MissionEditRequests)
- ✅ All Cache classes (SoldiersCache, MissionsCache, UsersCache)
- ✅ All Extensions (DateTimeExtension, EnumerableExtension, MissionExtension, MissionInstanceExtension, MissionPositionExtension, PositionExtension, SoldierExtension, SoldierMissionExtension, UserExtension)
- ✅ All Translators (MissionTranslator, SoldierTranslator, MissionInstanceTranslator, MissionPositionTranslator, SoldierMissionTranslator, UserTranslator, UserTokenTranslator, VacationTranslator)
- ✅ All Encryption (AESEncryptor, Sha512Encryptor)
- ✅ All DataLayer Models (Mission, MissionInstance, MissionPositions, Position, Soldier, SoldierMission, SoldierMissionCandidate, User, UserRole, UserToken, Vacation, VacationRequestStatus, AutoAssignmentsMeta, InteractiveAutoAssignLog)
- ✅ All DbContext (ShabzakDB, LocalDB, RemoteDB)
- ✅ DbFactory
