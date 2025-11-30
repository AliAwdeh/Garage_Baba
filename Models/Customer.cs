using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Project_Advanced.Models
{
    public class Customer : BaseEntity
    {
        [Required, StringLength(100)]
        public string FirstName { get; set; } = string.Empty;

        [Required, StringLength(100)]
        public string LastName { get; set; } = string.Empty;

        [Phone]
        public string? Phone { get; set; }

        [EmailAddress]
        public string? Email { get; set; }

        [StringLength(200)]
        public string? Address { get; set; }

        // Optional: link Identity user so they can log in and see their data
        public string? ApplicationUserId { get; set; }

        public ICollection<Vehicle> Vehicles { get; set; } = new List<Vehicle>();
        public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
        public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
    }
}
