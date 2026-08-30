# SSRS — LIMS Reports

Reports are authored in **Report Builder / Visual Studio (Report Server project)**
and deployed to **SQL Server Reporting Services**. They consume the SQL views
created in [`../02_views.sql`](../02_views.sql) — no business logic in reports.

## Report catalog

| Report                          | Source view / proc        | Purpose                                        |
|---------------------------------|---------------------------|------------------------------------------------|
| `SampleSummaryReport.rdl`       | `vw_SampleOverview`       | Sample register with progress & failed results |
| `PendingWorklist.rdl`           | `vw_PendingWorklist`      | Daily analyst worklist, grouped by priority    |
| `InstrumentCalibration.rdl`     | `vw_InstrumentCalibration`| Calibration schedule + overdue alerts          |
| `ResultTrendReport.rdl`         | `vw_ResultStatistics`     | 90-day trend & out-of-spec statistics          |
| `CertificateOfAnalysis.rdl`     | `usp_GetSampleByCode`     | Official CoA delivered to the client (PDF)     |

## Sample report

[`SampleSummaryReport.rdl`](SampleSummaryReport.rdl) is a ready-to-deploy
report (RDL 2016 schema) with:

- A **parameter** `@Status` (multi-value, defaults to all statuses)
- A **table** with progress percentage and out-of-spec highlighting
  (red when `FailedResults > 0`)
- A shared data source `LimsDb` pointing at the LIMS database

## Subscriptions & delivery

- Completed **Certificate of Analysis** PDFs are e-mailed to clients via
  data-driven subscriptions (one row per validated sample).
- The management dashboard report is cached and refreshed hourly.

## Integration

The REST API exposes `GET /api/reports/samples/{code}/certificate-url`
which returns the SSRS URL-encoded report link (`/ReportServer?/LIMS/CertificateOfAnalysis&SampleCode=...&rs:Format=PDF`)
so the front-end can download certificates without direct SSRS access.