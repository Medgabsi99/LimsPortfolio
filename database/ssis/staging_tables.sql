/* ============================================================================
   LIMS - SSIS staging objects
   Used by the LimsInstrumentImport.dtsx package (see README.md)
   ============================================================================ */
USE LimsDb;
GO

/* Staging table : bulk-loaded from CSV by SSIS, then merged */
IF OBJECT_ID(N'dbo.stg_InstrumentResults', N'U') IS NOT NULL DROP TABLE dbo.stg_InstrumentResults;
GO
CREATE TABLE dbo.stg_InstrumentResults
(
    ImportId        BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    SourceFile      NVARCHAR(260)   NOT NULL,
    SampleCode      VARCHAR(30)     NOT NULL,
    TestCode        VARCHAR(20)     NOT NULL,
    InstrumentCode  VARCHAR(30)     NULL,
    ResultValue     DECIMAL(18,6)   NOT NULL,
    MeasuredAt      DATETIME2(0)    NOT NULL,
    LoadedAt        DATETIME2(0)    NOT NULL CONSTRAINT DF_stg_LoadedAt DEFAULT(SYSUTCDATETIME())
);
GO

/* ----------------------------------------------------------------------------
   usp_MergeStagedResults
   Set-based, idempotent merge of staged instrument results into the LIMS.
   A (sample, test) pair that already has a result is skipped (first value wins,
   lab policy: no silent overwrite of an analytical result).
---------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_MergeStagedResults
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        /* 1) Insert results that do not exist yet */
        INSERT INTO dbo.Results (SampleTestId, ResultValue, Unit, Passed, InstrumentId, MeasuredAt, EnteredBy, Comment)
        SELECT
            st.SampleTestId,
            s.ResultValue,
            td.Unit,
            CASE WHEN (td.MinSpec IS NOT NULL AND s.ResultValue < td.MinSpec)
                   OR (td.MaxSpec IS NOT NULL AND s.ResultValue > td.MaxSpec)
                 THEN 0 ELSE 1 END,
            i.InstrumentId,
            s.MeasuredAt,
            N'SSIS',
            CONCAT(N'Imported from ', s.SourceFile)
        FROM dbo.stg_InstrumentResults s
        INNER JOIN dbo.Samples sam          ON sam.SampleCode = s.SampleCode
        INNER JOIN dbo.TestDefinitions td   ON td.TestCode    = s.TestCode
        INNER JOIN dbo.SampleTests st       ON st.SampleId    = sam.SampleId
                                           AND st.TestId      = td.TestId
        LEFT  JOIN dbo.Instruments i        ON i.InstrumentCode = s.InstrumentCode
        WHERE NOT EXISTS (SELECT 1 FROM dbo.Results r WHERE r.SampleTestId = st.SampleTestId);

        /* 2) Mark matching sample tests as completed */
        UPDATE st
        SET Status      = N'COMPLETED',
            CompletedAt = SYSUTCDATETIME(),
            InstrumentId = i.InstrumentId
        FROM dbo.SampleTests st
        INNER JOIN dbo.stg_InstrumentResults s ON s.SampleCode IN (SELECT SampleCode FROM dbo.Samples WHERE SampleId = st.SampleId)
                                              AND s.TestCode    IN (SELECT TestCode    FROM dbo.TestDefinitions WHERE TestId = st.TestId)
        LEFT  JOIN dbo.Instruments i ON i.InstrumentCode = s.InstrumentCode
        WHERE st.Status <> N'COMPLETED';

        /* 3) Roll samples forward */
        UPDATE sam
        SET Status      = N'COMPLETED',
            CompletedAt = SYSUTCDATETIME()
        FROM dbo.Samples sam
        WHERE sam.Status = N'IN_PROGRESS'
          AND NOT EXISTS (SELECT 1 FROM dbo.SampleTests st
                          WHERE st.SampleId = sam.SampleId
                            AND st.Status NOT IN (N'COMPLETED', N'CANCELLED'));

        /* 4) Audit */
        DECLARE @Rows INT = @@ROWCOUNT;
        INSERT INTO dbo.AuditLog (Source, Action, EntityRef, IsSuccess, Message)
        VALUES (N'SSIS', N'MERGE_STAGED', NULL, 1, CONCAT(N'Merged staged results, rows affected: ', @Rows));

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;

        INSERT INTO dbo.AuditLog (Source, Action, EntityRef, IsSuccess, Message)
        VALUES (N'SSIS', N'MERGE_STAGED', NULL, 0, ERROR_MESSAGE());
        THROW;
    END CATCH
END;
GO