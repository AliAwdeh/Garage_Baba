using System;
using System.ComponentModel.DataAnnotations;

namespace Project_Advanced.Models
{
    public class Payment : BaseEntity
    {
        public int InvoiceId { get; set; }
        public Invoice ? Invoice { get; set; }

        public DateTime PaidAt { get; set; } = DateTime.UtcNow;

        [Range(0, 9999999)]
        public decimal Amount { get; set; }

        [Required]
        public PaymentMethod Method { get; set; }

        // For Stripe test integration
        public string? StripePaymentIntentId { get; set; }

        [StringLength(200)]
        public string? Notes { get; set; }
    }
}
