using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinSim.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPortfolioItemUserInstrumentIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PortfolioItems_UserId",
                table: "PortfolioItems");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioItems_UserId_InstrumentId",
                table: "PortfolioItems",
                columns: new[] { "UserId", "InstrumentId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PortfolioItems_UserId_InstrumentId",
                table: "PortfolioItems");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioItems_UserId",
                table: "PortfolioItems",
                column: "UserId");
        }
    }
}
