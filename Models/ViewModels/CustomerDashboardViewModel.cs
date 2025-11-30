using System;
using System.Collections.Generic;

namespace Project_Advanced.Models.ViewModels
{
    public class CustomerDashboardViewModel
    {
        public string? CustomerName { get; set; }

        public List<CustomerInvoiceSummary> OutstandingInvoices { get; set; } = new();
        public List<CustomerAppointmentSummary> TodayAppointments { get; set; } = new();
        public List<CustomerWorkOrderSummary> OpenWorkOrders { get; set; } = new();

        public decimal TotalOutstanding =>
            OutstandingInvoices?.Sum(i => i.OutstandingAmount) ?? 0m;
    }

    public class CustomerInvoiceSummary
    {
        public int InvoiceId { get; set; }
        public DateTime IssuedAt { get; set; }
        public string Vehicle { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal OutstandingAmount { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    public class CustomerAppointmentSummary
    {
        public int AppointmentId { get; set; }
        public DateTime AppointmentDate { get; set; }
        public string Vehicle { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    public class CustomerWorkOrderSummary
    {
        public int WorkOrderId { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Vehicle { get; set; } = string.Empty;
        public string ProblemDescription { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }
}
