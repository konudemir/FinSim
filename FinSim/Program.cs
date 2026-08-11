using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using FinSim.Data;
using FinSim.Services;
using FinSim.Hubs;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<FinSimDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("BorsaDb")));

builder.Services.AddControllers();
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddHostedService<Worker>();
builder.Services.AddSignalR();
var app = builder.Build();
app.UseStaticFiles();
app.MapControllers();
app.MapHub<PriceHub>("/hubs/prices");
app.Run();
