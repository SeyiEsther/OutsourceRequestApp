using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OutsourceRequestApp.Data;
using OutsourceRequestApp.Models;
using OutsourceRequestApp.Services;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OutsourceRequestApp.Controllers
{
    public class OutsourceController : Controller
    {
        private readonly AppDbContext _db;
        private readonly WarehouseDbContext _warehouseDb;
        private readonly EmailService _email;

        private static readonly string[] AllowedMimeTypes =
        {
            "image/jpeg", "image/png", "image/gif", "image/bmp", "image/webp", "application/pdf"
        };

        private const long MaxUploadBytes = 10 * 1024 * 1024; // 10 MB
        private const int  PageSize       = 25;

        public OutsourceController(AppDbContext db, WarehouseDbContext warehouseDb, EmailService email)
        {
            _db          = db;
            _warehouseDb = warehouseDb;
            _email       = email;
        }

        // ----------------------------------------------------------------
        // GET: /Outsource  — paginated, server-side filtered
        // ----------------------------------------------------------------
        public async Task<IActionResult> Index(string? status, string? q, int page = 1)
        {
            // -- Stat counts (always across full dataset, not filtered) --
            ViewBag.TotalAll       = await _db.OutsourceRequests.CountAsync();
            ViewBag.TotalSubmitted = await _db.OutsourceRequests.CountAsync(r => r.Status == RequestStatus.Submitted);
            ViewBag.TotalReview    = await _db.OutsourceRequests.CountAsync(r =>
                r.Status == RequestStatus.ProductionPending || r.Status == RequestStatus.CostCompactPending ||
                r.Status == RequestStatus.SourcingPending   || r.Status == RequestStatus.MdPending);
            ViewBag.TotalApproved  = await _db.OutsourceRequests.CountAsync(r => r.Status == RequestStatus.Approved);
            ViewBag.TotalRejected  = await _db.OutsourceRequests.CountAsync(r =>
                r.Status == RequestStatus.Rejected || r.Status == RequestStatus.Cancelled);

            // -- Build filtered query --
            var query = _db.OutsourceRequests.AsQueryable();

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                query = query.Where(r =>
                    r.PartNumber.Contains(term) ||
                    (r.SapDescription != null && r.SapDescription.Contains(term)) ||
                    (r.CreatedByUsername != null && r.CreatedByUsername.Contains(term)));
            }

            var currentStatus = (status ?? "all").ToLower();
            query = currentStatus switch
            {
                "submitted" => query.Where(r => r.Status == RequestStatus.Submitted),
                "review"    => query.Where(r => r.Status == RequestStatus.ProductionPending || r.Status == RequestStatus.CostCompactPending ||
                                                  r.Status == RequestStatus.SourcingPending   || r.Status == RequestStatus.MdPending),
                "approved"  => query.Where(r => r.Status == RequestStatus.Approved),
                "rejected"  => query.Where(r => r.Status == RequestStatus.Rejected || r.Status == RequestStatus.Cancelled),
                _           => query
            };

            var totalFiltered = await query.CountAsync();
            var totalPages    = (int)Math.Ceiling((double)totalFiltered / PageSize);
            page = Math.Max(1, Math.Min(page, Math.Max(totalPages, 1)));

            var requests = await query
                .OrderByDescending(r => r.CreatedAt)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();

            ViewBag.CurrentPage   = page;
            ViewBag.TotalPages    = totalPages;
            ViewBag.TotalFiltered = totalFiltered;
            ViewBag.CurrentStatus = currentStatus;
            ViewBag.CurrentQ      = q ?? "";

            return View(requests);
        }

        // ----------------------------------------------------------------
        // GET: /Outsource/Create
        // ----------------------------------------------------------------
        [HttpGet]
        public IActionResult Create() => View(new OutsourceRequest());

        // ----------------------------------------------------------------
        // POST: /Outsource/Create
        // ----------------------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(OutsourceRequest model, IFormFile? imageUpload)
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
                var ext      = Path.GetExtension(imageUpload.FileName).ToLowerInvariant();
                var fileName = $"{Guid.NewGuid()}{ext}";
                var filePath = Path.Combine(uploads, fileName);
                await using var stream = new FileStream(filePath, FileMode.Create);
                await imageUpload.CopyToAsync(stream);
                model.AttachmentPath = $"uploads/{fileName}";
            }

            model.CreatedAt         = DateTime.Now;
            model.Status            = RequestStatus.Submitted;
            model.CreatedByUsername = User?.Identity?.Name ?? "Unknown";

            _db.OutsourceRequests.Add(model);
            await _db.SaveChangesAsync();

            // Fire-and-forget emails (don't block the HTTP response)
            var wpApprover = await _db.ApproverRoles.FirstOrDefaultAsync(r => r.RoleKey == "WP");
            _ = Task.Run(() =>
            {
                if (wpApprover != null) _email.SendToApprover(model, wpApprover);
                _email.SendSubmissionConfirmation(model);
            });

            return RedirectToAction(nameof(Track), new { id = model.RequestId });
        }

        // ----------------------------------------------------------------
        // GET: /Outsource/Details/5
        // ----------------------------------------------------------------
        public async Task<IActionResult> Details(int id)
        {
            var request = await _db.OutsourceRequests.FirstOrDefaultAsync(r => r.RequestId == id);
            if (request == null) return NotFound();

            ViewBag.Roles = await _db.ApproverRoles.ToListAsync();
            return View(request);
        }

        // ----------------------------------------------------------------
        // GET: /Outsource/Track/5
        // ----------------------------------------------------------------
        public async Task<IActionResult> Track(int id)
        {
            var request = await _db.OutsourceRequests.FirstOrDefaultAsync(r => r.RequestId == id);
            if (request == null) return NotFound();

            ViewBag.Roles           = await _db.ApproverRoles.ToListAsync();
            ViewBag.CurrentUsername = User?.Identity?.Name ?? "";
            return View(request);
        }

        // ----------------------------------------------------------------
        // GET: /Outsource/Print/5  — printable approval form
        // ----------------------------------------------------------------
        public async Task<IActionResult> Print(int id)
        {
            var request = await _db.OutsourceRequests.FirstOrDefaultAsync(r => r.RequestId == id);
            if (request == null) return NotFound();

            ViewBag.Roles = await _db.ApproverRoles.ToListAsync();
            return View(request);
        }

        // ----------------------------------------------------------------
        // POST: /Outsource/Cancel/5  — requester withdraws their own request
        // ----------------------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int id)
        {
            var request = await _db.OutsourceRequests.FirstOrDefaultAsync(r => r.RequestId == id);
            if (request == null) return NotFound();

            var currentUser = User?.Identity?.Name ?? "";

            if (!request.CreatedByUsername.Equals(currentUser, StringComparison.OrdinalIgnoreCase))
                return StatusCode(403, "You can only withdraw your own requests.");

            if (request.Status != RequestStatus.Submitted)
                return BadRequest("This request can no longer be withdrawn — it has already been reviewed.");

            request.Status          = RequestStatus.Cancelled;
            request.RejectionReason = $"Withdrawn by {currentUser} on {DateTime.Now:dd MMM yyyy HH:mm}";
            request.RejectedBy      = currentUser;
            request.RejectedAt      = DateTime.Now;

            await _db.SaveChangesAsync();

            // Notify the Work Prep approver so they don't review a withdrawn request
            var wpApprover = await _db.ApproverRoles.FirstOrDefaultAsync(r => r.RoleKey == "WP");
            if (wpApprover != null)
                _ = Task.Run(() => _email.SendCancellationNotice(request, wpApprover));

            return RedirectToAction(nameof(MyRequests));
        }

        // ----------------------------------------------------------------
        // Auth / status helpers for approval actions
        // ----------------------------------------------------------------
        private async Task<(OutsourceRequest? request, ApproverRole? approver, IActionResult? error)>
            LoadForReviewAsync(int id, string roleKey, string expectedStatus)
        {
            var request = await _db.OutsourceRequests.FirstOrDefaultAsync(r => r.RequestId == id);
            if (request == null)
                return (null, null, NotFound());

            var approver    = await _db.ApproverRoles.FirstOrDefaultAsync(r => r.RoleKey == roleKey);
            var currentUser = User?.Identity?.Name ?? "";

            if (approver == null || string.IsNullOrEmpty(approver.Email) ||
                !approver.Email.Equals(currentUser, StringComparison.OrdinalIgnoreCase))
                return (request, approver, StatusCode(403, "You are not assigned as the approver for this stage."));

            if (request.Status != expectedStatus)
                return (request, approver, BadRequest("This request is not awaiting action at this stage."));

            return (request, approver, null);
        }

        // ----------------------------------------------------------------
        // GET/POST: /Outsource/ReviewWorkPrep/5  (Stage 1 — Work Prep)
        // ----------------------------------------------------------------
        [HttpGet]
        public IActionResult ReviewWorkPrep(int id) =>
            RedirectToAction(nameof(Track), new { id });

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReviewWorkPrep(int id, string? comments, string action)
        {
            var (request, _, error) = await LoadForReviewAsync(id, "WP", RequestStatus.Submitted);
            if (error != null) return error;

            var currentUser = User?.Identity?.Name ?? "";
            request!.JFSignedBy   = currentUser;
            request.JFSignedDate = DateTime.Now;

            if (action == "approve")
            {
                request.Status = RequestStatus.ProductionPending;
                var nextApprover = await _db.ApproverRoles.FirstOrDefaultAsync(r => r.RoleKey == "PROD");
                _ = Task.Run(() =>
                {
                    if (nextApprover != null) _email.SendToApprover(request, nextApprover);
                    _email.SendConfirmationToRequester(request, "Work Preparation", true, comments ?? "");
                });
            }
            else
            {
                request.Status          = RequestStatus.Rejected;
                request.RejectedBy      = currentUser;
                request.RejectedAt      = DateTime.Now;
                request.RejectionReason = comments;
                _ = Task.Run(() =>
                    _email.SendConfirmationToRequester(request, "Work Preparation", false, comments ?? ""));
            }

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Track), new { id });
        }

        // ----------------------------------------------------------------
        // GET/POST: /Outsource/ReviewProduction/5  (Stage 2 — Production)
        // ----------------------------------------------------------------
        [HttpGet]
        public IActionResult ReviewProduction(int id) =>
            RedirectToAction(nameof(Track), new { id });

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReviewProduction(int id, string? comments, string action)
        {
            var (request, _, error) = await LoadForReviewAsync(id, "PROD", RequestStatus.ProductionPending);
            if (error != null) return error;

            var currentUser = User?.Identity?.Name ?? "";
            request!.LJSignedBy   = currentUser;
            request.LJSignedDate = DateTime.Now;

            if (action == "approve")
            {
                request.Status = RequestStatus.CostCompactPending;
                var nextApprover = await _db.ApproverRoles.FirstOrDefaultAsync(r => r.RoleKey == "BUYER");
                _ = Task.Run(() =>
                {
                    if (nextApprover != null) _email.SendToApprover(request, nextApprover);
                    _email.SendConfirmationToRequester(request, "Production", true, comments ?? "");
                });
            }
            else
            {
                request.Status          = RequestStatus.Rejected;
                request.RejectedBy      = currentUser;
                request.RejectedAt      = DateTime.Now;
                request.RejectionReason = comments;
                _ = Task.Run(() =>
                    _email.SendConfirmationToRequester(request, "Production", false, comments ?? ""));
            }

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Track), new { id });
        }

        // ----------------------------------------------------------------
        // GET/POST: /Outsource/ReviewCostCompact/5  (Stage 3 — Strategic Buyer)
        // ----------------------------------------------------------------
        [HttpGet]
        public IActionResult ReviewCostCompact(int id) =>
            RedirectToAction(nameof(Track), new { id });

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReviewCostCompact(int id, bool ppapRequired, decimal? costInhouse,
                                                decimal? costOutsource, string? costComments,
                                                string? scComments, string action)
        {
            var (request, _, error) = await LoadForReviewAsync(id, "BUYER", RequestStatus.CostCompactPending);
            if (error != null) return error;

            var currentUser = User?.Identity?.Name ?? "";

            request!.PpapRequired          = ppapRequired;
            request.CostInhousePerMonth   = costInhouse;
            request.CostOutsourcePerMonth = costOutsource;
            request.CostComments          = costComments;
            request.ScComments            = scComments;
            request.ScReviewedAt          = DateTime.Now;
            request.ScReviewedBy          = currentUser;

            if (action == "approve")
            {
                request.Status = RequestStatus.SourcingPending;
                var nextApprover = await _db.ApproverRoles.FirstOrDefaultAsync(r => r.RoleKey == "SOURCING");
                _ = Task.Run(() =>
                {
                    if (nextApprover != null) _email.SendToApprover(request, nextApprover);
                    _email.SendConfirmationToRequester(request, "Cost Compact", true, scComments ?? "");
                });
            }
            else
            {
                request.Status          = RequestStatus.Rejected;
                request.RejectedBy      = currentUser;
                request.RejectedAt      = DateTime.Now;
                request.RejectionReason = scComments;
                _ = Task.Run(() =>
                    _email.SendConfirmationToRequester(request, "Cost Compact", false, scComments ?? ""));
            }

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Track), new { id });
        }

        // ----------------------------------------------------------------
        // GET/POST: /Outsource/ReviewSourcing/5  (Stage 4 — Sourcing)
        // ----------------------------------------------------------------
        [HttpGet]
        public IActionResult ReviewSourcing(int id) =>
            RedirectToAction(nameof(Track), new { id });

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReviewSourcing(int id, string? comments, string action)
        {
            var (request, _, error) = await LoadForReviewAsync(id, "SOURCING", RequestStatus.SourcingPending);
            if (error != null) return error;

            var currentUser = User?.Identity?.Name ?? "";
            request!.SGSignedBy   = currentUser;
            request.SGSignedDate = DateTime.Now;

            if (action == "approve")
            {
                request.Status = RequestStatus.MdPending;
                var nextApprover = await _db.ApproverRoles.FirstOrDefaultAsync(r => r.RoleKey == "MD");
                _ = Task.Run(() =>
                {
                    if (nextApprover != null) _email.SendToApprover(request, nextApprover);
                    _email.SendConfirmationToRequester(request, "Sourcing", true, comments ?? "");
                });
            }
            else
            {
                request.Status          = RequestStatus.Rejected;
                request.RejectedBy      = currentUser;
                request.RejectedAt      = DateTime.Now;
                request.RejectionReason = comments;
                _ = Task.Run(() =>
                    _email.SendConfirmationToRequester(request, "Sourcing", false, comments ?? ""));
            }

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Track), new { id });
        }

        // ----------------------------------------------------------------
        // POST: /Outsource/ApproveMD/5  (Stage 5 — Managing Director, final)
        // ----------------------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveMD(int id, string? mdComments, string action)
        {
            var (request, _, error) = await LoadForReviewAsync(id, "MD", RequestStatus.MdPending);
            if (error != null) return error;

            var currentUser = User?.Identity?.Name ?? "";

            request!.MdReviewedAt = DateTime.Now;
            request.MdReviewedBy = currentUser;
            request.MdComments   = mdComments;
            request.Status       = action == "approve" ? RequestStatus.Approved : RequestStatus.Rejected;

            if (action != "approve")
            {
                request.RejectedBy      = currentUser;
                request.RejectedAt      = DateTime.Now;
                request.RejectionReason = mdComments;
            }

            _ = Task.Run(() =>
                _email.SendConfirmationToRequester(request, "Managing Director",
                    action == "approve", mdComments ?? ""));

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Track), new { id });
        }

        // ----------------------------------------------------------------
        // GET: /Outsource/MyApprovals
        // ----------------------------------------------------------------
        public async Task<IActionResult> MyApprovals()
        {
            var currentUser = User?.Identity?.Name ?? "";
            var roles       = await _db.ApproverRoles.ToListAsync();

            var myRole = roles.FirstOrDefault(r =>
                r.Email.Equals(currentUser, StringComparison.OrdinalIgnoreCase));

            if (myRole == null)
            {
                ViewBag.MyRole  = null;
                ViewBag.Pending = new System.Collections.Generic.List<OutsourceRequest>();
                ViewBag.Approved = 0;
                ViewBag.Rejected = 0;
                return View();
            }

            var statusFilter = myRole.RoleKey switch
            {
                "WP"       => RequestStatus.Submitted,
                "PROD"     => RequestStatus.ProductionPending,
                "BUYER"    => RequestStatus.CostCompactPending,
                "SOURCING" => RequestStatus.SourcingPending,
                "MD"       => RequestStatus.MdPending,
                _          => ""
            };

            var pending = await _db.OutsourceRequests
                .Where(r => r.Status == statusFilter)
                .OrderBy(r => r.CreatedAt)
                .ToListAsync();

            int approvedCount = myRole.RoleKey switch
            {
                "WP"       => await _db.OutsourceRequests.CountAsync(r =>
                    r.JFSignedBy != null && r.JFSignedBy.ToLower() == currentUser.ToLower() &&
                    !(r.Status == RequestStatus.Rejected && r.RejectedBy != null && r.RejectedBy.ToLower() == currentUser.ToLower())),
                "PROD"     => await _db.OutsourceRequests.CountAsync(r =>
                    r.LJSignedBy != null && r.LJSignedBy.ToLower() == currentUser.ToLower() &&
                    !(r.Status == RequestStatus.Rejected && r.RejectedBy != null && r.RejectedBy.ToLower() == currentUser.ToLower())),
                "BUYER"    => await _db.OutsourceRequests.CountAsync(r =>
                    r.ScReviewedBy != null && r.ScReviewedBy.ToLower() == currentUser.ToLower() &&
                    !(r.Status == RequestStatus.Rejected && r.RejectedBy != null && r.RejectedBy.ToLower() == currentUser.ToLower())),
                "SOURCING" => await _db.OutsourceRequests.CountAsync(r =>
                    r.SGSignedBy != null && r.SGSignedBy.ToLower() == currentUser.ToLower() &&
                    !(r.Status == RequestStatus.Rejected && r.RejectedBy != null && r.RejectedBy.ToLower() == currentUser.ToLower())),
                "MD"       => await _db.OutsourceRequests.CountAsync(r =>
                    r.MdReviewedBy != null && r.MdReviewedBy.ToLower() == currentUser.ToLower() &&
                    r.Status == RequestStatus.Approved),
                _          => 0
            };

            int rejectedCount = await _db.OutsourceRequests.CountAsync(r =>
                r.Status == RequestStatus.Rejected &&
                r.RejectedBy != null && r.RejectedBy.ToLower() == currentUser.ToLower());

            ViewBag.MyRole   = myRole;
            ViewBag.Pending  = pending;
            ViewBag.Approved = approvedCount;
            ViewBag.Rejected = rejectedCount;
            return View();
        }

        // ----------------------------------------------------------------
        // GET: /Outsource/MyRequests
        // ----------------------------------------------------------------
        public async Task<IActionResult> MyRequests()
        {
            var currentUser = User?.Identity?.Name ?? "";
            var requests = await _db.OutsourceRequests
                .Where(r => r.CreatedByUsername != null &&
                            r.CreatedByUsername.ToLower() == currentUser.ToLower())
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            return View(requests);
        }

        // ----------------------------------------------------------------
        // GET: /Outsource/Export  — download all requests as CSV
        // ----------------------------------------------------------------
        public async Task<IActionResult> Export()
        {
            var requests = await _db.OutsourceRequests
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            var sb = new StringBuilder();
            sb.AppendLine("ID,Part Number,SAP Description,Drawing Number,Quantity," +
                          "Start Date,End Date,Status,Submitted By,Submitted At," +
                          "Work Prep Signed By,Work Prep Signed At," +
                          "Production Signed By,Production Signed At," +
                          "PPAP Required,Cost In-house/Month,Cost Outsource/Month,Cost Notes," +
                          "Buyer Reviewer,Buyer Reviewed At,Buyer Comments," +
                          "Sourcing Signed By,Sourcing Signed At," +
                          "MD Reviewer,MD Reviewed At,MD Comments," +
                          "Rejected By,Rejected At,Rejection Reason");

            foreach (var r in requests)
            {
                sb.AppendLine(string.Join(",",
                    r.RequestId,
                    Csv(r.PartNumber),
                    Csv(r.SapDescription),
                    Csv(r.DrawingNumber),
                    r.Quantity,
                    r.StartDate?.ToString("dd/MM/yyyy") ?? "",
                    r.EndDate?.ToString("dd/MM/yyyy") ?? "",
                    Csv(RequestStatus.Label(r.Status)),
                    Csv(r.CreatedByUsername),
                    r.CreatedAt.ToString("dd/MM/yyyy HH:mm"),
                    Csv(r.JFSignedBy),
                    r.JFSignedDate?.ToString("dd/MM/yyyy HH:mm") ?? "",
                    Csv(r.LJSignedBy),
                    r.LJSignedDate?.ToString("dd/MM/yyyy HH:mm") ?? "",
                    r.PpapRequired.HasValue ? (r.PpapRequired.Value ? "Yes" : "No") : "",
                    r.CostInhousePerMonth?.ToString("F2") ?? "",
                    r.CostOutsourcePerMonth?.ToString("F2") ?? "",
                    Csv(r.CostComments),
                    Csv(r.ScReviewedBy),
                    r.ScReviewedAt?.ToString("dd/MM/yyyy HH:mm") ?? "",
                    Csv(r.ScComments),
                    Csv(r.SGSignedBy),
                    r.SGSignedDate?.ToString("dd/MM/yyyy HH:mm") ?? "",
                    Csv(r.MdReviewedBy),
                    r.MdReviewedAt?.ToString("dd/MM/yyyy HH:mm") ?? "",
                    Csv(r.MdComments),
                    Csv(r.RejectedBy),
                    r.RejectedAt?.ToString("dd/MM/yyyy HH:mm") ?? "",
                    Csv(r.RejectionReason)));
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv", $"outsource-requests-{DateTime.Now:yyyyMMdd-HHmm}.csv");
        }

        private static string Csv(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        // ----------------------------------------------------------------
        // GET: /Outsource/SearchPart?term=...
        // ----------------------------------------------------------------
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
