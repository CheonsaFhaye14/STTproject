using Microsoft.EntityFrameworkCore;
using STTproject.Models;
namespace STTproject.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options) { }
         
        public DbSet<SubDistributor> SubDistributors { get; set; }
        public DbSet<SalesInvoice> SalesInvoices { get; set; }
        public DbSet<SalesInvoiceItem> SalesInvoiceItems { get; set; }
        public DbSet<SubdItem> SubdItems { get; set; }
        public DbSet<Customer> Customers { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<SalesInvoice>()
                .HasOne(s => s.SubDistributor)
                .WithMany()
                .HasForeignKey(s => s.SubDistributorId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<SalesInvoice>()
                .HasOne(s => s.Customer)
                .WithMany()
                .HasForeignKey(s => s.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<SalesInvoiceItem>()
                .HasOne(i => i.SalesInvoice)
                .WithMany()
                .HasForeignKey(i => i.SalesInvoiceId)
                .OnDelete(DeleteBehavior.Cascade);
        }

    }
}
 