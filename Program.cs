using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Project_Advanced.Data;
using Project_Advanced.Models;
using Stripe;
using CustomerModel = Project_Advanced.Models.Customer;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using Project_Advanced.Services;

var builder = WebApplication.CreateBuilder(args);


var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");


builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var serverVersion = new MySqlServerVersion(new Version(8, 0, 36)); 

    options.UseMySql(connectionString, serverVersion);
    options.LogTo(Console.WriteLine, LogLevel.Information);

    if (builder.Environment.IsDevelopment())
    {
        options.EnableSensitiveDataLogging();
    }
});

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<IdentityUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddControllersWithViews();

var externalAiBaseUrl = builder.Configuration["ExternalAI:BaseUrl"]
    ?? throw new InvalidOperationException("ExternalAI:BaseUrl is not configured");

builder.Services.AddHttpClient("ExternalAI", client =>
{
    client.BaseAddress = new Uri(externalAiBaseUrl);
});

builder.Services.AddSingleton<WhisperService>();

var app = builder.Build();


StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"];

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.Use(async (context, next) =>
{
    if (context.Request.Path.HasValue &&
        context.Request.Path.Value.Contains("/Account/Register", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Redirect("/");
        return;
    }

    await next();
});


app.MapStaticAssets();

app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
   .WithStaticAssets();

app.MapRazorPages()
   .WithStaticAssets();

await EnsureRolesAndAdminAsync(app.Services, app.Configuration, app.Environment);
await EnsureSampleDataAsync(app.Services);

app.Run();


static async Task EnsureRolesAndAdminAsync(IServiceProvider services, IConfiguration config, IWebHostEnvironment env)
{
    using var scope = services.CreateScope();
    var scopedServices = scope.ServiceProvider;

    var context = scopedServices.GetRequiredService<ApplicationDbContext>();
    await context.Database.MigrateAsync();

    var roleManager = scopedServices.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scopedServices.GetRequiredService<UserManager<IdentityUser>>();

    string[] roles = { "Admin", "Customer" };
    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    if (env.IsDevelopment())
    {
        var adminEmail = config["AdminUser:Email"] ?? "admin@garage.local";
        var adminPassword = config["AdminUser:Password"] ?? "Admin!12345";

        var adminUser = await userManager.FindByEmailAsync(adminEmail);
        if (adminUser == null)
        {
            adminUser = new IdentityUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true
            };

            var createResult = await userManager.CreateAsync(adminUser, adminPassword);
            if (!createResult.Succeeded)
            {
                var errors = string.Join(",", createResult.Errors.Select(e => e.Description));
                throw new InvalidOperationException($"Failed to create seed admin user: {errors}");
            }
        }

        if (!await userManager.IsInRoleAsync(adminUser, "Admin"))
        {
            await userManager.AddToRoleAsync(adminUser, "Admin");
        }
    }
}


static async Task EnsureSampleDataAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var scopedServices = scope.ServiceProvider;
    var context = scopedServices.GetRequiredService<ApplicationDbContext>();

    var vehicleCount = await context.Vehicles.CountAsync();
    if (vehicleCount >= 50)
    {
        return; 
    }

    if (vehicleCount == 0)
    {
        var sampleCustomer = new CustomerModel
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "john.doe@test.com",
            Phone = "555-0100",
            Address = "123 Main St"
        };

        var vehicles = new List<Vehicle>
        {
            new Vehicle
            {
                PlateNumber = "ABC123",
                Make = "Toyota",
                Model = "Corolla",
                Year = 2018,
                CurrentOdometer = 65000,
                Customer = sampleCustomer
            },
            new Vehicle
            {
                PlateNumber = "XYZ789",
                Make = "Honda",
                Model = "Civic",
                Year = 2020,
                CurrentOdometer = 42000,
                Customer = sampleCustomer
            },
            new Vehicle
            {
                PlateNumber = "JKL456",
                Make = "Ford",
                Model = "F-150",
                Year = 2019,
                CurrentOdometer = 75000,
                Customer = sampleCustomer
            }
        };

        context.Vehicles.AddRange(vehicles);
        await context.SaveChangesAsync();

        // Seed a few example appointments for the seeded customer/vehicles
        var appointments = new List<Appointment>
        {
            new Appointment
            {
                CustomerId = sampleCustomer.Id,
                VehicleId = vehicles[0].Id,
                AppointmentDate = DateTime.UtcNow.AddDays(2).Date.AddHours(10),
                Reason = "Oil change and tire rotation",
                Status = AppointmentStatus.Pending
            },
            new Appointment
            {
                CustomerId = sampleCustomer.Id,
                VehicleId = vehicles[1].Id,
                AppointmentDate = DateTime.UtcNow.AddDays(5).Date.AddHours(14),
                Reason = "Brake inspection",
                Status = AppointmentStatus.Confirmed
            },
            new Appointment
            {
                CustomerId = sampleCustomer.Id,
                VehicleId = vehicles[2].Id,
                AppointmentDate = DateTime.UtcNow.AddDays(7).Date.AddHours(9),
                Reason = "Check engine light diagnosis",
                Status = AppointmentStatus.Pending
            }
        };

        context.Appointments.AddRange(appointments);
        await context.SaveChangesAsync();
    }

    // Add bulk dummy customers/vehicles up to at least 50 vehicles total
    var random = new Random(42);
    var makes = new[] { "Toyota", "Honda", "Ford", "BMW", "Audi", "Chevy", "Nissan", "Hyundai", "Kia", "Subaru" };
    var models = new[] { "Corolla", "Civic", "F-150", "3 Series", "A4", "Silverado", "Altima", "Elantra", "Soul", "Outback" };

    var needed = 50 - await context.Vehicles.CountAsync();
    if (needed <= 0) return;

    var customersToAdd = new List<CustomerModel>();
    var vehiclesToAdd = new List<Vehicle>();

    int customerIndex = 1;
    int vehicleIndex = 1;

    while (needed > 0)
    {
        var customer = new CustomerModel
        {
            FirstName = $"Test{customerIndex}",
            LastName = "User",
            Email = $"test{customerIndex}@example.com",
            Phone = $"555-01{customerIndex:000}",
            Address = $"{100 + customerIndex} Sample St"
        };
        customersToAdd.Add(customer);

        // give each customer up to 3 vehicles, but stop when we hit the needed count
        var perCustomer = Math.Min(3, needed);
        for (int i = 0; i < perCustomer; i++)
        {
            var make = makes[random.Next(makes.Length)];
            var model = models[random.Next(models.Length)];
            var year = random.Next(2008, 2024);
            var odometer = random.Next(20000, 180000);

            vehiclesToAdd.Add(new Vehicle
            {
                PlateNumber = $"TST{vehicleIndex:0000}",
                Make = make,
                Model = model,
                Year = year,
                CurrentOdometer = odometer,
                Customer = customer
            });

            vehicleIndex++;
            needed--;
            if (needed <= 0) break;
        }

        customerIndex++;
    }

    if (vehiclesToAdd.Count > 0)
    {
        context.Customers.AddRange(customersToAdd);
        context.Vehicles.AddRange(vehiclesToAdd);
        await context.SaveChangesAsync();
    }
}
