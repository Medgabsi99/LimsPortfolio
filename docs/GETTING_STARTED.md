# Getting Started

## Prerequisites

| Tool                          | Version            | Notes                                    |
|-------------------------------|--------------------|------------------------------------------|
| Visual Studio 2022            | 17.8+              | with ".NET desktop" + "ASP.NET" workloads |
| .NET SDK                      | 10.0               | `dotnet --version`                        |
| SQL Server                    | 2019+ (Express OK) | LocalDB or full instance                  |
| SSMS (optional)               | 19+                | to run the SQL scripts                    |
| SSDT / SSIS / SSRS (optional) | VS 2022 extensions | only for the BI assets                    |

## 1. Create the database
 
Open **SSMS** (or run `database/run-all.ps1`) and execute the scripts in order:
 
```sql
:r database/01_tables.sql
:r database/02_views.sql
:r database/03_stored_procedures.sql
:r database/04_seed_data.sql
:r database/05_token_revocation.sql
:r database/06_additional_procedures.sql
```
 
Or run the automated PowerShell script:
 
```powershell
powershell -ExecutionPolicy Bypass -File database/run-all.ps1
```
 
You now have `LimsDb` with reference samples, instruments, calibration data, and sample results.
 
## 2. Configure the connection string
 
Each service has an `appsettings.json` with:
 
```json
"ConnectionStrings": {
  "LimsDb": "Server=(localdb)\\MSSQLLocalDB;Database=LimsDb;Trusted_Connection=True;TrustServerCertificate=True;"
}
```
 
Adjust `Server=` to your SQL Server instance if needed.
 
## 3. Run the REST API & Web Dashboard
 
```bash
cd src/Lims.RestApi
dotnet run
```
 
- **Web UI**: Navigate to `http://localhost:5000` in your browser.
- **Swagger Documentation**: Navigate to `http://localhost:5000/swagger`.
- **Sign in**: Use `analyst1` / `Analyst@2026` or `qual.manager` / `Manager@2026`.
- The Web UI automatically saves the authentication token and maintains your session across page refreshes.
- Manual status transitions (e.g. validating/rejecting samples) and Audit Log exploration require the **Manager** role (`qual.manager`).

Default accounts (seeded in `dbo.Users`, PBKDF2-hashed passwords):

| Username       | Password       | Role    |
|----------------|----------------|---------|
| `analyst1`     | `Analyst@2026` | Analyst |
| `analyst2`     | `Analyst@2026` | Analyst |
| `qual.manager` | `Manager@2026` | Manager |

Try: `GET /api/samples?status=IN_PROGRESS` (with token), then sign in as
`qual.manager` and validate a sample with `PUT /api/samples/{code}/status`.

> Production note: move `Jwt:SigningKey` to an environment variable or
> a secrets store — never keep signing keys in `appsettings.json` in production.

## 4. Run the SOAP service

```bash
cd src/Lims.SoapService
dotnet run
```

- WSDL: `https://localhost:yyyy/LimsSampleService.asmx?wsdl`
- Test with a raw SOAP 1.1 POST:

```xml
<soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
  <soap:Body>
    <GetSampleStatus>
      <sampleCode>SMP-2026-00001</sampleCode>
    </GetSampleStatus>
  </soap:Body>
</soap:Envelope>
```

## 5. Run the Windows Service (console mode)

```bash
cd src/Lims.WindowsService
dotnet run
```

Then drop a CSV file in `C:\Lims\InstrumentData\incoming`:

```
SMP-2026-00002,ABSORB,SPEC-01,0.045,2026-08-28 14:30:00
```

The service imports it, archives the file and the sample status updates.

## 6. Run the unit tests

```bash
dotnet test LimsPortfolio.sln
```

## 7. Install as a real Windows Service (production)

```bat
sc create LimsInstrumentImport binPath= "<published exe path>" start= auto
sc start LimsInstrumentImport
```

## 8. BI assets (optional)

- **SSIS**: follow `database/ssis/README.md` to build the import package in SSDT.
- **SSRS**: deploy `database/ssrs/SampleSummaryReport.rdl` to your report server.
- **Crystal Reports**: see `reports/crystal/README.md` for the CoA documents.