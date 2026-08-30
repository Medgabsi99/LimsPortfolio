/* ============================================================================
   LIMS - Views (SQL Server / T-SQL)
   Reporting & business views consumed by the REST API, SSRS and Crystal Reports
   ============================================================================ */
USE LimsDb;
GO

/* ----------------------------------------------------------------------------
   vw_SampleOverview : one row per sample with client info, test counts and
   global progress. Used by the sample search screen and the SSRS report.
---------------------------------------------------------------------------- */
CREATE OR ALTER VIEW dbo.vw_SampleOverview
AS
SELECT
    s.SampleId,
    s.SampleCode,
    s.Description,
    s.Matrix,
    s.Status,
    s.Priority,
    c.ClientCode,
    c.CompanyName          AS ClientName,
    s.CollectedAt,
    s.CompletedAt,
    COUNT(st.SampleTestId)                       AS TotalTests,
    COUNT(CASE WHEN st.Status = N'COMPLETED' THEN 1 END) AS CompletedTests,
    COUNT(CASE WHEN st.Status = N'PENDING'   THEN 1 END) AS PendingTests,
    SUM(CASE WHEN r.Passed = 0 THEN 1 ELSE 0 END)        AS FailedResults,
    CASE
        WHEN COUNT(st.SampleTestId) = 0 THEN 0
        ELSE CAST(COUNT(CASE WHEN st.Status = N'COMPLETED' THEN 1 END) * 100.0
                  / COUNT(st.SampleTestId) AS DECIMAL(5,2))
    END                                           AS ProgressPercent
FROM dbo.Samples s
INNER JOIN dbo.Clients c        ON c.ClientId = s.ClientId
LEFT  JOIN dbo.SampleTests st   ON st.SampleId = s.SampleId
LEFT  JOIN dbo.Results r        ON r.SampleTestId = st.SampleTestId
GROUP BY s.SampleId, s.SampleCode, s.Description, s.Matrix, s.Status, s.Priority,
         c.ClientCode, c.CompanyName, s.CollectedAt, s.CompletedAt;
GO

/* ----------------------------------------------------------------------------
   vw_PendingWorklist : tests waiting for an analyst, ordered by priority and
   age. This is the daily worklist of the laboratory bench.
---------------------------------------------------------------------------- */
CREATE OR ALTER VIEW dbo.vw_PendingWorklist
AS
SELECT
    st.SampleTestId,
    s.SampleCode,
    s.Priority,
    td.TestCode,
    td.TestName,
    td.Method,
    td.Unit,
    td.MinSpec,
    td.MaxSpec,
    i.InstrumentCode,
    i.InstrumentName,
    DATEDIFF(DAY, s.CollectedAt, SYSUTCDATETIME()) AS AgeDays
FROM dbo.SampleTests st
INNER JOIN dbo.Samples s           ON s.SampleId = st.SampleId
INNER JOIN dbo.TestDefinitions td  ON td.TestId = st.TestId
LEFT  JOIN dbo.Instruments i       ON i.InstrumentId = st.InstrumentId
WHERE st.Status IN (N'PENDING', N'IN_PROGRESS')
  AND s.Status  IN (N'REGISTERED', N'IN_PROGRESS');
GO

/* ----------------------------------------------------------------------------
   vw_InstrumentCalibration : instruments with their next calibration due date
   and a flag when overdue. Critical for lab quality compliance.
---------------------------------------------------------------------------- */
CREATE OR ALTER VIEW dbo.vw_InstrumentCalibration
AS
SELECT
    i.InstrumentId,
    i.InstrumentCode,
    i.InstrumentName,
    i.Model,
    i.Location,
    i.LastCalibrationAt,
    i.CalibrationPeriodDays,
    DATEADD(DAY, i.CalibrationPeriodDays, i.LastCalibrationAt) AS NextCalibrationDue,
    CASE
        WHEN i.LastCalibrationAt IS NULL THEN 1
        WHEN DATEADD(DAY, i.CalibrationPeriodDays, i.LastCalibrationAt) < CAST(GETDATE() AS DATE) THEN 1
        ELSE 0
    END AS IsOverdue
FROM dbo.Instruments i
WHERE i.IsActive = 1;
GO

/* ----------------------------------------------------------------------------
   vw_ResultStatistics : per-test statistics over the last 90 days, used by
   management dashboards (SSRS / Crystal Reports) and trend analysis.
---------------------------------------------------------------------------- */
CREATE OR ALTER VIEW dbo.vw_ResultStatistics
AS
SELECT
    td.TestId,
    td.TestCode,
    td.TestName,
    td.Unit,
    td.MinSpec,
    td.MaxSpec,
    COUNT(r.ResultId)                                   AS ResultCount,
    CAST(AVG(r.ResultValue) AS DECIMAL(18,6))           AS AvgValue,
    CAST(MIN(r.ResultValue) AS DECIMAL(18,6))           AS MinValue,
    CAST(MAX(r.ResultValue) AS DECIMAL(18,6))           AS MaxValue,
    SUM(CASE WHEN r.Passed = 0 THEN 1 ELSE 0 END)       AS OutOfSpecCount,
    CAST(SUM(CASE WHEN r.Passed = 0 THEN 1 ELSE 0 END) * 100.0
         / NULLIF(COUNT(r.ResultId), 0) AS DECIMAL(5,2)) AS OutOfSpecPercent
FROM dbo.TestDefinitions td
INNER JOIN dbo.SampleTests st ON st.TestId = td.TestId
INNER JOIN dbo.Results r      ON r.SampleTestId = st.SampleTestId
WHERE r.MeasuredAt >= DATEADD(DAY, -90, SYSUTCDATETIME())
GROUP BY td.TestId, td.TestCode, td.TestName, td.Unit, td.MinSpec, td.MaxSpec;
GO