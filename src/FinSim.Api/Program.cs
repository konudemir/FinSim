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
using Microsoft.AspNetCore.Authorization;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<FinSimDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("BorsaDb")));

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddIdentityCore<User>()
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<FinSimDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddHostedService<MarketTickWorker>();
builder.Services.AddScoped<IEmailSender, EmailSender>();
builder.Services.AddScoped<ExternalPriceEngine>();
builder.Services.AddScoped<PriceSimEngine>();
builder.Services.AddScoped<OrderCheckEngine>();
builder.Services.AddScoped<MarginEngine>();
builder.Services.AddScoped<IInstrumentRepository, InstrumentRepository>();
builder.Services.AddScoped<InstrumentService>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<IPortfolioRepository, PortfolioRepository>();
builder.Services.AddScoped<ITransactionRepository, TransactionRepository>();
builder.Services.AddScoped<TransactionService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<IUnitOfWork, FinSim.Infrastructure.Data.UnitOfWork>();
builder.Services.AddScoped<IAdminAuditRepository, AdminAuditRepository>();
builder.Services.AddScoped<IRealtimeNotifier, RealtimeNotifier>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddSignalR();
builder.Services.AddScoped<IFundRepository, FundRepository>();
builder.Services.AddScoped<FundService>();
builder.Services.AddScoped<OrderExpiryEngine>();
builder.Services.AddScoped<ISnapshotRepository, SnapshotRepository>();
builder.Services.AddScoped<SnapshotService>();
builder.Services.AddHostedService<DailySnapshotWorker>();



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
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                if (ctx.Request.Cookies.TryGetValue("finsim_token", out var token))
                    ctx.Token = token;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

builder.Services.AddCors(o => o.AddPolicy("dev", p => p
    .WithOrigins("http://localhost:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

builder.Services.AddScoped<PriceSimEngine>();

// The browser User-Agent is required — without it Yahoo returns 429.
builder.Services.AddHttpClient<IExternalPriceSource, YahooChartPriceSource>(c =>
{
    c.BaseAddress = new Uri("https://query1.finance.yahoo.com/");
    c.Timeout = TimeSpan.FromSeconds(8);
    c.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
});


var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FinSimDbContext>();
    await db.Database.MigrateAsync();

    var priceSource = scope.ServiceProvider.GetRequiredService<IExternalPriceSource>();
    await DbSeeder.SeedInstrumentsAsync(db, priceSource);
    //await DbSeeder.SeedFundsAsync(db);

    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
    await DbSeeder.SeedAdminAsync(roleManager, userManager, builder.Configuration);
}
if (app.Environment.IsDevelopment())
    app.UseCors("dev");

app.UseFinSimExceptionHandler();
app.UseAuthentication();
app.UseCors();
app.UseAuthorization();
app.MapControllers();
app.MapHub<PriceHub>("/hubs/prices");
app.Run();
