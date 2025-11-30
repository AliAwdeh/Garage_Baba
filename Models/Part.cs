using System.ComponentModel.DataAnnotations;

namespace Project_Advanced.Models
{
    public class Part : BaseEntity
    {
        [Required, StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(50)]
        public string? PartNumber { get; set; }

        [Range(0, 999999)]
        public decimal UnitPrice { get; set; }

        public int StockQuantity { get; set; }
    }
}
