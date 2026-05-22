using System.ComponentModel.DataAnnotations.Schema;

namespace OutsourceRequestApp.Models
{
    [Table("zmm_myart01", Schema = "dbo")]
    public class Article
    {
        [Column("Material")]
        public string? Material { get; set; }

        [Column("MaterialDesc")]
        public string? MaterialDesc { get; set; }
    }
}