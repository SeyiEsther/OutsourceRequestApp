using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OutsourceRequestApp.Models
{
    public class OutsourceRequestCostLine
    {
        [Key]
        public int Id { get; set; }

        public int RequestId { get; set; }

        [Required]
        public string Description { get; set; } = string.Empty;

        public decimal InhouseCost { get; set; }
        public decimal OutsourceCost { get; set; }

        // Total = OutsourceCost - InhouseCost (calculated and stored for reporting)
        public decimal Total { get; set; }

        [ForeignKey(nameof(RequestId))]
        public OutsourceRequest? Request { get; set; }
    }
}
