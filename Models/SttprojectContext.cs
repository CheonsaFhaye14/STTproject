using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace STTproject.Models;

public partial class SttprojectContext : DbContext
{
    public SttprojectContext()
    {
    }

    public SttprojectContext(DbContextOptions<SttprojectContext> options)
        : base(options)
    {
    }

    public virtual DbSet<CompanyItem> CompanyItems { get; set; }

    public virtual DbSet<Customer> Customers { get; set; }

    public virtual DbSet<CustomerBranch> CustomerBranches { get; set; }

    public virtual DbSet<ItemsUom> ItemsUoms { get; set; }

    public virtual DbSet<SalesInvoice> SalesInvoices { get; set; }

    public virtual DbSet<SalesInvoiceItem> SalesInvoiceItems { get; set; }

    public virtual DbSet<SubDistributor> SubDistributors { get; set; }

    public virtual DbSet<SubdItem> SubdItems { get; set; }

    public virtual DbSet<User> Users { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseSqlServer("Name=DefaultConnection");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CompanyItem>(entity =>
        {
            entity.HasKey(e => e.CompanyItemId).HasName("PK__CompanyI__2A0E98388B4E0DF7");

            entity.ToTable("CompanyItem", "ojt");

            entity.HasIndex(e => e.ItemCode, "UQ__CompanyI__3ECC0FEA3C916558").IsUnique();

            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.ItemCode).HasMaxLength(50);
            entity.Property(e => e.ItemName).HasMaxLength(150);
            entity.Property(e => e.Principal).HasMaxLength(150);
            entity.Property(e => e.UpdatedDate).HasColumnType("datetime");
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(e => e.CustomerId).HasName("PK__Customer__A4AE64D8F3D04E31");

            entity.ToTable("Customer", "ojt");

            entity.HasIndex(e => e.CustomerCode, "UQ_ojt_Customer_Code").IsUnique();

            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CustomerCode).HasMaxLength(50);
            entity.Property(e => e.CustomerName).HasMaxLength(150);
            entity.Property(e => e.CustomerType).HasMaxLength(50);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
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

        modelBuilder.Entity<CustomerBranch>(entity =>
        {
            entity.HasKey(e => e.CustomerBranchId).HasName("PK__Customer__6F555BBDA9224030");

            entity.ToTable("CustomerBranch", "ojt");

            entity.HasIndex(e => e.CustomerId, "UX_CustomerBranch_Default")
                .IsUnique()
                .HasFilter("([IsDefault]=(1))");

            entity.Property(e => e.AddressLine).HasMaxLength(255);
            entity.Property(e => e.BranchName)
                .HasMaxLength(100)
                .HasDefaultValue("Main");
            entity.Property(e => e.City).HasMaxLength(100);
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.IsDefault).HasDefaultValue(true);
            entity.Property(e => e.Province).HasMaxLength(100);
            entity.Property(e => e.UpdatedDate).HasColumnType("datetime");
            entity.Property(e => e.ZipCode).HasMaxLength(20);

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.CustomerBranchCreatedByNavigations)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("FK_CustomerBranch_CreatedBy");

            entity.HasOne(d => d.Customer).WithOne(p => p.CustomerBranch)
                .HasForeignKey<CustomerBranch>(d => d.CustomerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CustomerBranch_Customer");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.CustomerBranchUpdatedByNavigations)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("FK_CustomerBranch_UpdatedBy");
        });

        modelBuilder.Entity<ItemsUom>(entity =>
        {
            entity.HasKey(e => e.ItemsUomId).HasName("PK__ItemsUom__53724957D2EF372F");

            entity.ToTable("ItemsUom", "ojt");

            entity.HasIndex(e => new { e.CompanyItemId, e.UomName }, "UQ_ItemsUom_CompanyItem_Uom").IsUnique();

            entity.HasIndex(e => e.CompanyItemId, "UX_ItemsUom_OneBaseUnit")
                .IsUnique()
                .HasFilter("([IsBaseUnit]=(1))");

            entity.Property(e => e.ConversionToBase).HasColumnType("decimal(18, 4)");
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.Price).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.UomName).HasMaxLength(50);
            entity.Property(e => e.UpdatedDate).HasColumnType("datetime");

            entity.HasOne(d => d.CompanyItem).WithOne(p => p.ItemsUom)
                .HasForeignKey<ItemsUom>(d => d.CompanyItemId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ItemsUom_CompanyItemId");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.ItemsUomCreatedByNavigations)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("FK_ItemsUom_CreatedBy");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.ItemsUomUpdatedByNavigations)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("FK_ItemsUom_UpdatedBy");
        });

        modelBuilder.Entity<SalesInvoice>(entity =>
        {
            entity.HasKey(e => e.SalesInvoiceId).HasName("PK__SalesInv__BA05CD1A467683E6");

            entity.ToTable("SalesInvoice", "ojt");

            entity.HasIndex(e => e.SalesInvoiceCode, "UQ__SalesInv__C94B6607D4E43E00").IsUnique();

            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.OrderType)
                .HasMaxLength(20)
                .HasDefaultValue("Invoice", "DF_SalesInvoice_OrderType");
            entity.Property(e => e.SalesInvoiceCode).HasMaxLength(50);
            entity.Property(e => e.UpdatedDate).HasColumnType("datetime");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.SalesInvoiceCreatedByNavigations)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("FK_SalesInvoice_CreatedBy");

            entity.HasOne(d => d.CustomerBranch).WithMany(p => p.SalesInvoices)
                .HasForeignKey(d => d.CustomerBranchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SalesInvoice_CustomerBranch");

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
            entity.HasKey(e => e.SalesInvoiceItemId).HasName("PK__SalesInv__BA84EC643ACD48C2");

            entity.ToTable("SalesInvoiceItem", "ojt");

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
            entity.HasKey(e => e.SubDistributorId).HasName("PK__SubDistr__954B9BCD3DD89182");

            entity.ToTable("SubDistributor", "ojt");

            entity.HasIndex(e => e.CompanySubdCode, "UQ_SubDistributor_CompanySubdCode").IsUnique();

            entity.HasIndex(e => e.SubdCode, "UQ__SubDistr__67B828E039B89C6A").IsUnique();

            entity.Property(e => e.CityMunicipality).HasMaxLength(100);
            entity.Property(e => e.CompanySubdCode).HasMaxLength(50);
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Province).HasMaxLength(100);
            entity.Property(e => e.SubdCode).HasMaxLength(50);
            entity.Property(e => e.SubdName).HasMaxLength(150);
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
            entity.HasKey(e => e.SubdItemId).HasName("PK__SubdItem__873BB65660984C0F");

            entity.ToTable("SubdItem", "ojt");

            entity.HasIndex(e => new { e.SubDistributorId, e.SubdItemCode }, "UQ_SubdItem_SubdId_Code").IsUnique();

            entity.HasIndex(e => new { e.SubDistributorId, e.CompanyItemId }, "UQ_SubdItem_Subd_CompanyItem").IsUnique();

            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.ItemName).HasMaxLength(150);
            entity.Property(e => e.SubdItemCode).HasMaxLength(50);
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
            entity.HasKey(e => e.UserId).HasName("PK__Users__1788CC4C058488CD");

            entity.ToTable("Users", "ojt");

            entity.HasIndex(e => e.Username, "UQ__Users__536C85E4B8FB930F").IsUnique();

            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.FullName).HasMaxLength(100);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.PasswordHash).HasMaxLength(255);
            entity.Property(e => e.Role).HasMaxLength(20);
            entity.Property(e => e.UpdatedDate).HasColumnType("datetime");
            entity.Property(e => e.Username).HasMaxLength(50);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
