using System.Text.Json;
using System.IO;
using FrontolFileAnalyzer.Core;

namespace FrontolFileAnalyzer;

internal sealed class AnalyzerSettings
{
    public bool ShowEmptyFields { get; set; } = true;
    public Dictionary<string, List<int>> HiddenFieldsByCommand { get; set; } = [];
    public Dictionary<string, double> RecordColumnWidths { get; set; } = [];
    public List<string> HiddenRecordColumns { get; set; } = [];
    public bool RecordColumnsInitialized { get; set; }
    public double RecordsPaneWidth { get; set; } = 655;
    public List<string> RecentFiles { get; set; } = [];
    public List<SalesReportHistoryEntry> SalesReportHistory { get; set; } = [];
}

internal sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FrontolFileAnalyzer",
        "settings.json");

    public AnalyzerSettings Load()
    {
        try
        {
            return File.Exists(_settingsPath)
                ? JsonSerializer.Deserialize<AnalyzerSettings>(File.ReadAllText(_settingsPath), JsonOptions) ?? new AnalyzerSettings()
                : new AnalyzerSettings();
        }
        catch
        {
            return new AnalyzerSettings();
        }
    }

    public void Save(AnalyzerSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // Настройки не должны мешать работе анализатора, если профиль пользователя недоступен.
        }
    }
}
