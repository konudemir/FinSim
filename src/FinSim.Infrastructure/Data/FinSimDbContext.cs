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
        public DbSet<PortfolioSnapshot> PortfolioSnapshots => Set<PortfolioSnapshot>();
        public DbSet<FavoriteInstrument> FavoriteInstruments => Set<FavoriteInstrument>();

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
            modelBuilder.Entity<PortfolioSnapshot>(e =>
            {
                e.Property(s => s.PortfolioValue).HasPrecision(18, 2);
                e.Property(s => s.CashTotal).HasPrecision(18, 2);
                e.Property(s => s.LongValue).HasPrecision(18, 2);
                e.Property(s => s.ShortUnrealized).HasPrecision(18, 2);
                e.Property(s => s.RealizedPnL).HasPrecision(18, 2);
                e.Property(s => s.NetDeposits).HasPrecision(18, 2);

                // Makes the daily capture idempotent at the database level, so a
                // restart or a second instance can't write the same day twice.
                e.HasIndex(s => new { s.UserId, s.Date }).IsUnique();

                e.HasOne(s => s.User)
                 .WithMany()
                 .HasForeignKey(s => s.UserId)
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
                e.Property(u => u.NetDeposits).HasPrecision(18, 2);
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
                e.HasIndex(i => i.Symbol).IsUnique();
                e.HasIndex(i => new { i.CurrentPrice, i.Id });
                e.Property(i => i.BasePrice).HasPrecision(18, 2);
                e.Property(i => i.CurrentPrice).HasPrecision(18, 2);
                e.Property(i => i.LastRealPrice).HasPrecision(18, 4);
                e.Property(i => i.RealSymbol).HasMaxLength(20);
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

                // Backs the portfolio board's "WHERE InstrumentId IN (subquery on
                // UserId)" keyset query the same way the FavoriteInstrument index
                // backs the favorites board.
                e.HasIndex(p => new { p.UserId, p.InstrumentId });
            });

            modelBuilder.Entity<Order>(e =>
            {
                e.Property(o => o.Price).HasPrecision(18, 2);
                e.Property(o => o.StopPrice).HasPrecision(18, 2);
                e.HasIndex(o => new { o.Status, o.ExpiresAt });

                e.Property(o => o.OrderType).HasConversion<string>().HasMaxLength(20);
                e.Property(o => o.Direction).HasConversion<string>().HasMaxLength(20);
                e.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
                e.Property(o => o.LockedAmount).HasPrecision(18, 2);

                e.HasIndex(o => o.Status);
                e.HasIndex(o => new { o.UserId, o.CreatedAt, o.Id });
            });

            modelBuilder.Entity<Transaction>(e =>
            {
                e.Property(t => t.ExecutedPrice).HasPrecision(18, 2);
                e.Property(t => t.TotalAmount).HasPrecision(18, 2);
                e.Property(t => t.BuyerRealizedPnL).HasPrecision(18, 2);
                e.Property(t => t.SellerRealizedPnL).HasPrecision(18, 2);

                e.HasIndex(t => new { t.InstrumentId, t.TransactionDate });
                e.HasIndex(t => t.BuyerUserId);
                e.HasIndex(t => t.SellerUserId);

                e.HasIndex(t => new { t.BuyerUserId, t.TransactionDate, t.Id });
                e.HasIndex(t => new { t.SellerUserId, t.TransactionDate, t.Id });

                e.HasOne(t => t.BuyerOrder)
                 .WithMany()
                 .HasForeignKey(t => t.BuyerOrderId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(t => t.SellerOrder)
                 .WithMany()
                 .HasForeignKey(t => t.SellerOrderId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(t => t.Buyer)
                 .WithMany()
                 .HasForeignKey(t => t.BuyerUserId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(t => t.Seller)
                 .WithMany()
                 .HasForeignKey(t => t.SellerUserId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<FavoriteInstrument>(e =>
            {
                e.HasIndex(f => new { f.UserId, f.InstrumentId }).IsUnique();

                e.HasOne(f => f.User)
                 .WithMany()
                 .HasForeignKey(f => f.UserId)
                 .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(f => f.Instrument)
                 .WithMany()
                 .HasForeignKey(f => f.InstrumentId)
                 .OnDelete(DeleteBehavior.Cascade);
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