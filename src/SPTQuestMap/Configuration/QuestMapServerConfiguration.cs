using System.Text.Json;
using System.Text.Json.Serialization;

namespace SPTQuestMap.Configuration;

public sealed record QuestMapServerConfiguration
{
    [JsonPropertyName("requireBrowserAuthentication")]
    public bool RequireBrowserAuthentication { get; init; } = true;

    internal static QuestMapServerConfiguration Load(string path, Action<string> warning)
    {
        try
        {
            if (!File.Exists(path))
            {
                var defaults = new QuestMapServerConfiguration();
                using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                JsonSerializer.Serialize(file, defaults, new JsonSerializerOptions { WriteIndented = true });
                return defaults;
            }

            return JsonSerializer.Deserialize<QuestMapServerConfiguration>(File.ReadAllText(path))
                ?? throw new JsonException("Expected a configuration object.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            warning($"SPT-QuestMap could not load '{path}'; browser authentication remains enabled. {exception.Message}");
            return new QuestMapServerConfiguration();
        }
    }
}
