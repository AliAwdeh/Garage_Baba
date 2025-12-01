using System;
using System.Linq;
using System.Threading.Tasks;
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
    public class AppointmentsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private const int PageSize = 10;

        public AppointmentsController(ApplicationDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // GET: Appointments
        public async Task<IActionResult> Index(int page = 1, string? search = null)
        {
            var appointments = _context.Appointments
                .Include(a => a.Customer)
                .Include(a => a.Vehicle)
                .AsQueryable();

            if (!User.IsInRole("Admin"))
            {
                var currentCustomer = await EnsureCurrentCustomerAsync();
                if (currentCustomer == null)
                {
                    return Forbid();
                }

                var customerQuery = appointments
                    .Where(a => a.CustomerId == currentCustomer.Id);

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var term = search.Trim();
                    customerQuery = customerQuery.Where(a =>
                        (a.Reason ?? "").Contains(term) ||
                        ((a.Customer!.FirstName + " " + a.Customer.LastName).Contains(term)) ||
                        (a.Vehicle != null && (
                            (a.Vehicle.PlateNumber ?? "").Contains(term) ||
                            (a.Vehicle.Make ?? "").Contains(term) ||
                            (a.Vehicle.Model ?? "").Contains(term)
                        )) ||
                        a.Status.ToString().Contains(term));
                }

                customerQuery = customerQuery.OrderByDescending(a => a.AppointmentDate);
                var customerPaged = await PaginatedList<Appointment>.CreateAsync(customerQuery, page, PageSize);
                ViewData["page"] = page;
                ViewData["search"] = search;
                return View(customerPaged);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                appointments = appointments.Where(a =>
                    (a.Reason ?? "").Contains(term) ||
                    ((a.Customer!.FirstName + " " + a.Customer.LastName).Contains(term)) ||
                    (a.Vehicle != null && (
                        (a.Vehicle.PlateNumber ?? "").Contains(term) ||
                        (a.Vehicle.Make ?? "").Contains(term) ||
                        (a.Vehicle.Model ?? "").Contains(term)
                    )) ||
                    a.Status.ToString().Contains(term));
            }

            var paged = await PaginatedList<Appointment>.CreateAsync(
                appointments.OrderByDescending(a => a.AppointmentDate),
                page,
                PageSize);
            ViewData["page"] = page;
            ViewData["search"] = search;

            return View(paged);
        }

        // GET: Appointments/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var appointment = await _context.Appointments
                .Include(a => a.Customer)
                .Include(a => a.Vehicle)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (appointment == null || !await CanAccessAppointmentAsync(appointment.CustomerId))
            {
                return NotFound();
            }

            return View(appointment);
        }

        // GET: Appointments/Create
        public async Task<IActionResult> Create()
        {
            ViewBag.IsAdmin = User.IsInRole("Admin");
            if (User.IsInRole("Admin"))
            {
                PopulateAdminDropDowns();
                return View(new Appointment
                {
                    AppointmentDate = GetDefaultAppointmentDate(),
                    Status = AppointmentStatus.Pending
                });
            }

            var currentCustomer = await EnsureCurrentCustomerAsync();
            if (currentCustomer == null)
            {
                return Forbid();
            }

            PopulateCustomerDropDowns(currentCustomer.Id);
            return View(new Appointment
            {
                CustomerId = currentCustomer.Id,
                Status = AppointmentStatus.Pending,
                AppointmentDate = GetDefaultAppointmentDate()
            });
        }

        // POST: Appointments/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("CustomerId,VehicleId,AppointmentDate,Reason,Status,Id")] Appointment appointment)
        {
            var isAdmin = User.IsInRole("Admin");
            if (!isAdmin)
            {
                var currentCustomer = await EnsureCurrentCustomerAsync();
                if (currentCustomer == null)
                {
                    return Forbid();
                }

                appointment.CustomerId = currentCustomer.Id;
                appointment.Status = AppointmentStatus.Pending;

                if (appointment.VehicleId.HasValue)
                {
                    var vehicleOwned = await _context.Vehicles.AnyAsync(v => v.Id == appointment.VehicleId && v.CustomerId == currentCustomer.Id);
                    if (!vehicleOwned)
                    {
                        ModelState.AddModelError(nameof(appointment.VehicleId), "Select one of your vehicles.");
                    }
                }
            }
            else
            {
                if (appointment.VehicleId.HasValue && appointment.CustomerId > 0)
                {
                    var vehicleBelongsToCustomer = await _context.Vehicles.AnyAsync(v =>
                        v.Id == appointment.VehicleId && v.CustomerId == appointment.CustomerId);
                    if (!vehicleBelongsToCustomer)
                    {
                        ModelState.AddModelError(nameof(appointment.VehicleId), "Select a vehicle that belongs to the chosen customer.");
                    }
                }
            }

            if (appointment.AppointmentDate.Minute != 0 || appointment.AppointmentDate.Second != 0)
            {
                ModelState.AddModelError(
                    nameof(Appointment.AppointmentDate),
                    "Appointments must start on the hour (e.g. 09:00, 10:00, 11:00)."
                );
            }

            await ValidateSlotAvailabilityAsync(appointment.AppointmentDate);
            ValidateAppointmentDate(appointment.AppointmentDate);

            if (ModelState.IsValid)
            {
                _context.Add(appointment);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            if (isAdmin)
            {
                PopulateAdminDropDowns(appointment.CustomerId, appointment.VehicleId);
            }
            else
            {
                var currentCustomer = await EnsureCurrentCustomerAsync();
                if (currentCustomer == null)
                {
                    return Forbid();
                }
                PopulateCustomerDropDowns(currentCustomer.Id, appointment.VehicleId);
            }

            ViewBag.IsAdmin = isAdmin;
            return View(appointment);
        }

        // GET: Appointments/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var appointment = await _context.Appointments.FindAsync(id);
            if (appointment == null || !await CanAccessAppointmentAsync(appointment.CustomerId))
            {
                return NotFound();
            }

            if (User.IsInRole("Admin"))
            {
                PopulateAdminDropDowns(appointment.CustomerId, appointment.VehicleId);
            }
            else
            {
                PopulateCustomerDropDowns(appointment.CustomerId, appointment.VehicleId);
            }

            ViewBag.IsAdmin = User.IsInRole("Admin");
            return View(appointment);
        }

        // POST: Appointments/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("CustomerId,VehicleId,AppointmentDate,Reason,Status,Id")] Appointment appointment)
        {
            if (id != appointment.Id)
            {
                return NotFound();
            }

            var isAdmin = User.IsInRole("Admin");
            var appointmentFromDb = await _context.Appointments.FirstOrDefaultAsync(a => a.Id == id);
            if (appointmentFromDb == null || !await CanAccessAppointmentAsync(appointmentFromDb.CustomerId))
            {
                return NotFound();
            }

            if (!isAdmin)
            {
                var currentCustomer = await EnsureCurrentCustomerAsync();
                if (currentCustomer == null)
                {
                    return Forbid();
                }

                if (appointment.VehicleId.HasValue)
                {
                    var ownsVehicle = await _context.Vehicles.AnyAsync(v => v.Id == appointment.VehicleId && v.CustomerId == currentCustomer.Id);
                    if (!ownsVehicle)
                    {
                        ModelState.AddModelError(nameof(appointment.VehicleId), "Select one of your vehicles.");
                    }
                }

                appointmentFromDb.AppointmentDate = appointment.AppointmentDate;
                appointmentFromDb.Reason = appointment.Reason;
                appointmentFromDb.VehicleId = appointment.VehicleId;
            }
            else
            {
                appointmentFromDb.CustomerId = appointment.CustomerId;
                appointmentFromDb.VehicleId = appointment.VehicleId;
                appointmentFromDb.AppointmentDate = appointment.AppointmentDate;
                appointmentFromDb.Reason = appointment.Reason;
                appointmentFromDb.Status = appointment.Status;

                if (appointmentFromDb.VehicleId.HasValue)
                {
                    var vehicleBelongsToCustomer = await _context.Vehicles.AnyAsync(v =>
                        v.Id == appointmentFromDb.VehicleId && v.CustomerId == appointmentFromDb.CustomerId);
                    if (!vehicleBelongsToCustomer)
                    {
                        ModelState.AddModelError(nameof(appointment.VehicleId), "Select a vehicle that belongs to the chosen customer.");
                    }
                }
            }

            if (appointment.AppointmentDate.Minute != 0 || appointment.AppointmentDate.Second != 0)
            {
                ModelState.AddModelError(
                    nameof(Appointment.AppointmentDate),
                    "Appointments must start on the hour (e.g. 09:00, 10:00, 11:00)."
                );
            }

            await ValidateSlotAvailabilityAsync(appointmentFromDb.AppointmentDate, appointmentFromDb.Id);
            ValidateAppointmentDate(appointmentFromDb.AppointmentDate);

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(appointmentFromDb);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!AppointmentExists(appointment.Id))
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

            if (isAdmin)
            {
                PopulateAdminDropDowns(appointment.CustomerId, appointment.VehicleId);
            }
            else
            {
                PopulateCustomerDropDowns(appointment.CustomerId, appointment.VehicleId);
            }

            ViewBag.IsAdmin = isAdmin;
            return View(appointment);
        }

        // GET: Appointments/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var appointment = await _context.Appointments
                .Include(a => a.Customer)
                .Include(a => a.Vehicle)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (appointment == null || !await CanAccessAppointmentAsync(appointment.CustomerId))
            {
                return NotFound();
            }

            return View(appointment);
        }

        // POST: Appointments/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var appointment = await _context.Appointments.FindAsync(id);
            if (appointment != null && await CanAccessAppointmentAsync(appointment.CustomerId))
            {
                _context.Appointments.Remove(appointment);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }

        private bool AppointmentExists(int id)
        {
            return _context.Appointments.Any(e => e.Id == id);
        }

        private async Task<bool> CanAccessAppointmentAsync(int customerId)
        {
            if (User.IsInRole("Admin"))
            {
                return true;
            }

            var currentCustomer = await EnsureCurrentCustomerAsync();
            return currentCustomer != null && currentCustomer.Id == customerId;
        }

[HttpGet]
public async Task<IActionResult> GetAvailableSlots(DateTime date)
{
    const int openingHour = 9;
    const int closingHour = 17; // last start at 16:00

    // Get all existing appointments for that date (not cancelled)
    var existing = await _context.Appointments
        .Where(a => a.AppointmentDate.Date == date.Date)
        .Where(a => a.Status != AppointmentStatus.Cancelled)
        .Select(a => a.AppointmentDate)
        .ToListAsync();

    var slots = new List<string>();

    for (int hour = openingHour; hour < closingHour; hour++)
    {
        var slotStart = date.Date.AddHours(hour);
        var slotEnd = slotStart.AddHours(1);

        // conflict if any existing appointment overlaps [slotStart, slotEnd)
        var conflict = existing.Any(a =>
            a >= slotStart && a < slotEnd
        );

        if (!conflict)
        {
            slots.Add(slotStart.ToString("HH:mm")); // e.g. "09:00"
        }
    }

    return Json(slots);
}

        private async Task<Customer?> EnsureCurrentCustomerAsync()
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                return null;
            }

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

            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser != null && !await _userManager.IsInRoleAsync(currentUser, "Customer"))
            {
                await _userManager.AddToRoleAsync(currentUser, "Customer");
            }

            return customer;
        }

        private void PopulateAdminDropDowns(int? customerId = null, int? vehicleId = null)
        {
            var customers = _context.Customers
                .Select(c => new
                {
                    c.Id,
                    Name = $"{c.FirstName} {c.LastName} ({c.Email})",
                    Label = $"{c.FirstName} {c.LastName} - {(string.IsNullOrWhiteSpace(c.Phone) ? "No phone" : c.Phone)}{(string.IsNullOrWhiteSpace(c.Email) ? "" : $" ({c.Email})")}"
                }).ToList();

            var vehicles = _context.Vehicles
                .Include(v => v.Customer)
                .Select(v => new
                {
                    v.Id,
                    Label = $"{v.PlateNumber} - {v.Make} {v.Model} ({v.Customer.FirstName} {v.Customer.LastName})",
                    v.CustomerId
                }).ToList();

            ViewBag.CustomerOptions = customers;
            ViewBag.VehicleOptions = vehicles;
            ViewData["CustomerId"] = new SelectList(customers, "Id", "Label", customerId);
            ViewData["VehicleId"] = new SelectList(vehicles, "Id", "Label", vehicleId);
        }

        private void PopulateCustomerDropDowns(int customerId, int? vehicleId = null)
        {
            var vehicles = _context.Vehicles
                .Where(v => v.CustomerId == customerId)
                .Select(v => new
                {
                    v.Id,
                    Label = $"{v.PlateNumber} - {v.Make} {v.Model}"
                }).ToList();

            ViewData["CustomerId"] = new SelectList(new[] { new { Id = customerId, Name = "You" } }, "Id", "Name", customerId);
            ViewData["VehicleId"] = new SelectList(vehicles, "Id", "Label", vehicleId);
        }

        private static DateTime GetDefaultAppointmentDate()
        {
            var now = DateTime.Now;
            var tomorrow = now.Date.AddDays(1);
            return tomorrow.AddHours(9);
        }

        private void ValidateAppointmentDate(DateTime appointmentDate)
        {
            var minUtc = DateTime.UtcNow.Date.AddDays(1);
            var appointmentUtc = appointmentDate.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(appointmentDate, DateTimeKind.Local).ToUniversalTime()
                : appointmentDate.ToUniversalTime();

            if (appointmentUtc < minUtc)
            {
                ModelState.AddModelError(nameof(Appointment.AppointmentDate), "Appointment date must be from tomorrow onward.");
            }
        }
        private async Task ValidateSlotAvailabilityAsync(DateTime appointmentDate, int? ignoreAppointmentId = null)
        {
            // Appointments are 1 hour long: [start, start+1)
            var slotStart = appointmentDate;
            var slotEnd = appointmentDate.AddHours(1);

            var query = _context.Appointments
                .Where(a => a.AppointmentDate.Date == appointmentDate.Date)
                .Where(a => a.AppointmentDate >= slotStart && a.AppointmentDate < slotEnd);

            if (ignoreAppointmentId.HasValue)
            {
                query = query.Where(a => a.Id != ignoreAppointmentId.Value);
            }

            var exists = await query.AnyAsync();
            if (exists)
            {
                ModelState.AddModelError(nameof(Appointment.AppointmentDate),
                    "This time slot is already taken.");
            }
        }

    }
}
