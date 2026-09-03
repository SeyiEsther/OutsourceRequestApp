-- ============================================================
-- OutsourceRequestApp — schema update (run manually in SSMS)
--
-- This app applies NO schema changes itself: there is no
-- Database.Migrate() call and no runtime SQL execution anywhere in
-- the code. The C# files under /Migrations are inert unless someone
-- explicitly runs `dotnet ef database update` — this project does not
-- do that, so they exist only as EF's own record of the model.
--
-- Every statement below checks before it acts, so this script is
-- safe to run against the database in ANY current state (already
-- fully up to date, partially updated, or missing entirely) and
-- safe to re-run.
--
-- Run this against the OutsourceConnection database
-- (CSM_OutsourceRequests per appsettings.json — adjust the USE below
-- if your instance names it differently).
-- ============================================================

USE CSM_OutsourceRequests;
GO

-- 1) Base table ---------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'OutsourceRequests')
BEGIN
    CREATE TABLE dbo.OutsourceRequests (
        RequestId         INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        PartNumber        NVARCHAR(MAX)  NOT NULL,
        SapDescription    NVARCHAR(MAX)  NULL,
        DrawingNumber     NVARCHAR(MAX)  NULL,
        Quantity          INT            NOT NULL,
        StartDate         DATETIME2      NULL,
        EndDate           DATETIME2      NULL,
        Reason            NVARCHAR(MAX)  NOT NULL,
        Status            NVARCHAR(MAX)  NOT NULL,
        CreatedByUsername NVARCHAR(MAX)  NOT NULL,
        CreatedAt         DATETIME2      NOT NULL
    );
END
GO

-- 2) Columns added since the base table (each checked individually,
--    so this is safe no matter which of these your table already has)

IF COL_LENGTH('dbo.OutsourceRequests', 'AttachmentPath') IS NULL
    ALTER TABLE dbo.OutsourceRequests ADD AttachmentPath NVARCHAR(MAX) NULL;

IF COL_LENGTH('dbo.OutsourceRequests', 'ScReviewedAt') IS NULL
    ALTER TABLE dbo.OutsourceRequests ADD ScReviewedAt DATETIME2 NULL;

IF COL_LENGTH('dbo.OutsourceRequests', 'ScReviewedBy') IS NULL
    ALTER TABLE dbo.OutsourceRequests ADD ScReviewedBy NVARCHAR(MAX) NULL;

IF COL_LENGTH('dbo.OutsourceRequests', 'ScComments') IS NULL
    ALTER TABLE dbo.OutsourceRequests ADD ScComments NVARCHAR(MAX) NULL;

IF COL_LENGTH('dbo.OutsourceRequests', 'FinanceReviewedAt') IS NULL
    ALTER TABLE dbo.OutsourceRequests ADD FinanceReviewedAt DATETIME2 NULL;

IF COL_LENGTH('dbo.OutsourceRequests', 'FinanceReviewedBy') IS NULL
    ALTER TABLE dbo.OutsourceRequests ADD FinanceReviewedBy NVARCHAR(MAX) NULL;

IF COL_LENGTH('dbo.OutsourceRequests', 'FinanceComments') IS NULL
    ALTER TABLE dbo.OutsourceRequests ADD FinanceComments NVARCHAR(MAX) NULL;

IF COL_LENGTH('dbo.OutsourceRequests', 'MdReviewedAt') IS NULL
    ALTER TABLE dbo.OutsourceRequests ADD MdReviewedAt DATETIME2 NULL;

IF COL_LENGTH('dbo.OutsourceRequests', 'MdReviewedBy') IS NULL
    ALTER TABLE dbo.OutsourceRequests ADD MdReviewedBy NVARCHAR(MAX) NULL;

IF COL_LENGTH('dbo.OutsourceRequests', 'MdComments') IS NULL
    ALTER TABLE dbo.OutsourceRequests ADD MdComments NVARCHAR(MAX) NULL;

IF COL_LENGTH('dbo.OutsourceRequests', 'PpapRequired') IS NULL
    ALTER TABLE dbo.OutsourceRequests ADD PpapRequired BIT NULL;

IF COL_LENGTH('dbo.OutsourceRequests', 'CostInhousePerMonth') IS NULL
    ALTER TABLE dbo.OutsourceRequests ADD CostInhousePerMonth DECIMAL(18,2) NULL;

IF COL_LENGTH('dbo.OutsourceRequests', 'CostOutsourcePerMonth') IS NULL
    ALTER TABLE dbo.OutsourceRequests ADD CostOutsourcePerMonth DECIMAL(18,2) NULL;

IF COL_LENGTH('dbo.OutsourceRequests', 'CostComments') IS NULL
    ALTER TABLE dbo.OutsourceRequests ADD CostComments NVARCHAR(MAX) NULL;

IF COL_LENGTH('dbo.OutsourceRequests', 'LastReminderSentAt') IS NULL
    ALTER TABLE dbo.OutsourceRequests ADD LastReminderSentAt DATETIME2 NULL;

-- Sign-off + rejection fields from the 5-stage approval rebuild.
-- These are the columns the app's model expects but that had NO EF
-- migration at all until this audit — the most likely gap on the
-- live database.
IF COL_LENGTH('dbo.OutsourceRequests', 'JFSignedBy') IS NULL
    ALTER TABLE dbo.OutsourceRequests ADD JFSignedBy NVARCHAR(MAX) NULL;

IF COL_LENGTH('dbo.OutsourceRequests', 'JFSignedDate') IS NULL
    ALTER TABLE dbo.OutsourceRequests ADD JFSignedDate DATETIME2 NULL;

IF COL_LENGTH('dbo.OutsourceRequests', 'LJSignedBy') IS NULL
    ALTER TABLE dbo.OutsourceRequests ADD LJSignedBy NVARCHAR(MAX) NULL;

IF COL_LENGTH('dbo.OutsourceRequests', 'LJSignedDate') IS NULL
    ALTER TABLE dbo.OutsourceRequests ADD LJSignedDate DATETIME2 NULL;

IF COL_LENGTH('dbo.OutsourceRequests', 'SGSignedBy') IS NULL
    ALTER TABLE dbo.OutsourceRequests ADD SGSignedBy NVARCHAR(MAX) NULL;

IF COL_LENGTH('dbo.OutsourceRequests', 'SGSignedDate') IS NULL
    ALTER TABLE dbo.OutsourceRequests ADD SGSignedDate DATETIME2 NULL;

IF COL_LENGTH('dbo.OutsourceRequests', 'RejectionReason') IS NULL
    ALTER TABLE dbo.OutsourceRequests ADD RejectionReason NVARCHAR(MAX) NULL;

IF COL_LENGTH('dbo.OutsourceRequests', 'RejectedBy') IS NULL
    ALTER TABLE dbo.OutsourceRequests ADD RejectedBy NVARCHAR(MAX) NULL;

IF COL_LENGTH('dbo.OutsourceRequests', 'RejectedAt') IS NULL
    ALTER TABLE dbo.OutsourceRequests ADD RejectedAt DATETIME2 NULL;
GO

-- 3) ApproverRoles table (holds who is assigned to each approval role) --
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ApproverRoles')
BEGIN
    CREATE TABLE dbo.ApproverRoles (
        Id              INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        RoleKey         NVARCHAR(MAX) NOT NULL,
        RoleDisplayName NVARCHAR(MAX) NOT NULL,
        Username        NVARCHAR(MAX) NOT NULL,
        FullName        NVARCHAR(MAX) NOT NULL,
        Email           NVARCHAR(MAX) NOT NULL
    );
END
GO

-- 4) AppSettings table (SMTP config, reminder interval, admin list) -----
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AppSettings')
BEGIN
    CREATE TABLE dbo.AppSettings (
        Id           INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        SettingKey   NVARCHAR(MAX) NOT NULL,
        SettingValue NVARCHAR(MAX) NOT NULL
    );
END
GO

-- ============================================================
-- After running this, verify columns actually landed with e.g.:
--   SELECT name FROM sys.columns WHERE object_id = OBJECT_ID('dbo.OutsourceRequests') ORDER BY name;
-- ============================================================
