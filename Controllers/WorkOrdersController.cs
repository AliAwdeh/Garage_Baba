using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Project_Advanced.Data;
using Project_Advanced.Models;
using Project_Advanced.Models.ViewModels;
using System.Linq;

namespace Project_Advanced.Controllers
{
    [Authorize] // later you can restrict to Admin for some actions
    public class WorkOrdersController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;

        public WorkOrdersController(ApplicationDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        private const int PageSize = 10;

        // GET: WorkOrders
        public async Task<IActionResult> Index(string? searchPlate, WorkOrderStatus? status, int page = 1)
        {
            var query = _context.WorkOrders
                .Include(w => w.Vehicle)
                    .ThenInclude(v => v.Customer)
                .Include(w => w.Items)
                .AsQueryable();

            if (!User.IsInRole("Admin"))
            {
                var currentCustomer = await EnsureCurrentCustomerAsync();
                if (currentCustomer == null)
                {
                    return Forbid();
                }
                query = query.Where(w => w.Vehicle.CustomerId == currentCustomer.Id);
            }

            if (!string.IsNullOrWhiteSpace(searchPlate))
            {
                query = query.Where(w =>
                    w.Vehicle.PlateNumber.Contains(searchPlate) ||
                    w.Vehicle.Make.Contains(searchPlate) ||
                    w.Vehicle.Model.Contains(searchPlate) ||
                    (w.Vehicle.Customer.FirstName + " " + w.Vehicle.Customer.LastName)
                        .Contains(searchPlate));
            }

            if (status.HasValue)
            {
                query = query.Where(w => w.Status == status);
            }

            var list = await PaginatedList<Project_Advanced.Models.WorkOrder>.CreateAsync(
                query.OrderByDescending(w => w.CreatedAt),
                page,
                PageSize);

            ViewData["searchPlate"] = searchPlate;
            ViewData["status"] = status;
            ViewData["page"] = page;

            return View(list);
        }

        // GET: WorkOrders/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var workOrder = await _context.WorkOrders
                .Include(w => w.Vehicle)
                    .ThenInclude(v => v.Customer)
                .Include(w => w.Items)
                    .ThenInclude(i => i.Part)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (workOrder == null || !await CanAccessWorkOrderAsync(workOrder.Vehicle.CustomerId)) return NotFound();

            var invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.WorkOrderId == workOrder.Id);
            ViewBag.Invoice = invoice;

            return View(workOrder);
        }

        // GET: WorkOrders/Create
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create()
        {
            var vehicles = await BuildVehicleLookupAsync();
            ViewData["VehicleId"] = new SelectList(vehicles, "Id", "Text");
            return View();
        }

        // POST: WorkOrders/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create([Bind("VehicleId,ProblemDescription,RecordedOdometer")] WorkOrder workOrder)
        {
            ModelState.Remove(nameof(WorkOrder.Vehicle));
            ModelState.Remove(nameof(WorkOrder.Items));

            if (workOrder.VehicleId <= 0)
            {
                ModelState.AddModelError(nameof(workOrder.VehicleId), "Select a vehicle by plate number.");
            }

            if (ModelState.IsValid)
            {
                workOrder.CreatedAt = DateTime.UtcNow;
                workOrder.Status = WorkOrderStatus.Open;
                _context.Add(workOrder);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Edit), new { id = workOrder.Id });
            }

            var vehicles = await BuildVehicleLookupAsync();
            ViewData["VehicleId"] = new SelectList(vehicles, "Id", "Text", workOrder.VehicleId);
            return View(workOrder);
        }

        // GET: WorkOrders/Edit/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var workOrder = await _context.WorkOrders
                .Include(w => w.Vehicle)
                    .ThenInclude(v => v.Customer)
                .Include(w => w.Items)
                    .ThenInclude(i => i.Part)
                .FirstOrDefaultAsync(w => w.Id == id);

            if (workOrder == null) return NotFound();

            ViewBag.Parts = await _context.Parts.ToListAsync();
            var vehicles = await BuildVehicleLookupAsync();
            ViewData["VehicleId"] = new SelectList(vehicles, "Id", "Text", workOrder.VehicleId);

            return View(workOrder);
        }

        // POST: WorkOrders/Edit/5  (edit main fields only)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id, [Bind("Id,VehicleId,ProblemDescription,RecordedOdometer,Status,CreatedAt")] WorkOrder workOrder)
        {
            if (id != workOrder.Id) return NotFound();

            ModelState.Remove(nameof(WorkOrder.Vehicle));
            ModelState.Remove(nameof(WorkOrder.Items));

            if (workOrder.VehicleId <= 0)
            {
                ModelState.AddModelError(nameof(workOrder.VehicleId), "Select a vehicle.");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(workOrder);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!WorkOrderExists(workOrder.Id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            var vehicles = await BuildVehicleLookupAsync();
            ViewData["VehicleId"] = new SelectList(vehicles, "Id", "Text", workOrder.VehicleId);
            ViewBag.Parts = await _context.Parts.ToListAsync();
            return View(workOrder);
        }

        // POST: WorkOrders/AddItem
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AddItem(int workOrderId, WorkOrderItem item)
        {
            item.WorkOrderId = workOrderId;

            var validationMessages = new List<string>();
            Part? selectedPart = null;

            // normalize inputs before validation
            if (item.ItemType == WorkOrderItemType.Part)
            {
                if (!item.PartId.HasValue)
                {
                    validationMessages.Add("Select a part.");
                }
                else
                {
                    selectedPart = await _context.Parts.FindAsync(item.PartId.Value);
                    if (selectedPart != null)
                    {
                        if (item.UnitPrice <= 0)
                        {
                            item.UnitPrice = selectedPart.UnitPrice;
                        }
                        if (string.IsNullOrWhiteSpace(item.Description))
                        {
                            item.Description = selectedPart.Name;
                        }

                        if (item.Quantity <= 0)
                        {
                            validationMessages.Add("Quantity must be greater than zero.");
                        }
                        else
                        {
                            if (item.Quantity % 1 != 0)
                            {
                                validationMessages.Add("Part quantity must be a whole number.");
                            }
                            else
                            {
                                var quantityToUse = (int)Math.Ceiling(item.Quantity);
                                if (quantityToUse > selectedPart.StockQuantity)
                                {
                                    validationMessages.Add($"Not enough stock for {selectedPart.Name}. Available: {selectedPart.StockQuantity}.");
                                }
                            }
                        }
                    }
                    else
                    {
                        validationMessages.Add("Selected part not found.");
                    }
                }
            }
            else
            {
                item.PartId = null; // labor should not carry a part
                if (string.IsNullOrWhiteSpace(item.Description))
                {
                    validationMessages.Add("Description is required for labor.");
                }
                if (item.Quantity <= 0)
                {
                    validationMessages.Add("Quantity must be greater than zero.");
                }
            }

            ModelState.Clear();
            TryValidateModel(item);
            foreach (var msg in validationMessages)
            {
                ModelState.AddModelError(string.Empty, msg);
            }

            if (!ModelState.IsValid)
            {
                TempData["WorkOrderItemError"] = string.Join("; ", ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .Where(e => !string.IsNullOrWhiteSpace(e)));
                return RedirectToAction(nameof(Edit), new { id = workOrderId });
            }

            _context.WorkOrderItems.Add(item);

            if (item.ItemType == WorkOrderItemType.Part && selectedPart != null)
            {
                var quantityToUse = (int)Math.Ceiling(item.Quantity);
                selectedPart.StockQuantity -= quantityToUse;
                _context.Parts.Update(selectedPart);
            }

            await _context.SaveChangesAsync();

            await RecalculateInvoiceForWorkOrderAsync(workOrderId);

            return RedirectToAction(nameof(Edit), new { id = workOrderId });
        }

        // POST: WorkOrders/DeleteItem/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteItem(int id)
        {
            var item = await _context.WorkOrderItems.FindAsync(id);
            if (item == null) return NotFound();

            var workOrderId = item.WorkOrderId;

            if (item.ItemType == WorkOrderItemType.Part && item.PartId.HasValue)
            {
                var part = await _context.Parts.FindAsync(item.PartId.Value);
                if (part != null)
                {
                    var quantityToReturn = (int)Math.Ceiling(item.Quantity);
                    part.StockQuantity += quantityToReturn;
                    _context.Parts.Update(part);
                }
            }

            _context.WorkOrderItems.Remove(item);
            await _context.SaveChangesAsync();

            await RecalculateInvoiceForWorkOrderAsync(workOrderId);

            return RedirectToAction(nameof(Edit), new { id = workOrderId });
        }

        // POST: WorkOrders/UpdateStatus
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateStatus(int id, WorkOrderStatus status)
        {
            var wo = await _context.WorkOrders.FindAsync(id);
            if (wo == null) return NotFound();

            wo.Status = status;
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Edit), new { id });
        }

        // GET: WorkOrders/Delete/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var workOrder = await _context.WorkOrders
                .Include(w => w.Vehicle)
                    .ThenInclude(v => v.Customer)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (workOrder == null) return NotFound();

            return View(workOrder);
        }

        // POST: WorkOrders/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var workOrder = await _context.WorkOrders.FindAsync(id);
            if (workOrder != null)
            {
                _context.WorkOrders.Remove(workOrder);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        private bool WorkOrderExists(int id)
        {
            return _context.WorkOrders.Any(e => e.Id == id);
        }

        private async Task<List<object>> BuildVehicleLookupAsync()
        {
            var vehicles = await _context.Vehicles
                .Include(v => v.Customer)
                .Select(v => new
                {
                    v.Id,
                    Text = v.PlateNumber + " - " + v.Make + " " + v.Model +
                           " (" + v.Customer.FirstName + " " + v.Customer.LastName + ")"
                })
                .ToListAsync();

            return vehicles.Cast<object>().ToList();
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

        private async Task<bool> CanAccessWorkOrderAsync(int customerId)
        {
            if (User.IsInRole("Admin"))
            {
                return true;
            }

            var currentCustomer = await EnsureCurrentCustomerAsync();
            return currentCustomer != null && currentCustomer.Id == customerId;
        }

        private async Task RecalculateInvoiceForWorkOrderAsync(int workOrderId)
        {
            var invoice = await _context.Invoices
                .Include(i => i.Payments)
                .FirstOrDefaultAsync(i => i.WorkOrderId == workOrderId);

            if (invoice == null) return;

            var subtotal = await _context.WorkOrderItems
                .Where(i => i.WorkOrderId == workOrderId)
                .SumAsync(i => i.Quantity * i.UnitPrice);

            invoice.Subtotal = subtotal;
            invoice.TaxAmount = 0m; // adjust if you add tax logic later
            var discount = invoice.Discount;
            invoice.Total = subtotal + invoice.TaxAmount - discount;

            var paid = invoice.Payments?.Sum(p => p.Amount) ?? 0m;
            var outstanding = invoice.Total - paid;

            if (invoice.Status != InvoiceStatus.Unpaid)
            {
                invoice.Status = outstanding > 0 ? InvoiceStatus.PartiallyPaid : InvoiceStatus.Paid;
            }

            _context.Invoices.Update(invoice);
            await _context.SaveChangesAsync();
        }
    }
}
