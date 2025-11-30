using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Project_Advanced.Models
{
    public class Vehicle : BaseEntity
    {
        [Required, StringLength(20)]
        public string PlateNumber { get; set; } = string.Empty;

        [Required, StringLength(50)]
        public string Make { get; set; } = string.Empty;

        [Required, StringLength(50)]
        public string Model { get; set; } = string.Empty;

        public int Year { get; set; }

        // For oil-change and reference
        public int? CurrentOdometer { get; set; }

        // FK
        public int CustomerId { get; set; }
        public Customer ? Customer { get; set; }

        public ICollection<WorkOrder> WorkOrders { get; set; } = new List<WorkOrder>();
        public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
    }
}
