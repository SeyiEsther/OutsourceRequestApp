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
using System.Collections.Generic;

namespace OutsourceRequestApp.Controllers
{
    public class OutsourceController : Controller
    {
        private readonly AppDbContext _db;
        private readonly WarehouseDbContext _warehouseDb;
        private readonly EmailService _email;
        private readonly IAuditLogService _audit;

        private static readonly string[] AllowedMimeTypes =
        {
            "image/jpeg", "image/png", "image/gif", "image/bmp", "image/webp", "application/pdf"
        };

        private const long MaxUploadBytes = 10 * 1024 * 1024;
        private const int  PageSize       = 25;

        public OutsourceController(AppDbContext db, WarehouseDbContext warehouseDb,
                                   EmailService email, IAuditLogService audit)
        {
            _db          = db;
            _warehouseDb = warehouseDb;
            _email       = email;
            _audit       = audit;
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        private string CurrentUser() => User?.Identity?.Name ?? "Unknown";

        private async Task<ApproverRole?> GetRole(string key) =>
            await _db.ApproverRoles.FirstOrDefaultAsync(r => r.RoleKey == key);

        private bool IsRole(ApproverRole? role) =>
            role != null && role.Username.Equals(CurrentUser(), StringComparison.OrdinalIgnoreCase);

        // ----------------------------------------------------------------
        // GET: /Outsource
        // ----------------------------------------------------------------
        public async Task<IActionResult> Index(string? status, string? q, int page = 1)
        {
            ViewBag.TotalAll       = await _db.OutsourceRequests.CountAsync();
            ViewBag.TotalSubmitted = await _db.OutsourceRequests.CountAsync(r => r.Status == RequestStatus.Submitted);
            ViewBag.TotalReview    = await _db.OutsourceRequests.CountAsync(r =>
                r.Status == RequestStatus.AwaitingLJApproval ||
                r.Status == RequestStatus.AwaitingCostImpact ||
                r.Status == RequestStatus.FinancePending ||
                r.Status == RequestStatus.MdPending);
            ViewBag.TotalApproved  = await _db.OutsourceRequests.CountAsync(r => r.Status == RequestStatus.Approved);
            ViewBag.TotalRejected  = await _db.OutsourceRequests.CountAsync(r =>
                r.Status == RequestStatus.Rejected || r.Status == RequestStatus.Cancelled);

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
                "review"    => query.Where(r =>
                    r.Status == RequestStatus.AwaitingLJApproval ||
                    r.Status == RequestStatus.AwaitingCostImpact ||
                    r.Status == RequestStatus.FinancePending ||
                    r.Status == RequestStatus.MdPending),
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
        public async Task<IActionResult> Create(OutsourceRequest model, IFormFile? imageUpload,
                                                string? submitAction,
                                                string? ppapNumber, string? concessionNumber,
                                                bool? specialPackingRequired, string? specialPackingDetails)
        {
            if (!ModelState.IsValid)
                return View(model);

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

            model.CreatedAt              = DateTime.Now;
            model.CreatedByUsername      = CurrentUser();
            model.PPAPNumber             = ppapNumber;
            model.ConcessionNumber       = concessionNumber;
            model.SpecialPackingRequired = specialPackingRequired;
            model.SpecialPackingDetails  = specialPackingDetails;

            bool isDraft = submitAction == "draft";
            model.Status = isDraft ? RequestStatus.Draft : RequestStatus.Submitted;

            _db.OutsourceRequests.Add(model);
            await _db.SaveChangesAsync();

            // Generate reference number
            model.RequestNumber = $"OSR-{model.CreatedAt.Year}-{model.RequestId:0000}";
            await _db.SaveChangesAsync();

            await _audit.LogAsync(model.RequestId, CurrentUser(),
                isDraft ? "Created (draft)" : "Submitted",
                null, model.Status);

            if (!isDraft)
            {
                var jfApprover = await GetRole("WorkPrepManager");
                _ = Task.Run(() =>
                {
                    if (jfApprover != null) _email.SendToApprover(model, jfApprover);
                    _email.SendSubmissionConfirmation(model);
                });
            }

            return RedirectToAction(nameof(Track), new { id = model.RequestId });
        }

        // ----------------------------------------------------------------
        // POST: /Outsource/SubmitDraft/5  — promote Draft → Submitted
        // ----------------------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitDraft(int id)
        {
            var req = await _db.OutsourceRequests.FirstOrDefaultAsync(r => r.RequestId == id);
            if (req == null) return NotFound();

            if (!req.CreatedByUsername.Equals(CurrentUser(), StringComparison.OrdinalIgnoreCase))
                return StatusCode(403);

            if (req.Status != RequestStatus.Draft)
                return BadRequest("Request is not in draft status.");

            var from = req.Status;
            req.Status = RequestStatus.Submitted;
            await _db.SaveChangesAsync();

            await _audit.LogAsync(req.RequestId, CurrentUser(), "Submitted from draft", from, req.Status);

            var jfApprover = await GetRole("WorkPrepManager");
            _ = Task.Run(() =>
            {
                if (jfApprover != null) _email.SendToApprover(req, jfApprover);
                _email.SendSubmissionConfirmation(req);
            });

            return RedirectToAction(nameof(Track), new { id });
        }

        // ----------------------------------------------------------------
        // GET: /Outsource/Details/5
        // ----------------------------------------------------------------
        public async Task<IActionResult> Details(int id)
        {
            var request = await _db.OutsourceRequests
                .Include(r => r.CostLines)
                .Include(r => r.AuditLogs)
                .FirstOrDefaultAsync(r => r.RequestId == id);
            if (request == null) return NotFound();

            ViewBag.Roles = await _db.ApproverRoles.ToListAsync();
            return View(request);
        }

        // ----------------------------------------------------------------
        // GET: /Outsource/Track/5
        // ----------------------------------------------------------------
        public async Task<IActionResult> Track(int id)
        {
            var request = await _db.OutsourceRequests
                .Include(r => r.CostLines)
                .FirstOrDefaultAsync(r => r.RequestId == id);
            if (request == null) return NotFound();

            ViewBag.Roles           = await _db.ApproverRoles.ToListAsync();
            ViewBag.CurrentUsername = CurrentUser();
            return View(request);
        }

        // ----------------------------------------------------------------
        // GET: /Outsource/Print/5
        // ----------------------------------------------------------------
        public async Task<IActionResult> Print(int id)
        {
            var request = await _db.OutsourceRequests
                .Include(r => r.CostLines)
                .FirstOrDefaultAsync(r => r.RequestId == id);
            if (request == null) return NotFound();

            ViewBag.Roles = await _db.ApproverRoles.ToListAsync();
            return View(request);
        }

        // ----------------------------------------------------------------
        // POST: /Outsource/Cancel/5
        // ----------------------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int id)
        {
            var request = await _db.OutsourceRequests.FirstOrDefaultAsync(r => r.RequestId == id);
            if (request == null) return NotFound();

            var currentUser = CurrentUser();

            if (!request.CreatedByUsername.Equals(currentUser, StringComparison.OrdinalIgnoreCase))
                return StatusCode(403, "You can only withdraw your own requests.");

            if (request.Status != RequestStatus.Submitted && request.Status != RequestStatus.Draft)
                return BadRequest("This request can no longer be withdrawn.");

            var from = request.Status;
            request.Status       = RequestStatus.Cancelled;
            request.RejectedBy   = currentUser;
            request.RejectedAt   = DateTime.Now;
            request.RejectionReason = $"Withdrawn by {currentUser}";

            await _db.SaveChangesAsync();

            await _audit.LogAsync(request.RequestId, currentUser, "Cancelled/Withdrawn", from, RequestStatus.Cancelled);

            var jfApprover = await GetRole("WorkPrepManager");
            if (jfApprover != null)
                _ = Task.Run(() => _email.SendCancellationNotice(request, jfApprover));

            return RedirectToAction(nameof(MyRequests));
        }

        // ----------------------------------------------------------------
        // POST: /Outsource/ApproveJF/5  — Step 2: John Fisher (WorkPrepManager)
        // ----------------------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveJF(int id, string? jfComments, string action,
                                                   string? rejectionReason)
        {
            var req = await _db.OutsourceRequests.FirstOrDefaultAsync(r => r.RequestId == id);
            if (req == null) return NotFound();

            var role = await GetRole("WorkPrepManager");
            if (!IsRole(role))
                return StatusCode(403, "You are not assigned as the Work Prep Manager.");

            if (req.Status != RequestStatus.Submitted)
                return BadRequest("Request is not awaiting JF approval.");

            var from = req.Status;
            req.JFSignedBy   = CurrentUser();
            req.JFSignedDate = DateTime.Now;
            req.JFComments   = jfComments;

            if (action == "approve")
            {
                req.Status = RequestStatus.AwaitingLJApproval;
                await _db.SaveChangesAsync();
                await _audit.LogAsync(req.RequestId, CurrentUser(), "JF Approved", from, req.Status, jfComments);

                var ljRole = await GetRole("ProductionManager");
                _ = Task.Run(() =>
                {
                    if (ljRole != null) _email.SendToApprover(req, ljRole);
                    _email.SendConfirmationToRequester(req, "Work Prep Manager", true, jfComments ?? "");
                });
            }
            else
            {
                req.Status          = RequestStatus.Rejected;
                req.RejectionReason = rejectionReason;
                req.RejectedBy      = CurrentUser();
                req.RejectedAt      = DateTime.Now;
                await _db.SaveChangesAsync();
                await _audit.LogAsync(req.RequestId, CurrentUser(), "JF Rejected", from, req.Status, rejectionReason);

                _ = Task.Run(() =>
                    _email.SendConfirmationToRequester(req, "Work Prep Manager", false, rejectionReason ?? ""));
            }

            return RedirectToAction(nameof(Track), new { id });
        }

        // ----------------------------------------------------------------
        // POST: /Outsource/ApproveLJ/5  — Step 3: Lukasz Jaworski (ProductionManager)
        // ----------------------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveLJ(int id, string? ljComments, string action,
                                                   string? rejectionReason)
        {
            var req = await _db.OutsourceRequests.FirstOrDefaultAsync(r => r.RequestId == id);
            if (req == null) return NotFound();

            var role = await GetRole("ProductionManager");
            if (!IsRole(role))
                return StatusCode(403, "You are not assigned as the Production Manager.");

            if (req.Status != RequestStatus.AwaitingLJApproval)
                return BadRequest("Request is not awaiting LJ approval.");

            var from = req.Status;
            req.LJSignedBy   = CurrentUser();
            req.LJSignedDate = DateTime.Now;
            req.LJComments   = ljComments;

            if (action == "approve")
            {
                req.Status = RequestStatus.AwaitingCostImpact;
                await _db.SaveChangesAsync();
                await _audit.LogAsync(req.RequestId, CurrentUser(), "LJ Approved", from, req.Status, ljComments);

                var sgRole = await GetRole("SupplyChainManager");
                _ = Task.Run(() =>
                {
                    _email.SendCostImpactRequest(req, sgRole!);
                    _email.SendConfirmationToRequester(req, "Production Manager", true, ljComments ?? "");
                });
            }
            else
            {
                req.Status          = RequestStatus.Rejected;
                req.RejectionReason = rejectionReason;
                req.RejectedBy      = CurrentUser();
                req.RejectedAt      = DateTime.Now;
                await _db.SaveChangesAsync();
                await _audit.LogAsync(req.RequestId, CurrentUser(), "LJ Rejected", from, req.Status, rejectionReason);

                _ = Task.Run(() =>
                    _email.SendConfirmationToRequester(req, "Production Manager", false, rejectionReason ?? ""));
            }

            return RedirectToAction(nameof(Track), new { id });
        }

        // ----------------------------------------------------------------
        // POST: /Outsource/SubmitCostImpact/5  — Step 4: SC enters cost lines
        // ----------------------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitCostImpact(int id, string? costComments,
            List<string>? lineDesc, List<decimal>? lineInhouse, List<decimal>? lineOutsource)
        {
            var req = await _db.OutsourceRequests
                .Include(r => r.CostLines)
                .FirstOrDefaultAsync(r => r.RequestId == id);
            if (req == null) return NotFound();

            var role = await GetRole("SupplyChainManager");
            if (!IsRole(role))
                return StatusCode(403, "You are not assigned as the Supply Chain Manager.");

            if (req.Status != RequestStatus.AwaitingCostImpact)
                return BadRequest("Request is not awaiting cost impact entry.");

            // Remove existing cost lines and replace
            _db.OutsourceRequestCostLines.RemoveRange(req.CostLines);

            if (lineDesc != null)
            {
                for (int i = 0; i < lineDesc.Count; i++)
                {
                    var desc = lineDesc[i];
                    if (string.IsNullOrWhiteSpace(desc)) continue;
                    var inh = lineInhouse != null && i < lineInhouse.Count ? lineInhouse[i] : 0;
                    var out_ = lineOutsource != null && i < lineOutsource.Count ? lineOutsource[i] : 0;
                    _db.OutsourceRequestCostLines.Add(new OutsourceRequestCostLine
                    {
                        RequestId    = id,
                        Description  = desc,
                        InhouseCost  = inh,
                        OutsourceCost = out_,
                        Total        = out_ - inh
                    });
                }
            }

            var from = req.Status;
            req.CostComments  = costComments;
            req.CostEnteredBy = CurrentUser();
            req.CostEnteredAt = DateTime.Now;
            req.Status        = RequestStatus.FinancePending;

            await _db.SaveChangesAsync();
            await _audit.LogAsync(req.RequestId, CurrentUser(), "Cost impact entered", from, req.Status, costComments);

            var sgRole = await GetRole("SupplyChainManager");
            _ = Task.Run(() =>
            {
                if (sgRole != null) _email.SendToApprover(req, sgRole);
                _email.SendConfirmationToRequester(req, "Cost Impact", true, "");
            });

            return RedirectToAction(nameof(Track), new { id });
        }

        // ----------------------------------------------------------------
        // POST: /Outsource/ApproveSG/5  — Step 5: Simon Graham (SupplyChainManager)
        // ----------------------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveSG(int id, string? sgComments, string action,
                                                   string? rejectionReason)
        {
            var req = await _db.OutsourceRequests.FirstOrDefaultAsync(r => r.RequestId == id);
            if (req == null) return NotFound();

            var role = await GetRole("SupplyChainManager");
            if (!IsRole(role))
                return StatusCode(403, "You are not assigned as the Supply Chain Manager.");

            if (req.Status != RequestStatus.FinancePending)
                return BadRequest("Request is not awaiting SG approval.");

            var from = req.Status;
            req.SGSignedBy   = CurrentUser();
            req.SGSignedDate = DateTime.Now;
            req.SGComments   = sgComments;

            if (action == "approve")
            {
                req.Status = RequestStatus.MdPending;
                await _db.SaveChangesAsync();
                await _audit.LogAsync(req.RequestId, CurrentUser(), "SG Approved", from, req.Status, sgComments);

                var mdRole = await GetRole("ManagingDirector");
                _ = Task.Run(() =>
                {
                    if (mdRole != null) _email.SendToApprover(req, mdRole);
                    _email.SendConfirmationToRequester(req, "Supply Chain Manager", true, sgComments ?? "");
                });
            }
            else
            {
                req.Status          = RequestStatus.Rejected;
                req.RejectionReason = rejectionReason;
                req.RejectedBy      = CurrentUser();
                req.RejectedAt      = DateTime.Now;
                await _db.SaveChangesAsync();
                await _audit.LogAsync(req.RequestId, CurrentUser(), "SG Rejected", from, req.Status, rejectionReason);

                _ = Task.Run(() =>
                    _email.SendConfirmationToRequester(req, "Supply Chain Manager", false, rejectionReason ?? ""));
            }

            return RedirectToAction(nameof(Track), new { id });
        }

        // ----------------------------------------------------------------
        // POST: /Outsource/ApproveMD/5  — Step 6: Managing Director
        // ----------------------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveMD(int id, string? mdComments, string action,
                                                   string? rejectionReason)
        {
            var req = await _db.OutsourceRequests.FirstOrDefaultAsync(r => r.RequestId == id);
            if (req == null) return NotFound();

            var role = await GetRole("ManagingDirector");
            if (!IsRole(role))
                return StatusCode(403, "You are not assigned as the Managing Director.");

            if (req.Status != RequestStatus.MdPending)
                return BadRequest("Request is not awaiting MD approval.");

            var from = req.Status;
            req.MDSignedBy   = CurrentUser();
            req.MDSignedDate = DateTime.Now;
            req.MDComments   = mdComments;

            if (action == "approve")
            {
                req.Status = RequestStatus.Approved;
                await _db.SaveChangesAsync();
                await _audit.LogAsync(req.RequestId, CurrentUser(), "MD Approved — Authorised", from, req.Status, mdComments);
            }
            else
            {
                req.Status          = RequestStatus.Rejected;
                req.RejectionReason = rejectionReason;
                req.RejectedBy      = CurrentUser();
                req.RejectedAt      = DateTime.Now;
                await _db.SaveChangesAsync();
                await _audit.LogAsync(req.RequestId, CurrentUser(), "MD Rejected", from, req.Status, rejectionReason);
            }

            _ = Task.Run(() =>
                _email.SendConfirmationToRequester(req, "Managing Director",
                    action == "approve", (action == "approve" ? mdComments : rejectionReason) ?? ""));

            return RedirectToAction(nameof(Track), new { id });
        }

        // ----------------------------------------------------------------
        // Legacy: /Outsource/Review  — kept for backward compat, maps to JF step
        // ----------------------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Review(int id, bool ppapRequired, decimal? costInhouse,
                                                decimal? costOutsource, string? costComments,
                                                string? scComments, string action)
        {
            // Delegate to ApproveJF for Submitted requests
            return await ApproveJF(id, scComments, action, scComments);
        }

        // ----------------------------------------------------------------
        // Legacy: /Outsource/ApproveFinance — maps to ApproveSG
        // ----------------------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveFinance(int id, string? financeComments, string action)
        {
            return await ApproveSG(id, financeComments, action, financeComments);
        }

        // ----------------------------------------------------------------
        // GET: /Outsource/MyApprovals
        // ----------------------------------------------------------------
        public async Task<IActionResult> MyApprovals()
        {
            var currentUser = CurrentUser();
            var roles       = await _db.ApproverRoles.ToListAsync();

            var myRole = roles.FirstOrDefault(r =>
                r.Username.Equals(currentUser, StringComparison.OrdinalIgnoreCase));

            if (myRole == null)
            {
                ViewBag.MyRole  = null;
                ViewBag.Pending = new List<OutsourceRequest>();
                ViewBag.Approved = 0;
                ViewBag.Rejected = 0;
                return View();
            }

            var statusFilter = myRole.RoleKey switch
            {
                "WorkPrepManager"    => RequestStatus.Submitted,
                "ProductionManager"  => RequestStatus.AwaitingLJApproval,
                "SupplyChainManager" => RequestStatus.FinancePending,
                "ManagingDirector"   => RequestStatus.MdPending,
                // Legacy keys
                "SC"      => RequestStatus.Submitted,
                "Finance" => RequestStatus.FinancePending,
                "MD"      => RequestStatus.MdPending,
                _         => ""
            };

            // SC also owns AwaitingCostImpact step
            List<OutsourceRequest> pending;
            if (myRole.RoleKey == "SupplyChainManager")
            {
                pending = await _db.OutsourceRequests
                    .Where(r => r.Status == RequestStatus.AwaitingCostImpact ||
                                r.Status == RequestStatus.FinancePending)
                    .OrderBy(r => r.CreatedAt)
                    .ToListAsync();
            }
            else if (string.IsNullOrEmpty(statusFilter))
            {
                pending = new List<OutsourceRequest>();
            }
            else
            {
                pending = await _db.OutsourceRequests
                    .Where(r => r.Status == statusFilter)
                    .OrderBy(r => r.CreatedAt)
                    .ToListAsync();
            }

            int approvedCount = myRole.RoleKey switch
            {
                "WorkPrepManager"    => await _db.OutsourceRequests.CountAsync(r => r.JFSignedBy != null && r.JFSignedBy.ToLower() == currentUser.ToLower()),
                "ProductionManager"  => await _db.OutsourceRequests.CountAsync(r => r.LJSignedBy != null && r.LJSignedBy.ToLower() == currentUser.ToLower()),
                "SupplyChainManager" => await _db.OutsourceRequests.CountAsync(r => r.SGSignedBy != null && r.SGSignedBy.ToLower() == currentUser.ToLower()),
                "ManagingDirector"   => await _db.OutsourceRequests.CountAsync(r => r.MDSignedBy != null && r.MDSignedBy.ToLower() == currentUser.ToLower() && r.Status == RequestStatus.Approved),
                "SC"      => await _db.OutsourceRequests.CountAsync(r => r.ScReviewedBy != null && r.ScReviewedBy.ToLower() == currentUser.ToLower() && r.Status != RequestStatus.Rejected && r.Status != RequestStatus.Cancelled),
                "Finance" => await _db.OutsourceRequests.CountAsync(r => r.FinanceReviewedBy != null && r.FinanceReviewedBy.ToLower() == currentUser.ToLower() && r.Status != RequestStatus.Rejected),
                "MD"      => await _db.OutsourceRequests.CountAsync(r => r.MdReviewedBy != null && r.MdReviewedBy.ToLower() == currentUser.ToLower() && r.Status == RequestStatus.Approved),
                _         => 0
            };

            int rejectedCount = await _db.OutsourceRequests.CountAsync(r =>
                r.RejectedBy != null && r.RejectedBy.ToLower() == currentUser.ToLower() &&
                r.Status == RequestStatus.Rejected);

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
            var currentUser = CurrentUser();
            var requests = await _db.OutsourceRequests
                .Where(r => r.CreatedByUsername != null &&
                            r.CreatedByUsername.ToLower() == currentUser.ToLower())
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            return View(requests);
        }

        // ----------------------------------------------------------------
        // GET: /Outsource/Export
        // ----------------------------------------------------------------
        public async Task<IActionResult> Export()
        {
            var requests = await _db.OutsourceRequests
                .Include(r => r.CostLines)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            var sb = new StringBuilder();
            sb.AppendLine("Ref,Part Number,SAP Description,Drawing Number,Quantity," +
                          "Start Date,End Date,Status,Submitted By,Submitted At," +
                          "PPAP Number,Concession Number,Special Packing," +
                          "JF Signed By,JF Date,JF Comments," +
                          "LJ Signed By,LJ Date,LJ Comments," +
                          "Cost Entered By,Cost Entered At,Cost Comments," +
                          "SG Signed By,SG Date,SG Comments," +
                          "MD Signed By,MD Date,MD Comments," +
                          "Rejection Reason");

            foreach (var r in requests)
            {
                sb.AppendLine(string.Join(",",
                    Csv(r.RequestNumber ?? $"OSR-{r.RequestId:000000}"),
                    Csv(r.PartNumber),
                    Csv(r.SapDescription),
                    Csv(r.DrawingNumber),
                    Csv(r.Quantity),
                    r.StartDate?.ToString("dd/MM/yyyy") ?? "",
                    r.EndDate?.ToString("dd/MM/yyyy") ?? "",
                    Csv(RequestStatus.Label(r.Status)),
                    Csv(r.CreatedByUsername),
                    r.CreatedAt.ToString("dd/MM/yyyy HH:mm"),
                    Csv(r.PPAPNumber),
                    Csv(r.ConcessionNumber),
                    r.SpecialPackingRequired.HasValue ? (r.SpecialPackingRequired.Value ? "Yes" : "No") : "",
                    Csv(r.JFSignedBy), r.JFSignedDate?.ToString("dd/MM/yyyy HH:mm") ?? "", Csv(r.JFComments),
                    Csv(r.LJSignedBy), r.LJSignedDate?.ToString("dd/MM/yyyy HH:mm") ?? "", Csv(r.LJComments),
                    Csv(r.CostEnteredBy), r.CostEnteredAt?.ToString("dd/MM/yyyy HH:mm") ?? "", Csv(r.CostComments),
                    Csv(r.SGSignedBy), r.SGSignedDate?.ToString("dd/MM/yyyy HH:mm") ?? "", Csv(r.SGComments),
                    Csv(r.MDSignedBy), r.MDSignedDate?.ToString("dd/MM/yyyy HH:mm") ?? "", Csv(r.MDComments),
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
