using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FinSim.Models;

namespace FinSim.Data
{
    public class FinSimDbContext : DbContext
    {
        public FinSimDbContext(DbContextOptions<FinSimDbContext> options)
        : base(options)
        {
        }
        public DbSet<User> Users => Set<User>();
        public DbSet<Instrument> Instruments => Set<Instrument>();
        public DbSet<PortfolioItem> PortfolioItems => Set<PortfolioItem>();
        public DbSet<Order> Orders => Set<Order>();
        public DbSet<Transaction> Transactions => Set<Transaction>();
        public DbSet<PriceHistory> PriceHistories => Set<PriceHistory>();


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>(e =>
            {
                e.Property(u => u.FreeCashBalance).HasPrecision(18, 2);
                e.Property(u => u.lockedCashBalance).HasPrecision(18, 2);
            });

            modelBuilder.Entity<Instrument>(e =>
            {
                e.Property(i => i.BasePrice).HasPrecision(18, 2);
                e.Property(i => i.CurrentPrice).HasPrecision(18, 2);
            });

            modelBuilder.Entity<PortfolioItem>(e =>
            {
                e.Property(p => p.AverageCost).HasPrecision(18, 4); // division
            });

            modelBuilder.Entity<Order>(e =>
            {
                e.Property(o => o.Price).HasPrecision(18, 2);

                e.Property(o => o.OrderType).HasConversion<string>().HasMaxLength(20);
                e.Property(o => o.Direction).HasConversion<string>().HasMaxLength(20);
                e.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);

                e.HasIndex(o => o.Status);
            });

            modelBuilder.Entity<Transaction>(e =>
            {
                e.Property(t => t.ExecutedPrice).HasPrecision(18, 2);
                e.Property(t => t.TotalAmount).HasPrecision(18, 2);

                e.HasOne(t => t.Order)
                .WithMany(o => o.Transactions)
                .HasForeignKey(t => t.OrderId)
                .OnDelete(DeleteBehavior.Restrict);
                
            });

            modelBuilder.Entity<PriceHistory>()
                .HasIndex(p => new { p.InstrumentId, p.RecordedAt });
            modelBuilder.Entity<Instrument>().HasData(
            new Instrument
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Symbol = "THYAO",
                Name = "Türk Hava Yolları",
                BasePrice = 100m,
                CurrentPrice = 100m,
                IsActive = true
            },
            new Instrument
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Symbol = "ASELS",
                Name = "Aselsan",
                BasePrice = 40m,
                CurrentPrice = 40m,
                IsActive = true
            },
            new Instrument
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Symbol = "GARAN",
                Name = "Garanti BBVA",
                BasePrice = 110m,
                CurrentPrice = 110m,
                IsActive = true
            },
            new Instrument
            {
                Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                Symbol = "TUPRS",
                Name = "Tüpraş",
                BasePrice = 155m,
                CurrentPrice = 155m,
                IsActive = true
            },
            new Instrument
            {
                Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
                Symbol = "AKBNK",
                Name = "Akbank",
                BasePrice = 45m,
                CurrentPrice = 45m,
                IsActive = true
            },
            new Instrument
            {
                Id = Guid.Parse("66666666-6666-6666-6666-666666666666"),
                Symbol = "EREGL",
                Name = "Erdemir Ereğli Demir Çelik",
                BasePrice = 50m,
                CurrentPrice = 50m,
                IsActive = true
            },
            new Instrument
            {
                Id = Guid.Parse("77777777-7777-7777-7777-777777777777"),
                Symbol = "BIMAS",
                Name = "BİM Birleşik Mağazalar",
                BasePrice = 380m,
                CurrentPrice = 380m,
                IsActive = true
            },
            new Instrument
            {
                Id = Guid.Parse("88888888-8888-8888-8888-888888888888"),
                Symbol = "SISE",
                Name = "Şişecam",
                BasePrice = 55m,
                CurrentPrice = 55m,
                IsActive = true
            },
            new Instrument
            {
                Id = Guid.Parse("99999999-9999-9999-9999-999999999999"),
                Symbol = "KCHOL",
                Name = "Koç Holding",
                BasePrice = 190m,
                CurrentPrice = 190m,
                IsActive = true
            },
            new Instrument
            {
                Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Symbol = "SAHOL",
                Name = "Sabancı Holding",
                BasePrice = 95m,
                CurrentPrice = 95m,
                IsActive = true
            },
            new Instrument
            {
                Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                Symbol = "FROTO",
                Name = "Ford Otosan",
                BasePrice = 1100m,
                CurrentPrice = 1100m,
                IsActive = true
            },
            new Instrument
            {
                Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                Symbol = "YKBNK",
                Name = "Yapı Kredi Bankası",
                BasePrice = 25m,
                CurrentPrice = 25m,
                IsActive = true
            },
            new Instrument
            {
                Id = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                Symbol = "HEKTS",
                Name = "Hektaş",
                BasePrice = 18m,
                CurrentPrice = 18m,
                IsActive = false // Added an inactive one for testing scenarios
            }
        );
        }
    
    }
}