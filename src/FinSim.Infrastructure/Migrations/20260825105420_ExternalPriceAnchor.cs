using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinSim.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExternalPriceAnchor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "LastRealPrice",
                table: "Instruments",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastRealPriceAt",
                table: "Instruments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RealSymbol",
                table: "Instruments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastRealPrice",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "LastRealPriceAt",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "RealSymbol",
                table: "Instruments");
        }
    }
}
