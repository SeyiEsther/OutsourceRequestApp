using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OutsourceRequestApp.Migrations
{
    /// <inheritdoc />
    public partial class AddSignatureAndRejectionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // These columns may already exist on databases that were updated outside EF.
            // Guard with IF NOT EXISTS so the migration is safe to re-run.
            migrationBuilder.Sql(@"
IF COL_LENGTH('OutsourceRequests', 'JFSignedBy') IS NULL
    ALTER TABLE [OutsourceRequests] ADD [JFSignedBy] nvarchar(max) NULL;
IF COL_LENGTH('OutsourceRequests', 'JFSignedDate') IS NULL
    ALTER TABLE [OutsourceRequests] ADD [JFSignedDate] datetime2 NULL;
IF COL_LENGTH('OutsourceRequests', 'LJSignedBy') IS NULL
    ALTER TABLE [OutsourceRequests] ADD [LJSignedBy] nvarchar(max) NULL;
IF COL_LENGTH('OutsourceRequests', 'LJSignedDate') IS NULL
    ALTER TABLE [OutsourceRequests] ADD [LJSignedDate] datetime2 NULL;
IF COL_LENGTH('OutsourceRequests', 'SGSignedBy') IS NULL
    ALTER TABLE [OutsourceRequests] ADD [SGSignedBy] nvarchar(max) NULL;
IF COL_LENGTH('OutsourceRequests', 'SGSignedDate') IS NULL
    ALTER TABLE [OutsourceRequests] ADD [SGSignedDate] datetime2 NULL;
IF COL_LENGTH('OutsourceRequests', 'RejectionReason') IS NULL
    ALTER TABLE [OutsourceRequests] ADD [RejectionReason] nvarchar(max) NULL;
IF COL_LENGTH('OutsourceRequests', 'RejectedBy') IS NULL
    ALTER TABLE [OutsourceRequests] ADD [RejectedBy] nvarchar(max) NULL;
IF COL_LENGTH('OutsourceRequests', 'RejectedAt') IS NULL
    ALTER TABLE [OutsourceRequests] ADD [RejectedAt] datetime2 NULL;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "JFSignedBy", table: "OutsourceRequests");
            migrationBuilder.DropColumn(name: "JFSignedDate", table: "OutsourceRequests");
            migrationBuilder.DropColumn(name: "LJSignedBy", table: "OutsourceRequests");
            migrationBuilder.DropColumn(name: "LJSignedDate", table: "OutsourceRequests");
            migrationBuilder.DropColumn(name: "SGSignedBy", table: "OutsourceRequests");
            migrationBuilder.DropColumn(name: "SGSignedDate", table: "OutsourceRequests");
            migrationBuilder.DropColumn(name: "RejectionReason", table: "OutsourceRequests");
            migrationBuilder.DropColumn(name: "RejectedBy", table: "OutsourceRequests");
            migrationBuilder.DropColumn(name: "RejectedAt", table: "OutsourceRequests");
        }
    }
}
