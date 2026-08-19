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

        public DbSet<Instrument> Instruments => Set<Instrument>();
        public DbSet<PortfolioItem> PortfolioItems => Set<PortfolioItem>();
        public DbSet<Order> Orders => Set<Order>();
        public DbSet<Transaction> Transactions => Set<Transaction>();
        public DbSet<AdminAdjustment> AdminAdjustments => Set<AdminAdjustment>();

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