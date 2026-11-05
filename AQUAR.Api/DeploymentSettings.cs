using Microsoft.Data.SqlClient;

namespace AQUAR.Api;

public static class DeploymentSettings
{
    public static string? ListenUrl(IConfiguration configuration)
    {
        var raw = configuration["PORT"];
        if (raw == null) return null;
        if (!int.TryParse(raw, out var port) || port is < 1 or > 65535)
            throw new InvalidOperationException("PORT must be an integer between 1 and 65535.");
        return $"http://0.0.0.0:{port}";
    }

    public static string[] AllowedOrigins(IConfiguration configuration)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        foreach (var origin in origins)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "https" && uri.Scheme != "http") ||
                !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
                origin.Contains('*') || origin.EndsWith('/'))
                throw new InvalidOperationException("CORS origins must be explicit http(s) origins without paths, wildcards or trailing slash.");
        }
        return origins;
    }

    public static void ValidateProduction(IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("Set ConnectionStrings__Default for production SQL Server.");
        SqlConnectionStringBuilder sql;
        try { sql = new SqlConnectionStringBuilder(connection); }
        catch (ArgumentException) { throw new InvalidOperationException("Production SQL Server connection configuration is invalid."); }
        if (sql.IntegratedSecurity || string.IsNullOrWhiteSpace(sql.DataSource) || string.IsNullOrWhiteSpace(sql.InitialCatalog))
            throw new InvalidOperationException("Production requires a reachable SQL Server and database; local Windows Authentication is not supported by this deployment.");
        if (!sql.Encrypt || sql.TrustServerCertificate)
            throw new InvalidOperationException("Production SQL Server requires Encrypt=True and TrustServerCertificate=False with a trusted server certificate.");
        var key = configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(key) || System.Text.Encoding.UTF8.GetByteCount(key) < 32 ||
            key.Contains("CHANGE", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("REPLACE", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Set Jwt__Key to a strong secret of at least 32 bytes; placeholder keys are not allowed.");
    }
}
