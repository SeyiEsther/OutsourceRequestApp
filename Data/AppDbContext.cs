using Microsoft.EntityFrameworkCore;
using OutsourceRequestApp.Models;

namespace OutsourceRequestApp.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options) { }

        public DbSet<OutsourceRequest> OutsourceRequests { get; set; }
        public DbSet<ApproverRole> ApproverRoles { get; set; }
        public DbSet<AppSetting> AppSettings { get; set; }
        public DbSet<OutsourceRequestCostLine> OutsourceRequestCostLines { get; set; }
        public DbSet<OutsourceRequestAuditLog> OutsourceRequestAuditLogs { get; set; }
    }
}
