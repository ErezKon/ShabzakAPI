# ShabzakAPI — Backend

> Backend Web API for **Shabzak (שבצ"ק)** — a military duty-scheduling / assignment system that assigns soldiers to mission time-slots, with both manual and automatic (fairness-scored) assignment.

`Shabzak` (Angular UI) is the companion frontend. `ShabzakSQL` (SQL Server) is the database. The three are orchestrated by the root `../docker-compose.yml`.

> **Security refactored:** JWT Bearer authentication now protects all endpoints (except `/Login`). Secrets (DB connection string, AES key, JWT signing key) are read from `appsettings.json` / environment variables — see [Configuration](#configuration). CORS is restricted to configured origins, and Swagger is gated to Development only.

---

## Table of Contents

- [Tech Stack](#tech-stack)
- [Solution Architecture](#solution-architecture)
- [Project Layout](#project-layout)
- [Request/Data Flow](#requestdata-flow)
- [Domain Model](#domain-model)
- [Encryption Strategy](#encryption-strategy)
- [Caching](#caching)
- [Auto-Assignment Engine](#auto-assignment-engine)
- [API Reference](#api-reference)
- [Configuration](#configuration)
- [Getting Started](#getting-started)
- [Conventions](#conventions)

---

## Tech Stack

| Concern       | Technology                                                                             |
| ---------------| ----------------------------------------------------------------------------------------|
| Runtime       | .NET 8 / ASP.NET Core Web API                                                          |
| ORM           | Entity Framework Core (SQL Server; SQLite fallback)                                    |
| DB            | Azure SQL Server (prod) / SQL Server container / local SQLite                          |
| Serialization | Newtonsoft.Json                                                                        |
| API docs      | Swashbuckle (Swagger)                                                                  |
| Crypto        | AES-256 (random IV, backward-compatible) for PII at rest, SHA-512 for password hashing |
| Auth          | JWT Bearer (`Microsoft.AspNetCore.Authentication.JwtBearer`)                           |

The solution targets four class-library/web projects wired together via project references (`ShabzakAPI.sln`).

## Solution Architecture

A classic layered / N-tier architecture. Dependencies point downward only:

```
04.ShabzakAPI  (HTTP layer: Controllers, ViewModels, Program.cs)
      │  depends on
      ▼
BL             (Business Logic: Services, Cache, Extensions, Models, Logging)
      │  depends on
      ▼
Translators    (DTO models + DB↔BL mappers + Encryption)
      │  depends on
      ▼
DataLayer      (EF Core DbContext + entity models + DbFactory)
```

- **`04.ShabzakAPI`** — Thin controllers that delegate to BL services; request/response `ViewModels`; DI + middleware setup in `Program.cs`.
- **`BL`** — Business rules, the auto-assignment engine, in-memory caches, and encrypt/translate extension helpers.
- **`Translators`** — "BL/API-facing" model classes (`Translators.Models`), bidirectional `ToBL()`/`ToDB()` mappers, and the `AESEncryptor` / `Sha512Encryptor`.
- **`DataLayer`** — EF Core entities (`DataLayer.Models`), `ShabzakDB` (SQL Server) and `LocalDB` (SQLite) contexts, and `DbFactory`.

## Project Layout

```
ShabzakAPI/
├── ShabzakAPI.sln
├── Dockerfile                       # multi-stage .NET 8 build → aspnet runtime
├── docker (via ../docker-compose.yml)
├── 04.ShabzakAPI/
│   ├── Program.cs                   # DI graph, CORS, Swagger, middleware
│   ├── appsettings*.json            # logging config (no connection string here)
│   ├── Controllers/                 # Mission, Soldiers, User, Metadata
│   └── ViewModels/                  # request/response DTOs
├── BL/
│   ├── Services/                    # AutoAssignService, MissionService, SoldierService,
│   │                                #   UserService, MetadataService, MissionInstanceService,
│   │                                #   MissionPositionService, SoldierMissionService, PositionHelper
│   ├── Cache/                       # SoldiersCache, MissionsCache, UsersCache (singletons)
│   ├── Extensions/                  # Encrypt/Decrypt + ToBL/ToDB + domain helpers
│   ├── Models/                      # BL models (scoring, validation, summaries, sessions)
│   ├── Logging/                     # File/console Logger
│   └── CSVLoader.cs                 # one-off seed importer
├── Translators/
│   ├── Models/                      # Soldier, Mission, MissionInstance, User, Vacation, ...
│   ├── Translators/                 # <Entity>Translator: ToBL / ToDB
│   ├── Extensions/                  # Encrypt/Decrypt for DataLayer models
│   └── Encryption/                  # AESEncryptor.cs, Sha512Encryptor.cs
└── DataLayer/
    ├── ShabzakDB.cs                 # SQL Server DbContext + Fluent API mapping
    ├── LocalDB.cs                   # SQLite DbContext (inherits ShabzakDB)
    ├── RemoteDB.cs                  # secondary context (currently disabled)
    ├── DbFactory.cs                 # Create() → ShabzakDB or LocalDB
    ├── Models/                      # EF entities + enums
    └── soldiers.txt                 # tab-delimited seed data
```

## Request/Data Flow

**Read path**

```
Angular → GET/POST api/... → Controller → BL Service
      → (often served from in-memory Cache) → EF Core query
      → decrypt PII (AES) → Translate DB→BL model → JSON response
```

**Write path**

```
Angular → POST api/... (ViewModel) → Controller → BL Service
      → Translate BL→DB model → encrypt PII (AES) → EF Core SaveChanges
      → cache reload (async) → return decrypted BL model
```

## Domain Model

Core entities (`DataLayer/Models`). Fields marked * are AES-encrypted at rest.

| Entity | Key fields | Relationships |
|---|---|---|
| `Soldier` | `Name*`, `PersonalNumber*`, `Phone*`, `Platoon*`, `Company*`, `Position` (CSV of enum ints), `Active` | has many `SoldierMission`, `Vacation` |
| `Mission` | `Name*`, `Description`, `SoldiersRequired`, `CommandersRequired`, `Duration`, `FromTime`, `ToTime`, `IsSpecial`, `ActualHours`, `RequiredRestAfter` | has many `MissionInstance`, `MissionPositions` |
| `MissionInstance` | `MissionId`, `FromTime`, `ToTime`, `IsFilled` | belongs to `Mission`; has many `SoldierMission` |
| `MissionPositions` | `MissionId`, `Position`, `Count` | belongs to `Mission` |
| `SoldierMission` | `SoldierId`, `MissionInstanceId`, `MissionPositionId` | junction: soldier ↔ instance ↔ position |
| `SoldierMissionCandidate` | + `CandidateId` (GUID) | temporary auto-assign proposals |
| `Vacation` | `SoldierId`, `From`, `To`, `Approved` (Pending/Approved/Denied) | belongs to `Soldier` |
| `User` | `Name`, `Password`, `Salt`, `Role`, `Activated`, `Enabled`, `SoldierId?` | optionally linked to `Soldier` |
| `InteractiveAutoAssignLog` | `SessionId`, `MissionInstanceId`, `Action`, `PicksJson`, `Timestamp` | audit log |

### `Position` enum (stored as CSV in `Soldier.Position`)

`0 Simple, 1 Marksman, 2 GrenadeLauncher, 3 Medic, 4 Negev, 5 Hamal, 6 Sniper, 7 Translator, 8 ShootingInstructor, 9 KravMagaInstructor, 10 DroneOperator, 11 PlatoonCommanderComms, 12 CompanyCommanderComms, 13 ClassCommander, 14 Sergant, 15 PlatoonCommander, 16 CompanyDeputy, 17 CompanyCommander`.

`PositionHelper` maps each position to the set of positions a holder can also fill (e.g. `Marksman → {Marksman, Sniper}`), used by the scoring engine for "similar position" matches.

## Encryption Strategy

- **PII at rest** — `Soldier` PII and `Mission.Name` are AES-256 encrypted before writes and decrypted after reads, via `*.Encrypt()` / `*.Decrypt()` extension methods (`Translators/Encryption/AESEncryptor.cs`). **New encryptions use a random IV prepended to the ciphertext** (`v2:` prefix). Decryption auto-detects the format for backward compatibility with existing data.
- **Passwords** — hashed with `SHA-512(password + perUserSalt)` (`Sha512Encryptor`). Salt is a per-user GUID.
- **Login credential derivation** — the account is seeded from a soldier: `Username = SHA-512(PersonalNumber)`, `Password = SHA-512(Phone)`. The frontend pre-hashes both with SHA-512 before sending.
- **AES key** — configured in `appsettings.json` under `Aes:Key` (and optionally `Aes:LegacyIV` for existing data). See [Configuration](#configuration).

## Caching

Three singleton in-memory caches under `BL/Cache`:

- `SoldiersCache` — holds decrypted `Soldier` (BL + DB) dictionaries keyed by id.
- `MissionsCache` — holds missions with positions & instances.
- `UsersCache` — holds users for authentication.

Each cache auto-reloads from the DB every 5 minutes via a `Timer`, and services call `ReloadAsync()` after mutations. Reads are served from cache to avoid repeated decryption.

## Auto-Assignment Engine

`BL/Services/AutoAssignService.cs` (~2100 lines) is the heart of the system.

**Batch mode (`AutoAssign`)** — generates up to `MaxSchedules` (default 4) candidate schedules, one per "starting mission." For each instance it computes available soldiers, ranks them, and fills required positions (strict pass, then relaxed). Candidates are persisted as `SoldierMissionCandidate` rows and to a JSON file under `AssignmentsResults/`. The best candidate is chosen by valid-instance count and an *evenness score* (coefficient of variation of assignment counts).

**Ranking factors (multiplicative)** — rest-time multiplier (staircase around each mission's `RequiredRestAfter`), position match (exact = 1.0, similar = 0.7–0.9), per-mission and total fairness (assignments and hours vs. average), optional over-qualification damping, and optional deterministic jitter for schedule diversity.

**Interactive mode (`StartInteractiveAutoAssign` / `ContinueInteractiveAutoAssign`)** — steps through instances, pausing (on every instance or only faulty ones) so a user can accept, override, or skip picks. Sessions live in a `ConcurrentDictionary` (max 16, 60-min idle timeout, background cleanup). Actions are written to `InteractiveAutoAssignLogs`.

**Replacement (`GetReplacementCandidates` / `ReplaceSoldierInMissionInstance`)** — ranks replacements by position/rest/fairness and supports a two-way swap between instances.

## API Reference

All routes are `api/[controller]/[action]`. Bodies are JSON; several actions take primitives via query string.

### `api/Mission`
`GetMissions` (GET) · `AddMission` · `UpdateMission` · `DeleteMission` · `AssignSoldiers` · `UnassignSoldier` · `AssignSoldiersToMissionInstance` · `GetAvailableSoldiers` · `GetMissionInstances` · `GetMissionInstancesInRange` · `AutoAssign` · `AcceptAssignCandidate` · `GetAllCandidates` (GET) · `GetCandidate` · `RemoveSoldierFromMissionInstance` · `StartInteractiveAutoAssign` · `ContinueInteractiveAutoAssign` · `CancelInteractiveAutoAssign` · `GetReplacementCandidates` · `ReplaceSoldierInMissionInstance`

### `api/Soldiers`
`GetSoldiers` (GET) · `ReloadCache` (GET) · `LoadCSV` (GET, dev-only seed) · `AddSoldier` · `UpdateSoldier` · `DeleteSoldier` · `RequestVacation` · `RespondToVacationRequest` · `GetVacations` · `GetSummary`

### `api/User`
`Login` · `ResetPassword` · `CreateUsersForSoldiers`

### `api/Metadata`
`GetAssignmentsPerSoldiers` · `GetHoursPerSoldiers` · `GetAssignmentsBreakdownPerSoldiers`

Swagger UI is exposed at `/swagger` (**Development environment only**).

> All endpoints except `Login` require a valid JWT in the `Authorization: Bearer <token>` header.

## Configuration

All secrets are externalized to `appsettings.json` or environment variables. **Never commit real secrets to source control.**

### `appsettings.json` keys

| Key | Required | Description |
|---|---|---|
| `ConnectionStrings:ShabzakDB` | **Yes** | SQL Server connection string |
| `Jwt:Key` | **Yes** | HMAC-SHA256 signing key (≥ 32 chars) |
| `Jwt:Issuer` | No | Token issuer claim (default: `ShabzakAPI`) |
| `Jwt:Audience` | No | Token audience claim (default: `ShabzakClient`) |
| `Jwt:ExpiryMinutes` | No | Token lifetime in minutes (default: `480`) |
| `Aes:Key` | **Yes** | AES-256 encryption key for PII at rest |
| `Aes:LegacyIV` | No | Legacy static IV for decrypting pre-existing data |
| `Cors:AllowedOrigins` | No | Comma-separated allowed origins (default: `http://localhost:5230`) |

### Environment variable overrides

ASP.NET Core supports `__` (double-underscore) notation for nested keys:

```bash
export ConnectionStrings__ShabzakDB="Server=...;User ID=sa;Password=...;Initial Catalog=ShabzakDB;"
export Jwt__Key="YourAtLeast32CharacterLongSecretKeyHere!!"
```

The `docker-compose.yml` injects these from a `.env` file. Copy `.env.example` to `.env` and fill in real values.

### Other settings

- **Local DB toggle** — set `DataLayer.DbFactory.UseLocal = true` to use a local SQLite file (`shabzak_local.db`) instead of SQL Server.
- **CORS** — restricted to origins listed in `Cors:AllowedOrigins` (configurable).
- **Swagger** — only available when `ASPNETCORE_ENVIRONMENT=Development`.
- **Logs** — written to `Logs/<timestamp>.log` relative to the working directory (mounted to the host in `docker-compose.yml`).
- **Auto-assign artifacts** — written to `AssignmentsResults/<guid>.json` relative to the working directory.

## Getting Started

### Run everything with Docker (recommended)

From the repository root (`../`):

```bash
docker-compose up --build
```

This starts `shabzak-api` (host port **5231** → container 8080), `shabzak-ui` (host **5230**), and `shabzak-sql` (host **1433**).

> Create a `.env` file at the repo root (copy from `.env.example`) with `MSSQL_SA_PASSWORD` and `JWT_KEY`.

### Run the API locally

Prerequisites: .NET 8 SDK, and either a reachable SQL Server or SQLite mode.

```bash
# from ShabzakAPI/
dotnet restore ShabzakAPI.sln
dotnet build ShabzakAPI.sln -c Debug
dotnet run --project 04.ShabzakAPI/04.ShabzakAPI.csproj
```

On startup the app reads configuration, configures JWT + AES + DB, runs `Database.EnsureCreated()`, primes the three caches, then serves the API (Swagger at `/swagger` in Development only).

To seed data in development, use the `GET api/Soldiers/LoadCSV` endpoint — it reads from `DataLayer/soldiers.txt` relative to the project directory. **This endpoint is blocked in production.**

## Conventions

- **Naming** — PascalCase (C#). Enum values are kept numerically in sync with the frontend.
- **Mapping** — always go through `*Translator.ToBL()` / `ToDB()`; never expose `DataLayer.Models` directly across the API boundary (except where the code currently does — see analysis).
- **Encryption** — PII is encrypted in `ToDB`/`Encrypt` and decrypted in `ToBL`/`Decrypt`; keep this transparent to callers.
- **Caching** — read through the singleton caches; call `ReloadAsync()` after writes.
- **DB access** — create a context via `DbFactory.Create()` inside a `using` block per unit of work.

---

_For a detailed review of bugs, security issues, and improvement opportunities (with before/after code), see [`CODE_ANALYSIS.md`](./CODE_ANALYSIS.md). An auto-maintained deep index of the codebase lives in [`AI_CONTEXT.md`](./AI_CONTEXT.md)._
