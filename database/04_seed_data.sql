/* ============================================================================
   LIMS - Reference / seed data
   ============================================================================ */
USE LimsDb;
GO

/* Clients ------------------------------------------------------------------ */
INSERT INTO dbo.Clients (ClientCode, CompanyName, ContactEmail, ContactPhone) VALUES
(N'CLI-001', N'PharmaLab Industries',   N'quality@pharmalab.tn',  N'+216 71 000 001'),
(N'CLI-002', N'AquaChem Water Co.',     N'lab@aquachem.tn',       N'+216 71 000 002'),
(N'CLI-003', N'AgroFood Processing',    N'qc@agrofood.tn',        N'+216 71 000 003'),
(N'CLI-004', N'MetalWorks SA',          N'hse@metalworks.tn',     N'+216 71 000 004');

/* Instruments ---------------------------------------------------------------*/
INSERT INTO dbo.Instruments (InstrumentCode, InstrumentName, Model, SerialNumber, Location, LastCalibrationAt, CalibrationPeriodDays) VALUES
(N'PHM-01',  N'pH Meter Bench A',      N'Mettler S220',   N'SN-88213', N'Lab 1 - Bench A',  DATEADD(DAY, -200, GETDATE()), 180),
(N'HPLC-01', N'HPLC System 1',         N'Agilent 1260',   N'SN-45190', N'Lab 2 - Room 201', DATEADD(DAY, -60,  GETDATE()), 365),
(N'BAL-01',  N'Analytical Balance',    N'Sartorius CPA',  N'SN-77341', N'Lab 1 - Bench C',  DATEADD(DAY, -30,  GETDATE()), 90),
(N'SPEC-01', N'UV-Vis Spectrophotometer', N'Shimadzu 1900', N'SN-19045', N'Lab 1 - Bench B', DATEADD(DAY, -400, GETDATE()), 365),
(N'KFT-01',  N'Karl Fischer Titrator', N'Mettler C30',    N'SN-30992', N'Lab 2 - Room 202', DATEADD(DAY, -10,  GETDATE()), 180);

/* Test catalogue ------------------------------------------------------------*/
INSERT INTO dbo.TestDefinitions (TestCode, TestName, Method, Unit, MinSpec, MaxSpec) VALUES
(N'PH',       N'pH Measurement',        N'ISO 10523:2008',      N'pH',    6.500000, 7.500000),
(N'ASSAY',    N'HPLC Assay',            N'USP <621>',           N'%',     98.000000, 102.000000),
(N'MOISTURE', N'Karl Fischer Moisture', N'USP <921>',           N'%',     NULL,      0.500000),
(N'ABSORB',   N'UV Absorbance 254nm',   N'ISO 15682',           N'AU',    NULL,      0.100000),
(N'DENSITY',  N'Density at 20C',        N'ISO 12185',           N'g/cm3', 0.998000, 1.002000);

/* Samples -------------------------------------------------------------------
   The generated codes are captured in variables so the script works
   regardless of the current year (codes are SMP-<currentYear>-NNNNN). */
DECLARE @SampleCode VARCHAR(30);
DECLARE @S1 VARCHAR(30), @S2 VARCHAR(30), @S3 VARCHAR(30), @S4 VARCHAR(30), @S5 VARCHAR(30);

EXEC dbo.usp_CreateSample @SampleCode OUTPUT, N'Batch 26-A-114 raw material', N'Raw material', 1, N'CLI-001', N'PH,ASSAY,MOISTURE', N'analyst1'; SET @S1 = @SampleCode;
EXEC dbo.usp_CreateSample @SampleCode OUTPUT, N'Drinking water - network point 7', N'Water', 2, N'CLI-002', N'PH,ABSORB', N'analyst1'; SET @S2 = @SampleCode;
EXEC dbo.usp_CreateSample @SampleCode OUTPUT, N'Finished product FP-8823', N'Finished product', 2, N'CLI-003', N'PH,DENSITY,MOISTURE', N'analyst2'; SET @S3 = @SampleCode;
EXEC dbo.usp_CreateSample @SampleCode OUTPUT, N'Wastewater discharge - line 3', N'Wastewater', 1, N'CLI-004', N'PH,ABSORB', N'analyst2'; SET @S4 = @SampleCode;
EXEC dbo.usp_CreateSample @SampleCode OUTPUT, N'Stability study T+3 months', N'Finished product', 3, N'CLI-001', N'ASSAY,MOISTURE', N'analyst1'; SET @S5 = @SampleCode;

/* Results for the first sample (all tests) ----------------------------------*/
EXEC dbo.usp_SubmitResult @S1, N'PH',       7.120000, N'PHM-01',  N'Auto import', N'WIN_SERVICE';
EXEC dbo.usp_SubmitResult @S1, N'ASSAY',   99.400000, N'HPLC-01', N'Auto import', N'WIN_SERVICE';
EXEC dbo.usp_SubmitResult @S1, N'MOISTURE', 0.210000, N'KFT-01',  N'Auto import', N'WIN_SERVICE';

/* Partial results for sample 2 ---------------------------------------------*/
EXEC dbo.usp_SubmitResult @S2, N'PH',       6.850000, N'PHM-01',  NULL, N'REST_API';

/* One out-of-spec result for sample 4 (wastewater) -------------------------*/
EXEC dbo.usp_SubmitResult @S4, N'PH',       9.300000, N'PHM-01',  N'Suspected contamination', N'VBS_SCRIPT';

/* Validate sample 1 ---------------------------------------------------------*/
EXEC dbo.usp_ChangeSampleStatus @S1, N'VALIDATED', N'Reviewed by lab manager', N'qual.manager';

-- ----------------------------------------------------------------------------
-- Lab accounts (JWT authentication, see src/Lims.RestApi/Controllers/AuthController.cs)
-- Passwords: analyst1 & analyst2 -> 'Analyst@2026'   |   qual.manager -> 'Manager@2026'
-- PBKDF2-SHA256, 100 000 iterations, per-user salt (see Lims.Core PasswordHasher).
-- ----------------------------------------------------------------------------
INSERT INTO dbo.Users (Username, DisplayName, Role, PasswordHash, PasswordSalt) VALUES
(N'analyst1',     N'Lab Analyst 1',  N'Analyst', '61422209B93A100B656CBFDEBA0D28F03F59099189C5DA65A837E71AECDFD1EB', '6F2A9D41C85B3E70A1D4F8C29B5E3716'),
(N'analyst2',     N'Lab Analyst 2',  N'Analyst', '0A6D6A4F2F646D1B6F2EFBB830245DC799DDC7F8C9C969A6D2BF79B99860FB0F', '3C7E1B94D6A8F205C4E9B7D31A6F8E20'),
(N'qual.manager', N'Quality Manager',N'Manager', 'DF6AC524AB79BEDCE97A1EEAC2EC1C1C6B7FBA783734D73296224B9D508E58A5', '9B4E27C1D8A35F06E2C719B4D6F8A351');
GO

PRINT N'Seed data loaded successfully.';
GO