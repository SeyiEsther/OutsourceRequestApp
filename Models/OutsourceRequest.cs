using System;
using System.ComponentModel.DataAnnotations;

namespace OutsourceRequestApp.Models
{
    public class OutsourceRequest
    {
        [Key]
        public int RequestId { get; set; }

        [Required]
        public string PartNumber { get; set; } = string.Empty;
        public string? SapDescription { get; set; }
        public string? DrawingNumber { get; set; }

        [Required]
        public int Quantity { get; set; }

        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        [Required]
        public string Reason { get; set; } = string.Empty;

        [Required]
        public string Status { get; set; } = "Submitted";

        [Required]
        public string CreatedByUsername { get; set; } = "Unknown";

        public string? AttachmentPath { get; set; }
        public DateTime CreatedAt { get; set; }

        // Approval timestamps
        public DateTime? ScReviewedAt { get; set; }
        public string? ScReviewedBy { get; set; }
        public string? ScComments { get; set; }

        public DateTime? FinanceReviewedAt { get; set; }
        public string? FinanceReviewedBy { get; set; }
        public string? FinanceComments { get; set; }

        public DateTime? MdReviewedAt { get; set; }
        public string? MdReviewedBy { get; set; }
        public string? MdComments { get; set; }

        // Section 2 fields (Supply Chain fills these)
        public bool? PpapRequired { get; set; }
        public decimal? CostInhousePerMonth { get; set; }
        public decimal? CostOutsourcePerMonth { get; set; }
        public string? CostComments { get; set; }

        // Reminder tracking
        public DateTime? LastReminderSentAt { get; set; }

        // Stage 1 — John Fisher, Work Preparation Manager (sign-only)
        public string? JFSignedBy { get; set; }
        public DateTime? JFSignedDate { get; set; }

        // Stage 2 — Lukasz Jaworski, Production Manager (sign-only)
        public string? LJSignedBy { get; set; }
        public DateTime? LJSignedDate { get; set; }

        // Stage 4 — Simon Graham, Sourcing & Procurement Manager (sign-only)
        public string? SGSignedBy { get; set; }
        public DateTime? SGSignedDate { get; set; }

        // Generic rejection record — populated at whichever stage rejects the request
        public string? RejectionReason { get; set; }
        public string? RejectedBy { get; set; }
        public DateTime? RejectedAt { get; set; }
    }
}
