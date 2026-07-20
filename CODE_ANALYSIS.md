# ShabzakAPI — Code Analysis & Review

A prioritized review of bugs, security vulnerabilities, and improvement opportunities in the backend. **No code was changed** — every "After" snippet below is a *recommendation*.

Because Shabzak stores PII for an entire military unit (names, personal/service numbers, phone numbers, unit structure), the security findings should be treated as high priority.

## Severity legend

| Level | Meaning |
|---|---|
| 🔴 Critical | Exploitable now / data breach / auth bypass |
| 🟠 High | Serious weakness; exploitable under common conditions |
| 🟡 Medium | Correctness bug, resource leak, or data-integrity risk |
| ⚪ Low | Code quality, maintainability, minor performance |

## Findings at a glance

| # | Severity | Area | Finding |
|---|---|---|---|
| 1 | 🔴 | AuthN/AuthZ | No server-side authentication or authorization on any endpoint |
| 2 | 🔴 | Secrets | Hardcoded DB credentials in source (`ShabzakDB`, `RemoteDB`) and compose |
| 3 | 🔴 | AuthZ | `ResetPassword` is unauthenticated, leaks the hash, and doesn't persist |
| 4 | 🟠 | Crypto | AES uses a static key **and** static IV (deterministic, unauthenticated) |
| 5 | 🟠 | Crypto | Passwords hashed with a single SHA-512 pass; non-constant-time compare |
| 6 | 🟠 | Data leak | `EnableSensitiveDataLogging()` always on |
| 7 | 🟠 | Data leak | Decrypted PII written to plaintext log files and candidate JSON on disk |
| 8 | 🟠 | Path traversal | `GetCandidate(guid)` builds a file path from unvalidated input |
| 9 | 🟠 | Config | Wide-open CORS; Swagger enabled in every environment |
| 10 | 🟡 | Build | `MetadataController` breakdown action returns the wrong type |
| 11 | 🟡 | Resource leak | `DbContext` not disposed in two `MissionService` methods |
| 12 | 🟡 | Race | `DeleteSoldier` reloads cache before `SaveChanges` |
| 13 | 🟡 | Concurrency | `SoldiersCache` singleton locking is a no-op |
| 14 | 🟡 | Bug | Cache `Timer` stored in a local variable (GC-eligible) |
| 15 | 🟡 | Performance | N+1 queries in `GetAvailableSoldiers` / `GetReplacementCandidates` |
| 16 | 🟡 | Correctness | `to.AddDays(10)` magic fudge in `MetadataService` date filters |
| 17 | ⚪ | Dead code | Unused locals in `MissionController.GetAvailableSoldiers` |
| 18 | ⚪ | Ops | `LoadCSV` dev endpoint with machine-specific paths, exposed + unauthenticated |
| 19 | ⚪ | Schema | `EnsureCreated()` instead of EF migrations |
| 20 | ⚪ | Concurrency | Shared `new Random()` in a singleton; blocking `.Wait()` calls |
| 21 | ⚪ | Memory | Unbounded encrypt/decrypt caches in `AESEncryptor` |

---

## 🔴 1. No server-side authentication or authorization

**Where:** `04.ShabzakAPI/Program.cs`, all controllers.

`Program.cs` calls `app.UseAuthorization()` but never registers an authentication scheme, and **no controller or action has an `[Authorize]` attribute**. Authorization middleware without authentication does nothing, so every endpoint is anonymous. Anyone who can reach the API can enumerate the whole unit's PII (`GET api/Soldiers/GetSoldiers` returns fully **decrypted** soldiers), delete soldiers/missions, reset passwords, and run auto-assign.

All access control currently lives in the Angular route guards — which run in the browser and are trivially bypassed by calling the API directly.

**Before** (`Program.cs`):
```csharp
var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpsRedirection();
app.UseAuthorization();      // no authentication registered → no-op
app.UseCors(corsPolicy);
app.MapControllers();
```

**After** (add JWT bearer auth + require it globally):
```csharp
// services
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
        };
    });
builder.Services.AddAuthorization();

// pipeline (order matters)
app.UseAuthentication();
app.UseAuthorization();
```

Then have `UserController.Login` issue a signed JWT (embedding the role), protect controllers with `[Authorize]`, and gate admin actions with `[Authorize(Roles = "Admin")]` (delete/reset/create-users/auto-assign). The Angular guards remain as UX only.

**Why:** Client-side guards are not a security boundary. Authorization must be enforced by the server on every request.

---

## 🔴 2. Hardcoded database credentials in source control

**Where:** `DataLayer/ShabzakDB.cs` (line ~37), `DataLayer/RemoteDB.cs` (line ~31), `../docker-compose.yml`.

Live credentials are committed to the repo:

**Before** (`ShabzakDB.cs`):
```csharp
var connectionString = @"Server=tcp:shabzakdb.database.windows.net,1433;Initial Catalog=ShabzakDB;Persist Security Info=False;User ID=erez;Password=Q1w2E3r4;...";
optionsBuilder.UseSqlServer(connectionString, b => b.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null));
```
`RemoteDB.cs` additionally exposes a public IP with `User ID=sa;Password=P@ssw0rd`, and `docker-compose.yml` hardcodes `MSSQL_SA_PASSWORD=MyStr0ngP@ss2026`.

**After** — read from configuration/environment/secret store and never commit secrets:
```csharp
protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
{
    if (!optionsBuilder.IsConfigured)
    {
        var cs = Environment.GetEnvironmentVariable("SHABZAK_DB_CONNECTION")
                 ?? throw new InvalidOperationException("SHABZAK_DB_CONNECTION not set");
        optionsBuilder.UseSqlServer(cs, b => b.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null));
    }
}
```
Prefer registering the context via DI with `builder.Configuration.GetConnectionString(...)` and use `dotnet user-secrets` locally, environment variables / Azure Key Vault in prod.

**Why:** These credentials are now compromised (they live in git history). **Rotate every leaked secret**, purge from history, and load secrets at runtime.

---

## 🔴 3. `ResetPassword` — unauthenticated, leaks the hash, and does not persist

**Where:** `04.ShabzakAPI/Controllers/UserController.cs` (`ResetPassword`).

**Before:**
```csharp
[HttpPost("ResetPassword")]
public string ResetPassword(ResetPasswordVM model)
{
    using var db = DbFactory.Create();
    var encUsername = model.Encrypted ? model.Username : Sha512Encryptor.Encrypt(model.Username);
    var cache = UsersCache.GetInstance();
    var user = cache.GetUser(encUsername);          // entity from cache, NOT tracked by `db`
    if (user == null) return "User not found.";
    var encPass = model.Encrypted ? model.Password : Sha512Encryptor.Encrypt(model.Password);
    var pass = Sha512Encryptor.Encrypt($"{encPass}{user.Salt}");
    user.Password = pass;
    db.SaveChanges();                                // saves nothing (user isn't attached)
    return pass;                                     // returns the stored hash to the caller
}
```

Three problems:
1. **No authorization** — any anonymous caller can reset any user's password by username.
2. **No persistence** — `user` comes from the cache and is detached from `db`; `db.SaveChanges()` is a no-op. The change lives only in memory until the next 5-minute cache reload wipes it.
3. **Information disclosure** — the resulting password hash is returned in the response.

**After** (sketch):
```csharp
[HttpPost("ResetPassword")]
[Authorize(Roles = "Admin")]        // or verify the caller is the same user + old password
public IActionResult ResetPassword(ResetPasswordVM model)
{
    using var db = DbFactory.Create();
    var encUsername = model.Encrypted ? model.Username : Sha512Encryptor.Encrypt(model.Username);
    var user = db.Users.FirstOrDefault(u => u.Name == encUsername);
    if (user is null) return NotFound();

    var encPass = model.Encrypted ? model.Password : Sha512Encryptor.Encrypt(model.Password);
    user.Salt = Guid.NewGuid().ToString();           // rotate the salt
    user.Password = PasswordHasher.Hash(encPass, user.Salt);  // see finding #5
    db.SaveChanges();
    UsersCache.GetInstance().ReloadAsync();
    return NoContent();                              // never echo the hash
}
```

**Why:** Password reset is a privileged operation, must be persisted through a tracked entity, and must never return secret material.

---

## 🟠 4. AES uses a static key **and** a static IV (deterministic, unauthenticated)

**Where:** `Translators/Encryption/AESEncryptor.cs`.

**Before:**
```csharp
public static readonly string GenKey = "g1rmNZsVU5+maAU/nVhWg89t9qSNAU3jPqCnLDT1TDk=";
public static readonly string GenIV  = "wb6ZsDrefHgpaebdweEjcg==";
...
rijAlg.Key = Convert.FromBase64String(GenKey);
rijAlg.IV  = Convert.FromBase64String(GenIV);   // same IV for every value
```

Two issues:
- **Static IV** → CBC becomes deterministic: identical plaintext always yields identical ciphertext. An attacker (or anyone with DB read access) can tell which soldiers share a `Platoon`/`Company`, and because empty fields are encrypted as a constant `"N/A"`, every default row is identifiable. This leaks structure even without the key.
- **No integrity** — CBC without a MAC is malleable and vulnerable to padding-oracle style attacks; ciphertext tampering is undetected.

**After** — use authenticated encryption (AES-GCM) with a fresh random nonce per value, and load the key from a secret store:
```csharp
// key comes from configuration / Key Vault, not source
public string Encrypt(string plainText)
{
    Span<byte> nonce = stackalloc byte[12];
    RandomNumberGenerator.Fill(nonce);
    var plain = Encoding.UTF8.GetBytes(plainText);
    var cipher = new byte[plain.Length];
    var tag = new byte[16];
    using var aes = new AesGcm(_key, 16);
    aes.Encrypt(nonce, plain, cipher, tag);
    // store nonce + tag + ciphertext together
    return Convert.ToBase64String(Concat(nonce, tag, cipher));
}
```

**Why:** Encryption at rest should be non-deterministic and authenticated. A static IV defeats much of the point of encrypting PII.

> Related: the static in-memory `encryptionDic`/`decryptionDic` keep plaintext↔ciphertext pairs in memory unbounded — see finding #21.

---

## 🟠 5. Weak password hashing + non-constant-time comparison + low-entropy credentials

**Where:** `Translators/Encryption/Sha512Encryptor.cs`, `BL/Services/UserService.cs`.

**Before** (`UserService.Login`):
```csharp
var encPass = Sha512Encryptor.Encrypt($"{password}{user.Salt}");   // single SHA-512 pass
if (user.Password.Equals(encPass)) { ... }                          // not constant-time
```

- **Fast hash** — a single SHA-512 round is designed to be *fast*, which is exactly wrong for passwords; it is cheap to brute-force offline.
- **Low-entropy inputs** — accounts are derived as `Username = SHA-512(personalNumber)`, `Password = SHA-512(phone)` (`UserService.CreateFromSoldier`). Personal numbers and phone numbers are short, structured, and often known within a unit — effectively guessable default credentials.
- **Timing side-channel** — `string.Equals` short-circuits, leaking information via timing.

**After** — use a slow, salted KDF and a fixed-time comparison:
```csharp
// hashing (e.g., PBKDF2; bcrypt/Argon2 via a library are also good)
public static string Hash(string password, string salt) =>
    Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(
        password, Encoding.UTF8.GetBytes(salt),
        iterations: 210_000, HashAlgorithmName.SHA512, outputLength: 32));

// verify
var expected = Convert.FromBase64String(user.Password);
var actual   = Convert.FromBase64String(PasswordHasher.Hash(password, user.Salt));
if (CryptographicOperations.FixedTimeEquals(expected, actual)) { ... }
```

Additionally, require users to set a real password on first login instead of using phone-derived defaults.

**Why:** Password storage must be slow-by-design, salted, and compared in constant time; default credentials derived from PII are trivially guessable.

---

## 🟠 6. `EnableSensitiveDataLogging()` is always on

**Where:** `DataLayer/ShabzakDB.cs` (`OnConfiguring`).

**Before:**
```csharp
optionsBuilder.EnableSensitiveDataLogging();   // logs parameter values, incl. PII, in all envs
```

**After:**
```csharp
#if DEBUG
optionsBuilder.EnableSensitiveDataLogging();
#endif
// or: if (env.IsDevelopment()) optionsBuilder.EnableSensitiveDataLogging();
```

**Why:** In production this writes query parameter values (which include encrypted-at-rest PII in plaintext at query time) to the logs.

---

## 🟠 7. Decrypted PII written to plaintext logs and candidate JSON on disk

**Where:** `BL/Services/SoldierService.cs`, `MissionService.cs` (many `Logger.Log($"...{JsonConvert.SerializeObject(soldier/mission)}")`), `BL/Logging/Logger.cs`, `AutoAssignService.FinalizeScheduleResult`.

**Before** (`SoldierService.AddSoldier`):
```csharp
Logger.Log($"Adding Soldier:\n {JsonConvert.SerializeObject(soldier, Formatting.Indented)}");
```
At this point the soldier is **decrypted**, so full names, personal numbers and phones are written to `Logs/*.log` (a directory bind-mounted to the host in `docker-compose.yml`). Auto-assign similarly serializes soldier data to `AssignmentsResults/<guid>.json`.

**After** — log identifiers only, and redact PII:
```csharp
Logger.Log($"Adding soldier id={soldier.Id}, positions={soldier.Position}");
```
Use structured logging (`ILogger<T>`) with redaction, and avoid persisting PII to flat files; if candidate schedules must be stored, store IDs (not names) and protect the directory.

**Why:** Logs and scratch files routinely end up in backups, dashboards, and support tickets. PII should never be written to them in the clear.

---

## 🟠 8. Path traversal in `GetCandidate`

**Where:** `BL/Services/AutoAssignService.cs` (`GetCandidate`), called by `MissionController.GetCandidate`.

**Before:**
```csharp
public AssignmentValidationModel GetCandidate(string guid)
{
    var path = Path.Combine(Directory.GetCurrentDirectory(), "AssignmentsResults", $"{guid}.json");
    if (!File.Exists(path)) throw new FileNotFoundException("Candidate not found.");
    var json = File.ReadAllText(path);
    ...
}
```
`guid` is attacker-controlled. A value like `../../appsettings` (or an absolute path) makes `Path.Combine` escape the intended folder, allowing arbitrary `*.json` reads.

**After** — validate that the input is actually a GUID before touching the filesystem:
```csharp
if (!Guid.TryParse(guid, out var parsed))
    throw new ArgumentException("Invalid candidate id.");
var dir = Path.Combine(Directory.GetCurrentDirectory(), "AssignmentsResults");
var path = Path.Combine(dir, $"{parsed}.json");
// defense in depth: ensure the resolved path stays within `dir`
if (!Path.GetFullPath(path).StartsWith(Path.GetFullPath(dir) + Path.DirectorySeparatorChar))
    throw new UnauthorizedAccessException();
```

**Why:** Never build filesystem paths from raw user input.

---

## 🟠 9. Wide-open CORS and Swagger always enabled

**Where:** `04.ShabzakAPI/Program.cs`.

**Before:**
```csharp
options.AddPolicy(corsPolicy, policy =>
{
    policy.AllowAnyOrigin();
    policy.AllowAnyMethod();
    policy.AllowAnyHeader();
});
...
//if (app.Environment.IsDevelopment())
//{
    app.UseSwagger();
    app.UseSwaggerUI();
//}
```

**After:**
```csharp
options.AddPolicy(corsPolicy, policy =>
{
    policy.WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>()!)
          .AllowAnyMethod()
          .AllowAnyHeader();
});
...
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
```

**Why:** An allow-all CORS policy lets any website drive the API from a victim's browser; Swagger in production advertises the full attack surface. (Once auth is added, also confirm `UseHttpsRedirection` matches how TLS is terminated in front of the container.)

---

## 🟡 10. `MetadataController` breakdown action returns the wrong type

**Where:** `04.ShabzakAPI/Controllers/MetadataController.cs` (`GetAssignmentsBreakdownPerSoldiers`).

The action is declared to return `Dictionary<string, Dictionary<string, int>>`, but the body builds and returns a `List<AssignmentsBreakdownVM>` (`AssignmentsBreakdownVM` is a class, not a dictionary).

**Before:**
```csharp
[HttpPost("GetAssignmentsBreakdownPerSoldiers")]
public Dictionary<string, Dictionary<string, int>> GetAssignmentsBreakdownPerSoldiers(GetMetadataRequest request)
{
    var ret = _metadataService.GetAssignmentsBreakdownPerSoldiers(request.From, request.To, request.SoldierType)
        .Select(r => new AssignmentsBreakdownVM { ... })
        .ToList();
    return ret;   // List<AssignmentsBreakdownVM>  ->  declared Dictionary<...>
}
```

**After** — make the signature match what is actually returned (this is also what the Angular `MetadataService` expects — an array of breakdown objects):
```csharp
[HttpPost("GetAssignmentsBreakdownPerSoldiers")]
public List<AssignmentsBreakdownVM> GetAssignmentsBreakdownPerSoldiers(GetMetadataRequest request)
{
    ...
    return ret;
}
```

**Why:** As written this is a type mismatch (`CS0029`) that will not compile — verify with `dotnet build`. Whatever is deployed differs from this source, or the project last built before this action was edited. Either way the declared contract is wrong and should match the returned shape.

---

## 🟡 11. `DbContext` not disposed in two `MissionService` methods

**Where:** `BL/Services/MissionService.cs` — `AssignSoldiersToMission`, `UnassignSoldiersToMission`.

**Before:**
```csharp
public Mission AssignSoldiersToMission(int missionId, int missionInstanceId, IEnumerable<...> soldiers)
{
    var db = DataLayer.DbFactory.Create();   // no `using` → never disposed
    ...
    return GetDBMissionById(db, missionId).ToBL();
}
```
Every other method in the class uses `using var db = ...`. These two leak the context (and its pooled DB connection) on each call.

**After:**
```csharp
public Mission AssignSoldiersToMission(int missionId, int missionInstanceId, IEnumerable<...> soldiers)
{
    using var db = DataLayer.DbFactory.Create();
    ...
}
```

**Why:** Undisposed `DbContext`s hold connections and tracked state; under load this exhausts the connection pool.

---

## 🟡 12. `DeleteSoldier` reloads the cache before saving

**Where:** `BL/Services/SoldierService.cs` (`DeleteSoldier`).

**Before:**
```csharp
db.Soldiers.Remove(soldier);
SoldiersCache.ReloadAsync();   // reads DB *before* the delete is committed
db.SaveChanges();
```
The async reload can (and often will) query the database before `SaveChanges()` commits, so the cache is repopulated with the soldier that was just "deleted."

**After:**
```csharp
db.Soldiers.Remove(soldier);
db.SaveChanges();
SoldiersCache.ReloadAsync();   // reload only after the change is durable
```

**Why:** Cache invalidation must follow the write, not precede it. (`AddSoldier`/`Update` already order these correctly — this one is reversed.)

---

## 🟡 13. `SoldiersCache` singleton locking is a no-op

**Where:** `BL/Cache/SoldiersCache.cs` (`GetInstance`, and the reader/writer methods).

**Before:**
```csharp
public static IEnumerable<Soldier> Soldiers => soldiersDic?.Values?.ToList() ?? [];
...
public static SoldiersCache GetInstance()
{
    lock (Soldiers)                 // locks a brand-new List each call → no mutual exclusion
    {
        if (Instance == null)
        {
            lock (Soldiers) { Instance = new SoldiersCache(); }
        }
        return Instance;
    }
}
```
`Soldiers` is a property that allocates a **new** `List` on every access, so each `lock (Soldiers)` locks a different object. The "double-checked locking" guards nothing, and readers using `lock (soldiersDic)` race with `ReloadCache()` which reassigns `soldiersDic` to a new dictionary.

**After** — lock on a dedicated static object (or use `Lazy<T>`):
```csharp
private static readonly object _gate = new();
public static SoldiersCache GetInstance()
{
    if (Instance is not null) return Instance;
    lock (_gate) { Instance ??= new SoldiersCache(); }
    return Instance;
}
```
For the dictionaries, either lock a single dedicated object consistently for both reads and the reassignment in `ReloadCache`, or switch to `ConcurrentDictionary` / an immutable snapshot swapped via `Interlocked.Exchange`.

**Why:** Locking on a per-call temporary provides no synchronization; concurrent init/reload can produce duplicate instances or torn reads.

---

## 🟡 14. Cache reload `Timer` is stored in a local variable

**Where:** `BL/Cache/SoldiersCache.cs` constructor (and check `MissionsCache` for the same pattern).

**Before:**
```csharp
private SoldiersCache()
{
    var timer = new Timer(state => ReloadCache(), null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
}   // `timer` goes out of scope → eligible for GC → periodic reload may silently stop
```

**After:**
```csharp
private readonly Timer _reloadTimer;
private SoldiersCache()
{
    _reloadTimer = new Timer(_ => ReloadCache(), null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
}
```

**Why:** `System.Threading.Timer` is collected if nothing references it; keeping a field guarantees it keeps firing for the cache's lifetime.

---

## 🟡 15. N+1 queries in availability / replacement scoring

**Where:** `BL/Services/MissionService.cs` — `GetAvailableSoldiers`, `GetReplacementCandidates` (+ `ComputeFairnessScore`).

**Before** (per-soldier queries inside the loop over the whole pool):
```csharp
foreach (var soldierId in pool)                 // pool can be every soldier
{
    var assignedForInstance = db.SoldierMission
        .Count(sm => sm.SoldierId == soldierId && sm.MissionInstanceId == missionInstanceId) > 0;   // query 1
    var soldierMissions = db.SoldierMission
        .Where(mi => mi.SoldierId == soldierId)
        .Include(...).Include(...).ToList();                                                          // query 2
    // GetReplacementCandidates also calls ComputeFairnessScore → db.SoldierMission.Count(...)       // query 3
}
```
For N soldiers this is ~2–3×N round-trips.

**After** — load once, group in memory:
```csharp
var bySoldier = db.SoldierMission
    .Include(sm => sm.MissionInstance).ThenInclude(mi => mi.Mission)
    .GroupBy(sm => sm.SoldierId)
    .ToDictionary(g => g.Key, g => g.OrderBy(sm => sm.MissionInstance.FromTime).ToList());

foreach (var soldierId in pool)
{
    bySoldier.TryGetValue(soldierId, out var soldierMissions);
    // compute assignedForInstance / rest times / fairness from the in-memory list
}
```

**Why:** One query plus in-memory grouping scales far better than hundreds of small queries; latency here grows linearly with unit size.

---

## 🟡 16. `to.AddDays(10)` magic fudge in `MetadataService` date filters

**Where:** `BL/Services/MetadataService.cs` — all three methods filter with `... && sm.MissionInstance.ToTime <= to.AddDays(10)`.

**Before:**
```csharp
.Where(sm => sm.MissionInstance.FromTime >= absoluteFrom && sm.MissionInstance.ToTime <= to.AddDays(10))
```
The end of the requested range is silently extended by 10 days, so statistics include assignments outside the window the caller asked for (and the amount is an unexplained constant).

**After** — respect the requested range (expand to end-of-day at most, and make it explicit):
```csharp
var rangeEnd = to.Date.AddDays(1).AddTicks(-1);   // inclusive end-of-day
.Where(sm => sm.MissionInstance.FromTime >= absoluteFrom && sm.MissionInstance.ToTime <= rangeEnd)
```

**Why:** Fairness/hours reports should reflect the selected dates; an undocumented +10-day fudge makes the "justice" charts misleading and hard to reconcile.

---

## ⚪ 17. Dead code in `MissionController.GetAvailableSoldiers`

```csharp
var ret = _missionService.GetAvailableSoldiers(request.MissionInstanceId, request.SoldiersPool);
int? val = 0;
var temp = val ?? int.MaxValue;   // computed, never used
return ret;
```
Remove the two leftover debug lines.

## ⚪ 18. `LoadCSV` seed endpoint should not ship

**Where:** `SoldiersController.LoadCSV` (`[HttpGet("LoadCSV")]`).

It is anonymous, reads machine-specific absolute paths (`C:\Users\Erez_Konforti\...`), mutates the DB, contains large commented-out blocks, and returns `null`. Move seeding to a guarded admin tool / EF seeding / a CLI, and delete the endpoint (or gate it behind `#if DEBUG` + `[Authorize(Roles="Admin")]`).

## ⚪ 19. Prefer EF migrations over `EnsureCreated()`

**Where:** `Program.cs` (`db.Database.EnsureCreated()`).

`EnsureCreated()` cannot evolve an existing schema (no migrations). As the model changes you will hit drift or be forced to drop the DB. Adopt `dotnet ef migrations` + `db.Database.Migrate()`.

## ⚪ 20. Shared `Random` and blocking `.Wait()`

- `AutoAssignService` holds `private readonly Random rand = new Random();` in a **singleton**; `Random` is not thread-safe, so concurrent runs can corrupt its state. Use `Random.Shared` (.NET 6+) or a `[ThreadStatic]`/locked instance.
- `reload.Wait()` (`AcceptAutoAssignCandidate`) and `session.Lock.Wait()` block pool threads. Prefer `await` end-to-end (`await MissionsCache.ReloadAsync()`), which also means making the call chain async.

## ⚪ 21. Unbounded encrypt/decrypt caches

**Where:** `Translators/Encryption/AESEncryptor.cs` (`encryptionDic`, `decryptionDic`).

These static dictionaries cache every plaintext↔ciphertext pair ever seen and never evict, so memory grows with distinct values, and plaintext PII lingers in memory indefinitely. Use a size-bounded cache (e.g., `MemoryCache` with limits) or drop the cache entirely — AES is fast.

---

## Suggested remediation order

1. **Stop the bleeding (secrets):** rotate the leaked DB passwords, AES key, and SA password; move all secrets to configuration; scrub git history. *(Findings #2, #4)*
2. **Add real auth:** JWT authentication + `[Authorize]` + role policies; lock down `ResetPassword`, `LoadCSV`, delete/create endpoints. *(#1, #3, #18)*
3. **Fix data exposure:** disable sensitive logging in prod, redact PII from logs/files, restrict CORS, gate Swagger, validate the candidate path. *(#6, #7, #8, #9)*
4. **Harden crypto:** AES-GCM w/ random nonce; PBKDF2/bcrypt/Argon2 + constant-time compare; force real passwords. *(#4, #5)*
5. **Correctness & stability:** fix the metadata return type, dispose contexts, reorder delete/reload, fix cache locking + timer, remove the date fudge. *(#10–#16)*
6. **Cleanup & scale:** N+1 queries, dead code, migrations, `Random`, blocking waits, bounded caches. *(#15, #17, #19, #20, #21)*

> Add automated tests around auth, the scoring/fairness math, and the assignment/replacement flows before refactoring — these areas are complex and currently untested.

