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
            }
        );
        }
    
    }
}