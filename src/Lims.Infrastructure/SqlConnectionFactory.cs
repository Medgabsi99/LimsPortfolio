using Microsoft.Data.SqlClient;

namespace Lims.Infrastructure;

/// <summary>
/// Creates <em>un-opened</em> SQL Server connections from configuration.
/// Dapper (QueryAsync / ExecuteAsync) opens the connection automatically
/// before executing the command and closes it when the reader is disposed.
/// Centralizes connection-string management and transient-fault retry settings.
/// </summary>
public interface ISqlConnectionFactory
{
    SqlConnection Create();
}

public class SqlConnectionFactory : ISqlConnectionFactory
{
    private readonly string _connectionString;

    public SqlConnectionFactory(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public SqlConnection Create()
    {
        // ConnectRetryCount / ConnectRetryInterval: transparent transient-fault
        // retry (SQL 2014+). The connection itself is NOT opened here; Dapper
        // opens it lazily when it executes the first command.
        var builder = new SqlConnectionStringBuilder(_connectionString)
        {
            ConnectRetryCount = 3,
            ConnectRetryInterval = 5,
            ApplicationName = "LIMS"
        };
        return new SqlConnection(builder.ConnectionString);
    }
}