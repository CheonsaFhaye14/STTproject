using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace STTproject.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ojt");

            migrationBuilder.CreateTable(
                name: "CompanyItem",
                schema: "ojt",
                columns: table => new
                {
                    CompanyItemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    Principal = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__CompanyI__2A0E983839E2C7B0", x => x.CompanyItemId);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                schema: "ojt",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Username = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Password = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    UpdatedDate = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Users__1788CC4CD89976AC", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "ItemsUom",
                schema: "ojt",
                columns: table => new
                {
                    ItemsUomId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyItemId = table.Column<int>(type: "int", nullable: false),
                    UomName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ConversionToBase = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    IsBaseUnit = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__ItemsUom__537249573414BFF9", x => x.ItemsUomId);
                    table.ForeignKey(
                        name: "FK_ItemsUom_CompanyItemId",
                        column: x => x.CompanyItemId,
                        principalSchema: "ojt",
                        principalTable: "CompanyItem",
                        principalColumn: "CompanyItemId");
                    table.ForeignKey(
                        name: "FK_ItemsUom_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "ojt",
                        principalTable: "Users",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_ItemsUom_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "ojt",
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "SubDistributor",
                schema: "ojt",
                columns: table => new
                {
                    SubDistributorId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubdCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SubdName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    CityMunicipality = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Province = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EncoderId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    CompanySubdCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__SubDistr__954B9BCD15E8FA9F", x => x.SubDistributorId);
                    table.ForeignKey(
                        name: "FK_SubDistributor_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "ojt",
                        principalTable: "Users",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_SubDistributor_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "ojt",
                        principalTable: "Users",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_SubDistributor_User",
                        column: x => x.EncoderId,
                        principalSchema: "ojt",
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "Customer",
                schema: "ojt",
                columns: table => new
                {
                    CustomerId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    CustomerType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SubDistributorId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    UpdatedDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Customer__A4AE64D8F7607638", x => x.CustomerId);
                    table.ForeignKey(
                        name: "FK_Customer_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "ojt",
                        principalTable: "Users",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_Customer_SubDistributor",
                        column: x => x.SubDistributorId,
                        principalSchema: "ojt",
                        principalTable: "SubDistributor",
                        principalColumn: "SubDistributorId");
                    table.ForeignKey(
                        name: "FK_Customer_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "ojt",
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "SubdItem",
                schema: "ojt",
                columns: table => new
                {
                    SubdItemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubdItemCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    SubDistributorId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    CompanyItemId = table.Column<int>(type: "int", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ItemsUomId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__SubdItem__873BB656E2CB39D1", x => x.SubdItemId);
                    table.ForeignKey(
                        name: "FK_SubdItem_CompanyItem",
                        column: x => x.CompanyItemId,
                        principalSchema: "ojt",
                        principalTable: "CompanyItem",
                        principalColumn: "CompanyItemId");
                    table.ForeignKey(
                        name: "FK_SubdItem_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "ojt",
                        principalTable: "Users",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_SubdItem_ItemsUomId",
                        column: x => x.ItemsUomId,
                        principalSchema: "ojt",
                        principalTable: "ItemsUom",
                        principalColumn: "ItemsUomId");
                    table.ForeignKey(
                        name: "FK_SubdItem_SubDistributor",
                        column: x => x.SubDistributorId,
                        principalSchema: "ojt",
                        principalTable: "SubDistributor",
                        principalColumn: "SubDistributorId");
                    table.ForeignKey(
                        name: "FK_SubdItem_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "ojt",
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "CustomerBranch",
                schema: "ojt",
                columns: table => new
                {
                    CustomerBranchId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    BranchName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, defaultValue: "Main"),
                    AddressLine = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Province = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ZipCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    UpdatedDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Customer__6F555BBD71F361FA", x => x.CustomerBranchId);
                    table.ForeignKey(
                        name: "FK_CustomerBranch_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "ojt",
                        principalTable: "Users",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_CustomerBranch_Customer",
                        column: x => x.CustomerId,
                        principalSchema: "ojt",
                        principalTable: "Customer",
                        principalColumn: "CustomerId");
                    table.ForeignKey(
                        name: "FK_CustomerBranch_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "ojt",
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "SalesInvoice",
                schema: "ojt",
                columns: table => new
                {
                    SalesInvoiceId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SalesInvoiceCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SalesInvoiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    CustomerBranchId = table.Column<int>(type: "int", nullable: false),
                    SubDistributorId = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    UpdatedDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    OrderType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Invoice")
                        .Annotation("Relational:DefaultConstraintName", "DF_SalesInvoice_OrderType"),
                    OrderDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__SalesInv__BA05CD1A2B3DB990", x => x.SalesInvoiceId);
                    table.ForeignKey(
                        name: "FK_SalesInvoice_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "ojt",
                        principalTable: "Users",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_SalesInvoice_Customer",
                        column: x => x.CustomerId,
                        principalSchema: "ojt",
                        principalTable: "Customer",
                        principalColumn: "CustomerId");
                    table.ForeignKey(
                        name: "FK_SalesInvoice_CustomerBranch",
                        column: x => x.CustomerBranchId,
                        principalSchema: "ojt",
                        principalTable: "CustomerBranch",
                        principalColumn: "CustomerBranchId");
                    table.ForeignKey(
                        name: "FK_SalesInvoice_SubDistributor",
                        column: x => x.SubDistributorId,
                        principalSchema: "ojt",
                        principalTable: "SubDistributor",
                        principalColumn: "SubDistributorId");
                    table.ForeignKey(
                        name: "FK_SalesInvoice_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "ojt",
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "SalesInvoiceItem",
                schema: "ojt",
                columns: table => new
                {
                    SalesInvoiceItemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SalesInvoiceId = table.Column<int>(type: "int", nullable: false),
                    SubdItemId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    UpdatedDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__SalesInv__BA84EC64DF1C1337", x => x.SalesInvoiceItemId);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceItem_Invoice",
                        column: x => x.SalesInvoiceId,
                        principalSchema: "ojt",
                        principalTable: "SalesInvoice",
                        principalColumn: "SalesInvoiceId");
                    table.ForeignKey(
                        name: "FK_SalesInvoiceItem_SubdItem",
                        column: x => x.SubdItemId,
                        principalSchema: "ojt",
                        principalTable: "SubdItem",
                        principalColumn: "SubdItemId");
                });

            migrationBuilder.CreateIndex(
                name: "UQ__CompanyI__3ECC0FEAA338318A",
                schema: "ojt",
                table: "CompanyItem",
                column: "ItemCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customer_CreatedBy",
                schema: "ojt",
                table: "Customer",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_SubDistributorId",
                schema: "ojt",
                table: "Customer",
                column: "SubDistributorId");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_UpdatedBy",
                schema: "ojt",
                table: "Customer",
                column: "UpdatedBy");

            migrationBuilder.CreateIndex(
                name: "UQ_ojt_Customer_Code",
                schema: "ojt",
                table: "Customer",
                column: "CustomerCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBranch_CreatedBy",
                schema: "ojt",
                table: "CustomerBranch",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBranch_UpdatedBy",
                schema: "ojt",
                table: "CustomerBranch",
                column: "UpdatedBy");

            migrationBuilder.CreateIndex(
                name: "UX_CustomerBranch_Default",
                schema: "ojt",
                table: "CustomerBranch",
                column: "CustomerId",
                unique: true,
                filter: "([IsDefault]=(1))");

            migrationBuilder.CreateIndex(
                name: "IX_ItemsUom_CreatedBy",
                schema: "ojt",
                table: "ItemsUom",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ItemsUom_UpdatedBy",
                schema: "ojt",
                table: "ItemsUom",
                column: "UpdatedBy");

            migrationBuilder.CreateIndex(
                name: "UQ_ItemsUom_CompanyItem_Uom",
                schema: "ojt",
                table: "ItemsUom",
                columns: new[] { "CompanyItemId", "UomName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ItemsUom_OneBaseUnit",
                schema: "ojt",
                table: "ItemsUom",
                column: "CompanyItemId",
                unique: true,
                filter: "([IsBaseUnit]=(1))");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoice_CreatedBy",
                schema: "ojt",
                table: "SalesInvoice",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoice_CustomerBranchId",
                schema: "ojt",
                table: "SalesInvoice",
                column: "CustomerBranchId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoice_CustomerId",
                schema: "ojt",
                table: "SalesInvoice",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoice_SubDistributorId",
                schema: "ojt",
                table: "SalesInvoice",
                column: "SubDistributorId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoice_UpdatedBy",
                schema: "ojt",
                table: "SalesInvoice",
                column: "UpdatedBy");

            migrationBuilder.CreateIndex(
                name: "UQ__SalesInv__C94B6607EE7F5D76",
                schema: "ojt",
                table: "SalesInvoice",
                column: "SalesInvoiceCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceItem_SalesInvoiceId",
                schema: "ojt",
                table: "SalesInvoiceItem",
                column: "SalesInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceItem_SubdItemId",
                schema: "ojt",
                table: "SalesInvoiceItem",
                column: "SubdItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SubDistributor_CreatedBy",
                schema: "ojt",
                table: "SubDistributor",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_SubDistributor_EncoderId",
                schema: "ojt",
                table: "SubDistributor",
                column: "EncoderId");

            migrationBuilder.CreateIndex(
                name: "IX_SubDistributor_UpdatedBy",
                schema: "ojt",
                table: "SubDistributor",
                column: "UpdatedBy");

            migrationBuilder.CreateIndex(
                name: "UQ__SubDistr__67B828E0F70364CF",
                schema: "ojt",
                table: "SubDistributor",
                column: "SubdCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_SubDistributor_CompanySubdCode",
                schema: "ojt",
                table: "SubDistributor",
                column: "CompanySubdCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubdItem_CompanyItemId",
                schema: "ojt",
                table: "SubdItem",
                column: "CompanyItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SubdItem_CreatedBy",
                schema: "ojt",
                table: "SubdItem",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_SubdItem_ItemsUomId",
                schema: "ojt",
                table: "SubdItem",
                column: "ItemsUomId");

            migrationBuilder.CreateIndex(
                name: "IX_SubdItem_UpdatedBy",
                schema: "ojt",
                table: "SubdItem",
                column: "UpdatedBy");

            migrationBuilder.CreateIndex(
                name: "UQ_SubdItem_Code_Per_Subd",
                schema: "ojt",
                table: "SubdItem",
                columns: new[] { "SubDistributorId", "SubdItemCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ__Users__536C85E421704CE2",
                schema: "ojt",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalesInvoiceItem",
                schema: "ojt");

            migrationBuilder.DropTable(
                name: "SalesInvoice",
                schema: "ojt");

            migrationBuilder.DropTable(
                name: "SubdItem",
                schema: "ojt");

            migrationBuilder.DropTable(
                name: "CustomerBranch",
                schema: "ojt");

            migrationBuilder.DropTable(
                name: "ItemsUom",
                schema: "ojt");

            migrationBuilder.DropTable(
                name: "Customer",
                schema: "ojt");

            migrationBuilder.DropTable(
                name: "CompanyItem",
                schema: "ojt");

            migrationBuilder.DropTable(
                name: "SubDistributor",
                schema: "ojt");

            migrationBuilder.DropTable(
                name: "Users",
                schema: "ojt");
        }
    }
}
