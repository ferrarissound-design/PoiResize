using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using PoiResize.Core.Models;

namespace PoiResize.Core.Persistence;

/// <summary>設定とプリセットの JSON 保存。壊れたファイルは無視して既定値に戻す。</summary>
public static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 日本語をそのまま書く
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true,
    };

    public static T? Load<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>一時ファイルに書いてから置き換える（書き込み中に落ちても壊れない）。</summary>
    public static void Save<T>(string path, T value)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, path, overwrite: true);
    }
}

/// <summary>アプリのデータ置き場（%APPDATA%\PoiResize）。</summary>
public sealed class AppDataStore
{
    public string Folder { get; }
    public string SettingsPath => Path.Combine(Folder, "settings.json");
    public string PresetsPath => Path.Combine(Folder, "presets.json");

    public AppDataStore(string? folder = null)
    {
        Folder = folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PoiResize");
    }

    public ProcessingSettings LoadSettings()
    {
        var s = JsonStore.Load<ProcessingSettings>(SettingsPath) ?? new ProcessingSettings();
        s.Normalize();
        return s;
    }

    public void SaveSettings(ProcessingSettings s) => JsonStore.Save(SettingsPath, s);

    public List<Preset> LoadPresets()
    {
        var list = JsonStore.Load<List<Preset>>(PresetsPath);
        if (list is null) return Preset.CreateDefaults().ToList();
        list.RemoveAll(p => string.IsNullOrWhiteSpace(p.Name) || p.Settings is null);
        return list;
    }

    public void SavePresets(IEnumerable<Preset> presets) => JsonStore.Save(PresetsPath, presets.ToList());
}
