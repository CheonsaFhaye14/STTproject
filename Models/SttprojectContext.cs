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

    public virtual DbSet<SalesInvoice> SalesInvoices { get; set; }

    public virtual DbSet<SalesInvoiceItem> SalesInvoiceItems { get; set; }

    public virtual DbSet<SubDistributor> SubDistributors { get; set; }

    public virtual DbSet<SubdItem> SubdItems { get; set; }

    public virtual DbSet<SubdItemUom> SubdItemUoms { get; set; }

    public virtual DbSet<User> Users { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseSqlServer("Name=DefaultConnection");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CompanyItem>(entity =>
        {
            entity.HasKey(e => e.CompanyItemId).HasName("PK__CompanyI__2A0E98382ABB7092");

            entity.ToTable("CompanyItem");

            entity.HasIndex(e => e.ItemCode, "UQ__CompanyI__3ECC0FEA6CFB0A91").IsUnique();

            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.ItemCode).HasMaxLength(50);
            entity.Property(e => e.ItemName).HasMaxLength(150);
            entity.Property(e => e.UpdatedDate).HasColumnType("datetime");
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(e => e.CustomerId).HasName("PK__Customer__A4AE64D8556BE0E2");

            entity.ToTable("Customer", tb => tb.HasTrigger("trg_Customer_CascadeDeactivate"));

            entity.HasIndex(e => e.CustomerCode, "UQ_dbo_Customer_Code").IsUnique();

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
            entity.HasKey(e => e.CustomerBranchId).HasName("PK__Customer__6F555BBDEFFE3AB0");

            entity.ToTable("CustomerBranch");

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

            entity.HasOne(d => d.Customer).WithMany(p => p.CustomerBranches)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CustomerBranch_Customer");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.CustomerBranchUpdatedByNavigations)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("FK_CustomerBranch_UpdatedBy");
        });

        modelBuilder.Entity<SalesInvoice>(entity =>
        {
            entity.HasKey(e => e.SalesInvoiceId).HasName("PK__SalesInv__BA05CD1AE159E4DB");

            entity.ToTable("SalesInvoice");

            entity.HasIndex(e => e.SalesInvoiceCode, "UQ__SalesInv__C94B6607980D5849").IsUnique();

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
            entity.HasKey(e => e.SalesInvoiceItemId).HasName("PK__SalesInv__BA84EC645D00B8C6");

            entity.ToTable("SalesInvoiceItem");

            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.Price).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.UpdatedDate).HasColumnType("datetime");

            entity.HasOne(d => d.SalesInvoice).WithMany(p => p.SalesInvoiceItems)
                .HasForeignKey(d => d.SalesInvoiceId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SalesInvoiceItem_Invoice");

            entity.HasOne(d => d.SubdItem).WithMany(p => p.SalesInvoiceItems)
                .HasForeignKey(d => d.SubdItemId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SalesInvoiceItem_SubdItem");

            entity.HasOne(d => d.SubdItemUom).WithMany(p => p.SalesInvoiceItems)
                .HasForeignKey(d => d.SubdItemUomId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SalesInvoiceItem_SubdItemUom");
        });

        modelBuilder.Entity<SubDistributor>(entity =>
        {
            entity.HasKey(e => e.SubDistributorId).HasName("PK__SubDistr__954B9BCDACF20B27");

            entity.ToTable("SubDistributor");

            entity.HasIndex(e => e.CompanySubdCode, "UQ_SubDistributor_CompanySubdCode").IsUnique();

            entity.HasIndex(e => e.SubdCode, "UQ__SubDistr__67B828E03B1D1EEA").IsUnique();

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
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SubDistributor_User");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.SubDistributorUpdatedByNavigations)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("FK_SubDistributor_UpdatedBy");
        });

        modelBuilder.Entity<SubdItem>(entity =>
        {
            entity.HasKey(e => e.SubdItemId).HasName("PK__SubdItem__873BB656817A28B1");

            entity.ToTable("SubdItem");

            entity.HasIndex(e => new { e.SubDistributorId, e.SubdItemCode }, "UQ_SubdItem_Code_Per_Subd").IsUnique();

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

        modelBuilder.Entity<SubdItemUom>(entity =>
        {
            entity.HasKey(e => e.SubdItemUomId).HasName("PK__SubdItem__6E1A39AD7B032BDC");

            entity.ToTable("SubdItemUom");

            entity.Property(e => e.ConversionToBase).HasColumnType("decimal(18, 4)");
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.IsSellable).HasDefaultValue(true, "DF_SubdItemUom_IsSellable");
            entity.Property(e => e.Price).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.UomName).HasMaxLength(50);
            entity.Property(e => e.UpdatedDate).HasColumnType("datetime");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.SubdItemUomCreatedByNavigations)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("FK_SubdItemUom_CreatedBy");

            entity.HasOne(d => d.SubdItem).WithMany(p => p.SubdItemUoms)
                .HasForeignKey(d => d.SubdItemId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SubdItemUom_SubdItem");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.SubdItemUomUpdatedByNavigations)
                .HasForeignKey(d => d.UpdatedBy)
                .HasConstraintName("FK_SubdItemUom_UpdatedBy");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PK__Users__1788CC4C6E31FE06");

            entity.HasIndex(e => e.Username, "UQ__Users__536C85E4469D4D32").IsUnique();

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
