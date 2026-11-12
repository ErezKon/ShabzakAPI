using BL.Cache;
using BL.Extensions;
using BL.Services;
using DataLayer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
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
    // Reject deleted/disabled accounts and refresh the role claim on every request.
    options.Events = new JwtBearerEvents { OnTokenValidated = TokenRevalidation.OnTokenValidated };
});

builder.Services.AddAuthorization(Policies.Configure);
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

var userService = new UserService(usersCache, soldiersCache);
builder.Services.AddSingleton(userService);

var scheduleService = new ScheduleService(soldiersCache);
builder.Services.AddSingleton(scheduleService);

var missionEditService = new MissionEditService(scheduleService);
builder.Services.AddSingleton(missionEditService);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // "Authorize" button in Swagger UI: paste the JWT returned by /api/User/Login (without the "Bearer " prefix).
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT from /api/User/Login"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

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

// if (app.Environment.IsDevelopment())
// {
    app.UseSwagger();
    app.UseSwaggerUI();
//}

if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors(corsPolicy);
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok("healthy"));

app.Run();
