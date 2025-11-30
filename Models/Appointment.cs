using System;
using System.ComponentModel.DataAnnotations;

namespace Project_Advanced.Models
{
    public class Appointment : BaseEntity
    {
        public int CustomerId { get; set; }
        public Customer ? Customer { get; set; }

        public int? VehicleId { get; set; }
        public Vehicle? Vehicle { get; set; }

        public DateTime AppointmentDate { get; set; }

        [StringLength(500)]
        public string? Reason { get; set; }

        public AppointmentStatus Status { get; set; } = AppointmentStatus.Pending;
    }
}
