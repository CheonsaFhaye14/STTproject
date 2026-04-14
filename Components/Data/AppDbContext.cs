using Microsoft.EntityFrameworkCore;
using STTproject.Components.Models;
namespace STTproject.Components.Data
{
    public class AppDbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options) { }

        public DbSet<SubDistributor> SubDistributors { get; set; }
        public DbSet<RecentActivity> RecentActivity { get; set; }
        public DbSet<SalesInvoice> SalesInvoice { get; set; }
        public DbSet<SalesInvoiceItems> SalesInvoiceItems { get; set; }
        public DbSet<SubdItemMap> SubdItemMap { get; set; }

    }
}
