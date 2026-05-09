using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace STTproject.Migrations
{
    /// <inheritdoc />
    public partial class RedefineCascadeDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ItemsUom_SubdItemId",
                schema: "ojt",
                table: "ItemsUom");

            migrationBuilder.DropIndex(
                name: "UQ_ojt_Customer_Code",
                schema: "ojt",
                table: "Customer");

            migrationBuilder.RenameIndex(
                name: "UQ__SubDistr__67B828E0B0F084A6",
                schema: "ojt",
                table: "SubDistributor",
                newName: "UQ_SubDistributor_SubdCode");

            migrationBuilder.AddColumn<string>(
                name: "Category",
                schema: "ojt",
                table: "CompanyItem",
                type: "varchar(100)",
                unicode: false,
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "EffectivityDate",
                schema: "ojt",
                table: "CompanyItem",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PriceIncreasePercent",
                schema: "ojt",
                table: "CompanyItem",
                type: "decimal(5,2)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ItemsUomPriceHistory",
                schema: "ojt",
                columns: table => new
                {
                    ItemsUomPriceHistoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemsUomId = table.Column<int>(type: "int", nullable: false),
                    CompanyItemId = table.Column<int>(type: "int", nullable: false),
                    OldPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NewPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PriceIncreasePercent = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    EffectivityDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    AppliedDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    CreatedBy = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__ItemsUom__4D74DA340C04A45B", x => x.ItemsUomPriceHistoryId);
                    table.ForeignKey(
                        name: "FK_ItemsUomPriceHistory_CompanyItem",
                        column: x => x.CompanyItemId,
                        principalSchema: "ojt",
                        principalTable: "CompanyItem",
                        principalColumn: "CompanyItemId");
                    table.ForeignKey(
                        name: "FK_ItemsUomPriceHistory_ItemsUom",
                        column: x => x.ItemsUomId,
                        principalSchema: "ojt",
                        principalTable: "ItemsUom",
                        principalColumn: "ItemsUomId");
                });

            migrationBuilder.CreateIndex(
                name: "UQ_CustomerBranch_BranchName_PerCustomer",
                schema: "ojt",
                table: "CustomerBranch",
                columns: new[] { "CustomerId", "BranchName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_ojt_Customer_Code",
                schema: "ojt",
                table: "Customer",
                columns: new[] { "CustomerCode", "SubDistributorId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_ojt_Customer_Name",
                schema: "ojt",
                table: "Customer",
                columns: new[] { "CustomerName", "SubDistributorId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemsUomPriceHistory_CompanyItemId",
                schema: "ojt",
                table: "ItemsUomPriceHistory",
                column: "CompanyItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemsUomPriceHistory_ItemsUomId",
                schema: "ojt",
                table: "ItemsUomPriceHistory",
                column: "ItemsUomId");

            migrationBuilder.AddForeignKey(
                name: "FK_ItemsUom_SubdItemId",
                schema: "ojt",
                table: "ItemsUom",
                column: "SubdItemId",
                principalSchema: "ojt",
                principalTable: "SubdItem",
                principalColumn: "SubdItemId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ItemsUom_SubdItemId",
                schema: "ojt",
                table: "ItemsUom");

            migrationBuilder.DropTable(
                name: "ItemsUomPriceHistory",
                schema: "ojt");

            migrationBuilder.DropIndex(
                name: "UQ_CustomerBranch_BranchName_PerCustomer",
                schema: "ojt",
                table: "CustomerBranch");

            migrationBuilder.DropIndex(
                name: "UQ_ojt_Customer_Code",
                schema: "ojt",
                table: "Customer");

            migrationBuilder.DropIndex(
                name: "UQ_ojt_Customer_Name",
                schema: "ojt",
                table: "Customer");

            migrationBuilder.DropColumn(
                name: "Category",
                schema: "ojt",
                table: "CompanyItem");

            migrationBuilder.DropColumn(
                name: "EffectivityDate",
                schema: "ojt",
                table: "CompanyItem");

            migrationBuilder.DropColumn(
                name: "PriceIncreasePercent",
                schema: "ojt",
                table: "CompanyItem");

            migrationBuilder.RenameIndex(
                name: "UQ_SubDistributor_SubdCode",
                schema: "ojt",
                table: "SubDistributor",
                newName: "UQ__SubDistr__67B828E0B0F084A6");

            migrationBuilder.CreateIndex(
                name: "UQ_ojt_Customer_Code",
                schema: "ojt",
                table: "Customer",
                column: "CustomerCode",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ItemsUom_SubdItemId",
                schema: "ojt",
                table: "ItemsUom",
                column: "SubdItemId",
                principalSchema: "ojt",
                principalTable: "SubdItem",
                principalColumn: "SubdItemId");
        }
    }
}
