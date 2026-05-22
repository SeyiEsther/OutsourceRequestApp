using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OutsourceRequestApp.Data;
using OutsourceRequestApp.Models;

namespace OutsourceRequestApp.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly AppDbContext            _db;

        public HomeController(ILogger<HomeController> logger, AppDbContext db)
        {
            _logger = logger;
            _db     = db;
        }

        public async Task<IActionResult> Index()
        {
            var currentUser = User?.Identity?.Name ?? "";

            // ── Admin check ──────────────────────────────────────────────
            var adminSetting = await _db.AppSettings
                .FirstOrDefaultAsync(s => s.SettingKey == "AdminUsers");

            ViewBag.IsAdmin = (adminSetting?.SettingValue ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Any(a => a.Trim().Equals(currentUser, StringComparison.OrdinalIgnoreCase));

            // ── Approver role check ──────────────────────────────────────
            var approverRole = await _db.ApproverRoles
                .FirstOrDefaultAsync(r =>
                    r.Username != null &&
                    r.Username.ToLower() == currentUser.ToLower());

            ViewBag.IsApprover      = approverRole != null;
            ViewBag.ApproverRoleKey = approverRole?.RoleKey ?? "";
            ViewBag.ApproverLabel   = approverRole?.RoleKey switch
            {
                "SC"      => "Supply Chain",
                "Finance" => "Finance Director",
                "MD"      => "Managing Director",
                _         => approverRole?.RoleDisplayName ?? ""
            };

            // Pending count for this approver
            if (approverRole != null)
            {
                var statusFilter = approverRole.RoleKey switch
                {
                    "SC"      => RequestStatus.Submitted,
                    "Finance" => RequestStatus.FinancePending,
                    "MD"      => RequestStatus.MdPending,
                    _         => ""
                };
                ViewBag.PendingCount = string.IsNullOrEmpty(statusFilter)
                    ? 0
                    : await _db.OutsourceRequests.CountAsync(r => r.Status == statusFilter);
            }
            else
            {
                ViewBag.PendingCount = 0;
            }

            // My requests count for non-approvers
            ViewBag.MyRequestsCount = await _db.OutsourceRequests
                .CountAsync(r => r.CreatedByUsername != null &&
                                 r.CreatedByUsername.ToLower() == currentUser.ToLower());

            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
