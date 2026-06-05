using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OutsourceRequestApp.Data;
using OutsourceRequestApp.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace OutsourceRequestApp.ViewComponents
{
    public class NavContextViewModel
    {
        public bool   IsAdmin         { get; set; }
        public bool   IsApprover      { get; set; }
        public string ApproverRoleKey { get; set; } = "";
        public string ApproverLabel   { get; set; } = "";
        public int    PendingCount    { get; set; }
    }

    public class NavContextViewComponent : ViewComponent
    {
        private readonly AppDbContext _db;

        public NavContextViewComponent(AppDbContext db)
        {
            _db = db;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var currentUser = HttpContext.User?.Identity?.Name ?? "";

            var adminSetting = await _db.AppSettings
                .FirstOrDefaultAsync(s => s.SettingKey == "AdminUsers");

            var isAdmin = (adminSetting?.SettingValue ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Any(a => a.Trim().Equals(currentUser, StringComparison.OrdinalIgnoreCase));

            var approverRole = await _db.ApproverRoles
                .FirstOrDefaultAsync(r =>
                    r.Username != null &&
                    r.Username.ToLower() == currentUser.ToLower());

            int    pendingCount  = 0;
            string approverLabel = "";

            if (approverRole != null)
            {
                approverLabel = approverRole.RoleKey switch
                {
                    "WorkPrepManager"    => "Work Prep Manager",
                    "ProductionManager"  => "Production Manager",
                    "SupplyChainManager" => "Supply Chain Manager",
                    "ManagingDirector"   => "Managing Director",
                    // Legacy keys
                    "SC"      => "Supply Chain",
                    "Finance" => "Finance",
                    "MD"      => "Managing Director",
                    _         => approverRole.RoleDisplayName
                };

                if (approverRole.RoleKey == "SupplyChainManager")
                {
                    pendingCount = await _db.OutsourceRequests
                        .CountAsync(r => r.Status == RequestStatus.AwaitingCostImpact ||
                                         r.Status == RequestStatus.FinancePending);
                }
                else
                {
                    var statusFilter = approverRole.RoleKey switch
                    {
                        "WorkPrepManager"   => RequestStatus.Submitted,
                        "ProductionManager" => RequestStatus.AwaitingLJApproval,
                        "ManagingDirector"  => RequestStatus.MdPending,
                        "SC"      => RequestStatus.Submitted,
                        "Finance" => RequestStatus.FinancePending,
                        "MD"      => RequestStatus.MdPending,
                        _         => ""
                    };

                    if (!string.IsNullOrEmpty(statusFilter))
                        pendingCount = await _db.OutsourceRequests
                            .CountAsync(r => r.Status == statusFilter);
                }
            }

            return View(new NavContextViewModel
            {
                IsAdmin         = isAdmin,
                IsApprover      = approverRole != null,
                ApproverRoleKey = approverRole?.RoleKey ?? "",
                ApproverLabel   = approverLabel,
                PendingCount    = pendingCount
            });
        }
    }
}
