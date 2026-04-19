using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace STTproject.Migrations
{
    /// <inheritdoc />
    public partial class AddComputedPricePerPieceColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop the default constraint on PricePerPiece
            migrationBuilder.Sql("DECLARE @ConstraintName NVARCHAR(MAX); SELECT @ConstraintName = CONSTRAINT_NAME FROM INFORMATION_SCHEMA.CONSTRAINT_COLUMN_USAGE WHERE TABLE_NAME = 'SubdItems' AND COLUMN_NAME = 'PricePerPiece' AND CONSTRAINT_TYPE = 'DEFAULT'; IF @ConstraintName IS NOT NULL EXEC('ALTER TABLE [SubdItems] DROP CONSTRAINT [' + @ConstraintName + ']');");

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
