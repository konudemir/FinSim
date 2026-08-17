using FinSim.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace FinSim.Infrastructure.Data;

public static class DbSeeder
{
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
}