using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Project_Advanced.Data;
using Project_Advanced.Models;
using Project_Advanced.Models.ViewModels;

namespace Project_Advanced.Controllers
{
    [Authorize]
    public class VehiclesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private const int PageSize = 10;

        public VehiclesController(ApplicationDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        private void LoadCustomerOptions(int? selectedId = null)
        {
            var customers = _context.Customers
                .Select(c => new
                {
                    c.Id,
                    Label = $"{c.FirstName} {c.LastName} - {(string.IsNullOrWhiteSpace(c.Phone) ? "No phone" : c.Phone)}{(string.IsNullOrWhiteSpace(c.Email) ? "" : $" ({c.Email})")}"
                })
                .ToList();

            ViewBag.CustomerOptions = customers;
            ViewData["CustomerId"] = new SelectList(customers, "Id", "Label", selectedId);
        }

        // GET: Vehicles
        public async Task<IActionResult> Index(string? search, int page = 1)
        {
            var vehicles = _context.Vehicles
                .Include(v => v.Customer)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                vehicles = vehicles.Where(v =>
                    v.PlateNumber.Contains(search) ||
                    v.Make.Contains(search) ||
                    v.Model.Contains(search) ||
                    (v.Customer.FirstName + " " + v.Customer.LastName).Contains(search));
            }

            if (!User.IsInRole("Admin"))
            {
                var currentCustomer = await EnsureCurrentCustomerAsync();
                if (currentCustomer == null)
                {
                    return Forbid();
                }

                vehicles = vehicles.Where(v => v.CustomerId == currentCustomer.Id);
            }

            ViewData["search"] = search;
            ViewData["page"] = page;

            var paged = await PaginatedList<Vehicle>.CreateAsync(
                vehicles.OrderBy(v => v.PlateNumber),
                page,
                PageSize);
            return View(paged);
        }

        // GET: Vehicles/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var vehicle = await _context.Vehicles
                .Include(v => v.Customer)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (vehicle == null)
            {
                return NotFound();
            }

            if (!await CanAccessVehicleAsync(vehicle.CustomerId))
            {
                return Forbid();
            }

            return View(vehicle);
        }

        // GET: Vehicles/Create
        public async Task<IActionResult> Create()
        {
            var isAdmin = User.IsInRole("Admin");
            ViewBag.IsAdmin = isAdmin;

            if (isAdmin)
            {
                LoadCustomerOptions();
                return View();
            }

            var currentCustomer = await EnsureCurrentCustomerAsync();
            if (currentCustomer == null)
            {
                return Forbid();
            }

            ViewData["CustomerId"] = new SelectList(new[] { new { Id = currentCustomer.Id, Name = "You" } }, "Id", "Name", currentCustomer.Id);
            return View(new Vehicle { CustomerId = currentCustomer.Id });
        }

        // POST: Vehicles/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("PlateNumber,Make,Model,Year,CurrentOdometer,CustomerId,Id")] Vehicle vehicle)
        {
            var isAdmin = User.IsInRole("Admin");
            var missingOwner = false;
            if (!isAdmin)
            {
                var currentCustomer = await EnsureCurrentCustomerAsync();
                if (currentCustomer == null)
                {
                    return Forbid();
                }
                vehicle.CustomerId = currentCustomer.Id;
            }
            else
            {
                if (vehicle.CustomerId <= 0)
                {
                    missingOwner = true;
                }
            }

            // Re-validate after enforcing CustomerId assignment/selection
            ModelState.Clear();
            TryValidateModel(vehicle);
            if (missingOwner)
            {
                ModelState.AddModelError(nameof(Vehicle.CustomerId), "Select an owner.");
            }

            if (ModelState.IsValid)
            {
                _context.Add(vehicle);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            ViewBag.IsAdmin = isAdmin;
            if (isAdmin)
            {
                LoadCustomerOptions(vehicle.CustomerId);
            }
            else
            {
                ViewData["CustomerId"] = new SelectList(new[] { new { Id = vehicle.CustomerId, Name = "You" } }, "Id", "Name", vehicle.CustomerId);
            }

            return View(vehicle);
        }

        // GET: Vehicles/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var vehicle = await _context.Vehicles.FindAsync(id);
            if (vehicle == null)
            {
                return NotFound();
            }

            if (!await CanAccessVehicleAsync(vehicle.CustomerId))
            {
                return Forbid();
            }

            ViewBag.IsAdmin = User.IsInRole("Admin");
            if (User.IsInRole("Admin"))
            {
                LoadCustomerOptions(vehicle.CustomerId);
            }
            else
            {
                ViewData["CustomerId"] = new SelectList(new[] { new { Id = vehicle.CustomerId, Name = "You" } }, "Id", "Name", vehicle.CustomerId);
            }
            return View(vehicle);
        }

        // POST: Vehicles/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("PlateNumber,Make,Model,Year,CurrentOdometer,CustomerId,Id")] Vehicle vehicle)
        {
            if (id != vehicle.Id)
            {
                return NotFound();
            }

            var dbVehicle = await _context.Vehicles.FindAsync(id);
            if (dbVehicle == null || !await CanAccessVehicleAsync(dbVehicle.CustomerId))
            {
                return NotFound();
            }

            var isAdmin = User.IsInRole("Admin");
            var missingOwner = false;
            if (isAdmin)
            {
                if (vehicle.CustomerId <= 0)
                {
                    missingOwner = true;
                }
                else
                {
                    dbVehicle.CustomerId = vehicle.CustomerId;
                }
            }

            dbVehicle.PlateNumber = vehicle.PlateNumber;
            dbVehicle.Make = vehicle.Make;
            dbVehicle.Model = vehicle.Model;
            dbVehicle.Year = vehicle.Year;
            dbVehicle.CurrentOdometer = vehicle.CurrentOdometer;

            // Re-validate after applying allowed updates
            ModelState.Clear();
            TryValidateModel(dbVehicle);
            if (missingOwner)
            {
                ModelState.AddModelError(nameof(Vehicle.CustomerId), "Select an owner.");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(dbVehicle);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!VehicleExists(dbVehicle.Id))
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

            ViewBag.IsAdmin = isAdmin;
            if (isAdmin)
            {
                LoadCustomerOptions(dbVehicle.CustomerId);
            }
            else
            {
                ViewData["CustomerId"] = new SelectList(new[] { new { Id = dbVehicle.CustomerId, Name = "You" } }, "Id", "Name", dbVehicle.CustomerId);
            }
            return View(dbVehicle);
        }

        // GET: Vehicles/Delete/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var vehicle = await _context.Vehicles
                .Include(v => v.Customer)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (vehicle == null)
            {
                return NotFound();
            }

            return View(vehicle);
        }

        // POST: Vehicles/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var vehicle = await _context.Vehicles.FindAsync(id);
            if (vehicle != null)
            {
                _context.Vehicles.Remove(vehicle);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool VehicleExists(int id)
        {
            return _context.Vehicles.Any(e => e.Id == id);
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

                var name = user.Email ?? user.UserName ?? "Customer";
                var first = name.Split('@').FirstOrDefault() ?? "Customer";
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

            if (!await _userManager.IsInRoleAsync(await _userManager.GetUserAsync(User), "Customer"))
            {
                await _userManager.AddToRoleAsync(await _userManager.GetUserAsync(User), "Customer");
            }

            return customer;
        }

        private async Task<bool> CanAccessVehicleAsync(int customerId)
        {
            if (User.IsInRole("Admin")) return true;

            var currentCustomer = await EnsureCurrentCustomerAsync();
            return currentCustomer != null && currentCustomer.Id == customerId;
        }
    }
}
