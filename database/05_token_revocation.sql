/* ============================================================================
   LIMS - Token Revocation Table (SQL Server / T-SQL)
   Persists individual JWT revocations (logout) so they survive service
   restarts and multi-instance deployments.
   Run AFTER 01_tables.sql (depends on LimsDb being present).
   ============================================================================ */
USE LimsDb;
GO

-- ── Table ─────────────────────────────────────────────────────────────────────
IF OBJECT_ID('dbo.RevokedTokens', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RevokedTokens
    (
        Jti          VARCHAR(128)  NOT NULL CONSTRAINT PK_RevokedTokens PRIMARY KEY,
        ExpiresAtUtc DATETIME2(0)  NOT NULL,   -- token's natural exp
        RevokedAtUtc DATETIME2(0)  NOT NULL CONSTRAINT DF_RevokedTokens_RevokedAt DEFAULT SYSUTCDATETIME()
    );

    -- Clustered on PK (Jti). Non-clustered covering index used by cleanup job.
    CREATE NONCLUSTERED INDEX IX_RevokedTokens_Expires
        ON dbo.RevokedTokens (ExpiresAtUtc);
END;
GO

/* ── dbo.usp_RevokeToken ──────────────────────────────────────────────────────
   Inserts a revoked JTI, ignoring duplicates (double-logout is fine).
   The caller sets ExpiresAtUtc to the token's exp so the cleanup job
   knows when the row can be removed. */
CREATE OR ALTER PROCEDURE dbo.usp_RevokeToken
    @Jti          VARCHAR(128),
    @ExpiresAtUtc DATETIME2(0)
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.RevokedTokens (Jti, ExpiresAtUtc)
    SELECT @Jti, @ExpiresAtUtc
    WHERE NOT EXISTS (
        SELECT 1 FROM dbo.RevokedTokens WHERE Jti = @Jti
    );
END;
GO

/* ── dbo.usp_IsTokenRevoked ───────────────────────────────────────────────────
   Returns 1 if the JTI is in the revocation list AND has not yet expired.
   Expired rows are harmless — they are pruned by usp_PruneRevokedTokens. */
CREATE OR ALTER PROCEDURE dbo.usp_IsTokenRevoked
    @Jti    VARCHAR(128),
    @IsRevoked BIT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT @IsRevoked = CAST(COUNT(1) AS BIT)
    FROM dbo.RevokedTokens
    WHERE Jti = @Jti
      AND ExpiresAtUtc > SYSUTCDATETIME();
END;
GO

/* ── dbo.usp_PruneRevokedTokens ──────────────────────────────────────────────
   Removes rows whose token has already expired (they can never be re-validated
   anyway). Call this from a SQL Agent job or a background timer. */
CREATE OR ALTER PROCEDURE dbo.usp_PruneRevokedTokens
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.RevokedTokens WHERE ExpiresAtUtc <= SYSUTCDATETIME();
    SELECT @@ROWCOUNT AS PrunedRows;
END;
GO
