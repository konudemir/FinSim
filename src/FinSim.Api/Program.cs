using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using FinSim.Infrastructure.Data;
using FinSim.Application.Services;
using FinSim.Api.Hubs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using FinSim.Api.Services;
using FinSim.Infrastructure.Repositories;
using FinSim.Application.Interfaces;
using FinSim.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using FinSim.Domain.Models;
using FinSim.Api.Middleware;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<FinSimDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("BorsaDb")));

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddIdentityCore<User>()
    .AddEntityFrameworkStores<FinSimDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddHostedService<MarketTickWorker>();
builder.Services.AddScoped<IEmailSender, EmailSender>();
builder.Services.AddScoped<PriceSimEngine>();
builder.Services.AddScoped<OrderCheckEngine>();
builder.Services.AddScoped<IInstrumentRepository, InstrumentRepository>();
builder.Services.AddScoped<InstrumentService>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<IPortfolioRepository, PortfolioRepository>();
builder.Services.AddScoped<ITransactionRepository, TransactionRepository>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddSignalR();

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(builder.Configuration["Cors__Origin"] ?? "http://localhost:5173")
     .AllowAnyHeader()
     .AllowAnyMethod()
     .AllowCredentials()));


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


var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FinSimDbContext>();
    await db.Database.MigrateAsync();
}
app.UseFinSimExceptionHandler();
app.UseStaticFiles();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<PriceHub>("/hubs/prices");
app.Run();
