using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace STTproject.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyItemPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ItemsUom_CompanyItemId",
                schema: "ojt",
                table: "ItemsUom");

            migrationBuilder.DropForeignKey(
                name: "FK_SubdItem_ItemsUomId",
                schema: "ojt",
                table: "SubdItem");

            migrationBuilder.DropIndex(
                name: "IX_SubdItem_ItemsUomId",
                schema: "ojt",
                table: "SubdItem");

            migrationBuilder.DropColumn(
                name: "ItemsUomId",
                schema: "ojt",
                table: "SubdItem");

            migrationBuilder.DropColumn(
                name: "Price",
                schema: "ojt",
                table: "SubdItem");

            migrationBuilder.RenameIndex(
                name: "UQ_SubdItem_Code_Per_Subd",
                schema: "ojt",
                table: "SubdItem",
                newName: "UQ_SubdItem_SubdId_Code");

            migrationBuilder.RenameIndex(
                name: "UQ__SubDistr__67B828E0F70364CF",
                schema: "ojt",
                table: "SubDistributor",
                newName: "UQ__SubDistr__67B828E0B0F084A6");

            migrationBuilder.RenameIndex(
                name: "UQ__SalesInv__C94B6607EE7F5D76",
                schema: "ojt",
                table: "SalesInvoice",
                newName: "UQ__SalesInv__C94B6607BF057E81");

            migrationBuilder.RenameColumn(
                name: "CompanyItemId",
                schema: "ojt",
                table: "ItemsUom",
                newName: "SubdItemId");

            migrationBuilder.RenameIndex(
                name: "UQ_ItemsUom_CompanyItem_Uom",
                schema: "ojt",
                table: "ItemsUom",
                newName: "UQ_ItemsUom_SubdItem_Uom");

            migrationBuilder.RenameIndex(
                name: "UQ__CompanyI__3ECC0FEAA338318A",
                schema: "ojt",
                table: "CompanyItem",
                newName: "UQ__CompanyI__3ECC0FEA1D5CA35D");

            migrationBuilder.AlterColumn<string>(
                name: "Role",
                schema: "ojt",
                table: "Users",
                type: "varchar(20)",
                unicode: false,
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "SubdItemCode",
                schema: "ojt",
                table: "SubdItem",
                type: "varchar(50)",
                unicode: false,
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "SubdName",
                schema: "ojt",
                table: "SubDistributor",
                type: "varchar(150)",
                unicode: false,
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(150)",
                oldMaxLength: 150);

            migrationBuilder.AlterColumn<string>(
                name: "SubdCode",
                schema: "ojt",
                table: "SubDistributor",
                type: "varchar(50)",
                unicode: false,
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "CompanySubdCode",
                schema: "ojt",
                table: "SubDistributor",
                type: "varchar(50)",
                unicode: false,
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AddColumn<int>(
                name: "ItemsUomId",
                schema: "ojt",
                table: "SalesInvoiceItem",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "SalesInvoiceCode",
                schema: "ojt",
                table: "SalesInvoice",
                type: "varchar(50)",
                unicode: false,
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "OrderType",
                schema: "ojt",
                table: "SalesInvoice",
                type: "varchar(20)",
                unicode: false,
                maxLength: 20,
                nullable: false,
                defaultValue: "Invoice",
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldDefaultValue: "Invoice")
                .Annotation("Relational:DefaultConstraintName", "DF_SalesInvoice_OrderType")
                .OldAnnotation("Relational:DefaultConstraintName", "DF_SalesInvoice_OrderType");

            migrationBuilder.AlterColumn<string>(
                name: "UomName",
                schema: "ojt",
                table: "ItemsUom",
                type: "varchar(50)",
                unicode: false,
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                schema: "ojt",
                table: "ItemsUom",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<int>(
                name: "ZipCode",
                schema: "ojt",
                table: "CustomerBranch",
                type: "int",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "BranchName",
                schema: "ojt",
                table: "CustomerBranch",
                type: "varchar(100)",
                unicode: false,
                maxLength: 100,
                nullable: false,
                defaultValue: "Main",
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldDefaultValue: "Main");

            migrationBuilder.AlterColumn<string>(
                name: "CustomerType",
                schema: "ojt",
                table: "Customer",
                type: "varchar(50)",
                unicode: false,
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "CustomerName",
                schema: "ojt",
                table: "Customer",
                type: "varchar(150)",
                unicode: false,
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(150)",
                oldMaxLength: 150);

            migrationBuilder.AlterColumn<string>(
                name: "CustomerCode",
                schema: "ojt",
                table: "Customer",
                type: "varchar(50)",
                unicode: false,
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "Principal",
                schema: "ojt",
                table: "CompanyItem",
                type: "varchar(150)",
                unicode: false,
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(150)",
                oldMaxLength: 150);

            migrationBuilder.AlterColumn<string>(
                name: "ItemCode",
                schema: "ojt",
                table: "CompanyItem",
                type: "varchar(50)",
                unicode: false,
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.CreateIndex(
                name: "UQ_SubdItem_Subd_CompanyItem",
                schema: "ojt",
                table: "SubdItem",
                columns: new[] { "SubDistributorId", "CompanyItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceItem_ItemsUomId",
                schema: "ojt",
                table: "SalesInvoiceItem",
                column: "ItemsUomId");

            migrationBuilder.AddForeignKey(
                name: "FK_ItemsUom_SubdItemId",
                schema: "ojt",
                table: "ItemsUom",
                column: "SubdItemId",
                principalSchema: "ojt",
                principalTable: "SubdItem",
                principalColumn: "SubdItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceItem_ItemsUom",
                schema: "ojt",
                table: "SalesInvoiceItem",
                column: "ItemsUomId",
                principalSchema: "ojt",
                principalTable: "ItemsUom",
                principalColumn: "ItemsUomId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ItemsUom_SubdItemId",
                schema: "ojt",
                table: "ItemsUom");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceItem_ItemsUom",
                schema: "ojt",
                table: "SalesInvoiceItem");

            migrationBuilder.DropIndex(
                name: "UQ_SubdItem_Subd_CompanyItem",
                schema: "ojt",
                table: "SubdItem");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceItem_ItemsUomId",
                schema: "ojt",
                table: "SalesInvoiceItem");

            migrationBuilder.DropColumn(
                name: "ItemsUomId",
                schema: "ojt",
                table: "SalesInvoiceItem");

            migrationBuilder.DropColumn(
                name: "Price",
                schema: "ojt",
                table: "ItemsUom");

            migrationBuilder.RenameIndex(
                name: "UQ_SubdItem_SubdId_Code",
                schema: "ojt",
                table: "SubdItem",
                newName: "UQ_SubdItem_Code_Per_Subd");

            migrationBuilder.RenameIndex(
                name: "UQ__SubDistr__67B828E0B0F084A6",
                schema: "ojt",
                table: "SubDistributor",
                newName: "UQ__SubDistr__67B828E0F70364CF");

            migrationBuilder.RenameIndex(
                name: "UQ__SalesInv__C94B6607BF057E81",
                schema: "ojt",
                table: "SalesInvoice",
                newName: "UQ__SalesInv__C94B6607EE7F5D76");

            migrationBuilder.RenameColumn(
                name: "SubdItemId",
                schema: "ojt",
                table: "ItemsUom",
                newName: "CompanyItemId");

            migrationBuilder.RenameIndex(
                name: "UQ_ItemsUom_SubdItem_Uom",
                schema: "ojt",
                table: "ItemsUom",
                newName: "UQ_ItemsUom_CompanyItem_Uom");

            migrationBuilder.RenameIndex(
                name: "UQ__CompanyI__3ECC0FEA1D5CA35D",
                schema: "ojt",
                table: "CompanyItem",
                newName: "UQ__CompanyI__3ECC0FEAA338318A");

            migrationBuilder.AlterColumn<string>(
                name: "Role",
                schema: "ojt",
                table: "Users",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(20)",
                oldUnicode: false,
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "SubdItemCode",
                schema: "ojt",
                table: "SubdItem",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(50)",
                oldUnicode: false,
                oldMaxLength: 50);

            migrationBuilder.AddColumn<int>(
                name: "ItemsUomId",
                schema: "ojt",
                table: "SubdItem",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                schema: "ojt",
                table: "SubdItem",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<string>(
                name: "SubdName",
                schema: "ojt",
                table: "SubDistributor",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(150)",
                oldUnicode: false,
                oldMaxLength: 150);

            migrationBuilder.AlterColumn<string>(
                name: "SubdCode",
                schema: "ojt",
                table: "SubDistributor",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(50)",
                oldUnicode: false,
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "CompanySubdCode",
                schema: "ojt",
                table: "SubDistributor",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(50)",
                oldUnicode: false,
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "SalesInvoiceCode",
                schema: "ojt",
                table: "SalesInvoice",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(50)",
                oldUnicode: false,
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "OrderType",
                schema: "ojt",
                table: "SalesInvoice",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Invoice",
                oldClrType: typeof(string),
                oldType: "varchar(20)",
                oldUnicode: false,
                oldMaxLength: 20,
                oldDefaultValue: "Invoice")
                .Annotation("Relational:DefaultConstraintName", "DF_SalesInvoice_OrderType")
                .OldAnnotation("Relational:DefaultConstraintName", "DF_SalesInvoice_OrderType");

            migrationBuilder.AlterColumn<string>(
                name: "UomName",
                schema: "ojt",
                table: "ItemsUom",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(50)",
                oldUnicode: false,
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "ZipCode",
                schema: "ojt",
                table: "CustomerBranch",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<string>(
                name: "BranchName",
                schema: "ojt",
                table: "CustomerBranch",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "Main",
                oldClrType: typeof(string),
                oldType: "varchar(100)",
                oldUnicode: false,
                oldMaxLength: 100,
                oldDefaultValue: "Main");

            migrationBuilder.AlterColumn<string>(
                name: "CustomerType",
                schema: "ojt",
                table: "Customer",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(50)",
                oldUnicode: false,
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "CustomerName",
                schema: "ojt",
                table: "Customer",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(150)",
                oldUnicode: false,
                oldMaxLength: 150);

            migrationBuilder.AlterColumn<string>(
                name: "CustomerCode",
                schema: "ojt",
                table: "Customer",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(50)",
                oldUnicode: false,
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "Principal",
                schema: "ojt",
                table: "CompanyItem",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(150)",
                oldUnicode: false,
                oldMaxLength: 150);

            migrationBuilder.AlterColumn<string>(
                name: "ItemCode",
                schema: "ojt",
                table: "CompanyItem",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(50)",
                oldUnicode: false,
                oldMaxLength: 50);

            migrationBuilder.CreateIndex(
                name: "IX_SubdItem_ItemsUomId",
                schema: "ojt",
                table: "SubdItem",
                column: "ItemsUomId");

            migrationBuilder.AddForeignKey(
                name: "FK_ItemsUom_CompanyItemId",
                schema: "ojt",
                table: "ItemsUom",
                column: "CompanyItemId",
                principalSchema: "ojt",
                principalTable: "CompanyItem",
                principalColumn: "CompanyItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_SubdItem_ItemsUomId",
                schema: "ojt",
                table: "SubdItem",
                column: "ItemsUomId",
                principalSchema: "ojt",
                principalTable: "ItemsUom",
                principalColumn: "ItemsUomId");
        }
    }
}
