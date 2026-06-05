using System.Threading.Tasks;

namespace OutsourceRequestApp.Services
{
    public interface IAuditLogService
    {
        Task LogAsync(int requestId, string actor, string action,
                      string? fromStatus = null, string? toStatus = null, string? notes = null);
    }
}
