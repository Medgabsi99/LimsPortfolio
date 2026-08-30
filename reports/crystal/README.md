# Crystal Reports — LIMS Reports

Crystal Reports is used for the **official client-facing documents**
(Certificates of Analysis, batch certificates) where pixel-perfect,
regulatory-grade layout is required. SSRS handles the internal dashboards
(see `../../database/ssrs/`).

## Report catalog

| Report (.rpt)                  | Data source (stored procedure)   | Purpose                                        |
|--------------------------------|----------------------------------|------------------------------------------------|
| `CertificateOfAnalysis.rpt`    | `usp_GetSampleByCode`            | Official CoA sent to clients (PDF export)      |
| `BatchReleaseCertificate.rpt`  | `usp_GetSampleByCode` + subreport| Batch release document for production          |
| `InstrumentLogbook.rpt`        | `vw_InstrumentCalibration`       | Instrument usage & calibration logbook         |

## Design conventions

- Reports connect through an **OLE DB (ADO.NET) / SQL Server Native Client**
  connection to `LimsDb` and call **stored procedures only** — no embedded SQL.
- Parameters are named after the procedure parameters
  (`@SampleCode` for the CoA).
- The CoA layout includes: client header, sample identification, results table
  with specification limits, pass/fail flags, analyst & reviewer signature blocks.
- Export targets: **PDF** (client delivery) and **Excel** (data exchange).

## Runtime integration

The .NET side prints/exports reports through the **Crystal Reports SDK
(CrystalDecisions.CrystalReports.Engine)** — typical pattern:

```csharp
// Pseudo-code of the export service (requires SAP Crystal Reports runtime)
using var report = new ReportDocument();
report.Load(@"C:\Lims\Reports\CertificateOfAnalysis.rpt");
report.SetDatabaseLogon(user, password, server, "LimsDb");
report.SetParameterValue("@SampleCode", sampleCode);

var options = new ExportOptions
{
    ExportFormatType = ExportFormatType.PortableDocFormat,
    ExportDestinationType = ExportDestinationType.DiskFile
};
options.DestinationOptions = new DiskFileDestinationOptions
{
    DiskFileName = $@"C:\Lims\Exports\CoA_{sampleCode}.pdf"
};
report.Export(options);
```

> The runtime (`CRRuntimeEngine`) is **not** included in this repository:
> install "SAP Crystal Reports, developer version for Visual Studio" and add
> references to `CrystalDecisions.CrystalReports.Engine` /
> `CrystalDecisions.Shared` when enabling this feature.

## Scheduling

Certificates are generated in batch by the Windows Service after a sample
reaches `VALIDATED` status, then attached to the client notification e-mail.