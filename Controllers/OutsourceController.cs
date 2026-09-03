using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly AccessControlService _access;
        private readonly ActiveDirectoryLookup _ad;
        private readonly ILogger<OutsourceController> _logger;

        private static readonly string[] AllowedMimeTypes =
        {
            "image/jpeg", "image/png", "image/gif", "image/bmp", "image/webp", "application/pdf"
        };

        private const long MaxUploadBytes = 10 * 1024 * 1024; // 10 MB
        private const int  PageSize       = 25;

        public OutsourceController(AppDbContext db, WarehouseDbContext warehouseDb,
                                   IServiceScopeFactory scopeFactory, AccessControlService access,
                                   ActiveDirectoryLookup ad, ILogger<OutsourceController> logger)
        {
            _db           = db;
            _warehouseDb  = warehouseDb;
            _scopeFactory = scopeFactory;
            _access       = access;
            _ad           = ad;
            _logger       = logger;
        }

        // ----------------------------------------------------------------
        // Dispatches an email on a background task using its OWN DI scope.
        // The controller's own EmailService/DbContext are disposed as soon as
        // the HTTP response completes, so a fire-and-forget Task.Run that closed
        // over them would hit an ObjectDisposedException and silently drop the
        // email. Resolving a fresh EmailService inside the task avoids that.
        // ----------------------------------------------------------------
        private void QueueEmail(Action<EmailService> send)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var email = scope.ServiceProvider.GetRequiredService<EmailService>();
                    send(email);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background email dispatch failed.");
                }
            });
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
            QueueEmail(email =>
            {
                if (wpApprover != null) email.SendToApprover(model, wpApprover);
                email.SendSubmissionConfirmation(model);
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

            // Computed via AccessControlService (email match, with an AD
            // display-name fallback) so the approve/reject form's visibility
            // always agrees with what the POST handlers actually authorise —
            // a raw e-mail compare in the view could show/hide the wrong thing
            // whenever the fallback match is what actually grants access.
            ViewBag.IsWpApprover       = await _access.IsAssignedApproverAsync("WP");
            ViewBag.IsProdApprover     = await _access.IsAssignedApproverAsync("PROD");
            ViewBag.IsBuyerApprover    = await _access.IsAssignedApproverAsync("BUYER");
            ViewBag.IsSourcingApprover = await _access.IsAssignedApproverAsync("SOURCING");
            ViewBag.IsMdApprover       = await _access.IsAssignedApproverAsync("MD");

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
                QueueEmail(email => email.SendCancellationNotice(request, wpApprover));

            return RedirectToAction(nameof(MyRequests));
        }

        // ----------------------------------------------------------------
        // GET/POST: /Outsource/ReviewWorkPrep/5  (Stage 1 — John Fisher)
        // ----------------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> ReviewWorkPrep(int id)
        {
            var request = await _db.OutsourceRequests.FirstOrDefaultAsync(r => r.RequestId == id);
            if (request == null) return NotFound();

            if (!await _access.IsAssignedApproverAsync("WP"))
                return StatusCode(403, "You are not assigned as the Work Preparation approver.");

            return View(request);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReviewWorkPrep(int id, string? comments, string action)
        {
            if (action != "approve" && action != "reject")
                return BadRequest("Invalid action.");

            var request = await _db.OutsourceRequests.FirstOrDefaultAsync(r => r.RequestId == id);
            if (request == null) return NotFound();

            if (!await _access.IsAssignedApproverAsync("WP"))
                return StatusCode(403, "You are not assigned as the Work Preparation approver.");

            if (request.Status != RequestStatus.Submitted)
                return BadRequest("This request is not awaiting Work Preparation review.");

            var currentUser = User?.Identity?.Name ?? "";
            request.JFSignedBy   = currentUser;
            request.JFSignedDate = DateTime.Now;

            if (action == "approve")
            {
                request.Status = RequestStatus.ProductionPending;
                var nextApprover = await _db.ApproverRoles.FirstOrDefaultAsync(r => r.RoleKey == "PROD");
                QueueEmail(email =>
                {
                    if (nextApprover != null) email.SendToApprover(request, nextApprover);
                    email.SendConfirmationToRequester(request, "Work Preparation", true, comments ?? "");
                });
            }
            else
            {
                request.Status          = RequestStatus.Rejected;
                request.RejectedBy      = currentUser;
                request.RejectedAt      = DateTime.Now;
                request.RejectionReason = comments;
                QueueEmail(email =>
                    email.SendConfirmationToRequester(request, "Work Preparation", false, comments ?? ""));
            }

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Track), new { id });
        }

        // ----------------------------------------------------------------
        // GET/POST: /Outsource/ReviewProduction/5  (Stage 2 — Lukasz Jaworski)
        // ----------------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> ReviewProduction(int id)
        {
            var request = await _db.OutsourceRequests.FirstOrDefaultAsync(r => r.RequestId == id);
            if (request == null) return NotFound();

            if (!await _access.IsAssignedApproverAsync("PROD"))
                return StatusCode(403, "You are not assigned as the Production approver.");

            return View(request);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReviewProduction(int id, string? comments, string action)
        {
            if (action != "approve" && action != "reject")
                return BadRequest("Invalid action.");

            var request = await _db.OutsourceRequests.FirstOrDefaultAsync(r => r.RequestId == id);
            if (request == null) return NotFound();

            if (!await _access.IsAssignedApproverAsync("PROD"))
                return StatusCode(403, "You are not assigned as the Production approver.");

            if (request.Status != RequestStatus.ProductionPending)
                return BadRequest("This request is not awaiting Production review.");

            var currentUser = User?.Identity?.Name ?? "";
            request.LJSignedBy   = currentUser;
            request.LJSignedDate = DateTime.Now;

            if (action == "approve")
            {
                request.Status = RequestStatus.CostCompactPending;
                var nextApprover = await _db.ApproverRoles.FirstOrDefaultAsync(r => r.RoleKey == "BUYER");
                QueueEmail(email =>
                {
                    if (nextApprover != null) email.SendToApprover(request, nextApprover);
                    email.SendConfirmationToRequester(request, "Production", true, comments ?? "");
                });
            }
            else
            {
                request.Status          = RequestStatus.Rejected;
                request.RejectedBy      = currentUser;
                request.RejectedAt      = DateTime.Now;
                request.RejectionReason = comments;
                QueueEmail(email =>
                    email.SendConfirmationToRequester(request, "Production", false, comments ?? ""));
            }

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Track), new { id });
        }

        // ----------------------------------------------------------------
        // GET/POST: /Outsource/ReviewCostCompact/5  (Stage 3 — Chris Welland, Strategic Buyer)
        // ----------------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> ReviewCostCompact(int id)
        {
            var request = await _db.OutsourceRequests.FirstOrDefaultAsync(r => r.RequestId == id);
            if (request == null) return NotFound();

            if (!await _access.IsAssignedApproverAsync("BUYER"))
                return StatusCode(403, "You are not assigned as the Strategic Buyer approver.");

            return View(request);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReviewCostCompact(int id, bool ppapRequired, decimal? costInhouse,
                                                decimal? costOutsource, string? costComments,
                                                string? scComments, string action)
        {
            if (action != "approve" && action != "reject")
                return BadRequest("Invalid action.");

            var request = await _db.OutsourceRequests.FirstOrDefaultAsync(r => r.RequestId == id);
            if (request == null) return NotFound();

            if (!await _access.IsAssignedApproverAsync("BUYER"))
                return StatusCode(403, "You are not assigned as the Strategic Buyer approver.");

            if (request.Status != RequestStatus.CostCompactPending)
                return BadRequest("This request is not awaiting Cost Compact review.");

            var currentUser = User?.Identity?.Name ?? "";

            request.PpapRequired          = ppapRequired;
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
                QueueEmail(email =>
                {
                    if (nextApprover != null) email.SendToApprover(request, nextApprover);
                    email.SendConfirmationToRequester(request, "Cost Compact", true, scComments ?? "");
                });
            }
            else
            {
                request.Status          = RequestStatus.Rejected;
                request.RejectedBy      = currentUser;
                request.RejectedAt      = DateTime.Now;
                request.RejectionReason = scComments;
                QueueEmail(email =>
                    email.SendConfirmationToRequester(request, "Cost Compact", false, scComments ?? ""));
            }

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Track), new { id });
        }

        // ----------------------------------------------------------------
        // GET/POST: /Outsource/ReviewSourcing/5  (Stage 4 — Simon Graham)
        // ----------------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> ReviewSourcing(int id)
        {
            var request = await _db.OutsourceRequests.FirstOrDefaultAsync(r => r.RequestId == id);
            if (request == null) return NotFound();

            if (!await _access.IsAssignedApproverAsync("SOURCING"))
                return StatusCode(403, "You are not assigned as the Sourcing approver.");

            return View(request);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReviewSourcing(int id, string? comments, string action)
        {
            if (action != "approve" && action != "reject")
                return BadRequest("Invalid action.");

            var request = await _db.OutsourceRequests.FirstOrDefaultAsync(r => r.RequestId == id);
            if (request == null) return NotFound();

            if (!await _access.IsAssignedApproverAsync("SOURCING"))
                return StatusCode(403, "You are not assigned as the Sourcing approver.");

            if (request.Status != RequestStatus.SourcingPending)
                return BadRequest("This request is not awaiting Sourcing review.");

            var currentUser = User?.Identity?.Name ?? "";
            request.SGSignedBy   = currentUser;
            request.SGSignedDate = DateTime.Now;

            if (action == "approve")
            {
                request.Status = RequestStatus.MdPending;
                var nextApprover = await _db.ApproverRoles.FirstOrDefaultAsync(r => r.RoleKey == "MD");
                QueueEmail(email =>
                {
                    if (nextApprover != null) email.SendToApprover(request, nextApprover);
                    email.SendConfirmationToRequester(request, "Sourcing", true, comments ?? "");
                });
            }
            else
            {
                request.Status          = RequestStatus.Rejected;
                request.RejectedBy      = currentUser;
                request.RejectedAt      = DateTime.Now;
                request.RejectionReason = comments;
                QueueEmail(email =>
                    email.SendConfirmationToRequester(request, "Sourcing", false, comments ?? ""));
            }

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Track), new { id });
        }

        // ----------------------------------------------------------------
        // POST: /Outsource/ApproveMD/5  (Stage 5 — Patrick MacDonough, final)
        // ----------------------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveMD(int id, string? mdComments, string action)
        {
            var request = await _db.OutsourceRequests.FirstOrDefaultAsync(r => r.RequestId == id);
            if (request == null) return NotFound();

            if (action != "approve" && action != "reject")
                return BadRequest("Invalid action.");

            if (!await _access.IsAssignedApproverAsync("MD"))
                return StatusCode(403, "You are not assigned as the Managing Director approver.");

            if (request.Status != RequestStatus.MdPending)
                return BadRequest("This request is not awaiting MD approval.");

            var currentUser = User?.Identity?.Name ?? "";

            request.MdReviewedAt = DateTime.Now;
            request.MdReviewedBy = currentUser;
            request.MdComments   = mdComments;
            request.Status       = action == "approve" ? RequestStatus.Approved : RequestStatus.Rejected;

            if (action != "approve")
            {
                request.RejectedBy      = currentUser;
                request.RejectedAt      = DateTime.Now;
                request.RejectionReason = mdComments;
            }

            QueueEmail(email =>
                email.SendConfirmationToRequester(request, "Managing Director",
                    action == "approve", mdComments ?? ""));

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Track), new { id });
        }

        // ----------------------------------------------------------------
        // GET: /Outsource/MyApprovals
        // ----------------------------------------------------------------
        public async Task<IActionResult> MyApprovals()
        {
            var currentUser = _access.CurrentUserName;

            var myRole = await _access.GetMyApproverRoleAsync();

            if (myRole == null)
            {
                ViewBag.MyRole  = null;
                ViewBag.Pending = new System.Collections.Generic.List<OutsourceRequest>();
                ViewBag.Approved = 0;
                ViewBag.Rejected = 0;
                return View();
            }

            var statusFilter = RequestStatus.PendingStatusForRole(myRole.RoleKey) ?? "";

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
                    Csv(_ad.DisplayNameOrRaw(r.CreatedByUsername)),
                    r.CreatedAt.ToString("dd/MM/yyyy HH:mm"),
                    Csv(_ad.DisplayNameOrRaw(r.JFSignedBy)),
                    r.JFSignedDate?.ToString("dd/MM/yyyy HH:mm") ?? "",
                    Csv(_ad.DisplayNameOrRaw(r.LJSignedBy)),
                    r.LJSignedDate?.ToString("dd/MM/yyyy HH:mm") ?? "",
                    r.PpapRequired.HasValue ? (r.PpapRequired.Value ? "Yes" : "No") : "",
                    r.CostInhousePerMonth?.ToString("F2") ?? "",
                    r.CostOutsourcePerMonth?.ToString("F2") ?? "",
                    Csv(r.CostComments),
                    Csv(_ad.DisplayNameOrRaw(r.ScReviewedBy)),
                    r.ScReviewedAt?.ToString("dd/MM/yyyy HH:mm") ?? "",
                    Csv(r.ScComments),
                    Csv(_ad.DisplayNameOrRaw(r.SGSignedBy)),
                    r.SGSignedDate?.ToString("dd/MM/yyyy HH:mm") ?? "",
                    Csv(_ad.DisplayNameOrRaw(r.MdReviewedBy)),
                    r.MdReviewedAt?.ToString("dd/MM/yyyy HH:mm") ?? "",
                    Csv(r.MdComments),
                    Csv(_ad.DisplayNameOrRaw(r.RejectedBy)),
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
