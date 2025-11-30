using System;
using System.Collections.Generic;
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
using Microsoft.AspNetCore.WebUtilities;
using System.Text;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Project_Advanced.Controllers
{
    [Authorize(Roles = "Admin")]
    public class CustomersController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private const int PageSize = 10;

        public CustomersController(ApplicationDbContext context, UserManager<IdentityUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
        }

        // GET: Customers
        public async Task<IActionResult> Index(string? search, int page = 1)
        {
            var query = _context.Customers.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(c =>
                    (c.FirstName + " " + c.LastName).Contains(term) ||
                    (c.Email ?? "").Contains(term) ||
                    (c.Phone ?? "").Contains(term) ||
                    (c.Address ?? "").Contains(term));
            }

            query = query.OrderBy(c => c.LastName).ThenBy(c => c.FirstName);

            var paged = await PaginatedList<Customer>.CreateAsync(query, page, PageSize);
            ViewData["page"] = page;
            ViewData["search"] = search;
            return View(paged);
        }

        // GET: Customers/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var customer = await _context.Customers
                .FirstOrDefaultAsync(m => m.Id == id);
            if (customer == null)
            {
                return NotFound();
            }

            return View(customer);
        }

        // GET: Customers/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Customers/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("FirstName,LastName,Phone,Email,Address,ApplicationUserId,Id")] Customer customer)
        {
            if (ModelState.IsValid)
            {
                _context.Add(customer);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(customer);
        }

        // GET: Customers/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var customer = await _context.Customers.FindAsync(id);
            if (customer == null)
            {
                return NotFound();
            }
            return View(customer);
        }

        // POST: Customers/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("FirstName,LastName,Phone,Email,Address,ApplicationUserId,Id")] Customer customer)
        {
            if (id != customer.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(customer);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!CustomerExists(customer.Id))
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
            return View(customer);
        }

        // GET: Customers/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var customer = await _context.Customers
                .FirstOrDefaultAsync(m => m.Id == id);
            if (customer == null)
            {
                return NotFound();
            }

            return View(customer);
        }

        // POST: Customers/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var customer = await _context.Customers.FindAsync(id);
            if (customer != null)
            {
                _context.Customers.Remove(customer);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool CustomerExists(int id)
        {
            return _context.Customers.Any(e => e.Id == id);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerateLoginLink(int id)
        {
            var customer = await _context.Customers.FindAsync(id);
            if (customer == null)
            {
                TempData["Error"] = "Customer not found.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(customer.Email))
            {
                TempData["Error"] = "Customer must have an email to generate a login link.";
                return RedirectToAction(nameof(Index));
            }

            IdentityUser? user = null;
            if (!string.IsNullOrEmpty(customer.ApplicationUserId))
            {
                user = await _userManager.FindByIdAsync(customer.ApplicationUserId);
            }
            if (user == null)
            {
                user = await _userManager.FindByEmailAsync(customer.Email);
            }

            // Create Identity user if missing
            if (user == null)
            {
                user = new IdentityUser
                {
                    Email = customer.Email,
                    UserName = customer.Email,
                    EmailConfirmed = true
                };
                var createResult = await _userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                {
                    TempData["Error"] = "Failed to create login user for this customer.";
                    return RedirectToAction(nameof(Index));
                }
            }

            // Ensure Customer role exists and is assigned
            if (!await _roleManager.RoleExistsAsync("Customer"))
            {
                await _roleManager.CreateAsync(new IdentityRole("Customer"));
            }
            if (!await _userManager.IsInRoleAsync(user, "Customer"))
            {
                await _userManager.AddToRoleAsync(user, "Customer");
            }

            // Link the customer record to this Identity user
            if (customer.ApplicationUserId != user.Id)
            {
                customer.ApplicationUserId = user.Id;
                _context.Customers.Update(customer);
                await _context.SaveChangesAsync();
            }

            // Generate reset password link so they can set their password
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var callbackUrl = Url.Page(
                "/Account/ResetPassword",
                pageHandler: null,
                values: new { area = "Identity", code = encoded, email = user.Email },
                protocol: Request.Scheme) ?? string.Empty;

            TempData["InviteLink"] = callbackUrl;
            TempData["InviteEmail"] = user.Email;

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RevokeLogin(int id)
        {
            var customer = await _context.Customers.FindAsync(id);
            if (customer == null)
            {
                TempData["Error"] = "Customer not found.";
                return RedirectToAction(nameof(Index));
            }

            IdentityUser? user = null;
            if (!string.IsNullOrEmpty(customer.ApplicationUserId))
            {
                user = await _userManager.FindByIdAsync(customer.ApplicationUserId);
            }
            if (user == null && !string.IsNullOrEmpty(customer.Email))
            {
                user = await _userManager.FindByEmailAsync(customer.Email);
            }

            if (user != null)
            {
                if (await _userManager.IsInRoleAsync(user, "Admin"))
                {
                    TempData["Error"] = "Cannot revoke login for an admin user.";
                    return RedirectToAction(nameof(Index));
                }

                var deleteResult = await _userManager.DeleteAsync(user);
                if (!deleteResult.Succeeded)
                {
                    TempData["Error"] = "Failed to revoke login for this customer.";
                    return RedirectToAction(nameof(Index));
                }
            }

            customer.ApplicationUserId = null;
            _context.Customers.Update(customer);
            await _context.SaveChangesAsync();

            TempData["RevokeMessage"] = $"Login revoked for {customer.Email ?? customer.FirstName}.";
            return RedirectToAction(nameof(Index));
        }
    }
}
