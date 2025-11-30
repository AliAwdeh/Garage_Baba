using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using Project_Advanced.Models.ViewModels;

namespace Project_Advanced.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private const int PageSize = 10;

        public AdminController(UserManager<IdentityUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            _userManager = userManager;
            _roleManager = roleManager;
        }

        public async Task<IActionResult> ManageUsers(int page = 1, string? search = null)
        {
            var usersQuery = _userManager.Users.AsQueryable();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                usersQuery = usersQuery.Where(u =>
                    (u.Email ?? "").Contains(term) ||
                    (u.UserName ?? "").Contains(term));
            }

            var usersPage = await PaginatedList<IdentityUser>.CreateAsync(
                usersQuery.OrderBy(u => u.Email ?? u.UserName),
                page,
                PageSize);

            var mapped = new List<AdminUserViewModel>();
            foreach (var u in usersPage)
            {
                mapped.Add(new AdminUserViewModel
                {
                    Id = u.Id,
                    Email = u.Email ?? u.UserName ?? string.Empty,
                    Roles = await _userManager.GetRolesAsync(u)
                });
            }

            var model = PaginatedList<AdminUserViewModel>.Create(
                mapped,
                usersPage.TotalCount,
                usersPage.PageIndex,
                usersPage.PageSize);

            ViewData["page"] = page;
            ViewData["search"] = search;

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PromoteToAdmin(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            // remove other roles before assigning admin
            var currentRoles = await _userManager.GetRolesAsync(user);
            if (currentRoles.Any())
            {
                await _userManager.RemoveFromRolesAsync(user, currentRoles);
            }

            if (!await _roleManager.RoleExistsAsync("Admin"))
            {
                await _roleManager.CreateAsync(new IdentityRole("Admin"));
            }

            if (!await _userManager.IsInRoleAsync(user, "Admin"))
            {
                await _userManager.AddToRoleAsync(user, "Admin");
            }

            return RedirectToAction(nameof(ManageUsers));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveAdmin(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            // prevent removing your own admin role to avoid locking out all admins
            var currentUserId = _userManager.GetUserId(User);
            if (user.Id == currentUserId)
            {
                TempData["Error"] = "You cannot remove your own admin role.";
                return RedirectToAction(nameof(ManageUsers));
            }

            var currentRoles = await _userManager.GetRolesAsync(user);
            if (currentRoles.Any())
            {
                await _userManager.RemoveFromRolesAsync(user, currentRoles);
            }
            // Optionally assign default role (e.g., Customer) after removing Admin
            if (await _roleManager.RoleExistsAsync("Customer"))
            {
                await _userManager.AddToRoleAsync(user, "Customer");
            }

            return RedirectToAction(nameof(ManageUsers));
        }
    }

    public class AdminUserViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public IEnumerable<string> Roles { get; set; } = Enumerable.Empty<string>();
    }
}
