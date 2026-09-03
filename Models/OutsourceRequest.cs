using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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
        [Range(1, int.MaxValue, ErrorMessage = "Quantity must be at least 1.")]
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

        /// <summary>
        /// When the request entered its CURRENT status — i.e. how long it has
        /// actually been sitting with whoever needs to act on it right now.
        /// Deliberately NOT the same as CreatedAt: a request that took days to
        /// clear Work Prep and Production would otherwise look instantly
        /// "overdue" for Sourcing the moment it lands there, since CreatedAt
        /// is the original submission time regardless of how far the request
        /// has since moved. Used for reminder timing and every "waiting X"
        /// display instead of CreatedAt.
        /// </summary>
        [NotMapped]
        public DateTime CurrentStageEnteredAt => Status switch
        {
            RequestStatus.ProductionPending  => JFSignedDate ?? CreatedAt,
            RequestStatus.CostCompactPending => LJSignedDate ?? CreatedAt,
            RequestStatus.SourcingPending    => ScReviewedAt ?? CreatedAt,
            RequestStatus.MdPending          => SGSignedDate ?? CreatedAt,
            _                                 => CreatedAt
        };
    }
}
