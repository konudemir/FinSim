using Microsoft.EntityFrameworkCore;
using FinSim.Data;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<FinSimDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("BorsaDb")));
    
var app = builder.Build();
