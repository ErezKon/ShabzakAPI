# ShabzakAPI — Backend AI Context

> **AI Instruction:** Every time you make a change to any file in the ShabzakAPI project, you **MUST** update this document to reflect that change — including new files, renamed files, new methods, changed architectures, new models, deleted items, etc. This file must always be accurate and up-to-date so future AI sessions can rely on it without re-analyzing the codebase. Also apply sthe same updates to the unified md document at the root folder UNIFIED_AI_CONTEXT.md

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
├── 04.ShabzakAPI/        # ASP.NET Core Web API (startup, controllers, view models)
│   ├── Program.cs         # Startup: DI, CORS, Swagger, caches, services
│   ├── Controllers/
│   │   ├── MissionController.cs
│   │   ├── SoldiersController.cs
│   │   ├── UserController.cs
│   │   └── MetadataController.cs
│   └── ViewModels/        # Request/response DTOs for the API layer
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
│   └── Logging/
├── Translators/            # Mapping layer between DataLayer ↔ BL models
│   ├── Models/             (BL/API-facing DTOs: Soldier, Mission, etc.)
│   ├── Translators/        (ToBL / ToDB mappers for each entity)
│   ├── Encryption/
│   │   ├── AESEncryptor.cs (AES-256, Rijndael — encrypts PII at rest)
│   │   └── Sha512Encryptor.cs (SHA-512 — password hashing)
│   └── Extensions/         (Encrypt/Decrypt for DataLayer models)
└── DataLayer/              # EF Core data access
    ├── ShabzakDB.cs        (DbContext — SQL Server, connection string, Fluent API config)
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

- **DataLayer** — EF Core entities stored encrypted (PII fields). Connection to Azure SQL.
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

- **At-Rest Encryption:** Soldier PII (Name, Phone, PersonalNumber, Platoon, Company) and Mission names are AES-256 encrypted before DB writes and decrypted on reads.
  - `AESEncryptor` — Uses Rijndael with static Key/IV. Caches encrypt/decrypt results in dictionaries.
  - Extension methods: `soldier.Encrypt()` / `soldier.Decrypt()` and same for missions.
- **Password Hashing:** `Sha512Encryptor.Encrypt(password + salt)` — SHA-512 with per-user salt.
- **Login:** Username = SHA-512(personalNumber), Password = SHA-512(phone). Salted again on storage.

### 2.5 Caching System

Three singleton caches, each auto-reloading from DB every 5 minutes:
- **`SoldiersCache`** — Holds both `DataLayer.Models.Soldier` and `Translators.Models.Soldier` dictionaries. Decrypts on load.
- **`MissionsCache`** — Holds missions with positions and instances. Decrypts on load.
- **`UsersCache`** — Holds user records for authentication.

Cache invalidation: Services call `SoldiersCache.ReloadAsync()` or `MissionsCache.ReloadAsync()` after mutations.

### 2.6 API Endpoints

#### MissionController (`api/Mission/`)

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `GetMissions` | Get all missions with instances and positions |
| POST | `AddMission` | Create a new mission |
| POST | `UpdateMission` | Update an existing mission |
| POST | `DeleteMission` | Delete a mission by ID |
| POST | `AssignSoldiers` | Assign soldiers to a mission instance |
| POST | `UnassignSoldier` | Remove a soldier from a mission instance |
| POST | `GetAvailableSoldiers` | Get soldiers available for a specific mission instance with rest-time scoring |
| POST | `GetMissionInstances` | Get instances of a specific mission |
| POST | `GetMissionInstancesInRange` | Get instances within a date range |
| POST | `AutoAssign` | Run batch auto-assignment algorithm |
| POST | `AcceptAssignCandidate` | Accept an auto-assign candidate schedule |
| GET | `GetAllCandidates` | Get all pending candidate schedule GUIDs |
| POST | `GetCandidate` | Get details of a specific candidate schedule |
| POST | `RemoveSoldierFromMissionInstance` | Remove a soldier and return updated missions |
| POST | `StartInteractiveAutoAssign` | Start interactive step-by-step auto-assign session |
| POST | `ContinueInteractiveAutoAssign` | Continue interactive session with user picks |
| POST | `CancelInteractiveAutoAssign` | Cancel an active interactive session |
| POST | `GetReplacementCandidates` | Get ranked replacement candidates for a soldier in an instance |
| POST | `ReplaceSoldierInMissionInstance` | Replace (or swap) a soldier in a mission instance |

#### SoldiersController (`api/Soldiers/`)

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `GetSoldiers` | Get all soldiers (optional cache reload) |
| GET | `ReloadCache` | Force reload soldier cache |
| GET | `LoadCSV` | Dev utility to load soldiers from file/JSON |
| POST | `AddSoldier` | Add a new soldier |
| POST | `UpdateSoldier` | Update soldier details |
| POST | `DeleteSoldier` | Delete a soldier |
| POST | `RequestVacation` | Request a vacation for a soldier |
| POST | `RespondToVacationRequest` | Approve/deny a vacation request |
| POST | `GetVacations` | Get vacations with optional filters |
| POST | `GetSummary` | Get soldier assignment summary (total missions, hours, breakdown) |

#### UserController (`api/User/`)

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `Login` | Authenticate user (SHA-512 hashed credentials) |
| POST | `ResetPassword` | Reset a user's password |
| POST | `CreateUsersForSoldiers` | Bulk create user accounts from soldier IDs |

#### MetadataController (`api/Metadata/`)

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `GetAssignmentsPerSoldiers` | Get assignment count per soldier in date range |
| POST | `GetHoursPerSoldiers` | Get total hours per soldier in date range |
| POST | `GetAssignmentsBreakdownPerSoldiers` | Get per-mission assignment breakdown per soldier |

### 2.7 Auto-Assignment Algorithm (AutoAssignService)

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

### 2.8 BL Models (Key)

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

---

## 3. Conventions & Patterns

- **Naming:** Backend uses PascalCase (C# convention). Enum values match between front/back.
- **Encryption:** All PII is encrypted before DB writes and decrypted after reads. Extension methods handle this transparently.
- **Cache Pattern:** Singleton caches with `GetInstance()`, periodic auto-reload, manual `ReloadAsync()` after mutations.
- **Translation Pattern:** `DataLayer.Models` ↔ `Translators.Models` via static `Translator` classes and `.ToBL()` / `.ToDB()` extension methods.
- **Error Handling:** Services log errors via `Logger` before rethrowing. Controllers wrap in `GeneralResponse<T>` for auth endpoints.

---

## 4. File Reference Quick-Lookup

### Most Important Files
| File | Purpose |
|------|---------|
| `04.ShabzakAPI/Program.cs` | DI setup, singletons, CORS, Swagger |
| `04.ShabzakAPI/Controllers/MissionController.cs` | All mission & auto-assign endpoints |
| `04.ShabzakAPI/Controllers/SoldiersController.cs` | Soldier CRUD, vacations, summary |
| `04.ShabzakAPI/Controllers/UserController.cs` | Login, password reset, user creation |
| `04.ShabzakAPI/Controllers/MetadataController.cs` | Statistics endpoints |
| `BL/Services/AutoAssignService.cs` | Core scheduling algorithm (batch + interactive) |
| `BL/Services/MissionService.cs` | Mission CRUD, available soldiers, replacement |
| `BL/Services/SoldierService.cs` | Soldier CRUD, vacations, summary |
| `BL/Services/UserService.cs` | User authentication and creation |
| `BL/Services/MetadataService.cs` | Assignment/hours statistics |
| `BL/Services/PositionHelper.cs` | Position similarity mapping |
| `BL/Cache/SoldiersCache.cs` | Soldier in-memory cache |
| `BL/Cache/MissionsCache.cs` | Mission in-memory cache |
| `BL/Cache/UsersCache.cs` | User in-memory cache |
| `DataLayer/ShabzakDB.cs` | EF Core DbContext + Fluent API config |
| `Translators/Encryption/AESEncryptor.cs` | AES encrypt/decrypt for PII |
| `Translators/Encryption/Sha512Encryptor.cs` | SHA-512 hashing for passwords |
| `Translators/Translators/SoldierTranslator.cs` | Soldier DB↔BL mapping |
| `Translators/Translators/MissionTranslator.cs` | Mission DB↔BL mapping |

---

## 5. Documentation Status

**XML Comments Added:**
- ✅ All Controllers (MissionController, SoldiersController, UserController, MetadataController)
- ✅ All BL Services (AutoAssignService, MissionService, SoldierService, UserService, MetadataService, MissionInstanceService, MissionPositionService, SoldierMissionService, PositionHelper)
- ✅ All Cache classes (SoldiersCache, MissionsCache, UsersCache)
- ✅ All Extensions (DateTimeExtension, EnumerableExtension, MissionExtension, MissionInstanceExtension, MissionPositionExtension, PositionExtension, SoldierExtension, SoldierMissionExtension, UserExtension)
- ✅ All Translators (MissionTranslator, SoldierTranslator, MissionInstanceTranslator, MissionPositionTranslator, SoldierMissionTranslator, UserTranslator, UserTokenTranslator, VacationTranslator)
- ✅ All Encryption (AESEncryptor, Sha512Encryptor)
- ✅ All DataLayer Models (Mission, MissionInstance, MissionPositions, Position, Soldier, SoldierMission, SoldierMissionCandidate, User, UserRole, UserToken, Vacation, VacationRequestStatus, AutoAssignmentsMeta, InteractiveAutoAssignLog)
- ✅ All DbContext (ShabzakDB, RemoteDB)
