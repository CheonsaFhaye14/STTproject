using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace STTproject.Migrations
{
    /// <inheritdoc />
    public partial class SalesInvoiceCodeOrderTypeUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UQ__SalesInv__C94B6607BF057E81",
                schema: "ojt",
                table: "SalesInvoice");

            migrationBuilder.CreateIndex(
                name: "UQ__SalesInv__Code_OrderType",
                schema: "ojt",
                table: "SalesInvoice",
                columns: new[] { "SalesInvoiceCode", "OrderType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UQ__SalesInv__Code_OrderType",
                schema: "ojt",
                table: "SalesInvoice");

            migrationBuilder.CreateIndex(
                name: "UQ__SalesInv__C94B6607BF057E81",
                schema: "ojt",
                table: "SalesInvoice",
                column: "SalesInvoiceCode",
                unique: true);
        }
    }
}
