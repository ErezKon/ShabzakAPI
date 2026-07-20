using BL.Cache;
using BL.Extensions;
using BL.Services;
using DataLayer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ShabzakAPI.Auth;
using System.Text;
using Translators.Encryption;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

DbFactory.ConnectionString = config.GetConnectionString("ShabzakDB")
    ?? throw new InvalidOperationException("ConnectionStrings:ShabzakDB is not configured.");

builder.Services.AddDbContext<ShabzakDB>();
using (var db = DbFactory.Create())
{
    db.Database.EnsureCreated();
    db.SaveChanges();
}

var aesKey = config["Encryption:AesKey"]
    ?? throw new InvalidOperationException("Encryption:AesKey is not configured.");
var aesIV = config["Encryption:AesIV"]
    ?? throw new InvalidOperationException("Encryption:AesIV is not configured.");
AESEncryptor.Configure(aesKey, aesIV);

var jwtKey = config["Jwt:Key"];
if (string.IsNullOrEmpty(jwtKey) || jwtKey.StartsWith("CHANGE_ME") || jwtKey.StartsWith("AUTO_GENERATED"))
{
    // Auto-generate a random key for development convenience.
    // WARNING: tokens are invalidated on every restart. Set Jwt:Key or Jwt__Key env var for persistence.
    jwtKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    Console.WriteLine("WARNING: Jwt:Key not configured — using auto-generated key. Tokens will not survive restarts.");
}
var jwtIssuer = config["Jwt:Issuer"] ?? "ShabzakAPI";
var jwtAudience = config["Jwt:Audience"] ?? "ShabzakApp";
var jwtExpiry = int.TryParse(config["Jwt:ExpiryMinutes"], out var exp) ? exp : 480;

JwtHelper.Configure(jwtKey, jwtIssuer, jwtAudience, jwtExpiry);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.FromMinutes(1)
    };
});

builder.Services.AddAuthorization();
builder.Services.AddControllers();

var usersCache = UsersCache.GetInstance();
builder.Services.AddSingleton(usersCache);

var soldiersCache = SoldiersCache.GetInstance();
builder.Services.AddSingleton(soldiersCache);

var missionsCache = MissionsCache.GetInstance();
builder.Services.AddSingleton(missionsCache);

var soldierService = new SoldierService();
builder.Services.AddSingleton(soldierService);

var missionService = new MissionService(soldiersCache);
builder.Services.AddSingleton(missionService);

var metadataService = new MetadataService(soldiersCache, missionsCache);
builder.Services.AddSingleton(metadataService);

var autoAssignService = new AutoAssignService(soldiersCache, missionsCache, missionService);
builder.Services.AddSingleton(autoAssignService);

var userService = new UserService(usersCache);
builder.Services.AddSingleton(userService);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var corsPolicy = "AllPolicy";
var allowedOrigins = config["Cors:AllowedOrigins"] ?? "*";
builder.Services.AddCors(options =>
{
    options.AddPolicy(corsPolicy, policy =>
    {
        if (allowedOrigins == "*")
        {
            policy.AllowAnyOrigin();
        }
        else
        {
            policy.WithOrigins(allowedOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries));
        }
        policy.AllowAnyMethod();
        policy.AllowAnyHeader();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors(corsPolicy);
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
