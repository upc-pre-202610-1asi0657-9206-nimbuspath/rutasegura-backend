using System.Security.Cryptography;
using System.Text;
using MySql.Data.MySqlClient;

namespace RutaSegura.Notification.Infrastructure.Channels;

/// <summary>Lock de sesion MySQL, no transaccional. Se libera al cerrar su conexion dedicada (sin pooling).</summary>
public sealed class MySqlDispatchLock(string connectionString)
{
    public async Task<IAsyncDisposable?> TryAcquireAsync(Guid workId, CancellationToken cancellationToken)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString) { Pooling = false };
        var key = "nf:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.Database + ":" + workId.ToString("N"))))[..60];
        var connection = new MySqlConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 5;
            command.CommandText = "SELECT GET_LOCK(@name, 0)";
            command.Parameters.AddWithValue("@name", key);
            if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1) return new Session(connection, key);
            await connection.DisposeAsync();
            return null;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    private sealed class Session(MySqlConnection connection, string key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var release = connection.CreateCommand();
                release.CommandTimeout = 3;
                release.CommandText = "SELECT RELEASE_LOCK(@name)";
                release.Parameters.AddWithValue("@name", key);
                await release.ExecuteScalarAsync(CancellationToken.None);
            }
            catch (Exception) { /* Cerrar la conexion sigue liberando el lock; no registrar secretos ni errores SQL. */ }
            finally { await connection.DisposeAsync(); }
        }
    }
}
