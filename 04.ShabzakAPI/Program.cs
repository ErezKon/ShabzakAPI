using BL.Cache;
using BL.Services;
using DataLayer;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddDbContext<ShabzakDB>();
var db = new ShabzakDB();
db.Database.EnsureCreated();
db.SaveChanges();
db.Dispose();

var soldiersCache = SoldiersCache.GetInstance();
builder.Services.AddSingleton(soldiersCache);

var missionsCache = MissionsCache.GetInstance();
builder.Services.AddSingleton(missionsCache);

var soldierService = new SoldierService();
builder.Services.AddSingleton(soldierService);

var missionService = new MissionService();
builder.Services.AddSingleton(missionService);

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
