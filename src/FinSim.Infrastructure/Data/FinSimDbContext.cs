using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using FinSim.Domain.Models;

namespace FinSim.Infrastructure.Data
{
    public class FinSimDbContext : IdentityDbContext<User, IdentityRole<Guid>, Guid>
    {
        public FinSimDbContext(DbContextOptions<FinSimDbContext> options)
        : base(options)
        {
        }
        public DbSet<PriceHistory> PriceHistory => Set<PriceHistory>();
        public DbSet<FundRebalance> FundRebalances => Set<FundRebalance>();
        public DbSet<FundRebalanceLine> FundRebalanceLines => Set<FundRebalanceLine>();

        public DbSet<Instrument> Instruments => Set<Instrument>();
        public DbSet<PortfolioItem> PortfolioItems => Set<PortfolioItem>();
        public DbSet<Order> Orders => Set<Order>();
        public DbSet<Transaction> Transactions => Set<Transaction>();
        public DbSet<AdminAdjustment> AdminAdjustments => Set<AdminAdjustment>();
        public DbSet<FundHolding> FundHoldings => Set<FundHolding>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<PriceHistory>(e =>
            {
                e.Property(p => p.Price).HasPrecision(18, 2);

                e.HasIndex(p => new { p.InstrumentId, p.Timestamp });

                e.HasOne(p => p.Instrument)
                .WithMany()
                .HasForeignKey(p => p.InstrumentId)
                .OnDelete(DeleteBehavior.Cascade);
            });
            modelBuilder.Entity<FundRebalance>(e =>
            {
                e.Property(r => r.NavBefore).HasPrecision(18, 2);
                e.Property(r => r.NavAfter).HasPrecision(18, 2);
                e.Property(r => r.DivisorBefore).HasPrecision(18, 6);
                e.Property(r => r.DivisorAfter).HasPrecision(18, 6);
                e.Property(r => r.PriceAtRebalance).HasPrecision(18, 2);

                e.HasIndex(r => new { r.FundId, r.CreatedAt });

                e.HasOne(r => r.Fund)
                 .WithMany()
                 .HasForeignKey(r => r.FundId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(r => r.AdminUser)
                 .WithMany()
                 .HasForeignKey(r => r.AdminUserId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.HasMany(r => r.Lines)
                 .WithOne(l => l.FundRebalance)
                 .HasForeignKey(l => l.FundRebalanceId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<FundRebalanceLine>(e =>
            {
                e.HasOne(l => l.Constituent)
                 .WithMany()
                 .HasForeignKey(l => l.ConstituentId)
                 .OnDelete(DeleteBehavior.Restrict);
            });
            modelBuilder.Entity<User>(e =>
            {
                e.Property(u => u.FreeCashBalance).HasPrecision(18, 2);
                e.Property(u => u.LockedCashBalance).HasPrecision(18, 2);
                e.Property(u => u.RealizedProfitLoss).HasPrecision(18, 2);
                e.Property(u => u.MarginUsed).HasPrecision(18, 2);
                e.Property<uint>("Version")
                 .IsRowVersion()
                 .HasColumnName("xmin");
            });

            modelBuilder.Entity<Instrument>(e =>
            {
                e.Property(i => i.BasePrice).HasPrecision(18, 2);
                e.Property(i => i.CurrentPrice).HasPrecision(18, 2);

                e.Property(i => i.Type).HasConversion<string>().HasMaxLength(20);
                e.Property(i => i.Divisor).HasPrecision(18, 6);

                e.HasMany(i => i.Holdings)
                 .WithOne(h => h.Fund)
                 .HasForeignKey(h => h.FundId)
                 .OnDelete(DeleteBehavior.Cascade);
            });
            modelBuilder.Entity<FundHolding>(e =>
            {
                e.HasIndex(h => new { h.FundId, h.ConstituentId }).IsUnique();

                e.HasOne(h => h.Constituent)
                 .WithMany()
                 .HasForeignKey(h => h.ConstituentId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<PortfolioItem>(e =>
            {
                e.Property(p => p.AverageCost).HasPrecision(18, 4); // division
            });

            modelBuilder.Entity<Order>(e =>
            {
                e.Property(o => o.Price).HasPrecision(18, 2);
                e.Property(o => o.StopPrice).HasPrecision(18, 2);

                e.Property(o => o.OrderType).HasConversion<string>().HasMaxLength(20);
                e.Property(o => o.Direction).HasConversion<string>().HasMaxLength(20);
                e.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
                e.Property(o => o.LockedAmount).HasPrecision(18, 2);

                e.HasIndex(o => o.Status);
            });

            modelBuilder.Entity<Transaction>(e =>
            {
                e.Property(t => t.ExecutedPrice).HasPrecision(18, 2);
                e.Property(t => t.TotalAmount).HasPrecision(18, 2);
                e.HasIndex(t => t.OrderId);
                e.Property(t => t.RealizedPnL).HasPrecision(18, 2);

                e.HasOne(t => t.Order)
                 .WithMany(o => o.Transactions)
                 .HasForeignKey(t => t.OrderId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<AdminAdjustment>(e =>
            {
                e.Property(a => a.CashDelta).HasPrecision(18, 2);
                e.Property(a => a.Price).HasPrecision(18, 2);
                e.Property(a => a.Type).HasConversion<string>().HasMaxLength(20);

                e.HasOne(a => a.AdminUser)
                 .WithMany()
                 .HasForeignKey(a => a.AdminUserId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(a => a.TargetUser)
                 .WithMany()
                 .HasForeignKey(a => a.TargetUserId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(a => a.Instrument)
                 .WithMany()
                 .HasForeignKey(a => a.InstrumentId)
                 .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}