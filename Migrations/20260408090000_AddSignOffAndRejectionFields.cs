using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OutsourceRequestApp.Migrations
{
    /// <inheritdoc />
    public partial class AddSignOffAndRejectionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Stage 1 — Work Preparation sign-off (John Fisher)
            migrationBuilder.AddColumn<string>(
                name: "JFSignedBy",
                table: "OutsourceRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "JFSignedDate",
                table: "OutsourceRequests",
                type: "datetime2",
                nullable: true);

            // Stage 2 — Production sign-off (Lukasz Jaworski)
            migrationBuilder.AddColumn<string>(
                name: "LJSignedBy",
                table: "OutsourceRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LJSignedDate",
                table: "OutsourceRequests",
                type: "datetime2",
                nullable: true);

            // Stage 4 — Sourcing sign-off (Simon Graham)
            migrationBuilder.AddColumn<string>(
                name: "SGSignedBy",
                table: "OutsourceRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SGSignedDate",
                table: "OutsourceRequests",
                type: "datetime2",
                nullable: true);

            // Generic rejection record — populated at whichever stage rejects
            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "OutsourceRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectedBy",
                table: "OutsourceRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RejectedAt",
                table: "OutsourceRequests",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "JFSignedBy",      table: "OutsourceRequests");
            migrationBuilder.DropColumn(name: "JFSignedDate",    table: "OutsourceRequests");
            migrationBuilder.DropColumn(name: "LJSignedBy",      table: "OutsourceRequests");
            migrationBuilder.DropColumn(name: "LJSignedDate",    table: "OutsourceRequests");
            migrationBuilder.DropColumn(name: "SGSignedBy",      table: "OutsourceRequests");
            migrationBuilder.DropColumn(name: "SGSignedDate",    table: "OutsourceRequests");
            migrationBuilder.DropColumn(name: "RejectionReason", table: "OutsourceRequests");
            migrationBuilder.DropColumn(name: "RejectedBy",      table: "OutsourceRequests");
            migrationBuilder.DropColumn(name: "RejectedAt",      table: "OutsourceRequests");
        }
    }
}
