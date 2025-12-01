using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Project_Advanced.Data;
using Project_Advanced.Models;
using Project_Advanced.Models.ViewModels;

namespace Project_Advanced.Controllers
{
    [Authorize]
    public class InvoicesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private const int PageSize = 10;

        public InvoicesController(ApplicationDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // GET: Invoices
        public async Task<IActionResult> Index(string? search, int page = 1)
        {
            var invoices = _context.Invoices
                .Include(i => i.Customer)
                .Include(i => i.WorkOrder)
                    .ThenInclude(w => w.Vehicle)
                .AsQueryable();

            if (!User.IsInRole("Admin"))
            {
                var currentCustomer = await EnsureCurrentCustomerAsync();
                if (currentCustomer == null)
                {
                    return Forbid();
                }
                invoices = invoices.Where(i => i.CustomerId == currentCustomer.Id);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                invoices = invoices.Where(i =>
                    i.Id.ToString().Contains(term) ||
                    ((i.Customer!.FirstName + " " + i.Customer!.LastName).Contains(term)) ||
                    (i.WorkOrder != null && i.WorkOrder.Vehicle != null && i.WorkOrder.Vehicle.PlateNumber.Contains(term)) ||
                    i.Status.ToString().Contains(term));
            }

            var paged = await PaginatedList<Invoice>.CreateAsync(
                invoices.OrderByDescending(i => i.IssuedAt),
                page,
                PageSize);

            ViewData["page"] = page;
            ViewData["search"] = search;
            return View(paged);
        }

        // GET: Invoices/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var invoice = await _context.Invoices
                .Include(i => i.Customer)
                .Include(i => i.WorkOrder)
                    .ThenInclude(w => w.Vehicle)
                .Include(i => i.WorkOrder)
                    .ThenInclude(w => w.Items)
                .Include(i => i.Payments)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (invoice == null || !await CanAccessInvoiceAsync(invoice.CustomerId))
            {
                return NotFound();
            }

            await RecalculateInvoiceTotalsAsync(invoice);

            return View(invoice);
        }

        // GET: Invoices/Create
        [Authorize(Roles = "Admin")]
        public IActionResult Create()
        {
            LoadLookups();
            return View();
        }

        // POST: Invoices/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create([Bind("WorkOrderId,CustomerId,IssuedAt,Subtotal,TaxAmount,Discount,Total,Status,Id")] Invoice invoice)
        {
            if (ModelState.IsValid)
            {
                _context.Add(invoice);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            LoadLookups(invoice.CustomerId, invoice.WorkOrderId);
            return View(invoice);
        }

        // GET: Invoices/Edit/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var invoice = await _context.Invoices.FindAsync(id);
            if (invoice == null)
            {
                return NotFound();
            }
            LoadLookups(invoice.CustomerId, invoice.WorkOrderId);
            return View(invoice);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GenerateForWorkOrder(int workOrderId)
        {
            // Load the work order with items, vehicle, and customer
            var workOrder = await _context.WorkOrders
                .Include(w => w.Vehicle)
                    .ThenInclude(v => v.Customer)
                .Include(w => w.Items)
                .FirstOrDefaultAsync(w => w.Id == workOrderId);

            if (workOrder == null)
            {
                return NotFound();
            }

            // If invoice already exists for this work order, go to it instead of creating a duplicate
            var existing = await _context.Invoices
                .FirstOrDefaultAsync(i => i.WorkOrderId == workOrderId);

            if (existing != null)
            {
                return RedirectToAction("Details", new { id = existing.Id });
            }

            var customerId = workOrder.Vehicle.CustomerId;
            var subtotal = workOrder.Items?.Sum(i => i.LineTotal) ?? 0m;

            // Adjust these according to your needs
            var taxRate = 0.0m; // e.g., 0.11m for 11% VAT
            var tax = subtotal * taxRate;
            var discount = 0m;
            var total = subtotal + tax - discount;

            var invoice = new Invoice
            {
                WorkOrderId = workOrder.Id,
                CustomerId = customerId,
                IssuedAt = DateTime.UtcNow,
                TaxAmount = tax,
                Discount = discount,
                Total = total,
                Status = InvoiceStatus.Unpaid
            };

            _context.Invoices.Add(invoice);
            await _context.SaveChangesAsync();

            // Redirect to view or edit the invoice
            return RedirectToAction("Details", new { id = invoice.Id });
        }

        // POST: Invoices/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id, [Bind("WorkOrderId,CustomerId,IssuedAt,Subtotal,TaxAmount,Discount,Total,Status,Id")] Invoice invoice)
        {
            if (id != invoice.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(invoice);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!InvoiceExists(invoice.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            LoadLookups(invoice.CustomerId, invoice.WorkOrderId);
            return View(invoice);
        }

        // GET: Invoices/Delete/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var invoice = await _context.Invoices
                .Include(i => i.Customer)
                .Include(i => i.WorkOrder)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (invoice == null)
            {
                return NotFound();
            }

            return View(invoice);
        }

        // POST: Invoices/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var invoice = await _context.Invoices.FindAsync(id);
            if (invoice != null)
            {
                _context.Invoices.Remove(invoice);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool InvoiceExists(int id)
        {
            return _context.Invoices.Any(e => e.Id == id);
        }

        private async Task RecalculateInvoiceTotalsAsync(Invoice invoice)
        {
            var subtotal = invoice.WorkOrder?.Items?.Sum(i => i.Quantity * i.UnitPrice) ?? 0m;
            var total = subtotal + invoice.TaxAmount - invoice.Discount;
            var paid = invoice.Payments?.Sum(p => p.Amount) ?? 0m;
            var outstanding = total - paid;

            var newStatus = invoice.Status;
            if (invoice.Status != InvoiceStatus.Unpaid)
            {
                newStatus = outstanding > 0 ? InvoiceStatus.PartiallyPaid : InvoiceStatus.Paid;
            }

            if (invoice.Subtotal != subtotal || invoice.Total != total || newStatus != invoice.Status)
            {
                invoice.Subtotal = subtotal;
                invoice.Total = total;
                invoice.Status = newStatus;
                _context.Invoices.Update(invoice);
                await _context.SaveChangesAsync();
            }
        }

        private async Task<Customer?> EnsureCurrentCustomerAsync()
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return null;

            var customer = await _context.Customers.FirstOrDefaultAsync(c => c.ApplicationUserId == userId);
            if (customer == null)
            {
                var user = await _userManager.GetUserAsync(User);
                if (user == null) return null;
                var first = (user.Email ?? user.UserName ?? "Customer").Split('@').FirstOrDefault() ?? "Customer";
                customer = new Customer
                {
                    ApplicationUserId = userId,
                    FirstName = first,
                    LastName = "User",
                    Email = user.Email
                };
                _context.Customers.Add(customer);
                await _context.SaveChangesAsync();
            }

            var userEntity = await _userManager.GetUserAsync(User);
            if (userEntity != null && !await _userManager.IsInRoleAsync(userEntity, "Customer"))
            {
                await _userManager.AddToRoleAsync(userEntity, "Customer");
            }

            return customer;
        }

        private async Task<bool> CanAccessInvoiceAsync(int customerId)
        {
            if (User.IsInRole("Admin")) return true;

            var currentCustomer = await EnsureCurrentCustomerAsync();
            return currentCustomer != null && currentCustomer.Id == customerId;
        }

        private void LoadLookups(int? selectedCustomerId = null, int? selectedWorkOrderId = null)
        {
            var customers = _context.Customers
                .Select(c => new
                {
                    c.Id,
                    Label = $"{c.FirstName} {c.LastName} - {(string.IsNullOrWhiteSpace(c.Phone) ? "No phone" : c.Phone)}{(string.IsNullOrWhiteSpace(c.Email) ? "" : $" ({c.Email})")}"
                })
                .ToList();

            var workOrders = _context.WorkOrders
                .Include(w => w.Vehicle)
                    .ThenInclude(v => v.Customer)
                .Select(w => new
                {
                    w.Id,
                    CustomerId = w.Vehicle.CustomerId,
                    Label = $"{w.Id} - {w.Vehicle.PlateNumber} {w.Vehicle.Make} {w.Vehicle.Model} - {w.Vehicle.Customer.FirstName} {w.Vehicle.Customer.LastName}"
                })
                .ToList();

            ViewBag.CustomerOptions = customers;
            ViewBag.WorkOrderOptions = workOrders;
            ViewData["CustomerId"] = new SelectList(customers, "Id", "Label", selectedCustomerId);
            ViewData["WorkOrderId"] = new SelectList(workOrders, "Id", "Label", selectedWorkOrderId);
        }
    }
}
