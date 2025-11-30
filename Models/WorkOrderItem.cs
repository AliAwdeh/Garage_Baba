using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Project_Advanced.Models
{
    public class WorkOrderItem : BaseEntity
    {
        public int WorkOrderId { get; set; }
        public WorkOrder ? WorkOrder { get; set; }

        [Required]
        public WorkOrderItemType ItemType { get; set; }

        // If it's a Part line
        public int? PartId { get; set; }
        public Part? Part { get; set; }

        [Required, StringLength(200)]
        public string Description { get; set; } = string.Empty;

        [Range(0, 9999)]
        public decimal Quantity { get; set; }

        [Range(0, 999999)]
        public decimal UnitPrice { get; set; }

        [NotMapped]
        public decimal LineTotal => Quantity * UnitPrice;
    }
}
