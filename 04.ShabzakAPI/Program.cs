using BL.Cache;
using BL.Extensions;
using BL.Services;
using DataLayer;
using Microsoft.EntityFrameworkCore;
using Translators.Encryption;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddDbContext<ShabzakDB>();
var db = new ShabzakDB();
db.Database.EnsureCreated();
db.SaveChanges();
db.Dispose();
//builder.Services.AddDbContext<RemoteDB>();
//var remotedb = new RemoteDB();
//remotedb.Database.EnsureCreated();
//remotedb.SaveChanges();
//remotedb.Dispose();

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

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


var corsPolicy = "AllPolicy";
builder.Services.AddCors(options =>
{
    options.AddPolicy(corsPolicy,
        policy =>
        {
            policy.AllowAnyOrigin();
            policy.AllowAnyMethod();
            policy.AllowAnyHeader();
        });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
//{
    app.UseSwagger();
    app.UseSwaggerUI();
//}

app.UseHttpsRedirection();

app.UseAuthorization();

app.UseCors(corsPolicy);

app.MapControllers();

app.Run();
