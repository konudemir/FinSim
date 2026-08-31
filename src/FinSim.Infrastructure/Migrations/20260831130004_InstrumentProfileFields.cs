using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinSim.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InstrumentProfileFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Instruments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Instruments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Employees",
                table: "Instruments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Industry",
                table: "Instruments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sector",
                table: "Instruments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SharesOutstanding",
                table: "Instruments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Website",
                table: "Instruments",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "City",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "Employees",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "Industry",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "Sector",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "SharesOutstanding",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "Website",
                table: "Instruments");
        }
    }
}
