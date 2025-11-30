using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Project_Advanced.Models;

namespace Project_Advanced.Data
{
    public class ApplicationDbContext : IdentityDbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }
        public DbSet<ChatConversation> ChatConversations { get; set; }
       public DbSet<ChatMessage> ChatMessages { get; set; }
        public DbSet<Customer> Customers { get; set; }
        public DbSet<Vehicle> Vehicles { get; set; }
        public DbSet<Part> Parts { get; set; }
        public DbSet<WorkOrder> WorkOrders { get; set; }
        public DbSet<WorkOrderItem> WorkOrderItems { get; set; }
        public DbSet<Invoice> Invoices { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<Appointment> Appointments { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<ChatConversation>()
              .HasMany(c => c.Messages)
              .WithOne(m => m.Conversation)
              .HasForeignKey(m => m.ConversationId)
              .OnDelete(DeleteBehavior.Cascade);

            // WorkOrderItem -> computed at runtime only
            builder.Entity<WorkOrderItem>()
                   .Ignore(w => w.LineTotal);

            builder.Entity<WorkOrder>()
                   .Ignore(w => w.Total);

            // relationships & cascade
            builder.Entity<Customer>()
                   .HasMany(c => c.Vehicles)
                   .WithOne(v => v.Customer)
                   .HasForeignKey(v => v.CustomerId)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Vehicle>()
                   .HasMany(v => v.WorkOrders)
                   .WithOne(w => w.Vehicle)
                   .HasForeignKey(w => w.VehicleId)
                   .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Vehicle>()
                   .HasIndex(v => v.PlateNumber)
                   .IsUnique();

            builder.Entity<Invoice>()
                   .HasMany(i => i.Payments)
                   .WithOne(p => p.Invoice)
                   .HasForeignKey(p => p.InvoiceId)
                   .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
