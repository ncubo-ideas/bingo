using System.Text.Json;

namespace BingoAppTests.Integration;

public sealed class EmulatorSettings
{
    public string Adb { get; private set; } = "adb";
    public string Serial { get; private set; } = "";
    public string CollaboratorSerial { get; private set; } = "";
    public string PlayerSerial { get; private set; } = "";
    public string Apk { get; private set; } = "";
    public string Package { get; private set; } = "com.ncubo.bingo";
    public string Environment { get; private set; } = "";

    public bool IsConfigured => Serial.Length > 0;

    public static EmulatorSettings Load()
    {
        EmulatorSettings settings = new EmulatorSettings();

        foreach (string name in new[] { "appsettings.json", "appsettings.local.json" })
        {
            string path = Path.Combine(AppContext.BaseDirectory, name);
            if (!File.Exists(path)) continue;

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

            if (!doc.RootElement.TryGetProperty("Emulator", out JsonElement section)) continue;

            settings.Adb = Value(section, "Adb", settings.Adb);
            settings.Serial = Value(section, "Serial", settings.Serial);
            settings.CollaboratorSerial = Value(section, "CollaboratorSerial", settings.CollaboratorSerial);
            settings.PlayerSerial = Value(section, "PlayerSerial", settings.PlayerSerial);
            settings.Apk = Value(section, "Apk", settings.Apk);
            settings.Package = Value(section, "Package", settings.Package);
            settings.Environment = Value(section, "Environment", settings.Environment);
        }

        return settings;
    }

    private static string Value(JsonElement section, string name, string otherwise)
    {
        if (!section.TryGetProperty(name, out JsonElement value)) return otherwise;
        string text = value.GetString()?.Trim() ?? "";
        return text.Length > 0 ? text : otherwise;
    }
}
