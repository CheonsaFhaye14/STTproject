using Microsoft.EntityFrameworkCore;

namespace STTproject.Data;

public partial class SttprojectContext
{
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ImportTemplate>()
            .Property(e => e.Version)
            .IsConcurrencyToken();
    }
}