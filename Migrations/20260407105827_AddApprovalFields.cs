using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OutsourceRequestApp.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CostComments",
                table: "OutsourceRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CostInhousePerMonth",
                table: "OutsourceRequests",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CostOutsourcePerMonth",
                table: "OutsourceRequests",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinanceComments",
                table: "OutsourceRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FinanceReviewedAt",
                table: "OutsourceRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinanceReviewedBy",
                table: "OutsourceRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastReminderSentAt",
                table: "OutsourceRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MdComments",
                table: "OutsourceRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MdReviewedAt",
                table: "OutsourceRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MdReviewedBy",
                table: "OutsourceRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PpapRequired",
                table: "OutsourceRequests",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScComments",
                table: "OutsourceRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScReviewedAt",
                table: "OutsourceRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScReviewedBy",
                table: "OutsourceRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ApproverRoles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RoleDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApproverRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SettingKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SettingValue = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApproverRoles");

            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropColumn(
                name: "CostComments",
                table: "OutsourceRequests");

            migrationBuilder.DropColumn(
                name: "CostInhousePerMonth",
                table: "OutsourceRequests");

            migrationBuilder.DropColumn(
                name: "CostOutsourcePerMonth",
                table: "OutsourceRequests");

            migrationBuilder.DropColumn(
                name: "FinanceComments",
                table: "OutsourceRequests");

            migrationBuilder.DropColumn(
                name: "FinanceReviewedAt",
                table: "OutsourceRequests");

            migrationBuilder.DropColumn(
                name: "FinanceReviewedBy",
                table: "OutsourceRequests");

            migrationBuilder.DropColumn(
                name: "LastReminderSentAt",
                table: "OutsourceRequests");

            migrationBuilder.DropColumn(
                name: "MdComments",
                table: "OutsourceRequests");

            migrationBuilder.DropColumn(
                name: "MdReviewedAt",
                table: "OutsourceRequests");

            migrationBuilder.DropColumn(
                name: "MdReviewedBy",
                table: "OutsourceRequests");

            migrationBuilder.DropColumn(
                name: "PpapRequired",
                table: "OutsourceRequests");

            migrationBuilder.DropColumn(
                name: "ScComments",
                table: "OutsourceRequests");

            migrationBuilder.DropColumn(
                name: "ScReviewedAt",
                table: "OutsourceRequests");

            migrationBuilder.DropColumn(
                name: "ScReviewedBy",
                table: "OutsourceRequests");
        }
    }
}
