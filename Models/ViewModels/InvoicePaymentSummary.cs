using System.Collections.Generic;
using Project_Advanced.Models;

namespace Project_Advanced.Models.ViewModels
{
    public class InvoicePaymentSummary
    {
        public Invoice Invoice { get; set; } = default!;
        public IEnumerable<Payment> Payments { get; set; } = new List<Payment>();
        public decimal TotalPaid { get; set; }
        public decimal Outstanding { get; set; }
    }
}
