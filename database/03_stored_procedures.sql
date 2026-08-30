/* ============================================================================
   LIMS - Stored Procedures (SQL Server / T-SQL)
   ============================================================================ */
USE LimsDb;
GO

/* ----------------------------------------------------------------------------
   dbo.usp_CreateSample
   Registers a new sample, attaches requested tests and writes the audit trail.
   Returns the generated SampleCode via output parameter.
---------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_CreateSample
    @SampleCode    VARCHAR(30)      OUTPUT,
    @Description   NVARCHAR(300),
    @Matrix        NVARCHAR(100),
    @Priority      TINYINT          = 2,
    @ClientCode    VARCHAR(20),
    @TestCodes     NVARCHAR(MAX),              -- CSV list, e.g. N'PH,HPLC,MOISTURE'
    @CreatedBy     NVARCHAR(100)    = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @ClientId INT = (SELECT ClientId FROM dbo.Clients WHERE ClientCode = @ClientCode AND IsActive = 1);
        IF @ClientId IS NULL
            THROW 50001, N'Unknown or inactive client code.', 1;

        /* Generate a sequential sample code : SMP-YYYY-NNNNN
           sp_getapplock serializes code generation to prevent duplicate sequence numbers
           under concurrent registrations. The lock is released at COMMIT/ROLLBACK. */
        DECLARE @Year INT = YEAR(GETDATE());
        DECLARE @LockResource NVARCHAR(255) = CONCAT(N'LIMS_SAMPLECODE_', @Year);
        DECLARE @LockResult INT;
        EXEC @LockResult = sp_getapplock
            @Resource       = @LockResource,
            @LockMode       = N'Exclusive',
            @LockOwner      = N'Transaction',
            @LockTimeout    = 5000;
        IF @LockResult < 0
            THROW 50004, N'Could not acquire the sample-code lock (timeout).', 1;

        DECLARE @Seq INT = ISNULL((SELECT MAX(CAST(RIGHT(SampleCode, 5) AS INT))
                                   FROM dbo.Samples WITH (UPDLOCK, HOLDLOCK)
                                   WHERE SampleCode LIKE CONCAT('SMP-', @Year, '-%')), 0) + 1;
        SET @SampleCode = CONCAT('SMP-', @Year, '-', FORMAT(@Seq, '00000'));

        INSERT INTO dbo.Samples (SampleCode, Description, Matrix, Status, Priority, ClientId, CreatedBy)
        VALUES (@SampleCode, @Description, @Matrix, N'REGISTERED', @Priority, @ClientId, ISNULL(@CreatedBy, SUSER_SNAME()));

        DECLARE @SampleId INT = SCOPE_IDENTITY();

        /* Attach requested tests (ignore unknown codes) */
        INSERT INTO dbo.SampleTests (SampleId, TestId, Status)
        SELECT @SampleId, td.TestId, N'PENDING'
        FROM dbo.TestDefinitions td
        WHERE td.IsActive = 1
          AND CONCAT(N',', @TestCodes, N',') LIKE CONCAT(N'%,', td.TestCode, N',%');

        INSERT INTO dbo.SampleStatusHistory (SampleId, OldStatus, NewStatus, ChangedBy, Comment)
        VALUES (@SampleId, NULL, N'REGISTERED', ISNULL(@CreatedBy, SUSER_SNAME()), N'Sample registered');

        COMMIT TRANSACTION;
        SELECT @SampleCode AS SampleCode, @SampleId AS SampleId;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;   -- rethrow to the caller (API returns 400/500)
    END CATCH
END;
GO

/* ----------------------------------------------------------------------------
   dbo.usp_GetSampleByCode
   Returns the sample header + all its tests and results (JSON-ready shape).
---------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_GetSampleByCode
    @SampleCode VARCHAR(30)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT s.SampleId, s.SampleCode, s.Description, s.Matrix, s.Status, s.Priority,
           c.ClientCode, c.CompanyName AS ClientName,
           s.CollectedAt, s.CompletedAt, s.CreatedBy, s.CreatedAt
    FROM dbo.Samples s
    INNER JOIN dbo.Clients c ON c.ClientId = s.ClientId
    WHERE s.SampleCode = @SampleCode;

    SELECT st.SampleTestId, td.TestCode, td.TestName, td.Method, td.Unit,
           st.Status AS TestStatus, st.StartedAt, st.CompletedAt,
           i.InstrumentCode,
           r.ResultValue, r.Passed, r.MeasuredAt, r.Comment
    FROM dbo.SampleTests st
    INNER JOIN dbo.Samples s          ON s.SampleId = st.SampleId
    INNER JOIN dbo.TestDefinitions td ON td.TestId  = st.TestId
    LEFT  JOIN dbo.Instruments i      ON i.InstrumentId = st.InstrumentId
    LEFT  JOIN dbo.Results r          ON r.SampleTestId = st.SampleTestId
    WHERE s.SampleCode = @SampleCode
    ORDER BY td.TestCode;
END;
GO

/* ----------------------------------------------------------------------------
   dbo.usp_SearchSamples
   Paged sample search with optional filters (all nullable).
---------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_SearchSamples
    @SearchText  NVARCHAR(100) = NULL,
    @Status      VARCHAR(20)   = NULL,
    @ClientCode  VARCHAR(20)   = NULL,
    @PageNumber  INT           = 1,
    @PageSize    INT           = 20
AS
BEGIN
    SET NOCOUNT ON;

    IF @PageNumber < 1 SET @PageNumber = 1;
    IF @PageSize  < 1 OR @PageSize > 200 SET @PageSize = 20;

    DECLARE @Offset INT = (@PageNumber - 1) * @PageSize;

    SELECT v.SampleId, v.SampleCode, v.Description, v.Matrix, v.Status, v.Priority,
           v.ClientCode, v.ClientName, v.CollectedAt, v.CompletedAt,
           v.TotalTests, v.CompletedTests, v.PendingTests, v.FailedResults, v.ProgressPercent
    FROM dbo.vw_SampleOverview v
    WHERE (@SearchText IS NULL OR v.SampleCode LIKE CONCAT(N'%', @SearchText, N'%')
                              OR v.Description LIKE CONCAT(N'%', @SearchText, N'%')
                              OR v.ClientName   LIKE CONCAT(N'%', @SearchText, N'%'))
      AND (@Status     IS NULL OR v.Status     = @Status)
      AND (@ClientCode IS NULL OR v.ClientCode = @ClientCode)
    ORDER BY v.Priority ASC, v.CollectedAt DESC
    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;

    SELECT COUNT(1) AS TotalCount
    FROM dbo.vw_SampleOverview v
    WHERE (@SearchText IS NULL OR v.SampleCode LIKE CONCAT(N'%', @SearchText, N'%')
                              OR v.Description LIKE CONCAT(N'%', @SearchText, N'%')
                              OR v.ClientName   LIKE CONCAT(N'%', @SearchText, N'%'))
      AND (@Status     IS NULL OR v.Status     = @Status)
      AND (@ClientCode IS NULL OR v.ClientCode = @ClientCode);
END;
GO

/* ----------------------------------------------------------------------------
   dbo.usp_SubmitResult
   Records an instrument/analyst result, evaluates pass/fail against the spec
   limits, updates the test status and rolls the sample status forward.
   Used by REST API, SOAP service, Windows Service and SSIS.
---------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_SubmitResult
    @SampleCode   VARCHAR(30),
    @TestCode     VARCHAR(20),
    @ResultValue  DECIMAL(18,6),
    @InstrumentCode VARCHAR(30) = NULL,
    @Comment      NVARCHAR(500) = NULL,
    @Source       VARCHAR(50)   = N'REST_API'
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @SampleTestId INT, @TestId INT, @SampleId INT;

        SELECT @SampleTestId = st.SampleTestId,
               @TestId       = st.TestId,
               @SampleId     = st.SampleId
        FROM dbo.SampleTests st
        INNER JOIN dbo.Samples s ON s.SampleId = st.SampleId
        WHERE s.SampleCode = @SampleCode
          AND st.TestId = (SELECT TestId FROM dbo.TestDefinitions WHERE TestCode = @TestCode);

        IF @SampleTestId IS NULL
            THROW 50002, N'Sample/test combination not found.', 1;

        DECLARE @InstrumentId INT = NULL;
        IF @InstrumentCode IS NOT NULL
            SELECT @InstrumentId = InstrumentId FROM dbo.Instruments WHERE InstrumentCode = @InstrumentCode;

        /* Evaluate against specification limits */
        DECLARE @MinSpec DECIMAL(18,6), @MaxSpec DECIMAL(18,6), @Unit VARCHAR(30);
        SELECT @MinSpec = MinSpec, @MaxSpec = MaxSpec, @Unit = Unit
        FROM dbo.TestDefinitions WHERE TestId = @TestId;

        DECLARE @Passed BIT =
            CASE WHEN (@MinSpec IS NOT NULL AND @ResultValue < @MinSpec)
                   OR (@MaxSpec IS NOT NULL AND @ResultValue > @MaxSpec)
                 THEN 0 ELSE 1 END;

        INSERT INTO dbo.Results (SampleTestId, ResultValue, Unit, Passed, InstrumentId, Comment, EnteredBy)
        VALUES (@SampleTestId, @ResultValue, @Unit, @Passed, @InstrumentId, @Comment, @Source);

        UPDATE dbo.SampleTests
        SET Status = N'COMPLETED', CompletedAt = SYSUTCDATETIME(), InstrumentId = @InstrumentId
        WHERE SampleTestId = @SampleTestId;

        /* Roll the sample forward : if every test is completed -> COMPLETED */
        DECLARE @Pending INT = (SELECT COUNT(1) FROM dbo.SampleTests
                                WHERE SampleId = @SampleId AND Status NOT IN (N'COMPLETED', N'CANCELLED'));

        DECLARE @OldStatus VARCHAR(20) = (SELECT Status FROM dbo.Samples WHERE SampleId = @SampleId);
        DECLARE @NewStatus VARCHAR(20) = CASE WHEN @Pending = 0 THEN N'COMPLETED' ELSE N'IN_PROGRESS' END;

        UPDATE dbo.Samples
        SET Status = @NewStatus,
            CompletedAt = CASE WHEN @NewStatus = N'COMPLETED' THEN SYSUTCDATETIME() ELSE CompletedAt END
        WHERE SampleId = @SampleId;

        IF @OldStatus <> @NewStatus
            INSERT INTO dbo.SampleStatusHistory (SampleId, OldStatus, NewStatus, ChangedBy, Comment)
            VALUES (@SampleId, @OldStatus, @NewStatus, @Source, CONCAT(N'Auto transition after result for test ', @TestCode));

        INSERT INTO dbo.AuditLog (Source, Action, EntityRef, IsSuccess, Message)
        VALUES (@Source, N'SUBMIT_RESULT', @SampleCode, 1,
                CONCAT(N'Test ', @TestCode, N' = ', @ResultValue, N' (', IIF(@Passed = 1, N'PASS', N'FAIL'), N')'));

        COMMIT TRANSACTION;
        SELECT @Passed AS Passed, @NewStatus AS SampleStatus;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;

        INSERT INTO dbo.AuditLog (Source, Action, EntityRef, IsSuccess, Message)
        VALUES (@Source, N'SUBMIT_RESULT', @SampleCode, 0, ERROR_MESSAGE());

        THROW;
    END CATCH
END;
GO

/* ----------------------------------------------------------------------------
   dbo.usp_ChangeSampleStatus
   Manual status transition with audit trail (validation by a lab manager).
---------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_ChangeSampleStatus
    @SampleCode VARCHAR(30),
    @NewStatus  VARCHAR(20),
    @Comment    NVARCHAR(500) = NULL,
    @ChangedBy  NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @SampleId INT, @OldStatus VARCHAR(20);
        SELECT @SampleId = SampleId, @OldStatus = Status
        FROM dbo.Samples WHERE SampleCode = @SampleCode;

        IF @SampleId IS NULL
            THROW 50003, N'Sample not found.', 1;

        UPDATE dbo.Samples
        SET Status = @NewStatus,
            CompletedAt = CASE WHEN @NewStatus IN (N'COMPLETED', N'VALIDATED') THEN SYSUTCDATETIME() ELSE CompletedAt END
        WHERE SampleId = @SampleId;

        INSERT INTO dbo.SampleStatusHistory (SampleId, OldStatus, NewStatus, ChangedBy, Comment)
        VALUES (@SampleId, @OldStatus, @NewStatus, ISNULL(@ChangedBy, SUSER_SNAME()), @Comment);

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

/* ----------------------------------------------------------------------------
   dbo.usp_GetDashboardStats
   Single round-trip for the home dashboard (multiple result sets).
---------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_GetDashboardStats
AS
BEGIN
    SET NOCOUNT ON;

    /* 1) Samples by status */
    SELECT Status, COUNT(1) AS SampleCount
    FROM dbo.Samples
    GROUP BY Status;

    /* 2) Overdue instrument calibrations */
    SELECT InstrumentCode, InstrumentName, NextCalibrationDue
    FROM dbo.vw_InstrumentCalibration
    WHERE IsOverdue = 1;

    /* 3) Out-of-spec results last 30 days */
    SELECT TestCode, TestName, OutOfSpecCount
    FROM dbo.vw_ResultStatistics
    WHERE OutOfSpecCount > 0
    ORDER BY OutOfSpecCount DESC;
END;
GO

/* ----------------------------------------------------------------------------
   dbo.usp_LogAudit : lightweight helper used by middleware components
---------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_LogAudit
    @Source    VARCHAR(50),
    @Action    VARCHAR(100),
    @EntityRef VARCHAR(50)  = NULL,
    @IsSuccess BIT,
    @Message   NVARCHAR(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.AuditLog (Source, Action, EntityRef, IsSuccess, Message)
    VALUES (@Source, @Action, @EntityRef, @IsSuccess, @Message);
END;
GO

/* ----------------------------------------------------------------------------
    dbo.usp_GetUserByUsername
    Loads a lab account for JWT authentication (hash material included -
    verification happens in C#, endpoint reachable only by the API host).
---------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_GetUserByUsername
    @Username VARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT UserId,
           Username,
           DisplayName,
           Role,
           PasswordHash,
           PasswordSalt,
           IsActive,
           TokenVersion
    FROM dbo.Users
    WHERE Username = @Username;
END;
GO

/* ----------------------------------------------------------------------------
    dbo.usp_GetUserById
    Loads one account by primary key (user administration).
---------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_GetUserById
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT UserId, Username, DisplayName, Role, IsActive, TokenVersion, CreatedAt
    FROM dbo.Users
    WHERE UserId = @UserId;
END;
GO

/* ----------------------------------------------------------------------------
    dbo.usp_ListUsers
    All accounts WITHOUT hash material - for the admin user list screen.
---------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_ListUsers
AS
BEGIN
    SET NOCOUNT ON;

    SELECT UserId, Username, DisplayName, Role, IsActive, CreatedAt
    FROM dbo.Users
    ORDER BY Username;
END;
GO

/* ----------------------------------------------------------------------------
    dbo.usp_CreateUser
    Creates a lab account. The unique username constraint rejects duplicates
    (surface as HTTP 409 Conflict). Hash + salt computed in C# (PBKDF2).
---------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_CreateUser
    @Username     VARCHAR(50),
    @DisplayName  NVARCHAR(100),
    @Role         VARCHAR(20),
    @PasswordHash CHAR(64),
    @PasswordSalt CHAR(32),
    @CreatedBy    NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.Users (Username, DisplayName, Role, PasswordHash, PasswordSalt)
    VALUES (@Username, @DisplayName, @Role, @PasswordHash, @PasswordSalt);

    SELECT CAST(SCOPE_IDENTITY() AS INT) AS UserId;

    DECLARE @msg NVARCHAR(300) = CONCAT('Role=', @Role, ' by ', ISNULL(@CreatedBy, 'api'));
    EXEC dbo.usp_LogAudit 'REST_API', 'USER_CREATE', @Username, 1, @msg;
END;
GO

/* ----------------------------------------------------------------------------
    dbo.usp_SetUserActive
    Activates / deactivates an account and bumps TokenVersion so that any
    token already issued for the user stops validating immediately.
---------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_SetUserActive
    @UserId    INT,
    @IsActive  BIT,
    @ChangedBy NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Users
    SET IsActive     = @IsActive,
        TokenVersion = TokenVersion + 1
    WHERE UserId = @UserId;

    DECLARE @rows INT = @@ROWCOUNT;
    IF @rows > 0
    BEGIN
        DECLARE @ref VARCHAR(10) = CAST(@UserId AS VARCHAR(10));
        DECLARE @msg NVARCHAR(300) = CONCAT('IsActive=', @IsActive, ' by ', ISNULL(@ChangedBy, 'api'));
        EXEC dbo.usp_LogAudit 'REST_API', 'USER_SET_ACTIVE', @ref, 1, @msg;
    END

    SELECT @rows AS Affected;   -- rows-affected cannot be relied on with SET NOCOUNT ON
END;
GO

/* ----------------------------------------------------------------------------
    dbo.usp_ChangePassword
    Replaces the password material (old password verified in C#) and bumps
    TokenVersion: every other session of this user is revoked.
---------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_ChangePassword
    @UserId       INT,
    @NewHash      CHAR(64),
    @NewSalt      CHAR(32),
    @ChangedBy    NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Users
    SET PasswordHash  = @NewHash,
        PasswordSalt  = @NewSalt,
        TokenVersion  = TokenVersion + 1
    WHERE UserId = @UserId;

    DECLARE @rows INT = @@ROWCOUNT;
    IF @rows > 0
    BEGIN
        DECLARE @ref VARCHAR(10) = CAST(@UserId AS VARCHAR(10));
        DECLARE @msg NVARCHAR(300) = CONCAT('changed by ', ISNULL(@ChangedBy, 'api'));
        EXEC dbo.usp_LogAudit 'REST_API', 'PASSWORD_CHANGE', @ref, 1, @msg;
    END

    SELECT @rows AS Affected;   -- rows-affected cannot be relied on with SET NOCOUNT ON
END;
GO

/* ----------------------------------------------------------------------------
    dbo.usp_ResetPassword
    Manager-driven password reset (no old password required) + TokenVersion bump.
---------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_ResetPassword
    @UserId    INT,
    @NewHash   CHAR(64),
    @NewSalt   CHAR(32),
    @ChangedBy NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Users
    SET PasswordHash = @NewHash,
        PasswordSalt = @NewSalt,
        TokenVersion = TokenVersion + 1
    WHERE UserId = @UserId;

    DECLARE @rows INT = @@ROWCOUNT;
    IF @rows > 0
    BEGIN
        DECLARE @ref VARCHAR(10) = CAST(@UserId AS VARCHAR(10));
        DECLARE @msg NVARCHAR(300) = CONCAT('by ', ISNULL(@ChangedBy, 'api'));
        EXEC dbo.usp_LogAudit 'REST_API', 'PASSWORD_RESET', @ref, 1, @msg;
    END

    SELECT @rows AS Affected;   -- rows-affected cannot be relied on with SET NOCOUNT ON
END;
GO

/* ----------------------------------------------------------------------------
   dbo.usp_GetInstruments
   Returns the instruments/calibration view. Exists as a named SP so the
   C# layer never contains raw SQL strings (SP-only architecture policy).
   ---------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_GetInstruments
AS
BEGIN
    SET NOCOUNT ON;
    SELECT InstrumentId, InstrumentCode, InstrumentName,
           LastCalibrationAt  AS LastCalibrationDate,
           NextCalibrationDue AS NextCalibrationDate,
           IsOverdue
    FROM   dbo.vw_InstrumentCalibration
    ORDER  BY IsOverdue DESC, NextCalibrationDue;
END;
GO

/* ----------------------------------------------------------------------------
   dbo.usp_ValidateStatusTransition  (helper – called by usp_ChangeSampleStatus)
   Returns 1 if the FromStatus -> ToStatus transition is defined in the LIMS
   workflow rules, 0 otherwise.  Keeps the business rule in one SQL location.
   ---------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_ValidateStatusTransition
    @FromStatus NVARCHAR(20),
    @ToStatus   NVARCHAR(20),
    @IsValid    BIT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    -- Inline transition table mirrors DomainValidators.AllowedTransitions
    SELECT @IsValid = CAST(COUNT(1) AS BIT)
    FROM (VALUES
        ('REGISTERED',   'IN_PROGRESS'),
        ('IN_PROGRESS',  'COMPLETED'),
        ('IN_PROGRESS',  'REJECTED'),
        ('COMPLETED',    'VALIDATED'),
        ('COMPLETED',    'REJECTED'),
        ('VALIDATED',    'ARCHIVED')
    ) AS T(FromStatus, ToStatus)
    WHERE T.FromStatus = @FromStatus
      AND T.ToStatus   = @ToStatus;
END;
GO