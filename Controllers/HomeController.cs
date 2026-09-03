using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OutsourceRequestApp.Data;
using OutsourceRequestApp.Models;
using OutsourceRequestApp.Services;

namespace OutsourceRequestApp.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly AppDbContext            _db;
        private readonly AccessControlService    _access;

        public HomeController(ILogger<HomeController> logger, AppDbContext db, AccessControlService access)
        {
            _logger = logger;
            _db     = db;
            _access = access;
        }

        public async Task<IActionResult> Index()
        {
            var currentUser = User?.Identity?.Name ?? "";

            // ── Admin check ──────────────────────────────────────────────
            ViewBag.IsAdmin = await _access.IsAdminAsync();

            // ── Approver role check ──────────────────────────────────────
            var approverRole = await _access.GetMyApproverRoleAsync();

            ViewBag.IsApprover      = approverRole != null;
            ViewBag.ApproverRoleKey = approverRole?.RoleKey ?? "";
            ViewBag.ApproverLabel   = approverRole != null
                ? (RequestStatus.RoleLabel(approverRole.RoleKey) is { Length: > 0 } label ? label : approverRole.RoleDisplayName)
                : "";

            // Pending count + live queue preview for this approver
            if (approverRole != null)
            {
                var statusFilter = RequestStatus.PendingStatusForRole(approverRole.RoleKey);

                if (!string.IsNullOrEmpty(statusFilter))
                {
                    ViewBag.PendingCount = await _db.OutsourceRequests.CountAsync(r => r.Status == statusFilter);
                    ViewBag.QueuePreview = await _db.OutsourceRequests
                        .Where(r => r.Status == statusFilter)
                        .OrderBy(r => r.CreatedAt)
                        .Take(5)
                        .ToListAsync();
                }
                else
                {
                    ViewBag.PendingCount = 0;
                    ViewBag.QueuePreview = new System.Collections.Generic.List<OutsourceRequest>();
                }
            }
            else
            {
                ViewBag.PendingCount = 0;
                ViewBag.QueuePreview = new System.Collections.Generic.List<OutsourceRequest>();
            }

            // My requests count + status breakdown for non-approvers
            var myRequests = await _db.OutsourceRequests
                .Where(r => r.CreatedByUsername != null &&
                            r.CreatedByUsername.ToLower() == currentUser.ToLower())
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            ViewBag.MyRequestsCount = myRequests.Count;
            ViewBag.MySubmitted = myRequests.Count(r => r.Status == RequestStatus.Submitted);
            ViewBag.MyReview    = myRequests.Count(r => r.Status == RequestStatus.ProductionPending || r.Status == RequestStatus.CostCompactPending ||
                                                          r.Status == RequestStatus.SourcingPending   || r.Status == RequestStatus.MdPending);
            ViewBag.MyApproved  = myRequests.Count(r => r.Status == RequestStatus.Approved);
            ViewBag.MyRejected  = myRequests.Count(r => r.Status == RequestStatus.Rejected || r.Status == RequestStatus.Cancelled);

            // Recent requests for the dashboard table
            ViewBag.RecentRequests = myRequests.Take(3).ToList();

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
