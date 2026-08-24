using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinSim.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OrderBookRewrite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_AspNetUsers_UserId",
                table: "Transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Orders_OrderId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_InstrumentId",
                table: "Transactions");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "Transactions",
                newName: "SellerUserId");

            migrationBuilder.RenameColumn(
                name: "RealizedPnL",
                table: "Transactions",
                newName: "SellerRealizedPnL");

            migrationBuilder.RenameColumn(
                name: "OrderId",
                table: "Transactions",
                newName: "SellerOrderId");

            migrationBuilder.RenameIndex(
                name: "IX_Transactions_UserId",
                table: "Transactions",
                newName: "IX_Transactions_SellerUserId");

            migrationBuilder.RenameIndex(
                name: "IX_Transactions_OrderId",
                table: "Transactions",
                newName: "IX_Transactions_SellerOrderId");

            migrationBuilder.AddColumn<Guid>(
                name: "BuyerOrderId",
                table: "Transactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<decimal>(
                name: "BuyerRealizedPnL",
                table: "Transactions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BuyerUserId",
                table: "Transactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<double>(
                name: "Volume",
                table: "PriceHistory",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<decimal>(
                name: "AvgPrice",
                table: "Orders",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "FilledQuantity",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "ImmediateOrCancel",
                table: "Orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsBot",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_BuyerOrderId",
                table: "Transactions",
                column: "BuyerOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_BuyerUserId",
                table: "Transactions",
                column: "BuyerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_InstrumentId_TransactionDate",
                table: "Transactions",
                columns: new[] { "InstrumentId", "TransactionDate" });

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_AspNetUsers_BuyerUserId",
                table: "Transactions",
                column: "BuyerUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_AspNetUsers_SellerUserId",
                table: "Transactions",
                column: "SellerUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Orders_BuyerOrderId",
                table: "Transactions",
                column: "BuyerOrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Orders_SellerOrderId",
                table: "Transactions",
                column: "SellerOrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_AspNetUsers_BuyerUserId",
                table: "Transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_AspNetUsers_SellerUserId",
                table: "Transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Orders_BuyerOrderId",
                table: "Transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Orders_SellerOrderId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_BuyerOrderId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_BuyerUserId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_InstrumentId_TransactionDate",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "BuyerOrderId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "BuyerRealizedPnL",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "BuyerUserId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Volume",
                table: "PriceHistory");

            migrationBuilder.DropColumn(
                name: "AvgPrice",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FilledQuantity",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ImmediateOrCancel",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "IsBot",
                table: "AspNetUsers");

            migrationBuilder.RenameColumn(
                name: "SellerUserId",
                table: "Transactions",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "SellerRealizedPnL",
                table: "Transactions",
                newName: "RealizedPnL");

            migrationBuilder.RenameColumn(
                name: "SellerOrderId",
                table: "Transactions",
                newName: "OrderId");

            migrationBuilder.RenameIndex(
                name: "IX_Transactions_SellerUserId",
                table: "Transactions",
                newName: "IX_Transactions_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_Transactions_SellerOrderId",
                table: "Transactions",
                newName: "IX_Transactions_OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_InstrumentId",
                table: "Transactions",
                column: "InstrumentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_AspNetUsers_UserId",
                table: "Transactions",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Orders_OrderId",
                table: "Transactions",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
