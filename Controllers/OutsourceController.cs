using Microsoft.AspNetCore.Mvc;
using OutsourceRequestApp.Data;
using OutsourceRequestApp.Models;
using OutsourceRequestApp.Services;
using System;
using System.IO;
using System.Linq;

namespace OutsourceRequestApp.Controllers
{
    public class OutsourceController : Controller
    {
        private readonly AppDbContext _db;
        private readonly WarehouseDbContext _warehouseDb;
        private readonly EmailService _email;

        // Allowed MIME types for uploads
        private static readonly string[] AllowedMimeTypes =
        {
            "image/jpeg", "image/png", "image/gif", "image/bmp", "image/webp", "application/pdf"
        };

        // 10 MB upload limit
        private const long MaxUploadBytes = 10 * 1024 * 1024;

        public OutsourceController(AppDbContext db, WarehouseDbContext warehouseDb, EmailService email)
        {
            _db = db;
            _warehouseDb = warehouseDb;
            _email = email;
        }

        // GET: /Outsource
        public IActionResult Index()
        {
            var requests = _db.OutsourceRequests
                              .OrderByDescending(r => r.CreatedAt)
                              .ToList();
            return View(requests);
        }

        // GET: /Outsource/Create
        [HttpGet]
        public IActionResult Create()
        {
            return View(new OutsourceRequest());
        }

        // POST: /Outsource/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(OutsourceRequest model, IFormFile? imageUpload)
        {
            if (!ModelState.IsValid)
                return View(model);

            // Server-side file validation
            if (imageUpload != null && imageUpload.Length > 0)
            {
                if (imageUpload.Length > MaxUploadBytes)
                {
                    ModelState.AddModelError(string.Empty, "Attachment must not exceed 10 MB.");
                    return View(model);
                }

                var mimeType = imageUpload.ContentType?.ToLowerInvariant() ?? "";
                if (!AllowedMimeTypes.Contains(mimeType))
                {
                    ModelState.AddModelError(string.Empty,
                        "Only image files (JPEG, PNG, GIF, BMP, WebP) and PDF documents are allowed.");
                    return View(model);
                }

                var uploads = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                Directory.CreateDirectory(uploads);
                var ext = Path.GetExtension(imageUpload.FileName).ToLowerInvariant();
                var fileName = $"{Guid.NewGuid()}{ext}";
                var filePath = Path.Combine(uploads, fileName);
                using var stream = new FileStream(filePath, FileMode.Create);
                imageUpload.CopyTo(stream);
                model.AttachmentPath = $"uploads/{fileName}";
            }

            model.CreatedAt = DateTime.Now;
            model.Status = "Submitted";
            model.CreatedByUsername = User?.Identity?.Name ?? "Unknown";

            _db.OutsourceRequests.Add(model);
            _db.SaveChanges();

            // Email the SC approver
            var scApprover = _db.ApproverRoles.FirstOrDefault(r => r.RoleKey == "SC");
            if (scApprover != null)
                _email.SendToApprover(model, scApprover);

            // Redirect straight to the tracker so the submitter can see their request
            return RedirectToAction(nameof(Track), new { id = model.RequestId });
        }

        // GET: /Outsource/Details/5
        public IActionResult Details(int id)
        {
            var request = _db.OutsourceRequests.FirstOrDefault(r => r.RequestId == id);
            if (request == null) return NotFound();

            ViewBag.Roles = _db.ApproverRoles.ToList();
            return View(request);
        }

        // GET: /Outsource/Track/5  (the shipping-tracker style view)
        public IActionResult Track(int id)
        {
            var request = _db.OutsourceRequests.FirstOrDefault(r => r.RequestId == id);
            if (request == null) return NotFound();

            ViewBag.Roles = _db.ApproverRoles.ToList();
            ViewBag.CurrentUsername = User?.Identity?.Name ?? "";

            return View(request);
        }

        // GET: /Outsource/Review/5  (SC fills Section 2)
        [HttpGet]
        public IActionResult Review(int id)
        {
            var request = _db.OutsourceRequests.FirstOrDefault(r => r.RequestId == id);
            if (request == null) return NotFound();

            // Only the SC approver can access this
            var scApprover = _db.ApproverRoles.FirstOrDefault(r => r.RoleKey == "SC");
            var currentUser = User?.Identity?.Name ?? "";

            if (scApprover == null || !scApprover.Username.Equals(currentUser, StringComparison.OrdinalIgnoreCase))
                return StatusCode(403, "You are not assigned as the Supply Chain approver.");

            return View(request);
        }

        // POST: /Outsource/Review/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Review(int id, bool ppapRequired, decimal? costInhouse,
                                    decimal? costOutsource, string? costComments,
                                    string? scComments, string action)
        {
            var request = _db.OutsourceRequests.FirstOrDefault(r => r.RequestId == id);
            if (request == null) return NotFound();

            request.PpapRequired = ppapRequired;
            request.CostInhousePerMonth = costInhouse;
            request.CostOutsourcePerMonth = costOutsource;
            request.CostComments = costComments;
            request.ScComments = scComments;
            request.ScReviewedAt = DateTime.Now;
            request.ScReviewedBy = User?.Identity?.Name;

            if (action == "approve")
            {
                request.Status = "Finance_Pending";
                var financeApprover = _db.ApproverRoles.FirstOrDefault(r => r.RoleKey == "Finance");
                if (financeApprover != null)
                    _email.SendToApprover(request, financeApprover);
                _email.SendConfirmationToRequester(request, "Supply Chain", true, scComments ?? "");
            }
            else
            {
                request.Status = "Rejected";
                _email.SendConfirmationToRequester(request, "Supply Chain", false, scComments ?? "");
            }

            _db.SaveChanges();
            return RedirectToAction(nameof(Track), new { id });
        }

        // POST: /Outsource/ApproveFinance/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ApproveFinance(int id, string? financeComments, string action)
        {
            var request = _db.OutsourceRequests.FirstOrDefault(r => r.RequestId == id);
            if (request == null) return NotFound();

            var finApprover = _db.ApproverRoles.FirstOrDefault(r => r.RoleKey == "Finance");
            var currentUser = User?.Identity?.Name ?? "";

            if (finApprover == null || !finApprover.Username.Equals(currentUser, StringComparison.OrdinalIgnoreCase))
                return StatusCode(403, "You are not assigned as the Finance approver.");

            request.FinanceReviewedAt = DateTime.Now;
            request.FinanceReviewedBy = currentUser;
            request.FinanceComments = financeComments;

            if (action == "approve")
            {
                request.Status = "MD_Pending";
                var mdApprover = _db.ApproverRoles.FirstOrDefault(r => r.RoleKey == "MD");
                if (mdApprover != null)
                    _email.SendToApprover(request, mdApprover);
                _email.SendConfirmationToRequester(request, "Finance", true, financeComments ?? "");
            }
            else
            {
                request.Status = "Rejected";
                _email.SendConfirmationToRequester(request, "Finance", false, financeComments ?? "");
            }

            _db.SaveChanges();
            return RedirectToAction(nameof(Track), new { id });
        }

        // POST: /Outsource/ApproveMD/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ApproveMD(int id, string? mdComments, string action)
        {
            var request = _db.OutsourceRequests.FirstOrDefault(r => r.RequestId == id);
            if (request == null) return NotFound();

            var mdApprover = _db.ApproverRoles.FirstOrDefault(r => r.RoleKey == "MD");
            var currentUser = User?.Identity?.Name ?? "";

            if (mdApprover == null || !mdApprover.Username.Equals(currentUser, StringComparison.OrdinalIgnoreCase))
                return StatusCode(403, "You are not assigned as the Managing Director approver.");

            request.MdReviewedAt = DateTime.Now;
            request.MdReviewedBy = currentUser;
            request.MdComments = mdComments;
            request.Status = action == "approve" ? "Approved" : "Rejected";

            _email.SendConfirmationToRequester(request, "Managing Director",
                                               action == "approve", mdComments ?? "");

            _db.SaveChanges();
            return RedirectToAction(nameof(Track), new { id });
        }

        // GET: /Outsource/MyApprovals — shows requests waiting on the current user
        public IActionResult MyApprovals()
        {
            var currentUser = User?.Identity?.Name ?? "";
            var roles = _db.ApproverRoles.ToList();

            var myRole = roles.FirstOrDefault(r =>
                r.Username.Equals(currentUser, StringComparison.OrdinalIgnoreCase));

            if (myRole == null)
            {
                ViewBag.MyRole = null;
                ViewBag.Pending = new List<OutsourceRequest>();
                ViewBag.Approved = 0;
                ViewBag.Rejected = 0;
                return View();
            }

            var statusFilter = myRole.RoleKey switch
            {
                "SC"      => "Submitted",
                "Finance" => "Finance_Pending",
                "MD"      => "MD_Pending",
                _         => ""
            };

            var pending = _db.OutsourceRequests
                .Where(r => r.Status == statusFilter)
                .OrderBy(r => r.CreatedAt)
                .ToList();

            // Calculate how many this user has approved (passed to next stage or final approval)
            int approvedCount = myRole.RoleKey switch
            {
                "SC" => _db.OutsourceRequests.Count(r =>
                    r.ScReviewedBy != null &&
                    r.ScReviewedBy.ToLower() == currentUser.ToLower() &&
                    r.Status != "Rejected"),
                "Finance" => _db.OutsourceRequests.Count(r =>
                    r.FinanceReviewedBy != null &&
                    r.FinanceReviewedBy.ToLower() == currentUser.ToLower() &&
                    r.Status != "Rejected"),
                "MD" => _db.OutsourceRequests.Count(r =>
                    r.MdReviewedBy != null &&
                    r.MdReviewedBy.ToLower() == currentUser.ToLower() &&
                    r.Status == "Approved"),
                _ => 0
            };

            // Calculate how many this user has rejected
            int rejectedCount = myRole.RoleKey switch
            {
                "SC" => _db.OutsourceRequests.Count(r =>
                    r.ScReviewedBy != null &&
                    r.ScReviewedBy.ToLower() == currentUser.ToLower() &&
                    r.Status == "Rejected"),
                "Finance" => _db.OutsourceRequests.Count(r =>
                    r.FinanceReviewedBy != null &&
                    r.FinanceReviewedBy.ToLower() == currentUser.ToLower() &&
                    r.Status == "Rejected"),
                "MD" => _db.OutsourceRequests.Count(r =>
                    r.MdReviewedBy != null &&
                    r.MdReviewedBy.ToLower() == currentUser.ToLower() &&
                    r.Status == "Rejected"),
                _ => 0
            };

            ViewBag.MyRole = myRole;
            ViewBag.Pending = pending;
            ViewBag.Approved = approvedCount;
            ViewBag.Rejected = rejectedCount;

            return View();
        }

        // GET: /Outsource/MyRequests — shows the current user's own submitted requests
        public IActionResult MyRequests()
        {
            var currentUser = User?.Identity?.Name ?? "";
            var requests = _db.OutsourceRequests
                .Where(r => r.CreatedByUsername != null &&
                            r.CreatedByUsername.ToLower() == currentUser.ToLower())
                .OrderByDescending(r => r.CreatedAt)
                .ToList();

            return View(requests);
        }

        // SAP part search — searches both part number and description
        [HttpGet]
        public IActionResult SearchPart(string term)
        {
            if (string.IsNullOrWhiteSpace(term)) return Json(Array.Empty<object>());

            var trimmed = term.Trim();
            var parts = _warehouseDb.Articles
                .Where(a => a.Material.Contains(trimmed) || a.MaterialDesc.Contains(trimmed))
                .Select(a => new { partNumber = a.Material, description = a.MaterialDesc })
                .Take(10)
                .ToList();

            return Json(parts);
        }
    }
}
