using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinSim.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InstrumentSearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Instruments_CurrentPrice_Id",
                table: "Instruments",
                columns: new[] { "CurrentPrice", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Instruments_Symbol",
                table: "Instruments",
                column: "Symbol",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Instruments_CurrentPrice_Id",
                table: "Instruments");

            migrationBuilder.DropIndex(
                name: "IX_Instruments_Symbol",
                table: "Instruments");
        }
    }
}
