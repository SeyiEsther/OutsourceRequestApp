using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OutsourceRequestApp.Data;
using OutsourceRequestApp.Models;
using OutsourceRequestApp.Services;
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
        private readonly AccessControlService _access;

        public NavContextViewComponent(AppDbContext db, AccessControlService access)
        {
            _db     = db;
            _access = access;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var isAdmin      = await _access.IsAdminAsync();
            var approverRole = await _access.GetMyApproverRoleAsync();

            int    pendingCount  = 0;
            string approverLabel = "";

            if (approverRole != null)
            {
                // Shorter labels than the dashboard's full job titles — this is a
                // narrow sidebar, not the Home page.
                approverLabel = approverRole.RoleKey switch
                {
                    "WP"       => "Work Preparation",
                    "PROD"     => "Production",
                    "BUYER"    => "Strategic Buyer",
                    "SOURCING" => "Sourcing",
                    "MD"       => "Managing Director",
                    _          => approverRole.RoleDisplayName
                };

                var statusFilter = RequestStatus.PendingStatusForRole(approverRole.RoleKey);

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
