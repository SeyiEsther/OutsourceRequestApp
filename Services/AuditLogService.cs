using System;
using System.Threading.Tasks;
using OutsourceRequestApp.Data;
using OutsourceRequestApp.Models;

namespace OutsourceRequestApp.Services
{
    public class AuditLogService : IAuditLogService
    {
        private readonly AppDbContext _db;

        public AuditLogService(AppDbContext db)
        {
            _db = db;
        }

        public async Task LogAsync(int requestId, string actor, string action,
                                   string? fromStatus = null, string? toStatus = null, string? notes = null)
        {
            _db.OutsourceRequestAuditLogs.Add(new OutsourceRequestAuditLog
            {
                RequestId  = requestId,
                Timestamp  = DateTime.Now,
                Actor      = actor,
                Action     = action,
                FromStatus = fromStatus,
                ToStatus   = toStatus,
                Notes      = notes
            });
            await _db.SaveChangesAsync();
        }
    }
}
