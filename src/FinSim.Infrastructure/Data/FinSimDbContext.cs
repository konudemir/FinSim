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

        public DbSet<Instrument> Instruments => Set<Instrument>();
        public DbSet<PortfolioItem> PortfolioItems => Set<PortfolioItem>();
        public DbSet<Order> Orders => Set<Order>();
        public DbSet<Transaction> Transactions => Set<Transaction>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>(e =>
            {
                e.Property(u => u.FreeCashBalance).HasPrecision(18, 2);
                e.Property(u => u.LockedCashBalance).HasPrecision(18, 2);
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
                e.Property(o => o.LockedAmount).HasPrecision(18, 2);

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

            
        }
    }
}