using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinSim.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TransactionUserDateIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Transactions_BuyerUserId_TransactionDate_Id",
                table: "Transactions",
                columns: new[] { "BuyerUserId", "TransactionDate", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_SellerUserId_TransactionDate_Id",
                table: "Transactions",
                columns: new[] { "SellerUserId", "TransactionDate", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_BuyerUserId_TransactionDate_Id",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_SellerUserId_TransactionDate_Id",
                table: "Transactions");
        }
    }
}
