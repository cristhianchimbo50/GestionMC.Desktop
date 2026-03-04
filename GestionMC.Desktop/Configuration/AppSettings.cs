using System;
using System.IO;
using System.Text.Json;

namespace GestionMC.Desktop.Configuration;

public class AppSettings
{
    public SriCredentialsSettings SriCredentials { get; set; } = new();
    public OracleSettings Oracle { get; set; } = new();
}

public class SriCredentialsSettings
{
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class OracleSettings
{
    public string Host { get; set; } = string.Empty;
    public string Port { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public static class AppSettingsLoader
{
    private const string FileName = "appsettings.Development.json";

    public static AppSettings Load()
    {
        var baseDir = AppContext.BaseDirectory;
        var path = Path.Combine(baseDir, FileName);

        if (!File.Exists(path))
            throw new FileNotFoundException($"No existe el archivo de configuración '{FileName}' en {baseDir}");

        var json = File.ReadAllText(path);
        var settings = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return settings ?? new AppSettings();
    }
}
