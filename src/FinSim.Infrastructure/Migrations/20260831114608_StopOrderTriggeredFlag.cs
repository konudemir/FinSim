using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinSim.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StopOrderTriggeredFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Triggered",
                table: "Orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);
                migrationBuilder.Sql("""
                    UPDATE "Orders"
                    SET "Triggered" = TRUE, "ImmediateOrCancel" = FALSE
                    WHERE "StopPrice" IS NOT NULL
                    AND "ImmediateOrCancel" = TRUE
                    AND "Status" IN ('Pending', 'PartiallyFilled');
                    """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Triggered",
                table: "Orders");
        }
    }
}
