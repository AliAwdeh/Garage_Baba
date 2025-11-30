using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Project_Advanced.Models
{
    public class WorkOrder : BaseEntity
    {
        public int VehicleId { get; set; }
        public Vehicle ? Vehicle { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Required]
        public WorkOrderStatus Status { get; set; } = WorkOrderStatus.Open;

        // free text typed by customer or admin
        [StringLength(1000)]
        public string? ProblemDescription { get; set; }

        // snapshot for oil changes etc.
        public int? RecordedOdometer { get; set; }

        public ICollection<WorkOrderItem> Items { get; set; } = new List<WorkOrderItem>();

        // convenience read-only
        public decimal Total =>
            Items?.Sum(i => i.LineTotal) ?? 0m;
    }
}
