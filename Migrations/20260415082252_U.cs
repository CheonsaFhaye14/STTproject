using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace STTproject.Migrations
{
    /// <inheritdoc />
    public partial class U : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "InvoiceNumber",
                table: "SalesInvoiceItems");

            migrationBuilder.DropColumn(
                name: "Province",
                table: "SalesInvoiceItems");

            migrationBuilder.DropColumn(
                name: "SubdCode",
                table: "SalesInvoiceItems");

            migrationBuilder.DropColumn(
                name: "InvoiceNumber",
                table: "RecentActivitys");

            migrationBuilder.RenameColumn(
                name: "SubdId",
                table: "SalesInvoiceItems",
                newName: "SubdItemId");

            migrationBuilder.RenameColumn(
                name: "SubdId",
                table: "RecentActivitys",
                newName: "SubDistributorId");

            migrationBuilder.RenameColumn(
                name: "SubdCode",
                table: "RecentActivitys",
                newName: "BatchId");

            migrationBuilder.RenameColumn(
                name: "Province",
                table: "RecentActivitys",
                newName: "SalesInvoiceId");

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

            migrationBuilder.CreateTable(
                name: "Customer",
                columns: table => new
                {
                    CustomerId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CustomerCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CustomerAddress = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customer", x => x.CustomerId);
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
                name: "IX_SalesInvoiceItems_SubdItemId",
                table: "SalesInvoiceItems",
                column: "SubdItemId");

            migrationBuilder.CreateIndex(
                name: "IX_RecentActivitys_SalesInvoiceId",
                table: "RecentActivitys",
                column: "SalesInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_RecentActivitys_SubDistributorId",
                table: "RecentActivitys",
                column: "SubDistributorId");

            migrationBuilder.AddForeignKey(
                name: "FK_RecentActivitys_SalesInvoices_SalesInvoiceId",
                table: "RecentActivitys",
                column: "SalesInvoiceId",
                principalTable: "SalesInvoices",
                principalColumn: "SalesInvoiceId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RecentActivitys_SubDistributors_SubDistributorId",
                table: "RecentActivitys",
                column: "SubDistributorId",
                principalTable: "SubDistributors",
                principalColumn: "SubDistributorId",
                onDelete: ReferentialAction.Cascade);

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
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoices_SubDistributors_SubDistributorId",
                table: "SalesInvoices",
                column: "SubDistributorId",
                principalTable: "SubDistributors",
                principalColumn: "SubDistributorId",
                onDelete: ReferentialAction.Cascade);

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
                name: "FK_RecentActivitys_SalesInvoices_SalesInvoiceId",
                table: "RecentActivitys");

            migrationBuilder.DropForeignKey(
                name: "FK_RecentActivitys_SubDistributors_SubDistributorId",
                table: "RecentActivitys");

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
                name: "IX_SalesInvoiceItems_SubdItemId",
                table: "SalesInvoiceItems");

            migrationBuilder.DropIndex(
                name: "IX_RecentActivitys_SalesInvoiceId",
                table: "RecentActivitys");

            migrationBuilder.DropIndex(
                name: "IX_RecentActivitys_SubDistributorId",
                table: "RecentActivitys");

            migrationBuilder.DropColumn(
                name: "SubDistributorId",
                table: "SubdItems");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "SubDistributorId",
                table: "SalesInvoices");

            migrationBuilder.RenameColumn(
                name: "SubdItemId",
                table: "SalesInvoiceItems",
                newName: "SubdId");

            migrationBuilder.RenameColumn(
                name: "SubDistributorId",
                table: "RecentActivitys",
                newName: "SubdId");

            migrationBuilder.RenameColumn(
                name: "SalesInvoiceId",
                table: "RecentActivitys",
                newName: "Province");

            migrationBuilder.RenameColumn(
                name: "BatchId",
                table: "RecentActivitys",
                newName: "SubdCode");

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

            migrationBuilder.AddColumn<int>(
                name: "InvoiceNumber",
                table: "SalesInvoiceItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Province",
                table: "SalesInvoiceItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SubdCode",
                table: "SalesInvoiceItems",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "InvoiceNumber",
                table: "RecentActivitys",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
