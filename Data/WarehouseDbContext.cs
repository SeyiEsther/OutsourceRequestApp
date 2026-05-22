using Microsoft.EntityFrameworkCore;
using OutsourceRequestApp.Models;

namespace OutsourceRequestApp.Data
{
    public class WarehouseDbContext : DbContext
    {
        public WarehouseDbContext(DbContextOptions<WarehouseDbContext> options)
            : base(options)
        {
        }

        public DbSet<Article> Articles { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Article>()
                .ToTable("zmm_myart01", "dbo")
                .HasNoKey();
        }
    }
}