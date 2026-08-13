using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FinSim.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MoreInstruments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Orders",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.UpdateData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: new Guid("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                columns: new[] { "BasePrice", "IsActive" },
                values: new object[] { 4m, true });

            migrationBuilder.InsertData(
                table: "Instruments",
                columns: new[] { "Id", "BasePrice", "CurrentPrice", "IsActive", "Name", "Symbol" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-0000-0000-000000000001"), 14m, 14m, true, "İş Bankası (C)", "ISCTR" },
                    { new Guid("10000000-0000-0000-0000-000000000002"), 260m, 260m, true, "Tofaş Otomobil Fabrikası", "TOASO" },
                    { new Guid("10000000-0000-0000-0000-000000000003"), 140m, 140m, true, "Arçelik", "ARCLK" },
                    { new Guid("10000000-0000-0000-0000-000000000004"), 48m, 48m, true, "Türk Telekom", "TTKOM" },
                    { new Guid("10000000-0000-0000-0000-000000000005"), 27m, 27m, true, "VakıfBank", "VAKBN" },
                    { new Guid("10000000-0000-0000-0000-000000000006"), 21m, 21m, true, "Petkim", "PETKM" },
                    { new Guid("10000000-0000-0000-0000-000000000007"), 58m, 58m, true, "Enka İnşaat", "ENKAI" },
                    { new Guid("10000000-0000-0000-0000-000000000008"), 520m, 520m, true, "Migros Ticaret", "MGROS" },
                    { new Guid("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), 220m, 220m, true, "Pegasus Hava Yolları", "PGSUS" },
                    { new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"), 95m, 95m, true, "Turkcell", "TCELL" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000004"));

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000005"));

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000006"));

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000007"));

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000008"));

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: new Guid("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"));

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"));

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "Orders");

            migrationBuilder.UpdateData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: new Guid("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                columns: new[] { "BasePrice", "IsActive" },
                values: new object[] { 18m, false });
        }
    }
}
