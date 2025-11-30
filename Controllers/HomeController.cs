using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Project_Advanced.Data;
using Project_Advanced.Models;
using Project_Advanced.Models.ViewModels;

namespace Project_Advanced.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;

        public HomeController(
            ILogger<HomeController> logger,
            ApplicationDbContext context,
            UserManager<IdentityUser> userManager)
        {
            _logger = logger;
            _context = context;
            _userManager = userManager;
        }

        // Home page: Admin -> admin dashboard, Customer -> personal dashboard
        [Authorize]
        public async Task<IActionResult> Index()
        {
            // If admin, use existing admin dashboard
            if (User.IsInRole("Admin"))
            {
                return RedirectToAction("Index", "Dashboard");
            }

            // Normal authenticated user -> customer dashboard
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var customer = await _context.Customers
                .FirstOrDefaultAsync(c => c.ApplicationUserId == user.Id);

            if (customer == null)
            {
                // User exists but has no customer profile yet
                var emptyVm = new CustomerDashboardViewModel
                {
                    CustomerName = user.UserName ?? user.Email ?? "Customer"
                };
                return View(emptyVm);
            }

            var today = DateTime.Today;

            // 1) Outstanding payments / invoices for this customer
            var invoices = await _context.Invoices
                .Include(i => i.WorkOrder)
                    .ThenInclude(w => w.Vehicle)
                .Include(i => i.Payments)
                .Where(i => i.CustomerId == customer.Id)
                .ToListAsync();

            var outstandingInvoices = invoices
                .Select(i =>
                {
                    var paid = i.Payments?.Sum(p => p.Amount) ?? 0m;
                    var outstanding = i.Total - paid;

                    return new
                    {
                        Invoice = i,
                        Paid = paid,
                        Outstanding = outstanding
                    };
                })
                .Where(x => x.Outstanding > 0)
                .OrderByDescending(x => x.Invoice.IssuedAt)
                .Take(5)
                .Select(x => new CustomerInvoiceSummary
                {
                    InvoiceId = x.Invoice.Id,
                    IssuedAt = x.Invoice.IssuedAt,
                    Vehicle = x.Invoice.WorkOrder?.Vehicle != null
                        ? $"{x.Invoice.WorkOrder.Vehicle.PlateNumber} - {x.Invoice.WorkOrder.Vehicle.Make} {x.Invoice.WorkOrder.Vehicle.Model}"
                        : "N/A",
                    TotalAmount = x.Invoice.Total,
                    PaidAmount = x.Paid,
                    OutstandingAmount = x.Outstanding,
                    Status = x.Invoice.Status.ToString()
                })
                .ToList();

            // 2) Today's appointments
            var todayAppointments = await _context.Appointments
                .Include(a => a.Vehicle)
                .Where(a => a.CustomerId == customer.Id)
                .Where(a => a.AppointmentDate.Date == today)
                .Where(a => a.Status != AppointmentStatus.Cancelled)
                .OrderBy(a => a.AppointmentDate)
                .Select(a => new CustomerAppointmentSummary
                {
                    AppointmentId = a.Id,
                    AppointmentDate = a.AppointmentDate,
                    Vehicle = a.Vehicle != null
                        ? $"{a.Vehicle.PlateNumber} - {a.Vehicle.Make} {a.Vehicle.Model}"
                        : "N/A",
                    Reason = a.Reason ?? "",
                    Status = a.Status.ToString()
                })
                .ToListAsync();

            // 3) Open work orders (not completed / invoiced)
            var closedStatuses = new[] { WorkOrderStatus.Completed, WorkOrderStatus.Invoiced };

            var openWorkOrders = await _context.WorkOrders
                .Include(w => w.Vehicle)
                    .ThenInclude(v => v.Customer)
                .Where(w => w.Vehicle.CustomerId == customer.Id)
                .Where(w => !closedStatuses.Contains(w.Status))
                .OrderByDescending(w => w.CreatedAt)
                .Select(w => new CustomerWorkOrderSummary
                {
                    WorkOrderId = w.Id,
                    CreatedAt = w.CreatedAt,
                    Vehicle = $"{w.Vehicle.PlateNumber} - {w.Vehicle.Make} {w.Vehicle.Model}",
                    ProblemDescription = w.ProblemDescription ?? "",
                    Status = w.Status.ToString()
                })
                .ToListAsync();

            var vm = new CustomerDashboardViewModel
            {
                CustomerName = $"{customer.FirstName} {customer.LastName}".Trim(),
                OutstandingInvoices = outstandingInvoices,
                TodayAppointments = todayAppointments,
                OpenWorkOrders = openWorkOrders
            };

            return View(vm);
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
