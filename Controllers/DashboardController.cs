using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Project_Advanced.Data;
using Project_Advanced.Models;
using Project_Advanced.Models.ViewModels;

namespace Project_Advanced.Controllers
{
    [Authorize(Roles = "Admin")]
    public class DashboardController : Controller
    {
        private readonly ApplicationDbContext _context;

        public DashboardController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(DateTime? laborFrom = null, DateTime? laborTo = null)
        {
            var nowUtc = DateTime.UtcNow;
            var todayUtc = nowUtc.Date;
            var todayLocal = DateTime.Today;
            var weekStartUtc = GetWeekStart(todayUtc);
            var monthStartUtc = new DateTime(todayUtc.Year, todayUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            // Labor revenue date range defaults to last 30 days (inclusive)
            var defaultStartLocal = todayLocal.AddDays(-29);
            var startLocal = laborFrom?.Date ?? defaultStartLocal;
            var endLocal = laborTo?.Date ?? todayLocal;
            if (endLocal < startLocal)
            {
                (startLocal, endLocal) = (endLocal, startLocal);
            }

            var startUtc = DateTime.SpecifyKind(startLocal, DateTimeKind.Local).ToUniversalTime();
            var endExclusiveUtc = DateTime.SpecifyKind(endLocal.AddDays(1), DateTimeKind.Local).ToUniversalTime();

            var closedStatuses = new[] { WorkOrderStatus.Completed, WorkOrderStatus.Invoiced };

            var vm = new AdminDashboardViewModel
            {
                WorkOrdersCreatedToday = await _context.WorkOrders.CountAsync(w => w.CreatedAt >= todayUtc),
                WorkOrdersCreatedThisWeek = await _context.WorkOrders.CountAsync(w => w.CreatedAt >= weekStartUtc),
                WorkOrdersCreatedThisMonth = await _context.WorkOrders.CountAsync(w => w.CreatedAt >= monthStartUtc),

                WorkOrdersClosedToday = await _context.WorkOrders.CountAsync(w => closedStatuses.Contains(w.Status) && w.CreatedAt >= todayUtc),
                WorkOrdersClosedThisWeek = await _context.WorkOrders.CountAsync(w => closedStatuses.Contains(w.Status) && w.CreatedAt >= weekStartUtc),
                WorkOrdersClosedThisMonth = await _context.WorkOrders.CountAsync(w => closedStatuses.Contains(w.Status) && w.CreatedAt >= monthStartUtc),

                SalesToday = await _context.Payments.Where(p => p.PaidAt >= todayUtc).SumAsync(p => (decimal?)p.Amount) ?? 0,
                SalesThisWeek = await _context.Payments.Where(p => p.PaidAt >= weekStartUtc).SumAsync(p => (decimal?)p.Amount) ?? 0,
                SalesThisMonth = await _context.Payments.Where(p => p.PaidAt >= monthStartUtc).SumAsync(p => (decimal?)p.Amount) ?? 0,

                LaborRevenueTotal = await _context.WorkOrderItems
                    .Where(i => i.ItemType == WorkOrderItemType.Labor)
                    .SumAsync(i => (decimal?)(i.Quantity * i.UnitPrice)) ?? 0,
                PartsRevenueTotal = await _context.WorkOrderItems
                    .Where(i => i.ItemType == WorkOrderItemType.Part)
                    .SumAsync(i => (decimal?)(i.Quantity * i.UnitPrice)) ?? 0,
                LaborRangeStart = startLocal,
                LaborRangeEnd = endLocal,
                LaborRevenueInRange = await _context.WorkOrderItems
                    .Where(i => i.ItemType == WorkOrderItemType.Labor)
                    .Where(i => i.WorkOrder != null &&
                                i.WorkOrder.CreatedAt >= startUtc &&
                                i.WorkOrder.CreatedAt < endExclusiveUtc)
                    .SumAsync(i => (decimal?)(i.Quantity * i.UnitPrice)) ?? 0,

                TodaysAppointments = await _context.Appointments
                    .Include(a => a.Customer)
                    .Include(a => a.Vehicle)
                    .Where(a => a.AppointmentDate >= todayLocal && a.AppointmentDate < todayLocal.AddDays(1))
                    .OrderBy(a => a.AppointmentDate)
                    .ToListAsync()
            };

            var outstandingInvoices = await _context.Invoices
                .Include(i => i.Customer)
                .Include(i => i.WorkOrder)
                    .ThenInclude(w => w.Vehicle)
                .Select(i => new
                {
                    Invoice = i,
                    Paid = i.Payments.Sum(p => (decimal?)p.Amount) ?? 0m
                })
                .ToListAsync();

            vm.OutstandingInvoices = outstandingInvoices
                .Select(x => new OutstandingInvoiceSummary
                {
                    Invoice = x.Invoice,
                    Paid = x.Paid,
                    Outstanding = x.Invoice.Total - x.Paid
                })
                .Where(x => x.Outstanding > 0)
                .OrderByDescending(x => x.Outstanding)
                .Take(10)
                .ToList();

            vm.OutstandingTotal = vm.OutstandingInvoices.Sum(x => x.Outstanding);

            return View(vm);
        }

        private static DateTime GetWeekStart(DateTime todayUtc)
        {
            var dayOfWeek = todayUtc.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)todayUtc.DayOfWeek;
            return todayUtc.AddDays(-(dayOfWeek - 1));
        }
    }
}
