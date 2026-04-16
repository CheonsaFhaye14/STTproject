using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace STTproject.Migrations
{
    /// <inheritdoc />
    public partial class FixRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceItems_SalesInvoices_SalesInvoiceId",
                table: "SalesInvoiceItems");

            migrationBuilder.DropTable(
                name: "RecentActivitys");

            migrationBuilder.DropColumn(
                name: "CustomerAddress",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "CustomerCode",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "CustomerName",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "CustomerType",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "SubdCode",
                table: "SalesInvoiceItems");

            migrationBuilder.RenameColumn(
                name: "SubdId",
                table: "SalesInvoiceItems",
                newName: "SubdItemId");

            migrationBuilder.RenameColumn(
                name: "Province",
                table: "SalesInvoiceItems",
                newName: "Quantity");

            migrationBuilder.RenameColumn(
                name: "InvoiceNumber",
                table: "SalesInvoiceItems",
                newName: "Price");

            migrationBuilder.AddColumn<int>(
                name: "SubDistributorId",
                table: "SubdItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CustomerId",
                table: "SalesInvoices",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SubDistributorId",
                table: "SalesInvoices",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<int>(
                name: "SalesInvoiceId",
                table: "SalesInvoiceItems",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SalesInvoiceId1",
                table: "SalesInvoiceItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Customer",
                columns: table => new
                {
                    CustomerId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CustomerCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CustomerAddress = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SubDistributorId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customer", x => x.CustomerId);
                    table.ForeignKey(
                        name: "FK_Customer_SubDistributors_SubDistributorId",
                        column: x => x.SubDistributorId,
                        principalTable: "SubDistributors",
                        principalColumn: "SubDistributorId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubdItems_SubDistributorId",
                table: "SubdItems",
                column: "SubDistributorId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_CustomerId",
                table: "SalesInvoices",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_SubDistributorId",
                table: "SalesInvoices",
                column: "SubDistributorId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceItems_SalesInvoiceId1",
                table: "SalesInvoiceItems",
                column: "SalesInvoiceId1");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceItems_SubdItemId",
                table: "SalesInvoiceItems",
                column: "SubdItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_SubDistributorId",
                table: "Customer",
                column: "SubDistributorId");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceItems_SalesInvoices_SalesInvoiceId",
                table: "SalesInvoiceItems",
                column: "SalesInvoiceId",
                principalTable: "SalesInvoices",
                principalColumn: "SalesInvoiceId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceItems_SalesInvoices_SalesInvoiceId1",
                table: "SalesInvoiceItems",
                column: "SalesInvoiceId1",
                principalTable: "SalesInvoices",
                principalColumn: "SalesInvoiceId");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceItems_SubdItems_SubdItemId",
                table: "SalesInvoiceItems",
                column: "SubdItemId",
                principalTable: "SubdItems",
                principalColumn: "SubdItemId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoices_Customer_CustomerId",
                table: "SalesInvoices",
                column: "CustomerId",
                principalTable: "Customer",
                principalColumn: "CustomerId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoices_SubDistributors_SubDistributorId",
                table: "SalesInvoices",
                column: "SubDistributorId",
                principalTable: "SubDistributors",
                principalColumn: "SubDistributorId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SubdItems_SubDistributors_SubDistributorId",
                table: "SubdItems",
                column: "SubDistributorId",
                principalTable: "SubDistributors",
                principalColumn: "SubDistributorId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceItems_SalesInvoices_SalesInvoiceId",
                table: "SalesInvoiceItems");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceItems_SalesInvoices_SalesInvoiceId1",
                table: "SalesInvoiceItems");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceItems_SubdItems_SubdItemId",
                table: "SalesInvoiceItems");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoices_Customer_CustomerId",
                table: "SalesInvoices");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoices_SubDistributors_SubDistributorId",
                table: "SalesInvoices");

            migrationBuilder.DropForeignKey(
                name: "FK_SubdItems_SubDistributors_SubDistributorId",
                table: "SubdItems");

            migrationBuilder.DropTable(
                name: "Customer");

            migrationBuilder.DropIndex(
                name: "IX_SubdItems_SubDistributorId",
                table: "SubdItems");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoices_CustomerId",
                table: "SalesInvoices");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoices_SubDistributorId",
                table: "SalesInvoices");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceItems_SalesInvoiceId1",
                table: "SalesInvoiceItems");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceItems_SubdItemId",
                table: "SalesInvoiceItems");

            migrationBuilder.DropColumn(
                name: "SubDistributorId",
                table: "SubdItems");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "SubDistributorId",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "SalesInvoiceId1",
                table: "SalesInvoiceItems");

            migrationBuilder.RenameColumn(
                name: "SubdItemId",
                table: "SalesInvoiceItems",
                newName: "SubdId");

            migrationBuilder.RenameColumn(
                name: "Quantity",
                table: "SalesInvoiceItems",
                newName: "Province");

            migrationBuilder.RenameColumn(
                name: "Price",
                table: "SalesInvoiceItems",
                newName: "InvoiceNumber");

            migrationBuilder.AddColumn<string>(
                name: "CustomerAddress",
                table: "SalesInvoices",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CustomerCode",
                table: "SalesInvoices",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CustomerName",
                table: "SalesInvoices",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CustomerType",
                table: "SalesInvoices",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<int>(
                name: "SalesInvoiceId",
                table: "SalesInvoiceItems",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "SubdCode",
                table: "SalesInvoiceItems",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "RecentActivitys",
                columns: table => new
                {
                    RecentActivityId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvoiceNumber = table.Column<int>(type: "int", nullable: false),
                    Province = table.Column<int>(type: "int", nullable: false),
                    SubdCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SubdId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecentActivitys", x => x.RecentActivityId);
                });

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceItems_SalesInvoices_SalesInvoiceId",
                table: "SalesInvoiceItems",
                column: "SalesInvoiceId",
                principalTable: "SalesInvoices",
                principalColumn: "SalesInvoiceId");
        }
    }
}
