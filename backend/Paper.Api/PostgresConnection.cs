using Npgsql;

namespace Paper.Api;

public static class PostgresConnection
{
    public static string Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Set ConnectionStrings__Database for PostgreSQL. No local fallback is allowed.");
        try
        {
            NpgsqlConnectionStringBuilder connection;
            if (value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(value);
                var credentials = uri.UserInfo.Split(':', 2);
                if (credentials.Length != 2) throw new FormatException();
                connection = new NpgsqlConnectionStringBuilder
                {
                    Host = uri.Host, Port = uri.IsDefaultPort || uri.Port < 0 ? 5432 : uri.Port,
                    Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
                    Username = Uri.UnescapeDataString(credentials[0]), Password = Uri.UnescapeDataString(credentials[1]),
                    SslMode = SslMode.VerifyFull
                };
                // Accept libpq's supported TLS settings; reject options we cannot safely preserve.
                foreach (var item in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var pair = item.Split('=', 2);
                    var key = Uri.UnescapeDataString(pair[0]);
                    var option = pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : "";
                    if (key == "sslmode")
                    {
                        connection.SslMode = option switch
                        {
                            "require" => SslMode.Require, "verify-full" => SslMode.VerifyFull,
                            "verify-ca" => SslMode.VerifyCA, "disable" => SslMode.Disable,
                            _ => throw new FormatException()
                        };
                    }
                    else if (key == "channel_binding")
                    {
                        connection.ChannelBinding = option switch
                        {
                            "require" => ChannelBinding.Require, "prefer" => ChannelBinding.Prefer,
                            "disable" => ChannelBinding.Disable, _ => throw new FormatException()
                        };
                    }
                    else throw new FormatException();
                }
            }
            else connection = new NpgsqlConnectionStringBuilder(value);
            if (string.IsNullOrWhiteSpace(connection.Host) || string.IsNullOrWhiteSpace(connection.Database) ||
                string.IsNullOrWhiteSpace(connection.Username)) throw new FormatException();
            // A brief wake-up delay is normal for a serverless database.
            connection.Timeout = 30;
            connection.CommandTimeout = 30;
            connection.MaxPoolSize = 10;
            return connection.ConnectionString;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or KeyNotFoundException)
        {
            // Never include the supplied URL, credentials, or the parser's inner error in logs.
            throw new InvalidOperationException("Invalid PostgreSQL connection string. Use the Neon PostgreSQL URL or a valid Npgsql connection string.");
        }
    }
}
