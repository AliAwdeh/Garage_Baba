using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Project_Advanced.Data;
using Project_Advanced.Models;
using Project_Advanced.Models.ViewModels;
using Stripe;
using Stripe.Checkout;
using PaymentMethodEnum = Project_Advanced.Models.PaymentMethod;

namespace Project_Advanced.Controllers
{
    [Authorize]
    public class PaymentsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IConfiguration _configuration;
        private readonly ILogger<PaymentsController> _logger;
        private const int PageSize = 10;

        public PaymentsController(ApplicationDbContext context, UserManager<IdentityUser> userManager, IConfiguration configuration, ILogger<PaymentsController> logger)
        {
            _context = context;
            _userManager = userManager;
            _configuration = configuration;
            _logger = logger;
        }

        // GET: Payments (Admin) - all payments
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Index(string? search, int page = 1)
        {
            var invoices = _context.Invoices
                .Include(i => i.Customer)
                .Include(i => i.WorkOrder)
                    .ThenInclude(w => w.Vehicle)
                .Include(i => i.Payments)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                invoices = invoices.Where(i =>
                    i.Id.ToString().Contains(term) ||
                    ((i.Customer!.FirstName + " " + i.Customer.LastName).Contains(term)) ||
                    (i.WorkOrder != null && i.WorkOrder.Vehicle != null && i.WorkOrder.Vehicle.PlateNumber.Contains(term)) ||
                    i.Status.ToString().Contains(term));
            }

            var totalCount = await invoices.CountAsync();
            var items = await invoices
                .OrderByDescending(i => i.IssuedAt)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(i => new InvoicePaymentSummary
                {
                    Invoice = i,
                    Payments = i.Payments.OrderByDescending(p => p.PaidAt).ToList(),
                    TotalPaid = i.Payments.Sum(p => p.Amount),
                    Outstanding = i.Total - i.Payments.Sum(p => p.Amount)
                })
                .ToListAsync();

            var paged = PaginatedList<InvoicePaymentSummary>.Create(items, totalCount, page, PageSize);

            ViewData["page"] = page;
            ViewData["search"] = search;
            return View(paged);
        }

        // GET: Payments/Details/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var payment = await _context.Payments
                .Include(p => p.Invoice)
                    .ThenInclude(i => i.WorkOrder)
                        .ThenInclude(w => w.Vehicle)
                .Include(p => p.Invoice)
                    .ThenInclude(i => i.Customer)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (payment == null) return NotFound();

            return View(payment);
        }

        // GET: Payments/Create (admin manual entry)
        [Authorize(Roles = "Admin")]
        public IActionResult Create()
        {
            ViewData["InvoiceId"] = new SelectList(_context.Invoices, "Id", "Id");
            return View();
        }

        // Customer view: list payments and outstanding balances
        public async Task<IActionResult> MyPayments(int page = 1)
        {
            var customer = await EnsureCurrentCustomerAsync();
            if (customer == null) return Forbid();

            var invoicesQuery = _context.Invoices
                .Include(i => i.WorkOrder)
                    .ThenInclude(w => w.Vehicle)
                .Include(i => i.Payments)
                .Where(i => i.CustomerId == customer.Id)
                .OrderByDescending(i => i.IssuedAt)
                .Select(i => new CustomerInvoicePaymentViewModel
                {
                    Invoice = i,
                    Paid = i.Payments != null ? i.Payments.Sum(p => p.Amount) : 0m,
                    Outstanding = i.Total - (i.Payments != null ? i.Payments.Sum(p => p.Amount) : 0m)
                });

            var paged = await PaginatedList<CustomerInvoicePaymentViewModel>.CreateAsync(invoicesQuery, page, PageSize);
            ViewData["page"] = page;

            return View(paged);
        }

        // POST: Payments/Create (admin only manual entry)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create([Bind("InvoiceId,PaidAt,Amount,Method,StripePaymentIntentId,Notes,Id")] Payment payment)
        {
            if (ModelState.IsValid)
            {
                _context.Add(payment);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            ViewData["InvoiceId"] = new SelectList(_context.Invoices, "Id", "Id", payment.InvoiceId);
            return View(payment);
        }

        // GET: Payments/Edit/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var payment = await _context.Payments.FindAsync(id);
            if (payment == null)
            {
                return NotFound();
            }
            ViewData["InvoiceId"] = new SelectList(_context.Invoices, "Id", "Id", payment.InvoiceId);
            return View(payment);
        }

        // POST: Payments/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id, [Bind("InvoiceId,PaidAt,Amount,Method,StripePaymentIntentId,Notes,Id")] Payment payment)
        {
            if (id != payment.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(payment);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!PaymentExists(payment.Id))
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
            ViewData["InvoiceId"] = new SelectList(_context.Invoices, "Id", "Id", payment.InvoiceId);
            return View(payment);
        }

        // GET: Payments/Delete/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var payment = await _context.Payments
                .Include(p => p.Invoice)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (payment == null)
            {
                return NotFound();
            }

            return View(payment);
        }

        // POST: Payments/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var payment = await _context.Payments.FindAsync(id);
            if (payment != null)
            {
                _context.Payments.Remove(payment);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool PaymentExists(int id)
        {
            return _context.Payments.Any(e => e.Id == id);
        }

        private async Task<Project_Advanced.Models.Customer?> EnsureCurrentCustomerAsync()
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return null;

            return await _context.Customers.FirstOrDefaultAsync(c => c.ApplicationUserId == userId);
        }

        // Customer Stripe payment kickoff: create Stripe Checkout Session
        [HttpGet]
        public async Task<IActionResult> BeginStripePayment(int invoiceId, decimal amount = 0)
        {
            var customer = await EnsureCurrentCustomerAsync();
            if (customer == null) return Forbid();

            var invoice = await _context.Invoices
                .Include(i => i.Payments)
                .FirstOrDefaultAsync(i => i.Id == invoiceId && i.CustomerId == customer.Id);

            if (invoice == null) return NotFound();

            var outstanding = invoice.Total - (invoice.Payments?.Sum(p => p.Amount) ?? 0m);
            if (outstanding <= 0)
            {
                return RedirectToAction(nameof(MyPayments));
            }

            var amountToPay = amount > 0 && amount <= outstanding ? amount : outstanding;
            if (amountToPay <= 0)
            {
                TempData["PaymentMessage"] = "Invalid payment amount.";
                return RedirectToAction(nameof(MyPayments));
            }

            TempData["PaymentMessage"] = "Payment initiated. Complete the checkout to finish.";

            var successUrl = Url.Action(nameof(StripeSuccess), "Payments", new { invoiceId = invoice.Id }, Request.Scheme);
            var cancelUrl = Url.Action(nameof(MyPayments), "Payments", null, Request.Scheme);

            var options = new SessionCreateOptions
            {
                Mode = "payment",
                SuccessUrl = successUrl,
                CancelUrl = cancelUrl,
                Metadata = new Dictionary<string, string>
                {
                    { "invoiceId", invoice.Id.ToString() },
                    { "customerId", customer.Id.ToString() },
                    { "requestedAmount", amountToPay.ToString("0.00") }
                },
                LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions
                    {
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            Currency = "usd",
                            UnitAmount = (long)(amountToPay * 100),
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = $"Invoice #{invoice.Id}"
                            }
                        },
                        Quantity = 1
                    }
                }
            };

            var service = new SessionService();
            var session = await service.CreateAsync(options);

            return Redirect(session.Url);
        }

        // Stripe success landing - final state confirmed by webhook
        [HttpGet]
        public async Task<IActionResult> StripeSuccess(int invoiceId)
        {
            var invoice = await _context.Invoices
                .Include(i => i.Payments)
                .FirstOrDefaultAsync(i => i.Id == invoiceId);

            if (invoice == null)
            {
                TempData["PaymentMessage"] = "Payment lookup failed.";
                return RedirectToAction(nameof(MyPayments));
            }

            var outstanding = invoice.Total - (invoice.Payments?.Sum(p => p.Amount) ?? 0m);
            if (outstanding <= 0)
            {
                TempData["PaymentSuccess"] = "Payment succeeded and your invoice is now marked as paid.";
            }
            else
            {
                TempData["PaymentMessage"] = "Payment initiated. Waiting for Stripe confirmation.";
            }
            return RedirectToAction(nameof(MyPayments));
        }
        [HttpPost("payments/stripewebhook")]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public async Task<IActionResult> StripeWebhook()
{
    var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
    _logger.LogInformation("Stripe webhook hit. Body length={Len}", json?.Length ?? 0);

    var signatureHeader = Request.Headers["Stripe-Signature"].FirstOrDefault();
    var webhookSecret = _configuration["Stripe:WebhookSecret"];

    Event stripeEvent;

    // If we have a secret in config, do strict signature validation
    if (!string.IsNullOrEmpty(webhookSecret) && !string.IsNullOrEmpty(signatureHeader))
    {
        try
        {
            // 👇 NOTE: explicitly disable API version mismatch exception
            stripeEvent = EventUtility.ConstructEvent(
                json,
                signatureHeader,
                webhookSecret,
                tolerance: 300,
                throwOnApiVersionMismatch: false
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Stripe webhook signature validation failed. Message={Message}",
                ex.Message);
            return BadRequest($"Webhook Error: {ex.Message}");
        }
    }
    else
    {
        // Dev fallback: no signature validation (only for localhost demo)
        try
        {
            // 👇 same here if you want to be explicit
            stripeEvent = EventUtility.ParseEvent(json, throwOnApiVersionMismatch: false);
            _logger.LogWarning("Stripe webhook processed WITHOUT signature validation (no WebhookSecret configured).");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse Stripe event without signature validation.");
            return BadRequest("Failed to parse event");
        }
    }

    _logger.LogInformation("Stripe event received: {Type}", stripeEvent.Type);

    if (stripeEvent.Type == Events.CheckoutSessionCompleted)
    {
        var session = stripeEvent.Data.Object as Session;
        if (session?.Metadata == null)
        {
            _logger.LogWarning("CheckoutSessionCompleted event received without metadata.");
            return Ok();
        }

        if (!session.Metadata.TryGetValue("invoiceId", out var invoiceIdString) ||
            !int.TryParse(invoiceIdString, out var invoiceId))
        {
            _logger.LogWarning("Stripe session missing or invalid invoiceId metadata.");
            return Ok();
        }

        decimal requestedAmount = 0;
        if (session.Metadata.TryGetValue("requestedAmount", out var requested))
        {
            decimal.TryParse(requested, out requestedAmount);
        }

        var invoice = await _context.Invoices
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null)
        {
            _logger.LogWarning("Invoice {InvoiceId} not found during webhook.", invoiceId);
            return Ok();
        }

        var outstanding = invoice.Total - (invoice.Payments?.Sum(p => p.Amount) ?? 0m);
        if (outstanding <= 0)
        {
            _logger.LogInformation("Invoice {InvoiceId} already fully paid; skipping webhook payment.", invoiceId);
            return Ok();
        }

        var amountTotal = (session.AmountTotal ?? 0) / 100m;
        var payAmount = Math.Min(outstanding, requestedAmount > 0 ? requestedAmount : amountTotal);

        var intentId = session.PaymentIntentId;
        if (!string.IsNullOrEmpty(intentId) && invoice.Payments.Any(p => p.StripePaymentIntentId == intentId))
        {
            _logger.LogInformation("PaymentIntent {IntentId} already recorded for invoice {InvoiceId}.", intentId, invoiceId);
            return Ok();
        }

        var payment = new Payment
        {
            InvoiceId = invoice.Id,
            PaidAt = DateTime.UtcNow,
            Amount = payAmount,
            Method = PaymentMethodEnum.Card,
            StripePaymentIntentId = intentId ?? Guid.NewGuid().ToString(),
            Notes = "Stripe Checkout payment"
        };

        _context.Payments.Add(payment);

        var outstandingAfter = outstanding - payAmount;
        if (outstandingAfter <= 0)
        {
            invoice.Status = InvoiceStatus.Paid;
            _context.Invoices.Update(invoice);
            _logger.LogInformation("Invoice {InvoiceId} marked as Paid via webhook.", invoiceId);
        }

        await _context.SaveChangesAsync();
    }

    return Ok();
}


    }
}
