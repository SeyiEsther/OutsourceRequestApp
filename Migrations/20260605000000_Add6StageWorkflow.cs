using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OutsourceRequestApp.Migrations
{
    /// <inheritdoc />
    public partial class Add6StageWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ----------------------------------------------------------------
            // OutsourceRequests — new columns
            // ----------------------------------------------------------------

            migrationBuilder.AddColumn<string>(
                name: "RequestNumber",
                table: "OutsourceRequests",
                type: "nvarchar(50)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PPAPNumber",
                table: "OutsourceRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConcessionNumber",
                table: "OutsourceRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SpecialPackingRequired",
                table: "OutsourceRequests",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpecialPackingDetails",
                table: "OutsourceRequests",
                type: "nvarchar(max)",
                nullable: true);

            // Step 2 — John Fisher
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

            migrationBuilder.AddColumn<string>(
                name: "JFComments",
                table: "OutsourceRequests",
                type: "nvarchar(max)",
                nullable: true);

            // Step 3 — Lukasz Jaworski
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

            migrationBuilder.AddColumn<string>(
                name: "LJComments",
                table: "OutsourceRequests",
                type: "nvarchar(max)",
                nullable: true);

            // Step 4 — Cost Impact
            migrationBuilder.AddColumn<string>(
                name: "CostEnteredBy",
                table: "OutsourceRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CostEnteredAt",
                table: "OutsourceRequests",
                type: "datetime2",
                nullable: true);

            // Step 5 — Simon Graham
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

            migrationBuilder.AddColumn<string>(
                name: "SGComments",
                table: "OutsourceRequests",
                type: "nvarchar(max)",
                nullable: true);

            // Step 6 — Managing Director
            migrationBuilder.AddColumn<string>(
                name: "MDSignedBy",
                table: "OutsourceRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MDSignedDate",
                table: "OutsourceRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MDComments",
                table: "OutsourceRequests",
                type: "nvarchar(max)",
                nullable: true);

            // Rejection
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

            // Legacy bit for old PPAP field (rename PpapRequired → PpapRequiredLegacy, keep data)
            migrationBuilder.AddColumn<bool>(
                name: "PpapRequiredLegacy",
                table: "OutsourceRequests",
                type: "bit",
                nullable: true);

            // Migrate PpapRequired data to PpapRequiredLegacy
            migrationBuilder.Sql("UPDATE OutsourceRequests SET PpapRequiredLegacy = PpapRequired WHERE PpapRequired IS NOT NULL");

            // ----------------------------------------------------------------
            // Quantity: int → nvarchar(500)
            // Add new nvarchar col, populate from old int col, drop old col
            // ----------------------------------------------------------------

            migrationBuilder.AddColumn<string>(
                name: "Quantity_New",
                table: "OutsourceRequests",
                type: "nvarchar(500)",
                nullable: true);

            migrationBuilder.Sql("UPDATE OutsourceRequests SET Quantity_New = CAST(Quantity AS nvarchar(500))");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "OutsourceRequests");

            migrationBuilder.RenameColumn(
                name: "Quantity_New",
                table: "OutsourceRequests",
                newName: "Quantity");

            // Make the renamed column NOT NULL with a default
            migrationBuilder.AlterColumn<string>(
                name: "Quantity",
                table: "OutsourceRequests",
                type: "nvarchar(500)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldNullable: true);

            // ----------------------------------------------------------------
            // New table: OutsourceRequestCostLines
            // ----------------------------------------------------------------

            migrationBuilder.CreateTable(
                name: "OutsourceRequestCostLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId    = table.Column<int>(type: "int", nullable: false),
                    Description  = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InhouseCost  = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OutsourceCost = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Total        = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutsourceRequestCostLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutsourceRequestCostLines_OutsourceRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "OutsourceRequests",
                        principalColumn: "RequestId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutsourceRequestCostLines_RequestId",
                table: "OutsourceRequestCostLines",
                column: "RequestId");

            // ----------------------------------------------------------------
            // New table: OutsourceRequestAuditLogs
            // ----------------------------------------------------------------

            migrationBuilder.CreateTable(
                name: "OutsourceRequestAuditLogs",
                columns: table => new
                {
                    Id         = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId  = table.Column<int>(type: "int", nullable: false),
                    Timestamp  = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Actor      = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Action     = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ToStatus   = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Notes      = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutsourceRequestAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutsourceRequestAuditLogs_OutsourceRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "OutsourceRequests",
                        principalColumn: "RequestId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutsourceRequestAuditLogs_RequestId",
                table: "OutsourceRequestAuditLogs",
                column: "RequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "OutsourceRequestCostLines");
            migrationBuilder.DropTable(name: "OutsourceRequestAuditLogs");

            // Reverse Quantity back to int (data loss on non-numeric values accepted in Down)
            migrationBuilder.AddColumn<int>(
                name: "Quantity_Old",
                table: "OutsourceRequests",
                type: "int",
                nullable: true);

            migrationBuilder.Sql("UPDATE OutsourceRequests SET Quantity_Old = TRY_CAST(Quantity AS int)");

            migrationBuilder.DropColumn(name: "Quantity", table: "OutsourceRequests");

            migrationBuilder.RenameColumn(
                name: "Quantity_Old",
                table: "OutsourceRequests",
                newName: "Quantity");

            migrationBuilder.AlterColumn<int>(
                name: "Quantity",
                table: "OutsourceRequests",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldNullable: true);

            // Drop all new columns
            string[] newCols = {
                "RequestNumber","PPAPNumber","ConcessionNumber",
                "SpecialPackingRequired","SpecialPackingDetails",
                "JFSignedBy","JFSignedDate","JFComments",
                "LJSignedBy","LJSignedDate","LJComments",
                "CostEnteredBy","CostEnteredAt",
                "SGSignedBy","SGSignedDate","SGComments",
                "MDSignedBy","MDSignedDate","MDComments",
                "RejectionReason","RejectedBy","RejectedAt","PpapRequiredLegacy"
            };
            foreach (var col in newCols)
                migrationBuilder.DropColumn(name: col, table: "OutsourceRequests");
        }
    }
}
