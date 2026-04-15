using Microsoft.EntityFrameworkCore;
using STTproject.Models;
namespace STTproject.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options) { }
         
        public DbSet<SubDistributor> SubDistributors { get; set; }
        public DbSet<RecentActivity> RecentActivitys { get; set; }
        public DbSet<SalesInvoice> SalesInvoices { get; set; }
        public DbSet<SalesInvoiceItem> SalesInvoiceItems { get; set; }
        public DbSet<SubdItem> SubdItems { get; set; }

    }
}
 