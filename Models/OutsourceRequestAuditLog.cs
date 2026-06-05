using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OutsourceRequestApp.Models
{
    public class OutsourceRequestAuditLog
    {
        [Key]
        public int Id { get; set; }

        public int RequestId { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.Now;

        [Required]
        public string Actor { get; set; } = string.Empty;

        [Required]
        public string Action { get; set; } = string.Empty;

        public string? FromStatus { get; set; }
        public string? ToStatus { get; set; }
        public string? Notes { get; set; }

        [ForeignKey(nameof(RequestId))]
        public OutsourceRequest? Request { get; set; }
    }
}
