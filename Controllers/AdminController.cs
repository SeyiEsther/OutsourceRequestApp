using Microsoft.AspNetCore.Mvc;
using OutsourceRequestApp.Data;
using OutsourceRequestApp.Models;
using OutsourceRequestApp.Services;

namespace OutsourceRequestApp.Controllers
{
    public class AdminController : Controller
    {
        private readonly AppDbContext _db;
        private readonly EmailService _email;

        public AdminController(AppDbContext db, EmailService email)
        {
            _db = db;
            _email = email;
        }

        // Check if current user is an admin
        private bool IsAdmin()
        {
            var admins = _db.AppSettings
                .FirstOrDefault(s => s.SettingKey == "AdminUsers")?.SettingValue ?? "";
            var currentUser = User.Identity?.Name ?? "";
            return admins.Split(',', StringSplitOptions.RemoveEmptyEntries)
                         .Any(a => a.Trim().Equals(currentUser, StringComparison.OrdinalIgnoreCase));
        }

        // GET: /Admin
        public IActionResult Index()
        {
            if (!IsAdmin()) return StatusCode(403, "Access denied. You must be an admin to view this page.");

            var roles = _db.ApproverRoles.ToList();
            var settings = _db.AppSettings.ToList();

            ViewBag.Roles = roles;
            ViewBag.Settings = settings;

            string Get(string key, string fallback = "") =>
                settings.FirstOrDefault(s => s.SettingKey == key)?.SettingValue ?? fallback;

            ViewBag.SmtpHost = Get("SmtpHost");
            ViewBag.SmtpPort = Get("SmtpPort", "25");
            ViewBag.SmtpFrom = Get("SmtpFrom");
            ViewBag.SmtpFromName = Get("SmtpFromName", "Outsource Portal");
            ViewBag.EmailDomain = Get("CompanyEmailDomain");
            ViewBag.ReminderHours = Get("ReminderHours", "24");
            ViewBag.AdminUsers = Get("AdminUsers");

            return View();
        }

        // POST: /Admin/SaveRole
        [HttpPost]
        public IActionResult SaveRole(string roleKey, string roleDisplayName, string username, string fullName, string email)
        {
            if (!IsAdmin()) return StatusCode(403, "Access denied. You must be an admin to view this page.");

            var role = _db.ApproverRoles.FirstOrDefault(r => r.RoleKey == roleKey);
            if (role == null)
            {
                role = new ApproverRole { RoleKey = roleKey };
                _db.ApproverRoles.Add(role);
            }

            role.RoleDisplayName = roleDisplayName;
            role.Username = username;
            role.FullName = fullName;
            role.Email = email;

            _db.SaveChanges();

            TempData["Success"] = $"{roleDisplayName} approver saved.";
            return RedirectToAction(nameof(Index));
        }

        // POST: /Admin/SaveSettings
        [HttpPost]
        public IActionResult SaveSettings(string smtpHost, string smtpPort, string smtpFrom,
                                          string smtpFromName, string emailDomain, string reminderHours)
        {
            if (!IsAdmin()) return StatusCode(403, "Access denied. You must be an admin to view this page.");

            void Set(string key, string value)
            {
                var s = _db.AppSettings.FirstOrDefault(x => x.SettingKey == key);
                if (s == null) { s = new AppSetting { SettingKey = key }; _db.AppSettings.Add(s); }
                s.SettingValue = value ?? "";
            }

            Set("SmtpHost", smtpHost);
            Set("SmtpPort", smtpPort);
            Set("SmtpFrom", smtpFrom);
            Set("SmtpFromName", smtpFromName);
            Set("CompanyEmailDomain", emailDomain);
            Set("ReminderHours", reminderHours);

            _db.SaveChanges();

            TempData["Success"] = "Email settings saved.";
            return RedirectToAction(nameof(Index));
        }

        // POST: /Admin/SaveAdmins
        [HttpPost]
        public IActionResult SaveAdmins(string adminUsers)
        {
            if (!IsAdmin()) return StatusCode(403, "Access denied. You must be an admin to view this page.");

            var s = _db.AppSettings.FirstOrDefault(x => x.SettingKey == "AdminUsers");
            if (s == null) { s = new AppSetting { SettingKey = "AdminUsers" }; _db.AppSettings.Add(s); }
            s.SettingValue = adminUsers ?? "";

            _db.SaveChanges();

            TempData["Success"] = "Admin users saved.";
            return RedirectToAction(nameof(Index));
        }

        // POST: /Admin/TestEmail
        [HttpPost]
        public IActionResult TestEmail()
        {
            if (!IsAdmin()) return StatusCode(403, "Access denied. You must be an admin to view this page.");

            // Send a test email to yourself
            var currentUser = User.Identity?.Name ?? "";
            var emailDomain = _db.AppSettings.FirstOrDefault(s => s.SettingKey == "CompanyEmailDomain")?.SettingValue ?? "company.com";
            var usernameOnly = currentUser.Contains('\\') ? currentUser.Split('\\')[1] : currentUser;

            var fakeReq = new OutsourceRequest
            {
                RequestId = 0,
                PartNumber = "TEST-001",
                SapDescription = "Test Part",
                Quantity = 1,
                CreatedAt = DateTime.Now,
                Reason = "This is a test email from the Outsource Portal."
            };

            var fakeApprover = new ApproverRole
            {
                FullName = currentUser,
                Email = $"{usernameOnly}@{emailDomain}"
            };

            _email.SendToApprover(fakeReq, fakeApprover);

            TempData["Success"] = $"Test email sent to {fakeApprover.Email}";
            return RedirectToAction(nameof(Index));
        }

        // Seed default admin (call once on first run)
        // GET: /Admin/Seed?username=DOMAIN\you
        public IActionResult Seed(string username)
        {
            // Only works if NO admin users are set yet
            var existing = _db.AppSettings.FirstOrDefault(s => s.SettingKey == "AdminUsers");
            if (existing != null && !string.IsNullOrEmpty(existing.SettingValue))
                return Content("Admin already seeded.");

            if (existing == null)
            {
                _db.AppSettings.Add(new AppSetting { SettingKey = "AdminUsers", SettingValue = username });
            }
            else
            {
                existing.SettingValue = username;
            }

            _db.SaveChanges();
            return Content($"Admin seeded: {username}. You can now log in to /Admin");
        }
    }
}
