/* ============================================================================
   LIMS - Additional Stored Procedures (run after 03_stored_procedures.sql)
   Adds: usp_GetAuditLog, usp_GetClients
   ============================================================================ */
USE LimsDb;
GO

/* ----------------------------------------------------------------------------
   dbo.usp_GetAuditLog
   Returns paged audit trail for the Manager audit panel.
   Filters: Source, Action, EntityRef partial match, IsSuccess, date range.
---------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_GetAuditLog
    @SearchText  NVARCHAR(100) = NULL,    -- partial match on Source, Action, EntityRef, Message
    @IsSuccess   BIT           = NULL,    -- NULL = both, 1 = success only, 0 = failures only
    @FromDate    DATETIME2     = NULL,
    @ToDate      DATETIME2     = NULL,
    @PageNumber  INT           = 1,
    @PageSize    INT           = 50
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Offset INT = (@PageNumber - 1) * @PageSize;
    DECLARE @Search NVARCHAR(102) = CONCAT('%', @SearchText, '%');

    SELECT AuditId, Source, Action, EntityRef, IsSuccess, Message, LoggedAt
    FROM   dbo.AuditLog
    WHERE  (@SearchText IS NULL
              OR Source    LIKE @Search
              OR Action    LIKE @Search
              OR EntityRef LIKE @Search
              OR Message   LIKE @Search)
      AND  (@IsSuccess IS NULL OR IsSuccess = @IsSuccess)
      AND  (@FromDate  IS NULL OR LoggedAt >= @FromDate)
      AND  (@ToDate    IS NULL OR LoggedAt <= @ToDate)
    ORDER BY LoggedAt DESC
    OFFSET  @Offset ROWS
    FETCH NEXT @PageSize ROWS ONLY;

    -- Total count for pagination
    SELECT COUNT(1)
    FROM   dbo.AuditLog
    WHERE  (@SearchText IS NULL
              OR Source    LIKE @Search
              OR Action    LIKE @Search
              OR EntityRef LIKE @Search
              OR Message   LIKE @Search)
      AND  (@IsSuccess IS NULL OR IsSuccess = @IsSuccess)
      AND  (@FromDate  IS NULL OR LoggedAt >= @FromDate)
      AND  (@ToDate    IS NULL OR LoggedAt <= @ToDate);
END;
GO

/* ----------------------------------------------------------------------------
   dbo.usp_GetClients
   Returns active client accounts for the registration form dropdown.
---------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_GetClients
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ClientId, ClientCode, CompanyName, ContactEmail
    FROM   dbo.Clients
    WHERE  IsActive = 1
    ORDER  BY ClientCode;
END;
GO
