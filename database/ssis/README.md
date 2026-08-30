# SSIS — Instrument Data Import Package (`LimsInstrumentImport.dtsx`)

This folder documents the **SSIS ETL package** used to bulk-import instrument
result files (CSV exports from lab analysers) into the LIMS database.
The package is designed in **SQL Server Data Tools (SSDT) / Visual Studio**
and deployed to the **SSIS Catalog** (`SSISDB`).

## Package design

### Control Flow

```
[On Error -> usp_LogAudit (Source='SSIS', IsSuccess=0)]
                                                   
1. SQL Task        : Truncate staging table dbo.stg_InstrumentResults
2. Foreach Loop    : Iterate *.csv files in \\lab-share\instrument-data\incoming
   |
   3. Flat File Source  -> Data Conversion -> OLE DB Destination (staging)
   4. File System Task  : Move processed file to \archive (or \error on failure)
5. Execute SQL Task: usp_MergeStagedResults (set-based MERGE into LIMS tables)
6. Execute SQL Task: usp_LogAudit (Source='SSIS', Action='IMPORT_COMPLETE')
7. Send Mail Task  : Notify lab IT on error (SMTP connection manager)
```

### Data Flow (inside the loop)

```
Flat File Source (CSV, comma, " text qualifier, codepage 1252)
   -> Derived Column  : normalize instrument code to UPPER
   -> Data Conversion : ResultValue -> DT_NUMERIC(18,6)
   -> Lookup          : TestDefinitions.TestCode  (no-match -> error output)
   -> Lookup          : Samples.SampleCode        (no-match -> error output)
   -> OLE DB Destination : dbo.stg_InstrumentResults (fast load)
```

### Package variables

| Variable            | Type    | Example                                  |
|---------------------|---------|------------------------------------------|
| `IncomingFolder`    | String  | `\\lab-share\instrument-data\incoming`   |
| `ArchiveFolder`     | String  | `\\lab-share\instrument-data\archive`    |
| `CurrentFile`       | String  | set by the Foreach Loop                  |
| `RowsImported`      | Int32   | propagated from Row Count task           |

### Configuration

- Connection strings and folder paths are externalized in an
  **Environment** (`LIMS-PROD`) inside the SSISDB catalog — no hardcoded paths.
- Package parameters mapped to environment variables at deployment.

## Staging table

See [`staging_tables.sql`](staging_tables.sql) — the staging table is truncated
on each run, loaded in bulk, then merged into the LIMS core tables via
`usp_MergeStagedResults` (idempotent: re-running a file never duplicates results).

## Scheduling

SQL Server Agent job `LIMS Instrument Import` runs the package every
**15 minutes** during lab hours (06:00–20:00), with retry + alerting.