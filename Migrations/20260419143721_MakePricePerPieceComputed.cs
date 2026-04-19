using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace STTproject.Migrations
{
    /// <inheritdoc />
    public partial class MakePricePerPieceComputed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop the default constraint that's blocking the column drop
            migrationBuilder.Sql("ALTER TABLE [SubdItems] DROP CONSTRAINT [DF__SubdItems__Price__01142BA1];");

            // Drop the PricePerPiece column
            migrationBuilder.Sql("ALTER TABLE [SubdItems] DROP COLUMN [PricePerPiece];");

            // Add computed column
            migrationBuilder.Sql("ALTER TABLE [SubdItems] ADD [PricePerPiece] AS (CASE WHEN [QuantityPerPiece] > 0 THEN [Price] / [QuantityPerPiece] ELSE 0 END) PERSISTED;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE [SubdItems] DROP COLUMN [PricePerPiece];");
            migrationBuilder.Sql("ALTER TABLE [SubdItems] ADD [PricePerPiece] int NOT NULL DEFAULT 0;");
        }
    }
}
