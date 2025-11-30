using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Project_Advanced.Models
{
    public class Invoice : BaseEntity
    {
        public int WorkOrderId { get; set; }
        public WorkOrder ? WorkOrder { get; set; }

        public int CustomerId { get; set; }
        public Customer ? Customer { get; set; }

        public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

        [Range(0, 9999999)]
        public decimal Subtotal { get; set; }

        [Range(0, 9999999)]
        public decimal TaxAmount { get; set; }

        [Range(0, 9999999)]
        public decimal Discount { get; set; }

        [Range(0, 9999999)]
        public decimal Total { get; set; }

        public InvoiceStatus Status { get; set; } = InvoiceStatus.Unpaid;

        public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    }
}
