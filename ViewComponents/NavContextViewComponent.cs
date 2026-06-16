using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OutsourceRequestApp.Data;
using OutsourceRequestApp.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace OutsourceRequestApp.ViewComponents
{
    /// <summary>
    /// Resolved per-request: tells the layout which nav links to show and
    /// how many items are currently awaiting the current user's approval.
    /// </summary>
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

            // ── Admin check ──────────────────────────────────────────────
            var adminSetting = await _db.AppSettings
                .FirstOrDefaultAsync(s => s.SettingKey == "AdminUsers");

            var isAdmin = (adminSetting?.SettingValue ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Any(a => a.Trim().Equals(currentUser, StringComparison.OrdinalIgnoreCase));

            // ── Approver role check ──────────────────────────────────────
            var approverRole = await _db.ApproverRoles
                .FirstOrDefaultAsync(r =>
                    r.Email != null &&
                    r.Email.ToLower() == currentUser.ToLower());

            int    pendingCount  = 0;
            string approverLabel = "";

            if (approverRole != null)
            {
                approverLabel = approverRole.RoleKey switch
                {
                    "SC"      => "Supply Chain",
                    "Finance" => "Finance",
                    "MD"      => "Managing Director",
                    _         => approverRole.RoleDisplayName
                };

                var statusFilter = approverRole.RoleKey switch
                {
                    "SC"      => RequestStatus.Submitted,
                    "Finance" => RequestStatus.FinancePending,
                    "MD"      => RequestStatus.MdPending,
                    _         => ""
                };

                if (!string.IsNullOrEmpty(statusFilter))
                    pendingCount = await _db.OutsourceRequests
                        .CountAsync(r => r.Status == statusFilter);
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
