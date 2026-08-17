using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FinSim.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveInstrumentSeedData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"));

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333333"));

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: new Guid("44444444-4444-4444-4444-444444444444"));

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: new Guid("55555555-5555-5555-5555-555555555555"));

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: new Guid("66666666-6666-6666-6666-666666666666"));

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: new Guid("77777777-7777-7777-7777-777777777777"));

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: new Guid("88888888-8888-8888-8888-888888888888"));

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: new Guid("99999999-9999-9999-9999-999999999999"));

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc"));

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: new Guid("dddddddd-dddd-dddd-dddd-dddddddddddd"));

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: new Guid("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"));

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
                    { new Guid("11111111-1111-1111-1111-111111111111"), 100m, 100m, true, "Türk Hava Yolları", "THYAO" },
                    { new Guid("22222222-2222-2222-2222-222222222222"), 40m, 40m, true, "Aselsan", "ASELS" },
                    { new Guid("33333333-3333-3333-3333-333333333333"), 110m, 110m, true, "Garanti BBVA", "GARAN" },
                    { new Guid("44444444-4444-4444-4444-444444444444"), 155m, 155m, true, "Tüpraş", "TUPRS" },
                    { new Guid("55555555-5555-5555-5555-555555555555"), 45m, 45m, true, "Akbank", "AKBNK" },
                    { new Guid("66666666-6666-6666-6666-666666666666"), 50m, 50m, true, "Erdemir Ereğli Demir Çelik", "EREGL" },
                    { new Guid("77777777-7777-7777-7777-777777777777"), 380m, 380m, true, "BİM Birleşik Mağazalar", "BIMAS" },
                    { new Guid("88888888-8888-8888-8888-888888888888"), 55m, 55m, true, "Şişecam", "SISE" },
                    { new Guid("99999999-9999-9999-9999-999999999999"), 190m, 190m, true, "Koç Holding", "KCHOL" },
                    { new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), 95m, 95m, true, "Sabancı Holding", "SAHOL" },
                    { new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), 1100m, 1100m, true, "Ford Otosan", "FROTO" },
                    { new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc"), 25m, 25m, true, "Yapı Kredi Bankası", "YKBNK" },
                    { new Guid("dddddddd-dddd-dddd-dddd-dddddddddddd"), 4m, 4m, true, "Hektaş", "HEKTS" },
                    { new Guid("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), 220m, 220m, true, "Pegasus Hava Yolları", "PGSUS" },
                    { new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"), 95m, 95m, true, "Turkcell", "TCELL" }
                });
        }
    }
}
