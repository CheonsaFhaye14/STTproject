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

    public virtual DbSet<ImportFile> ImportFiles { get; set; }

    public virtual DbSet<ImportFileColumnMapping> ImportFileColumnMappings { get; set; }

    public virtual DbSet<ImportFileSheet> ImportFileSheets { get; set; }

    public virtual DbSet<ImportRow> ImportRows { get; set; }

    public virtual DbSet<ImportTemplate> ImportTemplates { get; set; }

    public virtual DbSet<ImportTemplateColumn> ImportTemplateColumns { get; set; }

    public virtual DbSet<ImportTemplateSheet> ImportTemplateSheets { get; set; }

    public virtual DbSet<ItemsUom> ItemsUoms { get; set; }

    public virtual DbSet<ItemsUomPriceHistory> ItemsUomPriceHistories { get; set; }

    public virtual DbSet<SalesInvoice> SalesInvoices { get; set; }

    public virtual DbSet<SalesInvoiceItem> SalesInvoiceItems { get; set; }

    public virtual DbSet<SubDistributor> SubDistributors { get; set; }

    public virtual DbSet<SubdItem> SubdItems { get; set; }

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
            entity.Property(e => e.Principal)
                .HasMaxLength(150)
                .IsUnicode(false);
            entity.Property(e => e.StockPrice).HasColumnType("decimal(18, 2)");
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

            entity.Property(e => e.AppliedDate).HasColumnType("datetime");
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.EffectivityDate).HasColumnType("datetime");
            entity.Property(e => e.NewPrice).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.OldPrice).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.PriceIncreaseAmount).HasColumnType("decimal(18, 2)");

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

        modelBuilder.Entity<ImportFile>(entity =>
        {
            entity.HasKey(e => e.ImportFileId).HasName("PK__ImportFi__D9EEC2444B08C390");

            entity.HasIndex(e => e.Sha256, "IX_ImportFiles_Hash");

            entity.HasIndex(e => new { e.ImportType, e.SubDistributorId, e.UploadedDate }, "IX_ImportFiles_Lookup").IsDescending(false, false, true);

            entity.HasIndex(e => e.Sha256, "IX_ImportFiles_Sha256");

            entity.Property(e => e.ImportType)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.OriginalFileName).HasMaxLength(260);
            entity.Property(e => e.Sha256)
                .HasMaxLength(64)
                .IsUnicode(false)
                .IsFixedLength();
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Prepared", "DF_ImportFiles_Status");
            entity.Property(e => e.StoredPath).HasMaxLength(500);
            entity.Property(e => e.UploadedDate).HasDefaultValueSql("(sysutcdatetime())", "DF_ImportFiles_Uploaded");

            entity.HasOne(d => d.ImportTemplate).WithMany(p => p.ImportFiles)
                .HasForeignKey(d => d.ImportTemplateId)
                .HasConstraintName("FK_ImportFiles_Template");

            entity.HasOne(d => d.SubDistributor).WithMany(p => p.ImportFiles)
                .HasForeignKey(d => d.SubDistributorId)
                .HasConstraintName("FK_ImportFiles_SubDistributors");
        });

        modelBuilder.Entity<ImportFileColumnMapping>(entity =>
        {
            entity.HasKey(e => new { e.ImportFileSheetId, e.ImportTemplateColumnId }).HasName("PK__ImportFi__BCC745FAB54F8CB4");

            entity.Property(e => e.ActualHeader).HasMaxLength(200);

            entity.HasOne(d => d.ImportFileSheet).WithMany(p => p.ImportFileColumnMappings)
                .HasForeignKey(d => d.ImportFileSheetId)
                .HasConstraintName("FK__ImportFil__Impor__68D28DBC");

            entity.HasOne(d => d.ImportTemplateColumn).WithMany(p => p.ImportFileColumnMappings)
                .HasForeignKey(d => d.ImportTemplateColumnId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ImportFil__Impor__69C6B1F5");
        });

        modelBuilder.Entity<ImportFileSheet>(entity =>
        {
            entity.HasKey(e => e.ImportFileSheetId).HasName("PK__ImportFi__33A9B61AE43CC37D");

            entity.Property(e => e.SheetName).HasMaxLength(100);

            entity.HasOne(d => d.ImportFile).WithMany(p => p.ImportFileSheets)
                .HasForeignKey(d => d.ImportFileId)
                .HasConstraintName("FK__ImportFil__Impor__6501FCD8");

            entity.HasOne(d => d.ImportTemplateSheet).WithMany(p => p.ImportFileSheets)
                .HasForeignKey(d => d.ImportTemplateSheetId)
                .HasConstraintName("FK__ImportFil__Impor__65F62111");
        });

        modelBuilder.Entity<ImportRow>(entity =>
        {
            entity.HasKey(e => e.ImportRowId).HasName("PK__ImportRo__7A161D66E42E3B43");

            entity.HasIndex(e => new { e.ImportFileSheetId, e.RowNumber }, "IX_ImportRows_Sheet");

            entity.Property(e => e.IsSelected).HasDefaultValue(true);
            entity.Property(e => e.Status)
                .HasMaxLength(15)
                .IsUnicode(false);

            entity.HasOne(d => d.ImportFileSheet).WithMany(p => p.ImportRows)
                .HasForeignKey(d => d.ImportFileSheetId)
                .HasConstraintName("FK__ImportRow__Impor__6CA31EA0");
        });

        modelBuilder.Entity<ImportTemplate>(entity =>
        {
            entity.HasKey(e => e.ImportTemplateId).HasName("PK__ImportTe__AEC13D2C6C1A2EB2");

            entity.HasIndex(e => new { e.ImportType, e.SubDistributorId, e.Principal }, "UX_ImportTemplates_ActiveScope")
                .IsUnique()
                .HasFilter("([IsActive]=(1))");

            entity.Property(e => e.AllowGlobalFallback).HasDefaultValue(true, "DF_ImportTemplates_Fallback");
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

            entity.HasIndex(e => new { e.ImportTemplateId, e.HeaderText }, "UX_ITC_Template_Header").IsUnique();

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

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
