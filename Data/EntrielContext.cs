using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace STTproject.Data;

public partial class EntrielContext : DbContext
{
    public EntrielContext()
    {
    }

    public EntrielContext(DbContextOptions<EntrielContext> options)
        : base(options)
    {
    }

    public virtual DbSet<CompanyItem> CompanyItems { get; set; }

    public virtual DbSet<CompanyItemPriceHistory> CompanyItemPriceHistories { get; set; }

    public virtual DbSet<Customer> Customers { get; set; }

    public virtual DbSet<CustomerMapping> CustomerMappings { get; set; }

    public virtual DbSet<DeletedSalesInvoice> DeletedSalesInvoices { get; set; }

    public virtual DbSet<DeletedSalesInvoiceItem> DeletedSalesInvoiceItems { get; set; }

    public virtual DbSet<ImportBatch> ImportBatches { get; set; }

    public virtual DbSet<ImportError> ImportErrors { get; set; }

    public virtual DbSet<ImportErrorBatch> ImportErrorBatches { get; set; }

    public virtual DbSet<ImportErrorEvidence> ImportErrorEvidences { get; set; }

    public virtual DbSet<ImportFile> ImportFiles { get; set; }

    public virtual DbSet<ImportTemplate> ImportTemplates { get; set; }

    public virtual DbSet<ImportTemplateColumn> ImportTemplateColumns { get; set; }

    public virtual DbSet<ImportTemplateColumnAlias> ImportTemplateColumnAliases { get; set; }

    public virtual DbSet<ImportTemplateSheet> ImportTemplateSheets { get; set; }

    public virtual DbSet<ItemsUom> ItemsUoms { get; set; }

    public virtual DbSet<ItemsUomPriceHistory> ItemsUomPriceHistories { get; set; }

    public virtual DbSet<SalesInvoice> SalesInvoices { get; set; }

    public virtual DbSet<SalesInvoiceDeletionBatch> SalesInvoiceDeletionBatches { get; set; }

    public virtual DbSet<SalesInvoiceItem> SalesInvoiceItems { get; set; }

    public virtual DbSet<SubDistributor> SubDistributors { get; set; }

    public virtual DbSet<SubdItem> SubdItems { get; set; }

    public virtual DbSet<TestCustomer> TestCustomers { get; set; }

    public virtual DbSet<User> Users { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseSqlServer("Name=ConnectionStrings:DefaultConnection");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CompanyItem>(entity =>
        {
            entity.HasKey(e => e.CompanyItemId).HasName("PK__CompanyI__2A0E98388CD1744A");

            entity.ToTable("CompanyItem");

            entity.HasIndex(e => e.ItemCode, "UQ__CompanyI__3ECC0FEA8C13BEF6").IsUnique();

            entity.Property(e => e.Category)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())", "DF__Customer__Create__3E52440B")
                .HasColumnType("datetime");
            entity.Property(e => e.IsActive).HasDefaultValue(true, "DF__Customer__IsActi__3D5E1FD2");
            entity.Property(e => e.ItemCode)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.ItemName).HasMaxLength(150);
            entity.Property(e => e.Ppg)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("PPG");
            entity.Property(e => e.Principal)
                .HasMaxLength(150)
                .IsUnicode(false);
            entity.Property(e => e.StockPrice).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.StockPriceNational).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.StockPriceRegional).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.StockUomName).HasMaxLength(20);
            entity.Property(e => e.UpdatedDate).HasColumnType("datetime");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.CompanyItemCreatedByNavigations)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("FK_CompanyItem_CreatedBy");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.CompanyItemUpdatedByNavigations)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("FK_CompanyItem_UpdatedBy");
        });

        modelBuilder.Entity<CompanyItemPriceHistory>(entity =>
        {
            entity.HasKey(e => e.CompanyItemPriceHistoryId).HasName("PK__CompanyI__BA10F1344EDBBD81");

            entity.ToTable("CompanyItemPriceHistory");

            entity.HasIndex(e => new { e.CompanyItemId, e.PriceListType, e.EffectivityDateOnly }, "UX_CompanyItemPriceHistory_Pending")
                .IsUnique()
                .HasFilter("([AppliedDate] IS NULL)");

            entity.Property(e => e.AppliedDate).HasColumnType("datetime");
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.EffectivityDate).HasColumnType("datetime");
            entity.Property(e => e.EffectivityDateOnly).HasComputedColumnSql("(CONVERT([date],[EffectivityDate]))", true);
            entity.Property(e => e.NewPrice).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.OldPrice).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.PriceIncreaseAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.PriceListType)
                .HasMaxLength(20)
                .IsUnicode(false);

            entity.HasOne(d => d.CompanyItem).WithMany(p => p.CompanyItemPriceHistories)
                .HasForeignKey(d => d.CompanyItemId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__CompanyIt__Compa__3D2915A8");
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(e => e.CustomerId).HasName("PK__Customer__A4AE64D8494BD832");

            entity.ToTable("Customer");

            entity.HasIndex(e => new { e.CustomerCode, e.CustomerName, e.SubDistributorId, e.SubdCustCode, e.SubdCustName }, "UQ_Customer_Code_Name").IsUnique();

            entity.Property(e => e.AddressLine).HasMaxLength(255);
            entity.Property(e => e.City).HasMaxLength(100);
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CustomerCode)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.CustomerName)
                .HasMaxLength(150)
                .IsUnicode(false);
            entity.Property(e => e.CustomerType)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Province).HasMaxLength(100);
            entity.Property(e => e.SubdCustCode)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.SubdCustName)
                .HasMaxLength(200)
                .IsUnicode(false);
            entity.Property(e => e.UpdatedDate).HasColumnType("datetime");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.CustomerCreatedByNavigations)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("FK_Customer_CreatedBy");

            entity.HasOne(d => d.SubDistributor).WithMany(p => p.Customers)
                .HasForeignKey(d => d.SubDistributorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Customer_SubDistributor");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.CustomerUpdatedByNavigations)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("FK_Customer_UpdatedBy");
        });

        modelBuilder.Entity<CustomerMapping>(entity =>
        {
            entity.ToTable("CustomerMapping");

            entity.HasIndex(e => new { e.SubDistributorId, e.SubdCustCode }, "UX_CustomerMapping_Subd_SourceCode").IsUnique();

            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())", "DF_CustomerMapping_CreatedDate")
                .HasColumnType("datetime");
            entity.Property(e => e.SubdCustCode)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.SubdCustName)
                .HasMaxLength(200)
                .IsUnicode(false);
            entity.Property(e => e.UpdatedDate).HasColumnType("datetime");

            entity.HasOne(d => d.Customer).WithMany(p => p.CustomerMappings)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CustomerMapping_Customer");

            entity.HasOne(d => d.SubDistributor).WithMany(p => p.CustomerMappings)
                .HasForeignKey(d => d.SubDistributorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CustomerMapping_SubDistributor");
        });

        modelBuilder.Entity<DeletedSalesInvoice>(entity =>
        {
            entity.HasKey(e => e.DeletedSalesInvoiceId).HasName("PK__DeletedS__FAABA1B9C3C943E8");

            entity.ToTable("DeletedSalesInvoice");

            entity.HasIndex(e => e.DeletionBatchId, "IX_DSI_Batch");

            entity.Property(e => e.OrderType).HasMaxLength(50);
            entity.Property(e => e.SalesInvoiceCode).HasMaxLength(100);
            entity.Property(e => e.SalesMan).HasMaxLength(200);

            entity.HasOne(d => d.DeletionBatch).WithMany(p => p.DeletedSalesInvoices)
                .HasForeignKey(d => d.DeletionBatchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__DeletedSa__Delet__00AA174D");
        });

        modelBuilder.Entity<DeletedSalesInvoiceItem>(entity =>
        {
            entity.HasKey(e => e.DeletedSalesInvoiceItemId).HasName("PK__DeletedS__185AA697D2142B6D");

            entity.ToTable("DeletedSalesInvoiceItem");

            entity.HasIndex(e => e.DeletedSalesInvoiceId, "IX_DSII_Invoice");

            entity.Property(e => e.Amount).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.DeletedSalesInvoice).WithMany(p => p.DeletedSalesInvoiceItems)
                .HasForeignKey(d => d.DeletedSalesInvoiceId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__DeletedSa__Delet__038683F8");
        });

        modelBuilder.Entity<ImportBatch>(entity =>
        {
            entity.ToTable("ImportBatch");

            entity.HasIndex(e => e.ImportFileId, "IX_ImportBatch_ImportFileId");

            entity.Property(e => e.ImportBatchId).ValueGeneratedNever();
            entity.Property(e => e.BatchStatus)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.FileName).HasMaxLength(255);
            entity.Property(e => e.ImportType).HasMaxLength(50);
            entity.Property(e => e.Principal)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.UploadedAt).HasColumnType("datetime");

            entity.HasOne(d => d.ImportFile).WithMany(p => p.ImportBatches)
                .HasForeignKey(d => d.ImportFileId)
                .HasConstraintName("FK_ImportBatch_ImportFileId");

            entity.HasOne(d => d.SubDistributor).WithMany(p => p.ImportBatches)
                .HasForeignKey(d => d.SubDistributorId)
                .HasConstraintName("FK_ImportBatch_SubDistributor");

            entity.HasOne(d => d.UploadedByNavigation).WithMany(p => p.ImportBatches)
                .HasForeignKey(d => d.UploadedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ImportBatch_Users");
        });

        modelBuilder.Entity<ImportError>(entity =>
        {
            entity.ToTable("ImportError");

            entity.HasIndex(e => new { e.Fingerprint, e.Status }, "IX_ImportError_Fingerprint_Status").HasFilter("([Fingerprint] IS NOT NULL)");

            entity.HasIndex(e => e.PreviousImportErrorId, "IX_ImportError_PreviousImportErrorId").HasFilter("([PreviousImportErrorId] IS NOT NULL)");

            entity.HasIndex(e => e.Fingerprint, "UX_ImportError_Fingerprint_Open")
                .IsUnique()
                .HasFilter("([Fingerprint] IS NOT NULL AND [Status]<>'Resolved')");

            entity.Property(e => e.ImportErrorId).ValueGeneratedNever();
            entity.Property(e => e.CreatedAt).HasColumnType("datetime");
            entity.Property(e => e.ErrorMessage).HasMaxLength(1000);
            entity.Property(e => e.ErrorType)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.FieldName)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.Fingerprint)
                .HasMaxLength(64)
                .IsUnicode(false)
                .IsFixedLength();
            entity.Property(e => e.ResolutionType)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.ResolvedAt).HasColumnType("datetime");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .IsUnicode(false);

            entity.HasOne(d => d.ImportBatch).WithMany(p => p.ImportErrorImportBatches)
                .HasForeignKey(d => d.ImportBatchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ImportError_ImportBatch");

            entity.HasOne(d => d.PreviousImportError).WithMany(p => p.InversePreviousImportError)
                .HasForeignKey(d => d.PreviousImportErrorId)
                .HasConstraintName("FK_ImportError_PreviousImportError");

            entity.HasOne(d => d.ResolvedByNavigation).WithMany(p => p.ImportErrors)
                .HasForeignKey(d => d.ResolvedBy)
                .HasConstraintName("FK_ImportError_Users");

            entity.HasOne(d => d.VerifiedImportBatch).WithMany(p => p.ImportErrorVerifiedImportBatches)
                .HasForeignKey(d => d.VerifiedImportBatchId)
                .HasConstraintName("FK_ImportError_VerifiedImportBatch");
        });

        modelBuilder.Entity<ImportErrorBatch>(entity =>
        {
            entity.ToTable("ImportErrorBatch");

            entity.HasIndex(e => e.EvidenceId, "IX_ImportErrorBatch_EvidenceId");

            entity.HasIndex(e => e.ImportBatchId, "IX_ImportErrorBatch_ImportBatchId");

            entity.HasIndex(e => new { e.ImportErrorId, e.ImportBatchId }, "UQ_ImportErrorBatch_Error_Batch").IsUnique();

            entity.Property(e => e.FirstSeenAt).HasColumnType("datetime");
            entity.Property(e => e.LastSeenAt).HasColumnType("datetime");
            entity.Property(e => e.Occurrences).HasDefaultValue(1, "DF_ImportErrorBatch_Occurrences");

            entity.HasOne(d => d.Evidence).WithMany(p => p.ImportErrorBatches)
                .HasForeignKey(d => d.EvidenceId)
                .HasConstraintName("FK_ImportErrorBatch_Evidence");

            entity.HasOne(d => d.ImportBatch).WithMany(p => p.ImportErrorBatches)
                .HasForeignKey(d => d.ImportBatchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ImportErrorBatch_ImportBatch");

            entity.HasOne(d => d.ImportError).WithMany(p => p.ImportErrorBatches)
                .HasForeignKey(d => d.ImportErrorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ImportErrorBatch_ImportError");
        });

        modelBuilder.Entity<ImportErrorEvidence>(entity =>
        {
            entity.HasKey(e => e.EvidenceId);

            entity.ToTable("ImportErrorEvidence");

            entity.HasIndex(e => new { e.ImportErrorId, e.EvidenceHash }, "UX_ImportErrorEvidence_Error_Hash").IsUnique();

            entity.Property(e => e.EvidenceId).ValueGeneratedNever();
            entity.Property(e => e.CreatedAt).HasColumnType("datetime");
            entity.Property(e => e.EvidenceHash)
                .HasMaxLength(64)
                .IsUnicode(false)
                .IsFixedLength();

            entity.HasOne(d => d.ImportError).WithMany(p => p.ImportErrorEvidences)
                .HasForeignKey(d => d.ImportErrorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ImportErrorEvidence_ImportError");
        });

        modelBuilder.Entity<ImportFile>(entity =>
        {
            entity.ToTable("ImportFile");

            entity.HasIndex(e => e.FileHash, "UX_ImportFile_FileHash").IsUnique();

            entity.Property(e => e.ImportFileId).ValueGeneratedNever();
            entity.Property(e => e.CreatedAt).HasColumnType("datetime");
            entity.Property(e => e.FileHash)
                .HasMaxLength(64)
                .IsUnicode(false)
                .IsFixedLength();
            entity.Property(e => e.FileName).HasMaxLength(255);
            entity.Property(e => e.StoragePath).HasMaxLength(500);
        });

        modelBuilder.Entity<ImportTemplate>(entity =>
        {
            entity.HasKey(e => e.ImportTemplateId).HasName("PK__ImportTe__AEC13D2C6C1A2EB2");

            entity.HasIndex(e => new { e.ImportType, e.SubDistributorId, e.Principal, e.TemplateName }, "UX_ImportTemplates_ActiveName")
                .IsUnique()
                .HasFilter("([IsActive]=(1))");

            entity.Property(e => e.CreatedDate).HasDefaultValueSql("(sysutcdatetime())", "DF_ImportTemplates_Created");
            entity.Property(e => e.ImportType)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.IsActive).HasDefaultValue(true, "DF_ImportTemplates_Active");
            entity.Property(e => e.Principal).HasMaxLength(100);
            entity.Property(e => e.TemplateName).HasMaxLength(150);
            entity.Property(e => e.Version).HasDefaultValue(1, "DF_ImportTemplates_Version");

            entity.HasOne(d => d.SubDistributor).WithMany(p => p.ImportTemplates)
                .HasForeignKey(d => d.SubDistributorId)
                .HasConstraintName("FK_ImportTemplates_SubDistributors");
        });

        modelBuilder.Entity<ImportTemplateColumn>(entity =>
        {
            entity.HasKey(e => e.ImportTemplateColumnId).HasName("PK__ImportTe__F6EF3E090006E83F");

            entity.HasIndex(e => new { e.ImportTemplateId, e.SortOrder }, "IX_ImportTemplateColumns_Template");

            entity.HasIndex(e => new { e.ImportTemplateId, e.ImportTemplateSheetId, e.HeaderText }, "UX_ITC_Template_Header").IsUnique();

            entity.Property(e => e.FieldKey)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.HeaderText).HasMaxLength(200);
            entity.Property(e => e.RuleType)
                .HasMaxLength(30)
                .IsUnicode(false)
                .HasDefaultValue("Direct", "DF_ITC_RuleType");

            entity.HasOne(d => d.ImportTemplate).WithMany(p => p.ImportTemplateColumns)
                .HasForeignKey(d => d.ImportTemplateId)
                .HasConstraintName("FK_ITC_Template");

            entity.HasOne(d => d.ImportTemplateSheet).WithMany(p => p.ImportTemplateColumns)
                .HasForeignKey(d => d.ImportTemplateSheetId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ITC_Sheet");
        });

        modelBuilder.Entity<ImportTemplateColumnAlias>(entity =>
        {
            entity.HasKey(e => e.ImportTemplateColumnAliasId).HasName("PK__ImportTe__5AC20B7E06B9E5CC");

            entity.HasIndex(e => new { e.ImportTemplateColumnId, e.AliasText }, "UX_ITCA_Column_Alias").IsUnique();

            entity.Property(e => e.AliasText).HasMaxLength(200);

            entity.HasOne(d => d.ImportTemplateColumn).WithMany(p => p.ImportTemplateColumnAliases)
                .HasForeignKey(d => d.ImportTemplateColumnId)
                .HasConstraintName("FK_ITCA_Column");
        });

        modelBuilder.Entity<ImportTemplateSheet>(entity =>
        {
            entity.HasKey(e => e.ImportTemplateSheetId).HasName("PK__ImportTe__4755275865B88B8A");

            entity.Property(e => e.HeaderRowCount).HasDefaultValue(1, "DF_ITS_HeaderRowCount");
            entity.Property(e => e.HeaderRowMode)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Automatic");
            entity.Property(e => e.IsRequired).HasDefaultValue(true);
            entity.Property(e => e.SheetLabel).HasMaxLength(100);
            entity.Property(e => e.SheetMatchMode)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.SheetMatchValue).HasMaxLength(100);

            entity.HasOne(d => d.ImportTemplate).WithMany(p => p.ImportTemplateSheets)
                .HasForeignKey(d => d.ImportTemplateId)
                .HasConstraintName("FK__ImportTem__Impor__53D770D6");
        });

        modelBuilder.Entity<ItemsUom>(entity =>
        {
            entity.HasKey(e => e.ItemsUomId).HasName("PK__ItemsUom__53724957BA9B3FE8");

            entity.ToTable("ItemsUom");

            entity.HasIndex(e => new { e.SubdItemId, e.UomName }, "UQ_ItemsUom_SubdItem_Uom").IsUnique();

            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())", "DF__ItemsUom__Create__18EBB532")
                .HasColumnType("datetime");
            entity.Property(e => e.IsActive).HasDefaultValue(true, "DF__ItemsUom__IsActi__690797E6");
            entity.Property(e => e.Price).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.UomName)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.UpdatedDate).HasColumnType("datetime");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.ItemsUomCreatedByNavigations)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("FK_ItemsUom_CreatedBy");

            entity.HasOne(d => d.SubdItem).WithMany(p => p.ItemsUoms)
                .HasForeignKey(d => d.SubdItemId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ItemsUom_SubdItem");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.ItemsUomUpdatedByNavigations)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("FK_ItemsUom_UpdatedBy");
        });

        modelBuilder.Entity<ItemsUomPriceHistory>(entity =>
        {
            entity.HasKey(e => e.ItemsUomPriceHistoryId).HasName("PK__ItemsUom__4D74DA34B69D7D99");

            entity.ToTable("ItemsUomPriceHistory");

            entity.Property(e => e.AppliedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.EffectivityDate).HasColumnType("datetime");
            entity.Property(e => e.NewPrice).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.OldPrice).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.CompanyItem).WithMany(p => p.ItemsUomPriceHistories)
                .HasForeignKey(d => d.CompanyItemId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ItemsUomPriceHistory_CompanyItem");

            entity.HasOne(d => d.CompanyItemPriceHistory).WithMany(p => p.ItemsUomPriceHistories)
                .HasForeignKey(d => d.CompanyItemPriceHistoryId)
                .HasConstraintName("FK__ItemsUomP__Compa__3F115E1A");

            entity.HasOne(d => d.ItemsUom).WithMany(p => p.ItemsUomPriceHistories)
                .HasForeignKey(d => d.ItemsUomId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ItemsUomPriceHistory_ItemsUom");
        });

        modelBuilder.Entity<SalesInvoice>(entity =>
        {
            entity.HasKey(e => e.SalesInvoiceId).HasName("PK__SalesInv__BA05CD1AE9FBEC78");

            entity.ToTable("SalesInvoice");

            entity.HasIndex(e => new { e.SalesInvoiceCode, e.OrderType, e.SubDistributorId, e.CustomerId, e.SalesMan }, "UQ_SalesInvoice_Code_OrderType_Customer_SalesMan").IsUnique();

            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.OrderType)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Invoice", "DF_SalesInvoice_OrderType");
            entity.Property(e => e.SalesInvoiceCode)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.SalesMan).HasMaxLength(100);
            entity.Property(e => e.UpdatedDate).HasColumnType("datetime");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.SalesInvoiceCreatedByNavigations)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("FK_SalesInvoice_CreatedBy");

            entity.HasOne(d => d.Customer).WithMany(p => p.SalesInvoices)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SalesInvoice_Customer");

            entity.HasOne(d => d.SubDistributor).WithMany(p => p.SalesInvoices)
                .HasForeignKey(d => d.SubDistributorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SalesInvoice_SubDistributor");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.SalesInvoiceUpdatedByNavigations)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("FK_SalesInvoice_UpdatedBy");
        });

        modelBuilder.Entity<SalesInvoiceDeletionBatch>(entity =>
        {
            entity.HasKey(e => e.DeletionBatchId).HasName("PK__SalesInv__897FF3163671BA3D");

            entity.ToTable("SalesInvoiceDeletionBatch");

            entity.Property(e => e.DeletedDate).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.DeletionType)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Deleted");
        });

        modelBuilder.Entity<SalesInvoiceItem>(entity =>
        {
            entity.HasKey(e => e.SalesInvoiceItemId).HasName("PK__SalesInv__BA84EC64DC7B368D");

            entity.ToTable("SalesInvoiceItem");

            entity.Property(e => e.Amount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.UpdatedDate).HasColumnType("datetime");

            entity.HasOne(d => d.ItemsUom).WithMany(p => p.SalesInvoiceItems)
                .HasForeignKey(d => d.ItemsUomId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SalesInvoiceItem_ItemsUom");

            entity.HasOne(d => d.SalesInvoice).WithMany(p => p.SalesInvoiceItems)
                .HasForeignKey(d => d.SalesInvoiceId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SalesInvoiceItem_Invoice");

            entity.HasOne(d => d.SubdItem).WithMany(p => p.SalesInvoiceItems)
                .HasForeignKey(d => d.SubdItemId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SalesInvoiceItem_SubdItem");
        });

        modelBuilder.Entity<SubDistributor>(entity =>
        {
            entity.HasKey(e => e.SubDistributorId).HasName("PK__SubDistr__954B9BCD5B53D31B");

            entity.ToTable("SubDistributor");

            entity.HasIndex(e => e.SubdCode, "UQ_SubDistributor_SubdCode").IsUnique();

            entity.Property(e => e.CityMunicipality).HasMaxLength(100);
            entity.Property(e => e.CompanySubdCode)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())", "DF__SubDistri__Creat__10566F31")
                .HasColumnType("datetime");
            entity.Property(e => e.IsActive).HasDefaultValue(true, "DF__SubDistri__IsAct__0F624AF8");
            entity.Property(e => e.PriceListType)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.Province).HasMaxLength(100);
            entity.Property(e => e.SubdCode)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.SubdName)
                .HasMaxLength(150)
                .IsUnicode(false);
            entity.Property(e => e.UpdatedDate).HasColumnType("datetime");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.SubDistributorCreatedByNavigations)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("FK_SubDistributor_CreatedBy");

            entity.HasOne(d => d.Encoder).WithMany(p => p.SubDistributorEncoders)
                .HasForeignKey(d => d.EncoderId)
                .HasConstraintName("FK_SubDistributor_User");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.SubDistributorUpdatedByNavigations)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("FK_SubDistributor_UpdatedBy");
        });

        modelBuilder.Entity<SubdItem>(entity =>
        {
            entity.HasKey(e => e.SubdItemId).HasName("PK__SubdItem__873BB656501717AC");

            entity.ToTable("SubdItem");

            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.ItemName).HasMaxLength(150);
            entity.Property(e => e.SubdItemCode)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.UpdatedDate).HasColumnType("datetime");

            entity.HasOne(d => d.CompanyItem).WithMany(p => p.SubdItems)
                .HasForeignKey(d => d.CompanyItemId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SubdItem_CompanyItem");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.SubdItemCreatedByNavigations)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("FK_SubdItem_CreatedBy");

            entity.HasOne(d => d.SubDistributor).WithMany(p => p.SubdItems)
                .HasForeignKey(d => d.SubDistributorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SubdItem_SubDistributor");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.SubdItemUpdatedByNavigations)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("FK_SubdItem_UpdatedBy");
        });

        modelBuilder.Entity<TestCustomer>(entity =>
        {
            entity.HasKey(e => e.CustomerId).HasName("PK__TestCust__A4AE64D8C69B0AA2");

            entity.ToTable("TestCustomer");

            entity.HasIndex(e => new { e.CustomerCode, e.CustomerName }, "UQ_TestCustomer_Code_Name").IsUnique();

            entity.Property(e => e.AddressLine).HasMaxLength(255);
            entity.Property(e => e.City).HasMaxLength(100);
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CustomerCode)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.CustomerName)
                .HasMaxLength(150)
                .IsUnicode(false);
            entity.Property(e => e.CustomerType)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Province).HasMaxLength(100);
            entity.Property(e => e.UpdatedDate).HasColumnType("datetime");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.TestCustomerCreatedByNavigations)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("FK_TestCustomer_CreatedBy");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.TestCustomerUpdatedByNavigations)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("FK_TestCustomer_UpdatedBy");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PK__Users__1788CC4C13EF7F33");

            entity.HasIndex(e => e.Username, "UQ__Users__536C85E42A318390").IsUnique();

            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .IsUnicode(false);
            entity.Property(e => e.FullName).HasMaxLength(100);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Password).HasMaxLength(255);
            entity.Property(e => e.Role)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.UpdatedDate).HasColumnType("datetime");
            entity.Property(e => e.Username).HasMaxLength(50);
        });
        modelBuilder.HasSequence<int>("ImportBatchIdSequence");
        modelBuilder.HasSequence<int>("ImportErrorIdSequence");

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
