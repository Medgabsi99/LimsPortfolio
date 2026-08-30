/* ============================================================================
   LIMS - Database Schema (SQL Server / T-SQL)
   Laboratory Information Management System - Sample lifecycle management
   ----------------------------------------------------------------------------
   Run order : 01_tables.sql -> 02_views.sql -> 03_stored_procedures.sql
               -> 04_seed_data.sql
   Target    : SQL Server 2019+ (works on Express / Developer editions)
   ============================================================================ */

IF DB_ID(N'LimsDb') IS NULL
    CREATE DATABASE LimsDb;
GO
USE LimsDb;
GO

/* Safety: recreate everything from scratch on re-run
   (drop order respects FOREIGN KEY dependencies: children first) */
IF OBJECT_ID(N'dbo.AuditLog',            N'U') IS NOT NULL DROP TABLE dbo.AuditLog;
IF OBJECT_ID(N'dbo.SampleStatusHistory', N'U') IS NOT NULL DROP TABLE dbo.SampleStatusHistory;
IF OBJECT_ID(N'dbo.Results',             N'U') IS NOT NULL DROP TABLE dbo.Results;
IF OBJECT_ID(N'dbo.SampleTests',         N'U') IS NOT NULL DROP TABLE dbo.SampleTests;
IF OBJECT_ID(N'dbo.Samples',             N'U') IS NOT NULL DROP TABLE dbo.Samples;
IF OBJECT_ID(N'dbo.TestDefinitions',     N'U') IS NOT NULL DROP TABLE dbo.TestDefinitions;
IF OBJECT_ID(N'dbo.Instruments',         N'U') IS NOT NULL DROP TABLE dbo.Instruments;
IF OBJECT_ID(N'dbo.Clients',             N'U') IS NOT NULL DROP TABLE dbo.Clients;
IF OBJECT_ID(N'dbo.Users',               N'U') IS NOT NULL DROP TABLE dbo.Users;
GO

/* ----------------------------------------------------------------------------
   Clients : laboratories / industrial customers submitting samples
---------------------------------------------------------------------------- */
CREATE TABLE dbo.Clients
(
    ClientId      INT IDENTITY(1,1)  NOT NULL CONSTRAINT PK_Clients PRIMARY KEY,
    ClientCode    VARCHAR(20)        NOT NULL,
    CompanyName   NVARCHAR(150)      NOT NULL,
    ContactEmail  NVARCHAR(200)      NULL,
    ContactPhone  VARCHAR(30)        NULL,
    IsActive      BIT                NOT NULL CONSTRAINT DF_Clients_IsActive DEFAULT(1),
    CreatedAt     DATETIME2(0)       NOT NULL CONSTRAINT DF_Clients_CreatedAt DEFAULT(SYSUTCDATETIME()),

    CONSTRAINT UQ_Clients_ClientCode UNIQUE (ClientCode)
);
GO

/* ----------------------------------------------------------------------------
   Users : lab accounts for the REST API (JWT authentication).
   Passwords are stored as PBKDF2-SHA256 hashes (100 000 iterations,
   per-user random salt) - never in clear text.
   Roles : Analyst  -> register samples, submit results
           Manager  -> validate / reject samples (quality control)
---------------------------------------------------------------------------- */
CREATE TABLE dbo.Users
(
    UserId        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
    Username      VARCHAR(50)       NOT NULL,
    DisplayName   NVARCHAR(100)     NOT NULL,
    Role          VARCHAR(20)       NOT NULL,
    PasswordHash  CHAR(64)          NOT NULL,   -- PBKDF2-SHA256, hex encoded
    PasswordSalt  CHAR(32)          NOT NULL,   -- per-user random salt, hex encoded
    IsActive      BIT               NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT(1),
    -- Security stamp: incremented on password change / reset / deactivation.
    -- Every issued JWT carries the version it was issued for; tokens with an
    -- older version are rejected at validation time (instant revocation).
    TokenVersion  INT               NOT NULL CONSTRAINT DF_Users_TokenVersion DEFAULT(1),
    CreatedAt     DATETIME2(0)      NOT NULL CONSTRAINT DF_Users_CreatedAt DEFAULT(SYSUTCDATETIME()),

    CONSTRAINT UQ_Users_Username UNIQUE (Username),
    CONSTRAINT CK_Users_Role CHECK (Role IN ('Analyst', 'Manager'))
);
GO

/* ----------------------------------------------------------------------------
   Instruments : lab analysers that produce raw result files (middleware feeds)
---------------------------------------------------------------------------- */
CREATE TABLE dbo.Instruments
(
    InstrumentId       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Instruments PRIMARY KEY,
    InstrumentCode     VARCHAR(30)       NOT NULL,
    InstrumentName     NVARCHAR(100)     NOT NULL,
    Model              NVARCHAR(100)     NULL,
    SerialNumber       VARCHAR(50)       NULL,
    Location           NVARCHAR(100)     NULL,
    LastCalibrationAt  DATE              NULL,
    CalibrationPeriodDays INT            NOT NULL CONSTRAINT DF_Instruments_CalPeriod DEFAULT(365),
    IsActive           BIT               NOT NULL CONSTRAINT DF_Instruments_IsActive DEFAULT(1),

    CONSTRAINT UQ_Instruments_InstrumentCode UNIQUE (InstrumentCode),
    CONSTRAINT CK_Instruments_CalPeriod CHECK (CalibrationPeriodDays > 0)
);
GO

/* ----------------------------------------------------------------------------
   TestDefinitions : catalogue of analytical methods (e.g. pH, HPLC assay...)
---------------------------------------------------------------------------- */
CREATE TABLE dbo.TestDefinitions
(
    TestId        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TestDefinitions PRIMARY KEY,
    TestCode      VARCHAR(20)       NOT NULL,
    TestName      NVARCHAR(150)     NOT NULL,
    Method        NVARCHAR(200)     NULL,          -- e.g. "ISO 10523:2008"
    Unit          VARCHAR(30)       NULL,
    MinSpec       DECIMAL(18,6)     NULL,          -- lower specification limit
    MaxSpec       DECIMAL(18,6)     NULL,          -- upper specification limit
    IsActive      BIT               NOT NULL CONSTRAINT DF_TestDefinitions_IsActive DEFAULT(1),

    CONSTRAINT UQ_TestDefinitions_TestCode UNIQUE (TestCode)
);
GO

/* ----------------------------------------------------------------------------
   Samples : the core entity - one row per physical sample received
   Status workflow : REGISTERED -> IN_PROGRESS -> COMPLETED -> VALIDATED
                     (any state can go to REJECTED / CANCELLED)
---------------------------------------------------------------------------- */
CREATE TABLE dbo.Samples
(
    SampleId      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Samples PRIMARY KEY,
    SampleCode    VARCHAR(30)       NOT NULL,      -- e.g. SMP-2026-00042
    Description   NVARCHAR(300)     NULL,
    Matrix        NVARCHAR(100)     NULL,          -- water, soil, raw material...
    Status        VARCHAR(20)       NOT NULL CONSTRAINT DF_Samples_Status DEFAULT(N'REGISTERED'),
    Priority      TINYINT           NOT NULL CONSTRAINT DF_Samples_Priority DEFAULT(2), -- 1=High 2=Normal 3=Low
    ClientId      INT               NOT NULL,
    CollectedAt   DATETIME2(0)      NOT NULL CONSTRAINT DF_Samples_CollectedAt DEFAULT(SYSUTCDATETIME()),
    CompletedAt   DATETIME2(0)      NULL,
    CreatedBy     NVARCHAR(100)     NOT NULL CONSTRAINT DF_Samples_CreatedBy DEFAULT(SUSER_SNAME()),
    CreatedAt     DATETIME2(0)      NOT NULL CONSTRAINT DF_Samples_CreatedAt DEFAULT(SYSUTCDATETIME()),
    RowVersion    ROWVERSION,

    CONSTRAINT UQ_Samples_SampleCode UNIQUE (SampleCode),
    CONSTRAINT FK_Samples_Clients FOREIGN KEY (ClientId) REFERENCES dbo.Clients(ClientId),
    CONSTRAINT CK_Samples_Status CHECK (Status IN (N'REGISTERED', N'IN_PROGRESS', N'COMPLETED', N'VALIDATED', N'REJECTED', N'CANCELLED')),
    CONSTRAINT CK_Samples_Priority CHECK (Priority BETWEEN 1 AND 3)
);
GO

/* ----------------------------------------------------------------------------
   SampleTests : which analyses are requested on which sample
---------------------------------------------------------------------------- */
CREATE TABLE dbo.SampleTests
(
    SampleTestId  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SampleTests PRIMARY KEY,
    SampleId      INT               NOT NULL,
    TestId        INT               NOT NULL,
    Status        VARCHAR(20)       NOT NULL CONSTRAINT DF_SampleTests_Status DEFAULT(N'PENDING'), -- PENDING / IN_PROGRESS / COMPLETED / CANCELLED
    InstrumentId  INT               NULL,          -- instrument used for the analysis
    StartedAt     DATETIME2(0)      NULL,
    CompletedAt   DATETIME2(0)      NULL,

    CONSTRAINT FK_SampleTests_Samples     FOREIGN KEY (SampleId)     REFERENCES dbo.Samples(SampleId),
    CONSTRAINT FK_SampleTests_TestDefs    FOREIGN KEY (TestId)       REFERENCES dbo.TestDefinitions(TestId),
    CONSTRAINT FK_SampleTests_Instruments FOREIGN KEY (InstrumentId) REFERENCES dbo.Instruments(InstrumentId),
    CONSTRAINT CK_SampleTests_Status CHECK (Status IN (N'PENDING', N'IN_PROGRESS', N'COMPLETED', N'CANCELLED')),
    CONSTRAINT UQ_SampleTests_Sample_Test UNIQUE (SampleId, TestId)
);
GO

/* ----------------------------------------------------------------------------
   Results : measured values, one per completed sample test
---------------------------------------------------------------------------- */
CREATE TABLE dbo.Results
(
    ResultId      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Results PRIMARY KEY,
    SampleTestId  INT               NOT NULL,
    ResultValue   DECIMAL(18,6)     NOT NULL,
    Unit          VARCHAR(30)       NULL,
    Passed        BIT               NOT NULL,       -- within specification limits
    InstrumentId  INT               NULL,
    MeasuredAt    DATETIME2(0)      NOT NULL CONSTRAINT DF_Results_MeasuredAt DEFAULT(SYSUTCDATETIME()),
    EnteredBy     NVARCHAR(100)     NOT NULL CONSTRAINT DF_Results_EnteredBy DEFAULT(SUSER_SNAME()),
    Comment       NVARCHAR(500)     NULL,

    CONSTRAINT FK_Results_SampleTests FOREIGN KEY (SampleTestId) REFERENCES dbo.SampleTests(SampleTestId),
    CONSTRAINT FK_Results_Instruments FOREIGN KEY (InstrumentId) REFERENCES dbo.Instruments(InstrumentId)
);
GO

/* ----------------------------------------------------------------------------
   SampleStatusHistory : complete audit trail of the sample lifecycle (21 CFR Part 11)
---------------------------------------------------------------------------- */
CREATE TABLE dbo.SampleStatusHistory
(
    HistoryId    INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SampleStatusHistory PRIMARY KEY,
    SampleId     INT               NOT NULL,
    OldStatus    VARCHAR(20)       NULL,
    NewStatus    VARCHAR(20)       NOT NULL,
    ChangedBy    NVARCHAR(100)     NOT NULL CONSTRAINT DF_History_ChangedBy DEFAULT(SUSER_SNAME()),
    ChangedAt    DATETIME2(0)      NOT NULL CONSTRAINT DF_History_ChangedAt DEFAULT(SYSUTCDATETIME()),
    Comment      NVARCHAR(500)     NULL,

    CONSTRAINT FK_History_Samples FOREIGN KEY (SampleId) REFERENCES dbo.Samples(SampleId)
);
GO

/* ----------------------------------------------------------------------------
   AuditLog : generic technical audit (middleware imports, API calls...)
---------------------------------------------------------------------------- */
CREATE TABLE dbo.AuditLog
(
    AuditId      BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditLog PRIMARY KEY,
    Source       VARCHAR(50)       NOT NULL,        -- REST_API / SOAP_SERVICE / WIN_SERVICE / VBS_SCRIPT / SSIS
    Action       VARCHAR(100)      NOT NULL,
    EntityRef    VARCHAR(50)       NULL,            -- sample code or instrument code
    IsSuccess    BIT               NOT NULL,
    Message      NVARCHAR(1000)    NULL,
    LoggedAt     DATETIME2(0)      NOT NULL CONSTRAINT DF_AuditLog_LoggedAt DEFAULT(SYSUTCDATETIME())
);
GO

/* ----------------------------------------------------------------------------
   Indexes supporting the main search / dashboard queries
---------------------------------------------------------------------------- */
CREATE INDEX IX_Samples_Status            ON dbo.Samples(Status) INCLUDE (SampleCode, Priority, CollectedAt);
CREATE INDEX IX_Samples_ClientId          ON dbo.Samples(ClientId);
CREATE INDEX IX_SampleTests_SampleId      ON dbo.SampleTests(SampleId);
CREATE INDEX IX_SampleTests_Status        ON dbo.SampleTests(Status) INCLUDE (SampleId, TestId);
CREATE INDEX IX_Results_SampleTestId      ON dbo.Results(SampleTestId);
CREATE INDEX IX_Instruments_Calibration   ON dbo.Instruments(LastCalibrationAt) WHERE IsActive = 1;
CREATE INDEX IX_AuditLog_LoggedAt         ON dbo.AuditLog(LoggedAt DESC);
GO