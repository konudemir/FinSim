using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinSim.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PortfolioItemConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // xmin is a Postgres system column present on every table, so mapping it as
            // a concurrency token is a model-side change with no DDL. The AddColumn the
            // scaffolder generated for it has been removed; it would fail with 42701.
            migrationBuilder.DropIndex(
                name: "IX_PortfolioItems_UserId_InstrumentId",
                table: "PortfolioItems");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioItems_UserId_InstrumentId",
                table: "PortfolioItems",
                columns: new[] { "UserId", "InstrumentId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PortfolioItems_UserId_InstrumentId",
                table: "PortfolioItems");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioItems_UserId_InstrumentId",
                table: "PortfolioItems",
                columns: new[] { "UserId", "InstrumentId" });
        }
    }
}