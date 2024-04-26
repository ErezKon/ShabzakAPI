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

var soldierService = new SoldierService();
builder.Services.AddSingleton(soldierService);

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
//{
    app.UseSwagger();
    app.UseSwaggerUI();
//}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
