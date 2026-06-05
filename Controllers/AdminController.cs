using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OutsourceRequestApp.Data;
using OutsourceRequestApp.Models;
using OutsourceRequestApp.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OutsourceRequestApp.Controllers
{
    public class AdminController : Controller
    {
        private readonly AppDbContext _db;
        private readonly EmailService _email;
        private readonly IAuditLogService _audit;

        public AdminController(AppDbContext db, EmailService email, IAuditLogService audit)
        {
            _db    = db;
            _email = email;
            _audit = audit;
        }

        // ----------------------------------------------------------------
        // Auth helper
        // ----------------------------------------------------------------

        private async Task<bool> IsAdminAsync()
        {
            var setting = await _db.AppSettings
                .FirstOrDefaultAsync(s => s.SettingKey == "AdminUsers");
            var admins      = setting?.SettingValue ?? "";
            var currentUser = User.Identity?.Name ?? "";

            return admins.Split(',', StringSplitOptions.RemoveEmptyEntries)
                         .Any(a => a.Trim().Equals(currentUser, StringComparison.OrdinalIgnoreCase));
        }

        // ----------------------------------------------------------------
        // GET: /Admin
        // ----------------------------------------------------------------
        public async Task<IActionResult> Index()
        {
            if (!await IsAdminAsync())
                return StatusCode(403, "Access denied. You must be an admin to view this page.");

            var roles    = await _db.ApproverRoles.ToListAsync();
            var settings = await _db.AppSettings.ToListAsync();

            ViewBag.Roles    = roles;
            ViewBag.Settings = settings;

            string Get(string key, string fallback = "") =>
                settings.FirstOrDefault(s => s.SettingKey == key)?.SettingValue ?? fallback;

            ViewBag.SmtpHost      = Get("SmtpHost");
            ViewBag.SmtpPort      = Get("SmtpPort", "25");
            ViewBag.SmtpFrom      = Get("SmtpFrom");
            ViewBag.SmtpFromName  = Get("SmtpFromName", "Outsource Portal");
            ViewBag.EmailDomain   = Get("CompanyEmailDomain");
            ViewBag.ReminderHours = Get("ReminderHours", "24");
            ViewBag.AdminUsers       = Get("AdminUsers");
            ViewBag.CostImpactEmail  = Get("CostImpactEmail");

            return View();
        }

        // ----------------------------------------------------------------
        // POST: /Admin/SaveRole
        // ----------------------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveRole(string roleKey, string roleDisplayName,
                                                   string username, string fullName, string email)
        {
            if (!await IsAdminAsync())
                return StatusCode(403, "Access denied.");

            var role = await _db.ApproverRoles.FirstOrDefaultAsync(r => r.RoleKey == roleKey);
            if (role == null)
            {
                role = new ApproverRole { RoleKey = roleKey };
                _db.ApproverRoles.Add(role);
            }

            role.RoleDisplayName = roleDisplayName;
            role.Username        = username;
            role.FullName        = fullName;
            role.Email           = email;

            await _db.SaveChangesAsync();

            TempData["Success"] = $"{roleDisplayName} approver saved.";
            return RedirectToAction(nameof(Index));
        }

        // ----------------------------------------------------------------
        // POST: /Admin/SaveSettings
        // ----------------------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveSettings(string smtpHost, string smtpPort,
                                                       string smtpFrom, string smtpFromName,
                                                       string emailDomain, string reminderHours)
        {
            if (!await IsAdminAsync())
                return StatusCode(403, "Access denied.");

            async Task Set(string key, string value)
            {
                var s = await _db.AppSettings.FirstOrDefaultAsync(x => x.SettingKey == key);
                if (s == null) { s = new AppSetting { SettingKey = key }; _db.AppSettings.Add(s); }
                s.SettingValue = value ?? "";
            }

            await Set("SmtpHost",           smtpHost);
            await Set("SmtpPort",           smtpPort);
            await Set("SmtpFrom",           smtpFrom);
            await Set("SmtpFromName",       smtpFromName);
            await Set("CompanyEmailDomain", emailDomain);
            await Set("ReminderHours",      reminderHours);

            await _db.SaveChangesAsync();

            TempData["Success"] = "Email settings saved.";
            return RedirectToAction(nameof(Index));
        }

        // ----------------------------------------------------------------
        // POST: /Admin/SaveAdmins
        // ----------------------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveAdmins(string adminUsers)
        {
            if (!await IsAdminAsync())
                return StatusCode(403, "Access denied.");

            var s = await _db.AppSettings.FirstOrDefaultAsync(x => x.SettingKey == "AdminUsers");
            if (s == null) { s = new AppSetting { SettingKey = "AdminUsers" }; _db.AppSettings.Add(s); }
            s.SettingValue = adminUsers ?? "";

            await _db.SaveChangesAsync();

            TempData["Success"] = "Admin users saved.";
            return RedirectToAction(nameof(Index));
        }

        // ----------------------------------------------------------------
        // POST: /Admin/TestEmail
        // ----------------------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TestEmail()
        {
            if (!await IsAdminAsync())
                return StatusCode(403, "Access denied.");

            var currentUser = User.Identity?.Name ?? "";
            var emailDomain = (await _db.AppSettings
                .FirstOrDefaultAsync(s => s.SettingKey == "CompanyEmailDomain"))?.SettingValue
                ?? "company.com";

            var usernameOnly = currentUser.Contains('\\')
                ? currentUser.Split('\\')[1]
                : currentUser;

            var fakeReq = new OutsourceRequest
            {
                RequestId     = 0,
                PartNumber    = "TEST-001",
                SapDescription = "Test Part",
                Quantity      = "1",
                CreatedAt     = DateTime.Now,
                Reason        = "This is a test email from the Outsource Portal."
            };

            var fakeApprover = new ApproverRole
            {
                FullName = currentUser,
                Email    = $"{usernameOnly}@{emailDomain}"
            };

            _ = Task.Run(() => _email.SendToApprover(fakeReq, fakeApprover));

            TempData["Success"] = $"Test email sent to {fakeApprover.Email}";
            return RedirectToAction(nameof(Index));
        }

        // ----------------------------------------------------------------
        // POST: /Admin/SaveCostImpactEmail
        // ----------------------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveCostImpactEmail(string costImpactEmail)
        {
            if (!await IsAdminAsync()) return StatusCode(403, "Access denied.");

            var s = await _db.AppSettings.FirstOrDefaultAsync(x => x.SettingKey == "CostImpactEmail");
            if (s == null) { s = new AppSetting { SettingKey = "CostImpactEmail" }; _db.AppSettings.Add(s); }
            s.SettingValue = costImpactEmail ?? "";
            await _db.SaveChangesAsync();

            TempData["Success"] = "Cost impact email address saved.";
            return RedirectToAction(nameof(Index));
        }

        // ----------------------------------------------------------------
        // GET: /Admin/AuditLog?requestId=...
        // ----------------------------------------------------------------
        public async Task<IActionResult> AuditLog(int? requestId)
        {
            if (!await IsAdminAsync()) return StatusCode(403, "Access denied.");

            var query = _db.OutsourceRequestAuditLogs.AsQueryable();
            if (requestId.HasValue)
                query = query.Where(l => l.RequestId == requestId.Value);

            var logs = await query
                .OrderByDescending(l => l.Timestamp)
                .Take(500)
                .ToListAsync();

            ViewBag.RequestId = requestId;
            return View(logs);
        }

        // ----------------------------------------------------------------
        // POST: /Admin/ResetStatus
        // ----------------------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetStatus(int requestId, string newStatus)
        {
            if (!await IsAdminAsync()) return StatusCode(403, "Access denied.");

            var req = await _db.OutsourceRequests.FirstOrDefaultAsync(r => r.RequestId == requestId);
            if (req == null)
            {
                TempData["Error"] = $"Request {requestId} not found.";
                return RedirectToAction(nameof(Index));
            }

            var from = req.Status;
            req.Status = newStatus;
            await _db.SaveChangesAsync();

            await _audit.LogAsync(requestId, User.Identity?.Name ?? "Admin",
                "Admin status reset", from, newStatus);

            TempData["Success"] = $"OSR-{requestId:000000} status changed from {from} to {newStatus}.";
            return RedirectToAction(nameof(Index));
        }

        // ----------------------------------------------------------------
        // GET: /Admin/Seed?username=DOMAIN\you
        // One-time bootstrap: only works if no admin has been set yet.
        // After first admin is configured via the Admin panel, this is inert.
        // ----------------------------------------------------------------
        public async Task<IActionResult> Seed(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return Content("Usage: /Admin/Seed?username=DOMAIN\\yourusername");

            var existing = await _db.AppSettings
                .FirstOrDefaultAsync(s => s.SettingKey == "AdminUsers");

            if (existing != null && !string.IsNullOrEmpty(existing.SettingValue))
                return Content("Admin already configured. Use the Admin panel to manage admin users.");

            if (existing == null)
                _db.AppSettings.Add(new AppSetting { SettingKey = "AdminUsers", SettingValue = username });
            else
                existing.SettingValue = username;

            await _db.SaveChangesAsync();
            return Content($"Admin seeded: {username}. You can now access /Admin");
        }
    }
}
