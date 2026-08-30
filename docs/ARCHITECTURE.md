# LIMS — Architecture Overview

## 1. Context

The LIMS (Laboratory Information Management System) manages the complete
lifecycle of laboratory samples: registration, analysis worklists, result
capture (manual or instrument-fed), validation and client reporting.

It integrates with the surrounding information system (ERP, MES, client
portals) through REST and SOAP web services, and with lab instruments
through file-based middleware (Windows Service, VB Script, SSIS).

## 2. Component diagram

```
                          +---------------------+
   Lab analysts  <------> |  LIMS SPA Dashboard |  (Built-in wwwroot Web App)
                          +----------+----------+
                                     | REST/JSON
                                     v
+-------------+   CSV files   +------+-------+    REST (ASP.NET Core)
| Instruments |------------->|  Middleware  |-->+---------------------+
| (pH meters, |              |  components  |    |    Lims.RestApi     |
|  HPLC, ...) |              +------+-------+    +----------+----------+
+-------------+                     |                        |
      |  SOAP 1.1                   | Dapper                 | stored procs
      v                             v                        v
+------------------+    +---------------------+    +------------+
|  Lims.SoapService|    | Lims.WindowsService |    |  SQL Server|
| (ERP/MES interop)|    | (instrument import) |    |   LimsDb   |
+------------------+    +---------------------+    |  views/procs|
                                                   |  SSIS/SSRS |
      VB Script (ADO)  ---------------------------> +------------+
      (scheduled imports, Excel exports)                  |
                                                          v
                                              +------------------------+
                                              | SSIS: bulk instrument  |
                                              | SSRS: dashboards       |
                                              | Crystal: CoA documents |
                                              +------------------------+
```

## 3. .NET solution layout

| Project                | Type                | Responsibility                                        |
|------------------------|---------------------|-------------------------------------------------------|
| `Lims.Core`            | Class library       | Domain models, workflow rules, interfaces (no I/O)    |
| `Lims.Infrastructure`  | Class library       | Dapper repositories calling SQL stored procedures     |
| `Lims.RestApi`          | ASP.NET Core Web API| REST/JSON endpoints, Swagger & SPA Web UI             |
| `Lims.SoapService`      | ASP.NET Core        | SOAP 1.1 endpoint (WSDL) for legacy ERP/MES clients   |
| `Lims.WindowsService`   | Worker Service      | Windows Service importing instrument result files     |
| `Lims.Tests`            | xUnit               | Unit & integration tests of the domain and API logic  |

### Dependency rule

```
RestApi / SoapService / WindowsService  ->  Infrastructure  ->  Core
```

`Core` has **zero** infrastructure dependencies: business rules
(`DomainValidators`, `SampleStatus`, `InstrumentFileParser`, `PasswordHasher`)
are pure and unit-testable. All persistence goes through `ISampleRepository`,
`IUserRepository`, and `ISqlAuditReader`.

## 4. Data flow of a result

1. Instrument writes a CSV line `SMP-2026-00001,PH,PHM-01,7.12,...`
2. The **Windows Service** (or VB Script / SSIS) picks it up, parses it
   (`InstrumentFileParser`) and calls `usp_SubmitResult`.
3. The stored procedure evaluates the value against spec limits, records the
   result, updates the test status and rolls the sample status forward
   (`IN_PROGRESS` -> `COMPLETED`), writing an audit trail entry.
4. The REST API exposes the updated state; SSRS/Crystal reports pick it up;
   the SOAP service lets the ERP query the sample status.

## 5. Cross-cutting concerns

- **Security**: JWT authentication with role-based authorization (Analysts
  register samples and submit results, Managers validate/reject), PBKDF2-SHA256
  password hashing with per-user salts, parameterized stored procedures only
  (no SQL injection), no secrets in code (connection string and JWT signing key
  in configuration / user-secrets).
- **Auditability**: every result import, status change, and security action is traced
  (`AuditLog`, `SampleStatusHistory`) — fully compliant with 21 CFR Part 11 requirements.
- **Resilience**: transient-fault retry in the connection factory, per-line
  error isolation in the import worker, SOAP faults per spec.
- **Quality**: unit tests on the domain, CI pipeline with coverage on Azure DevOps.

## 6. Agile practices

The team works in Scrum: the solution is structured so that each feature
(e.g. "new test type", "new report") touches a small, well-bounded slice —
a stored procedure + a repository method + an endpoint — which maps naturally
to user stories and keeps sprint estimations reliable.