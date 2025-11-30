using Project_Advanced.Models;

namespace Project_Advanced.Models.ViewModels
{
    public class CustomerInvoicePaymentViewModel
    {
        public Invoice Invoice { get; set; } = null!;
        public decimal Paid { get; set; }
        public decimal Outstanding { get; set; }
    }
}
