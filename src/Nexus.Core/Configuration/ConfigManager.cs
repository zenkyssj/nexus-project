using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexus.Core.Configuration;

public class ConfigManager
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".nexus"
    );

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static bool ConfigExists() => File.Exists(ConfigPath);

    public static string GetConfigPath() => ConfigPath;

    public static string GetAgentPath() => Path.Combine(ConfigDir, "agent");

    public static NexusConfig Load()
    {
        if (!ConfigExists())
            throw new FileNotFoundException($"Config not found. Run 'nexus init' first.");

        var json = File.ReadAllText(ConfigPath);
        return JsonSerializer.Deserialize<NexusConfig>(json, JsonOptions)
               ?? throw new InvalidOperationException("Failed to parse config.");
    }

    public static void Save(NexusConfig config)
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }

    public static void Set(string key, string value)
    {
        var config = ConfigExists() ? Load() : new NexusConfig();

        switch (key.ToLower())
        {
            case "phone-number":
                config.AuthorizedNumber = value;
                break;
            case "claude-key":
                config.ClaudeApiKey = value;
                break;
            case "allowed-path":
                if (!config.AllowedPaths.Contains(value))
                    config.AllowedPaths.Add(value);
                break;
            case "session-timeout":
                if (int.TryParse(value, out var minutes))
                    config.SessionTimeoutMinutes = minutes;
                else
                    throw new ArgumentException("session-timeout must be a number (minutes).");
                break;
            default:
                throw new ArgumentException($"Unknown config key: '{key}'. Valid keys: phone-number, claude-key, allowed-path, session-timeout");
        }

        Save(config);
    }

    public static string GetBridgePath() => Path.Combine(ConfigDir, "bridge");

    public static string GetLogsPath() => Path.Combine(ConfigDir, "logs");

    public static string GetSessionsPath() => Path.Combine(ConfigDir, "sessions");
}
