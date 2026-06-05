using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace OutsourceRequestApp.Models
{
    public class OutsourceRequest
    {
        [Key]
        public int RequestId { get; set; }

        // Auto-generated human reference e.g. OSR-2026-0001
        public string? RequestNumber { get; set; }

        [Required]
        public string PartNumber { get; set; } = string.Empty;
        public string? SapDescription { get; set; }
        public string? DrawingNumber { get; set; }

        // Free-text quantity (e.g. "50 per week", "batch of 200")
        [Required]
        public string Quantity { get; set; } = string.Empty;

        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        [Required]
        public string Reason { get; set; } = string.Empty;

        [Required]
        public string Status { get; set; } = RequestStatus.Submitted;

        [Required]
        public string CreatedByUsername { get; set; } = "Unknown";

        public string? AttachmentPath { get; set; }
        public DateTime CreatedAt { get; set; }

        // PPAP / Concession
        public bool? PpapRequired { get; set; }
        public string? PPAPNumber { get; set; }
        public string? ConcessionNumber { get; set; }

        // Special packing
        public bool? SpecialPackingRequired { get; set; }
        public string? SpecialPackingDetails { get; set; }

        // Step 2 — John Fisher (Work Prep Manager)
        public string? JFSignedBy { get; set; }
        public DateTime? JFSignedDate { get; set; }
        public string? JFComments { get; set; }

        // Step 3 — Lukasz Jaworski (Production Manager)
        public string? LJSignedBy { get; set; }
        public DateTime? LJSignedDate { get; set; }
        public string? LJComments { get; set; }

        // Step 4 — Cost Impact (entered by Supply Chain)
        // Line-item costs stored in OutsourceRequestCostLine
        public string? CostEnteredBy { get; set; }
        public DateTime? CostEnteredAt { get; set; }
        public string? CostComments { get; set; }

        // Legacy cost fields (kept for existing data compatibility)
        public bool? PpapRequiredLegacy { get; set; }
        public decimal? CostInhousePerMonth { get; set; }
        public decimal? CostOutsourcePerMonth { get; set; }

        // Step 5 — Simon Graham (Supply Chain Manager)
        public string? SGSignedBy { get; set; }
        public DateTime? SGSignedDate { get; set; }
        public string? SGComments { get; set; }

        // Step 6 — Patrick MacDonough (Managing Director)
        public string? MDSignedBy { get; set; }
        public DateTime? MDSignedDate { get; set; }
        public string? MDComments { get; set; }

        // Rejection
        public string? RejectionReason { get; set; }
        public string? RejectedBy { get; set; }
        public DateTime? RejectedAt { get; set; }

        // Legacy approval fields (kept for existing data)
        public DateTime? ScReviewedAt { get; set; }
        public string? ScReviewedBy { get; set; }
        public string? ScComments { get; set; }

        public DateTime? FinanceReviewedAt { get; set; }
        public string? FinanceReviewedBy { get; set; }
        public string? FinanceComments { get; set; }

        public DateTime? MdReviewedAt { get; set; }
        public string? MdReviewedBy { get; set; }
        public string? MdComments { get; set; }

        // Reminder tracking
        public DateTime? LastReminderSentAt { get; set; }

        // Navigation properties
        public ICollection<OutsourceRequestCostLine> CostLines { get; set; } = new List<OutsourceRequestCostLine>();
        public ICollection<OutsourceRequestAuditLog> AuditLogs { get; set; } = new List<OutsourceRequestAuditLog>();
    }
}
