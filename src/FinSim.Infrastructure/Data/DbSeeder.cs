using FinSim.Domain.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using FinSim.Domain.Models.Enums;
using FinSim.Domain.Services;

namespace FinSim.Infrastructure.Data;

public static class DbSeeder
{
    private const string AdminRole = "Admin";
    private static readonly (string Symbol, string Name, decimal BasePrice, (string Symbol, int Quantity)[] Basket)[] Funds =
    [
        ("FSB30", "FinSim Büyükler Fonu", 100m,
            [("THYAO", 5), ("GARAN", 4), ("KCHOL", 3), ("TUPRS", 2), ("BIMAS", 1)]),
        ("FSBNK", "FinSim Bankacılık Fonu", 100m,
            [("GARAN", 10), ("AKBNK", 15), ("YKBNK", 30), ("ISCTR", 40), ("VAKBN", 25)]),
        ("FSSAN", "FinSim Sanayi Fonu", 100m,
            [("EREGL", 12), ("TOASO", 3), ("FROTO", 1), ("ARCLK", 5), ("PETKM", 20), ("SISE", 10)]),
        ("FSTUK", "FinSim Tüketim Fonu", 100m,
            [("BIMAS", 2), ("MGROS", 1), ("TCELL", 8), ("TTKOM", 12)]),
    ];

    private static readonly (string Symbol, string Name, decimal Price, bool Active)[] Instruments =
    [
        ("THYAO", "Türk Hava Yolları",            100m,  true),
        ("ASELS", "Aselsan",                        40m,  true),
        ("GARAN", "Garanti BBVA",                  110m,  true),
        ("TUPRS", "Tüpraş",                        155m,  true),
        ("AKBNK", "Akbank",                         45m,  true),
        ("EREGL", "Erdemir Ereğli Demir Çelik",     50m,  true),
        ("BIMAS", "BİM Birleşik Mağazalar",        380m,  true),
        ("SISE",  "Şişecam",                        55m,  true),
        ("KCHOL", "Koç Holding",                   190m,  true),
        ("SAHOL", "Sabancı Holding",                95m,  true),
        ("FROTO", "Ford Otosan",                  1100m,  true),
        ("YKBNK", "Yapı Kredi Bankası",             25m,  true),
        ("PGSUS", "Pegasus Hava Yolları",          220m,  true),
        ("TCELL", "Turkcell",                       95m,  true),
        ("ISCTR", "İş Bankası (C)",                 14m,  true),
        ("TOASO", "Tofaş Otomobil Fabrikası",      260m,  true),
        ("ARCLK", "Arçelik",                       140m,  true),
        ("TTKOM", "Türk Telekom",                   48m,  true),
        ("VAKBN", "VakıfBank",                      27m,  true),
        ("PETKM", "Petkim",                         21m,  true),
        ("ENKAI", "Enka İnşaat",                    58m,  true),
        ("MGROS", "Migros Ticaret",                520m,  true),
        ("HEKTS", "Hektaş",                          4m, false),   // inactive on purpose, for testing
    ];

    public static async Task SeedInstrumentsAsync(FinSimDbContext db, CancellationToken ct = default)
    {
        var existing = await db.Instruments
            .Select(i => i.Symbol!)
            .ToListAsync(ct);

        var missing = Instruments
            .Where(x => !existing.Contains(x.Symbol))
            .Select(x => new Instrument
            {
                Id           = Guid.NewGuid(),
                Symbol       = x.Symbol,
                Name         = x.Name,
                BasePrice    = x.Price,
                CurrentPrice = x.Price,
                IsActive     = x.Active
            })
            .ToList();

        if (missing.Count == 0) return;

        db.Instruments.AddRange(missing);
        await db.SaveChangesAsync(ct);
    }

    public static async Task SeedFundsAsync(FinSimDbContext db, CancellationToken ct = default)
    {
        var existingSymbols = await db.Instruments
            .Select(i => i.Symbol!)
            .ToListAsync(ct);

        var fundsToAdd = Funds.Where(f => !existingSymbols.Contains(f.Symbol)).ToList();
        if (fundsToAdd.Count == 0) return;

        var stocks = await db.Instruments
            .Where(i => i.IsActive && i.Type == InstrumentType.Stock)
            .ToDictionaryAsync(i => i.Symbol!, ct);

        var newFunds = new List<Instrument>();

        foreach (var f in fundsToAdd)
        {
            var constituents = new List<(Instrument Stock, int Quantity)>();
            var complete = true;

            foreach (var (symbol, quantity) in f.Basket)
            {
                if (!stocks.TryGetValue(symbol, out var stock))
                {
                    complete = false;
                    break;
                }
                constituents.Add((stock, quantity));
            }

            if (!complete) continue;

            var fund = new Instrument
            {
                Id           = Guid.NewGuid(),
                Symbol       = f.Symbol,
                Name         = f.Name,
                Type         = InstrumentType.Fund,
                BasePrice    = f.BasePrice,
                CurrentPrice = f.BasePrice,
                IsActive     = true
            };

            fund.Holdings = constituents.Select(c => new FundHolding
            {
                Id            = Guid.NewGuid(),
                FundId        = fund.Id,
                ConstituentId = c.Stock.Id,
                Quantity      = c.Quantity
            }).ToList();

            var nav = FundPricer.Nav(
                fund.Holdings, constituents.ToDictionary(c => c.Stock.Id, c => c.Stock.CurrentPrice));

            if (nav <= 0m) continue;

            fund.Divisor = FundPricer.DivisorFor(nav, fund.BasePrice);

            newFunds.Add(fund);
        }

        if (newFunds.Count == 0) return;

        db.Instruments.AddRange(newFunds);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Grants the Admin role to whichever account is registered under Seed:AdminEmail.
    /// Runs on every startup, so if that account doesn't exist yet (fresh database,
    /// admin hasn't registered), this is a no-op — it picks them up on a later restart
    /// rather than fabricating an account with no password.
    /// </summary>
    public static async Task SeedAdminAsync(
        RoleManager<IdentityRole<Guid>> roles,
        UserManager<User> users,
        IConfiguration config,
        CancellationToken ct = default)
    {
        if (!await roles.RoleExistsAsync(AdminRole))
        {
            await roles.CreateAsync(new IdentityRole<Guid>
            {
                Id = Guid.NewGuid(),
                Name = AdminRole,
                NormalizedName = AdminRole.ToUpperInvariant()
            });
        }

        var adminEmail = config["Seed:AdminEmail"];
        if (string.IsNullOrWhiteSpace(adminEmail)) return;

        var user = await users.FindByEmailAsync(adminEmail);
        if (user is null) return;

        if (!await users.IsInRoleAsync(user, AdminRole))
            await users.AddToRoleAsync(user, AdminRole);
    }
}