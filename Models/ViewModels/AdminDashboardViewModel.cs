using System;
using System.Collections.Generic;
using Project_Advanced.Models;

namespace Project_Advanced.Models.ViewModels
{
    public class AdminDashboardViewModel
    {
        public int WorkOrdersCreatedToday { get; set; }
        public int WorkOrdersCreatedThisWeek { get; set; }
        public int WorkOrdersCreatedThisMonth { get; set; }

        public int WorkOrdersClosedToday { get; set; }
        public int WorkOrdersClosedThisWeek { get; set; }
        public int WorkOrdersClosedThisMonth { get; set; }

        public decimal SalesToday { get; set; }
        public decimal SalesThisWeek { get; set; }
        public decimal SalesThisMonth { get; set; }

        public decimal LaborRevenueTotal { get; set; }
        public decimal PartsRevenueTotal { get; set; }

        public List<Appointment> TodaysAppointments { get; set; } = new();

        public List<OutstandingInvoiceSummary> OutstandingInvoices { get; set; } = new();
        public decimal OutstandingTotal { get; set; }
    }

    public class OutstandingInvoiceSummary
    {
        public Invoice Invoice { get; set; } = default!;
        public decimal Outstanding { get; set; }
        public decimal Paid { get; set; }
    }
}
