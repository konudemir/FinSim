using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinSim.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InstrumentTrgmIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"CREATE EXTENSION IF NOT EXISTS pg_trgm;");
            migrationBuilder.Sql(@"
                CREATE INDEX ""IX_Instruments_Symbol_Name_Trgm""
                ON ""Instruments"" USING gin (""Symbol"" gin_trgm_ops, ""Name"" gin_trgm_ops);");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Instruments_Symbol_Name_Trgm"";");
        }
    }
}
