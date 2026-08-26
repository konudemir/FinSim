using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinSim.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackfillMarketOrderType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Historically every order was stored as OrderType.Limit, even those placed
            // via PlaceMarketOrderAsync (an IOC limit at a synthetic collar price).
            // A stop order also gets ImmediateOrCancel = true once triggered, so it must
            // be excluded here (StopPrice IS NOT NULL) or a plain stop-loss would be
            // misreported as a market order it never was.
            migrationBuilder.Sql(
                """
                UPDATE "Orders"
                SET "OrderType" = 'Market'
                WHERE "OrderType" = 'Limit'
                  AND "ImmediateOrCancel" = TRUE
                  AND "StopPrice" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "Orders"
                SET "OrderType" = 'Limit'
                WHERE "OrderType" = 'Market'
                  AND "ImmediateOrCancel" = TRUE
                  AND "StopPrice" IS NULL;
                """);
        }
    }
}
