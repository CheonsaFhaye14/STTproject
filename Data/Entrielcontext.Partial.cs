using Microsoft.EntityFrameworkCore;

namespace STTproject.Data;

public partial class EntrielContext
{
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ImportTemplate>()
            .Property(e => e.Version)
            .IsConcurrencyToken();

        // ---- Import file / evidence deduplication (model only; the database objects already exist) ----

        // Ids for the dedup tables come from SQL sequences, like ImportBatchIdSequence and ImportErrorIdSequence.
        modelBuilder.HasSequence<int>("ImportFileIdSequence");
        modelBuilder.HasSequence<int>("ImportErrorEvidenceIdSequence");

        // Shared files and evidence are referenced by many rows and must never disappear with their parent, so EF is told to
        // Restrict (the scaffold's default for these links is ClientSetNull). The database foreign keys are NO ACTION, which
        // already refuses such a delete.
        modelBuilder.Entity<ImportBatch>()
            .HasOne(b => b.ImportFile)
            .WithMany(f => f.ImportBatches)
            .HasForeignKey(b => b.ImportFileId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ImportErrorBatch>()
            .HasOne(j => j.Evidence)
            .WithMany(e => e.ImportErrorBatches)
            .HasForeignKey(j => j.EvidenceId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ImportErrorEvidence>()
            .HasOne(e => e.ImportError)
            .WithMany(i => i.ImportErrorEvidences)
            .HasForeignKey(e => e.ImportErrorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}