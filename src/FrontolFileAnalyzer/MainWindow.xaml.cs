using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrontolFileAnalyzer.Core;
using Microsoft.Win32;

namespace FrontolFileAnalyzer;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly FrontolFileParser _parser = new();
    private readonly AppSettingsStore _settingsStore = new();
    private readonly AnalyzerSettings _settings;
    private readonly ObservableCollection<AnalyzedField> _visibleFields = [];
    private readonly ObservableCollection<MarkingFilterOption> _markingFilters = [];
    private readonly ObservableCollection<CommandFilterOption> _commandFilters = [];
    private readonly List<RecordColumnState> _recordColumns = [];
    private readonly HashSet<string> _selectedMarkingCodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> _modifiedLineNumbers = [];
    private readonly DispatcherTimer _searchTimer;
    private List<string> _workingLines = [];
    private List<string> _originalLines = [];
    private ObservableCollection<ParsedRecord> _records = [];
    private ICollectionView _recordsView = null!;
    private string _filePath = "Файл не выбран";
    private string _encodingLabel = "Кодировка: —";
    private string _recordCountLabel = "Строк: 0";
    private string _problemCountLabel = "Замечаний: 0";
    private string _statusMessage = "Откройте или перетащите файл обмена Frontol";
    private string _fieldVisibilityLabel = "Поля не выбраны";
    private string _loadProgressText = string.Empty;
    private string _selectedCellText = string.Empty;
    private string _encodingName = "UTF-8";
    private string _newLine = Environment.NewLine;
    private ParsedRecord? _selectedRecord;
    private AnalyzedField? _selectedField;
    private CommandFilterOption? _selectedCommandFilter;
    private string _searchText = string.Empty;
    private int _filterIndex;
    private int _loadProgressValue;
    private bool _loadProgressIsIndeterminate;
    private bool _isLoading;
    private bool _showEmptyFields;
    private bool _hasTrailingNewLine;
    private bool _isDirty;
    private bool _adjustingRecordColumns;
    private Encoding _sourceEncoding = new UTF8Encoding(false);
    private string _markingFilterLabel = "Все виды";
    private string? _loadedFileHash;
    private GridLength _recordsPaneWidth = new(655);
    private bool _recordsPaneCollapsedByLayout;

    public MainWindow()
    {
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            RecordsView.Refresh();
            SelectFirstVisibleRecord();
        };
        _settings = _settingsStore.Load();
        _showEmptyFields = _settings.ShowEmptyFields;
        ReplaceRecordsView(_records);
        InitializeComponent();
        InitializeRecordColumns();
        _recordsPaneWidth = new GridLength(Math.Max(360, _settings.RecordsPaneWidth));
        RecordsColumn.Width = _recordsPaneWidth;
        DataContext = this;
        RebuildRecentFilesMenu();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICollectionView RecordsView
    {
        get => _recordsView;
        private set => SetField(ref _recordsView, value);
    }

    public ObservableCollection<AnalyzedField> VisibleFields => _visibleFields;
    public ObservableCollection<MarkingFilterOption> MarkingFilters => _markingFilters;
    public ObservableCollection<CommandFilterOption> CommandFilters => _commandFilters;
    public string VersionLabel => ApplicationInfo.VersionLabel;

    public string SearchTextQuery => _searchText;

    public string SelectedCellText
    {
        get => _selectedCellText;
        private set => SetField(ref _selectedCellText, value);
    }

    public CommandFilterOption? SelectedCommandFilter
    {
        get => _selectedCommandFilter;
        set
        {
            if (SetField(ref _selectedCommandFilter, value))
            {
                RecordsView.Refresh();
                SelectFirstVisibleRecord();
            }
        }
    }

    public string MarkingFilterLabel
    {
        get => _markingFilterLabel;
        private set => SetField(ref _markingFilterLabel, value);
    }

    public string FilePath
    {
        get => _filePath;
        private set
        {
            if (SetField(ref _filePath, value))
            {
                OnPropertyChanged(nameof(FileDisplayName));
                OnPropertyChanged(nameof(HasCurrentFile));
                OnPropertyChanged(nameof(CanOpenCurrentFile));
            }
        }
    }

    public string FileDisplayName => Path.GetFileName(FilePath);

    public string EncodingLabel
    {
        get => _encodingLabel;
        private set => SetField(ref _encodingLabel, value);
    }

    public string RecordCountLabel
    {
        get => _recordCountLabel;
        private set => SetField(ref _recordCountLabel, value);
    }

    public string ProblemCountLabel
    {
        get => _problemCountLabel;
        private set => SetField(ref _problemCountLabel, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string FieldVisibilityLabel
    {
        get => _fieldVisibilityLabel;
        private set => SetField(ref _fieldVisibilityLabel, value);
    }

    public string LoadProgressText
    {
        get => _loadProgressText;
        private set => SetField(ref _loadProgressText, value);
    }

    public int LoadProgressValue
    {
        get => _loadProgressValue;
        private set => SetField(ref _loadProgressValue, value);
    }

    public bool LoadProgressIsIndeterminate
    {
        get => _loadProgressIsIndeterminate;
        private set => SetField(ref _loadProgressIsIndeterminate, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetField(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(CanStartLoad));
                OnPropertyChanged(nameof(CanOpenCurrentFile));
                OnPropertyChanged(nameof(CanConfigureFields));
                OnPropertyChanged(nameof(CanEditSelectedField));
                OnPropertyChanged(nameof(CanSaveFile));
                OnPropertyChanged(nameof(CanSaveChanges));
            }
        }
    }

    public bool ShowEmptyFields
    {
        get => _showEmptyFields;
        set
        {
            if (!SetField(ref _showEmptyFields, value))
            {
                return;
            }

            _settings.ShowEmptyFields = value;
            _settingsStore.Save(_settings);
            RefreshVisibleFields();
        }
    }

    public ParsedRecord? SelectedRecord
    {
        get => _selectedRecord;
        set
        {
            if (SetField(ref _selectedRecord, value))
            {
                RefreshVisibleFields();
                OnPropertyChanged(nameof(CanConfigureFields));
                OnPropertyChanged(nameof(CanEditSelectedField));
            }
        }
    }

    public AnalyzedField? SelectedField
    {
        get => _selectedField;
        set
        {
            if (SetField(ref _selectedField, value))
            {
                SelectedCellText = value?.RawValue ?? string.Empty;
                OnPropertyChanged(nameof(CanEditSelectedField));
            }
        }
    }

    public bool HasCurrentFile => File.Exists(FilePath);
    public bool HasLoadedRecords => _records.Count > 0;
    public bool CanStartLoad => !IsLoading;
    public bool CanOpenCurrentFile => !IsLoading && HasCurrentFile;
    public bool CanConfigureFields => !IsLoading && SelectedRecord?.Fields.Count > 0;
    public bool CanEditSelectedField => !IsLoading && SelectedRecord?.Kind == FrontolRecordKind.Data && SelectedField is not null;
    public bool CanSaveFile => !IsLoading && _workingLines.Count > 0;
    public bool CanSaveChanges => CanSaveFile && _isDirty;

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        ApplyRequestedWindowSize(this, arguments, "--window-size");
        var aboutScreenshotIndex = Array.FindIndex(arguments, argument => argument.Equals("--screenshot-about", StringComparison.OrdinalIgnoreCase));
        if (aboutScreenshotIndex >= 0 && arguments.Length > aboutScreenshotIndex + 1)
        {
            var window = new AboutWindow { Owner = this };
            ApplyRequestedWindowSize(window, arguments, "--dialog-size");
            window.Show();
            await Dispatcher.InvokeAsync(window.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            SaveWindowScreenshot(window, arguments[aboutScreenshotIndex + 1]);
            window.Close();
            Close();
            return;
        }

        var markingScreenshotIndex = Array.FindIndex(arguments, argument => argument.Equals("--screenshot-marking", StringComparison.OrdinalIgnoreCase));
        if (markingScreenshotIndex >= 0 && arguments.Length > markingScreenshotIndex + 1)
        {
            var window = new MarkingCodesWindow { Owner = this };
            ApplyRequestedWindowSize(window, arguments, "--dialog-size");
            window.Show();
            await Dispatcher.InvokeAsync(window.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            SaveWindowScreenshot(window, arguments[markingScreenshotIndex + 1]);
            window.Close();
            Close();
            return;
        }

        var commandsScreenshotIndex = Array.FindIndex(arguments, argument => argument.Equals("--screenshot-commands", StringComparison.OrdinalIgnoreCase));
        if (commandsScreenshotIndex >= 0 && arguments.Length > commandsScreenshotIndex + 1)
        {
            var window = new CommandReferenceWindow { Owner = this };
            ApplyRequestedWindowSize(window, arguments, "--dialog-size");
            window.Show();
            await Dispatcher.InvokeAsync(window.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            SaveWindowScreenshot(window, arguments[commandsScreenshotIndex + 1]);
            window.Close();
            Close();
            return;
        }

        var fieldsScreenshotIndex = Array.FindIndex(arguments, argument => argument.Equals("--screenshot-fields", StringComparison.OrdinalIgnoreCase));
        if (fieldsScreenshotIndex >= 0 && arguments.Length > fieldsScreenshotIndex + 2)
        {
            await LoadFileAsync(arguments[fieldsScreenshotIndex + 2]);
            if (SelectedRecord is not null)
            {
                var commandName = SelectedRecord.CommandName ?? "UNKNOWN";
                var window = new FieldVisibilityWindow(commandName, SelectedRecord.Fields, GetHiddenFields(commandName)) { Owner = this };
                ApplyRequestedWindowSize(window, arguments, "--dialog-size");
                window.Show();
                await Dispatcher.InvokeAsync(window.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                SaveWindowScreenshot(window, arguments[fieldsScreenshotIndex + 1]);
                window.Close();
            }

            Close();
            return;
        }

        var statisticsScreenshotIndex = Array.FindIndex(arguments, argument => argument.Equals("--screenshot-statistics", StringComparison.OrdinalIgnoreCase));
        if (statisticsScreenshotIndex >= 0 && arguments.Length > statisticsScreenshotIndex + 2)
        {
            await LoadFileAsync(arguments[statisticsScreenshotIndex + 2]);
            var window = new StatisticsWindow(_records, _modifiedLineNumbers.Count) { Owner = this };
            ApplyRequestedWindowSize(window, arguments, "--dialog-size");
            window.Show();
            await Dispatcher.InvokeAsync(window.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            SaveWindowScreenshot(window, arguments[statisticsScreenshotIndex + 1]);
            window.Close();
            Close();
            return;
        }

        var multiFilterScreenshotIndex = Array.FindIndex(arguments, argument => argument.Equals("--screenshot-marking-filter", StringComparison.OrdinalIgnoreCase));
        if (multiFilterScreenshotIndex >= 0 && arguments.Length > multiFilterScreenshotIndex + 2)
        {
            await LoadFileAsync(arguments[multiFilterScreenshotIndex + 2]);
            var options = _markingFilters.Where(option => option.Code is not null)
                .Select(option => new MarkingMultiFilterOption(option.Code!, option.Name, option.Count));
            var window = new MarkingMultiFilterWindow(options, _selectedMarkingCodes) { Owner = this };
            ApplyRequestedWindowSize(window, arguments, "--dialog-size");
            window.Show();
            await Dispatcher.InvokeAsync(window.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            SaveWindowScreenshot(window, arguments[multiFilterScreenshotIndex + 1]);
            window.Close();
            Close();
            return;
        }

        var fieldEditorScreenshotIndex = Array.FindIndex(arguments, argument => argument.Equals("--screenshot-field-editor", StringComparison.OrdinalIgnoreCase));
        if (fieldEditorScreenshotIndex >= 0 && arguments.Length > fieldEditorScreenshotIndex + 3 &&
            int.TryParse(arguments[fieldEditorScreenshotIndex + 3], out var requestedFieldNumber))
        {
            await LoadFileAsync(arguments[fieldEditorScreenshotIndex + 2]);
            var record = _records.FirstOrDefault(item => item.IsProductRecord);
            var field = record?.Fields.FirstOrDefault(item => item.Number == requestedFieldNumber);
            if (record is not null && field is not null)
            {
                var values = record.RawText.Split(';', StringSplitOptions.None);
                var definition = record.Definition?.ResolveFields(values).FirstOrDefault(item => item.Number == field.Number);
                var window = new FieldEditWindow(record.LineNumber, field, definition) { Owner = this };
                ApplyRequestedWindowSize(window, arguments, "--dialog-size");
                window.Show();
                await Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
                SaveWindowScreenshot(window, arguments[fieldEditorScreenshotIndex + 1]);
                window.Close();
            }
            Close();
            return;
        }

        var bulkEditorScreenshotIndex = Array.FindIndex(arguments, argument => argument.Equals("--screenshot-bulk-marking", StringComparison.OrdinalIgnoreCase));
        if (bulkEditorScreenshotIndex >= 0 && arguments.Length > bulkEditorScreenshotIndex + 2)
        {
            await LoadFileAsync(arguments[bulkEditorScreenshotIndex + 2]);
            var products = _records.Where(record => record.IsProductRecord).ToArray();
            var sources = products
                .GroupBy(record => record.ProductTypeCode ?? "0", StringComparer.OrdinalIgnoreCase)
                .Select(group => new BulkMarkingSourceOption(
                    group.Key,
                    FrontolReferenceCatalog.ProductTypeValues.TryGetValue(group.Key, out var name) ? name : $"Неизвестный код {group.Key}",
                    group.Count()));
            var targets = FrontolReferenceCatalog.ProductTypeValues.Select(pair => new BulkMarkingTargetOption(pair.Key, pair.Value));
            var window = new BulkMarkingEditWindow(sources, targets) { Owner = this };
            ApplyRequestedWindowSize(window, arguments, "--dialog-size");
            window.Show();
            await Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
            SaveWindowScreenshot(window, arguments[bulkEditorScreenshotIndex + 1]);
            window.Close();
            Close();
            return;
        }

        var screenshotIndex = Array.FindIndex(arguments, argument => argument.Equals("--screenshot", StringComparison.OrdinalIgnoreCase));
        if (screenshotIndex >= 0 && arguments.Length > screenshotIndex + 2)
        {
            var screenshotPath = arguments[screenshotIndex + 1];
            var inputPath = arguments[screenshotIndex + 2];
            await LoadFileAsync(inputPath);
            var markingFilterIndex = Array.FindIndex(arguments, argument =>
                argument.Equals("--marking-filter", StringComparison.OrdinalIgnoreCase));
            if (markingFilterIndex >= 0 && arguments.Length > markingFilterIndex + 1)
            {
                var requestedCode = arguments[markingFilterIndex + 1];
                _selectedMarkingCodes.Clear();
                if (MarkingFilters.Any(option => string.Equals(option.Code, requestedCode, StringComparison.OrdinalIgnoreCase)))
                {
                    _selectedMarkingCodes.Add(requestedCode);
                }
                UpdateMarkingFilterLabel();
                RecordsView.Refresh();
            }
            var searchIndex = Array.FindIndex(arguments, argument =>
                argument.Equals("--search", StringComparison.OrdinalIgnoreCase));
            if (searchIndex >= 0 && arguments.Length > searchIndex + 1)
            {
                SearchBox.Text = arguments[searchIndex + 1];
                ApplySearchFilterNow();
            }
            if (arguments.Any(argument => argument.Equals("--collapse-records", StringComparison.OrdinalIgnoreCase)))
            {
                SetRecordsPaneVisibility(false);
            }
            await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            SaveScreenshot(screenshotPath);
            Close();
            return;
        }

        var argument = ResolveStartupFilePath(arguments);
        if (argument is not null)
        {
            await LoadFileAsync(argument);
        }
    }

    private static string? ResolveStartupFilePath(IReadOnlyList<string> arguments)
    {
        for (var length = arguments.Count; length > 0; length--)
        {
            for (var start = 0; start + length <= arguments.Count; start++)
            {
                var candidate = string.Join(' ', arguments.Skip(start).Take(length)).Trim().Trim('"');
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        return null;
    }

    private async void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (IsLoading || !ConfirmDiscardChanges())
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Выберите файл обмена Frontol",
            Filter = "Файлы обмена (*.txt;*.$*;*)|*.txt;*.$*;*|Все файлы (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            await LoadFileAsync(dialog.FileName);
        }
    }

    private async void SaveFile_Click(object sender, RoutedEventArgs e)
    {
        if (!CanSaveFile)
        {
            return;
        }

        if (!CanSaveChanges)
        {
            StatusMessage = "Несохранённых изменений нет";
            return;
        }

        if (HasCurrentFile)
        {
            await SaveWorkingFileAsync(FilePath);
            return;
        }

        SaveFileAs_Click(sender, e);
    }

    private async void SaveFileAs_Click(object sender, RoutedEventArgs e)
    {
        if (!CanSaveFile)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Сохранить файл обмена Frontol",
            Filter = "Текстовый файл (*.txt)|*.txt|Все файлы (*.*)|*.*",
            FileName = HasCurrentFile ? Path.GetFileName(FilePath) : "base.txt",
            InitialDirectory = HasCurrentFile ? Path.GetDirectoryName(FilePath) : null,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            await SaveWorkingFileAsync(dialog.FileName);
        }
    }

    private async Task SaveWorkingFileAsync(string targetPath)
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Сохранение файла…";
            var fullTargetPath = Path.GetFullPath(targetPath);
            if (File.Exists(fullTargetPath) &&
                string.Equals(fullTargetPath, Path.GetFullPath(FilePath), StringComparison.OrdinalIgnoreCase) &&
                _loadedFileHash is not null)
            {
                var currentHash = await Task.Run(() => ComputeFileHash(fullTargetPath));
                if (!string.Equals(currentHash, _loadedFileHash, StringComparison.Ordinal))
                {
                    IsLoading = false;
                    var overwrite = MessageBox.Show(this,
                        "Исходный файл был изменён другой программой после его открытия.\n\nПродолжить и заменить файл? Текущая внешняя версия будет сохранена в .bak.",
                        "Файл изменён извне", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (overwrite != MessageBoxResult.Yes)
                    {
                        StatusMessage = "Сохранение отменено: файл изменён извне";
                        return;
                    }
                    IsLoading = true;
                }
            }

            var text = string.Join(_newLine, _workingLines) + (_hasTrailingNewLine ? _newLine : string.Empty);
            var targetDirectory = Path.GetDirectoryName(fullTargetPath)!;
            Directory.CreateDirectory(targetDirectory);
            var tempPath = Path.Combine(targetDirectory, $".{Path.GetFileName(fullTargetPath)}.{Guid.NewGuid():N}.tmp");
            var backupPath = fullTargetPath + ".bak";

            await Task.Run(() => File.WriteAllText(tempPath, text, _sourceEncoding));
            var writtenHash = await Task.Run(() => ComputeFileHash(tempPath));
            try
            {
                if (File.Exists(fullTargetPath))
                {
                    File.Replace(tempPath, fullTargetPath, backupPath, true);
                }
                else
                {
                    File.Move(tempPath, fullTargetPath);
                }
            }
            catch
            {
                if (File.Exists(fullTargetPath))
                {
                    File.Copy(fullTargetPath, backupPath, true);
                }
                File.Copy(tempPath, fullTargetPath, true);
                File.Delete(tempPath);
            }

            FilePath = fullTargetPath;
            _loadedFileHash = writtenHash;
            AddRecentFile(fullTargetPath);
            _modifiedLineNumbers.Clear();
            _originalLines = _workingLines.ToList();
            foreach (var record in _records)
            {
                record.IsModified = false;
            }
            RecordsView.Refresh();
            SetDirty(false);
            StatusMessage = File.Exists(backupPath)
                ? $"Файл сохранён. Резервная копия: {backupPath}"
                : $"Файл сохранён: {fullTargetPath}";
        }
        catch (Exception exception)
        {
            StatusMessage = "Не удалось сохранить файл";
            MessageBox.Show(this, exception.Message, "Ошибка сохранения", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OpenSourceFile_Click(object sender, RoutedEventArgs e)
    {
        if (!HasCurrentFile)
        {
            return;
        }

        TryStart(new ProcessStartInfo(FilePath) { UseShellExecute = true }, "Не удалось открыть исходный файл");
    }

    private void ShowInFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!HasCurrentFile)
        {
            return;
        }

        TryStart(
            new ProcessStartInfo("explorer.exe", $"/select,\"{FilePath}\"") { UseShellExecute = true },
            "Не удалось открыть папку с файлом");
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!HasLoadedRecords)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Экспорт видимых строк",
            Filter = "CSV (*.csv)|*.csv|Текстовый файл (*.txt)|*.txt",
            FileName = $"Frontol-анализ-{DateTime.Now:yyyyMMdd-HHmm}.csv"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var rows = RecordsView.Cast<ParsedRecord>().ToArray();
            var builder = new StringBuilder();
            builder.AppendLine("Строка;Раздел;Команда;Код;Наименование;Цена;Штрихкод;Маркировка;Статус;Исходная строка");
            foreach (var record in rows)
            {
                builder.AppendLine(string.Join(';', new[]
                {
                    record.LineNumber.ToString(), GroupDisplay(record.SectionGroup), record.CommandText,
                    record.CodeText, record.ContentText, record.PriceText, record.BarcodeText,
                    record.ProductTypeText, record.StatusText, record.RawText
                }.Select(Csv)));
            }

            await File.WriteAllTextAsync(dialog.FileName, builder.ToString(), new UTF8Encoding(true));
            StatusMessage = $"Экспортировано строк: {rows.Length:N0}";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Ошибка экспорта", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Compare_Click(object sender, RoutedEventArgs e)
    {
        if (!HasLoadedRecords)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Выберите файл для сравнения",
            Filter = "Файлы обмена (*.txt;*.$*;*)|*.txt;*.$*;*|Все файлы (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "Сравнение файлов…";
            var other = await Task.Run(() => _parser.ParseFile(dialog.FileName));
            var currentMap = ProductMap(_records);
            var otherMap = ProductMap(other.Records);
            var rows = BuildComparison(currentMap, otherMap);
            new CompareWindow(rows) { Owner = this }.ShowDialog();
            StatusMessage = $"Сравнение завершено: различий {rows.Count:N0}";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Ошибка сравнения", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void TryStart(ProcessStartInfo startInfo, string errorTitle)
    {
        try
        {
            Process.Start(startInfo);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, errorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ConfigureFields_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRecord is null || SelectedRecord.Fields.Count == 0)
        {
            return;
        }

        var commandName = SelectedRecord.CommandName ?? "UNKNOWN";
        var hidden = GetHiddenFields(commandName);
        var dialog = new FieldVisibilityWindow(commandName, SelectedRecord.Fields, hidden) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _settings.HiddenFieldsByCommand[NormalizeCommand(commandName)] = dialog.HiddenNumbers.Order().ToList();
        _settingsStore.Save(_settings);
        RefreshVisibleFields();
    }

    private void ConfigureRecordColumns_Click(object sender, RoutedEventArgs e)
    {
        var visibleKeys = RecordsGridView.Columns
            .Select(column => _recordColumns.First(state => ReferenceEquals(state.Column, column)).Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dialog = new RecordColumnsWindow(_recordColumns.Select(state =>
            new RecordColumnOption(state.Key, state.Header, visibleKeys.Contains(state.Key))))
        { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _settings.HiddenRecordColumns = dialog.Options.Where(option => !option.IsVisible).Select(option => option.Key).ToList();
        if (_settings.HiddenRecordColumns.Count == _recordColumns.Count)
        {
            _settings.HiddenRecordColumns.Remove("name");
        }

        if (dialog.ResetWidths)
        {
            _settings.RecordColumnWidths.Clear();
            foreach (var state in _recordColumns)
            {
                state.Column.Width = state.DefaultWidth;
            }
        }

        ApplyRecordColumnVisibility();
        FitRecordColumnsToWidth();
        SaveInterfaceSettings();
    }

    private void ConfigureMarkingFilter_Click(object sender, RoutedEventArgs e)
    {
        var options = _markingFilters
            .Where(option => option.Code is not null)
            .Select(option => new MarkingMultiFilterOption(option.Code!, option.Name, option.Count))
            .ToArray();
        var dialog = new MarkingMultiFilterWindow(options, _selectedMarkingCodes) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _selectedMarkingCodes.Clear();
        if (dialog.SelectedCodes.Count != options.Length)
        {
            foreach (var code in dialog.SelectedCodes)
            {
                _selectedMarkingCodes.Add(code);
            }
        }
        UpdateMarkingFilterLabel();
        RecordsView.Refresh();
        SelectFirstVisibleRecord();
    }

    private void Statistics_Click(object sender, RoutedEventArgs e)
    {
        if (HasLoadedRecords)
        {
            new StatisticsWindow(_records, _modifiedLineNumbers.Count) { Owner = this }.ShowDialog();
        }
    }

    private void GoToLine_Click(object sender, RoutedEventArgs e)
    {
        if (!HasLoadedRecords)
        {
            return;
        }

        var dialog = new GoToLineWindow(_records.Count, SelectedRecord?.LineNumber ?? 1) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        var lineNumber = dialog.LineNumber;

        var record = _records.FirstOrDefault(item => item.LineNumber == lineNumber);
        if (record is null)
        {
            StatusMessage = $"Строка {lineNumber:N0} отсутствует";
            return;
        }

        SearchBox.Text = string.Empty;
        SectionFilterBox.SelectedIndex = 0;
        SeverityFilterBox.SelectedIndex = 0;
        SelectedCommandFilter = _commandFilters.FirstOrDefault();
        _selectedMarkingCodes.Clear();
        UpdateMarkingFilterLabel();
        RecordsView.Refresh();
        SelectedRecord = record;
        Dispatcher.BeginInvoke(() =>
        {
            ExpandGroupExpanders(RecordsList);
            RecordsList.ScrollIntoView(record);
            RecordsList.Focus();
        }, System.Windows.Threading.DispatcherPriority.Background);
        StatusMessage = $"Переход к физической строке {lineNumber:N0}";
    }

    private static void ExpandGroupExpanders(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is Expander expander)
            {
                expander.IsExpanded = true;
            }
            ExpandGroupExpanders(child);
        }
    }

    private void MarkingCodes_Click(object sender, RoutedEventArgs e) =>
        new MarkingCodesWindow { Owner = this }.ShowDialog();

    private void CommandReference_Click(object sender, RoutedEventArgs e) =>
        new CommandReferenceWindow { Owner = this }.ShowDialog();

    private void About_Click(object sender, RoutedEventArgs e) =>
        new AboutWindow { Owner = this }.ShowDialog();

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.O && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            OpenFile_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }

        if (e.Key == Key.M &&
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
            Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            BulkEditMarking_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.M && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            ToggleRecordsPane_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }

        if (e.Key == Key.G && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            GoToLine_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }

        if (e.Key == Key.F2 && CanEditSelectedField)
        {
            EditField_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }

        if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                SaveFileAs_Click(sender, new RoutedEventArgs());
            }
            else
            {
                SaveFile_Click(sender, new RoutedEventArgs());
            }
            e.Handled = true;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (IsLoading)
        {
            e.Cancel = true;
            StatusMessage = "Дождитесь завершения текущей операции";
            return;
        }

        SaveInterfaceSettings();
        if (!_isDirty)
        {
            return;
        }

        var result = MessageBox.Show(this,
            "В файле есть несохранённые изменения. Сохранить их перед выходом?",
            "Несохранённые изменения", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Cancel)
        {
            e.Cancel = true;
        }
        else if (result == MessageBoxResult.Yes)
        {
            e.Cancel = true;
            SaveThenCloseAsync();
        }
        else
        {
            SetDirty(false);
        }
    }

    private async void SaveThenCloseAsync()
    {
        await SaveWorkingFileAsync(FilePath);
        if (!_isDirty)
        {
            Close();
        }
    }

    private void ToggleRecordsPane_Click(object sender, RoutedEventArgs e) =>
        SetRecordsPaneVisibility(RecordsPanel.Visibility != Visibility.Visible);

    private void SetRecordsPaneVisibility(bool isVisible)
    {
        if (!isVisible && RecordsColumn.Width.Value > 0)
        {
            _recordsPaneWidth = RecordsColumn.Width;
        }

        RecordsColumn.MinWidth = isVisible ? 300 : 0;
        RecordsColumn.Width = isVisible
            ? (_recordsPaneWidth.Value > 0 ? _recordsPaneWidth : new GridLength(655))
            : new GridLength(0);
        RecordsPanel.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        ToggleRecordsPaneButton.Content = isVisible ? "◀" : "▶";
        ToggleRecordsPaneButton.ToolTip = isVisible
            ? "Скрыть список строк (Ctrl+M)"
            : "Показать список строк (Ctrl+M)";
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        const double collapseBreakpoint = 1120;
        const double restoreBreakpoint = 1280;
        if (e.NewSize.Width < collapseBreakpoint && RecordsPanel.Visibility == Visibility.Visible)
        {
            _recordsPaneCollapsedByLayout = true;
            SetRecordsPaneVisibility(false);
        }
        else if (e.NewSize.Width > restoreBreakpoint && _recordsPaneCollapsedByLayout && RecordsPanel.Visibility != Visibility.Visible)
        {
            _recordsPaneCollapsedByLayout = false;
            SetRecordsPaneVisibility(true);
        }

        var showSecondaryDetails = e.NewSize.Height >= 560;
        if (FullCellTextPanel is not null)
        {
            FullCellTextPanel.Visibility = showSecondaryDetails ? Visibility.Visible : Visibility.Collapsed;
        }
        if (SelectedFieldDetailsPanel is not null)
        {
            SelectedFieldDetailsPanel.Visibility = showSecondaryDetails ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void InitializeRecordColumns()
    {
        _recordColumns.AddRange([
            new RecordColumnState("line", "Строка", LineColumn, 52, 42),
            new RecordColumnState("command", "Команда", CommandColumn, 108, 75),
            new RecordColumnState("code", "Код", CodeColumn, 88, 68),
            new RecordColumnState("name", "Наименование", NameColumn, 190, 115),
            new RecordColumnState("marking", "Вид маркировки", MarkingColumn, 160, 95),
            new RecordColumnState("status", "Статус", StatusColumn, 64, 58)
        ]);

        if (!_settings.RecordColumnsInitialized)
        {
            if (!_settings.HiddenRecordColumns.Contains("command", StringComparer.OrdinalIgnoreCase))
            {
                _settings.HiddenRecordColumns.Add("command");
            }
            _settings.RecordColumnsInitialized = true;
            _settingsStore.Save(_settings);
        }

        foreach (var state in _recordColumns)
        {
            if (_settings.RecordColumnWidths.TryGetValue(state.Key, out var width) && width >= state.MinimumWidth)
            {
                state.Column.Width = width;
            }
        }

        ApplyRecordColumnVisibility();
    }

    private void ApplyRecordColumnVisibility()
    {
        var hidden = _settings.HiddenRecordColumns.ToHashSet(StringComparer.OrdinalIgnoreCase);
        RecordsGridView.Columns.Clear();
        foreach (var state in _recordColumns.Where(state => !hidden.Contains(state.Key)))
        {
            RecordsGridView.Columns.Add(state.Column);
        }
    }

    private void RecordsList_SizeChanged(object sender, SizeChangedEventArgs e) => FitRecordColumnsToWidth();

    private void FitRecordColumnsToWidth()
    {
        if (_adjustingRecordColumns || RecordsGridView.Columns.Count == 0 || RecordsList.ActualWidth < 100)
        {
            return;
        }

        _adjustingRecordColumns = true;
        try
        {
            var visible = _recordColumns.Where(state => RecordsGridView.Columns.Contains(state.Column)).ToArray();
            var target = Math.Max(100, RecordsList.ActualWidth - 24);
            var current = visible.Sum(state => Math.Max(state.MinimumWidth, state.Column.ActualWidth > 0 ? state.Column.ActualWidth : state.Column.Width));
            if (current <= 0)
            {
                return;
            }

            var scale = target / current;
            foreach (var state in visible)
            {
                var width = state.Column.ActualWidth > 0 ? state.Column.ActualWidth : state.Column.Width;
                state.Column.Width = Math.Max(state.MinimumWidth, width * scale);
            }

            var difference = target - visible.Sum(state => state.Column.Width);
            var flexible = visible.FirstOrDefault(state => state.Key == "name") ?? visible[^1];
            flexible.Column.Width = Math.Max(flexible.MinimumWidth, flexible.Column.Width + difference);
        }
        finally
        {
            _adjustingRecordColumns = false;
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = !IsLoading && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!IsLoading && ConfirmDiscardChanges() && e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0 && File.Exists(files[0]))
        {
            await LoadFileAsync(files[0]);
        }
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = (sender as TextBox)?.Text?.Trim() ?? string.Empty;
        OnPropertyChanged(nameof(SearchTextQuery));
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void ApplySearchFilterNow()
    {
        _searchTimer.Stop();
        RecordsView.Refresh();
        SelectFirstVisibleRecord();
    }

    private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _filterIndex = SeverityFilterBox?.SelectedIndex ?? 0;
        RecordsView.Refresh();
        SelectFirstVisibleRecord();
    }

    private void ResetFilters_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        SectionFilterBox.SelectedIndex = 0;
        SeverityFilterBox.SelectedIndex = 0;
        SelectedCommandFilter = _commandFilters.FirstOrDefault();
        _selectedMarkingCodes.Clear();
        UpdateMarkingFilterLabel();
        RecordsView.Refresh();
        SelectFirstVisibleRecord();
    }

    private bool FilterRecord(object item)
    {
        if (item is not ParsedRecord record)
        {
            return false;
        }

        var matchesSearch = _searchText.Length == 0 || record.SearchText.Contains(_searchText, StringComparison.CurrentCultureIgnoreCase);
        if (!matchesSearch)
        {
            return false;
        }

        var sectionIndex = SectionFilterBox?.SelectedIndex ?? 0;
        var matchesSection = sectionIndex switch
        {
            1 => record.IsProductCommand,
            2 => !record.IsProductCommand && record.Kind is FrontolRecordKind.Command or FrontolRecordKind.Data,
            3 => record.Kind is FrontolRecordKind.Header or FrontolRecordKind.Comment or FrontolRecordKind.Empty,
            _ => true
        };
        if (!matchesSection)
        {
            return false;
        }

        if (SelectedCommandFilter?.CommandName is { } commandName &&
            !string.Equals(record.CommandName, commandName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (_selectedMarkingCodes.Count > 0 &&
            (record.ProductTypeCode is null || !_selectedMarkingCodes.Contains(record.ProductTypeCode)))
        {
            return false;
        }

        return _filterIndex switch
        {
            1 => record.Severity is IssueSeverity.Warning or IssueSeverity.Error,
            2 => record.Severity == IssueSeverity.Error,
            3 => record.Severity == IssueSeverity.None,
            _ => true
        };
    }

    private void RecordItem_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListViewItem item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    private void CopyRawLine_Click(object sender, RoutedEventArgs e) => CopyText(SelectedRecord?.RawText, "Исходная строка скопирована");
    private void CopyRecordCode_Click(object sender, RoutedEventArgs e) => CopyText(SelectedRecord?.CodeText, "Код скопирован");
    private void CopyRecordName_Click(object sender, RoutedEventArgs e) => CopyText(SelectedRecord?.ContentText, "Наименование скопировано");
    private void CopyFieldValue_Click(object sender, RoutedEventArgs e) => CopyText(SelectedField?.RawValue, "Значение поля скопировано");

    private void CopyFieldRow_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedField is { } field)
        {
            CopyText($"{field.Number};{field.Name};{field.RawValue};{field.Interpretation}", "Строка поля скопирована");
        }
    }

    private void FieldsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedField is not null)
        {
            CopyFieldValue_Click(sender, e);
            e.Handled = true;
        }
    }

    private void FieldsGrid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
    {
        if (FieldsGrid.SelectedCells.Count == 0)
        {
            return;
        }

        var cell = FieldsGrid.SelectedCells[0];
        if (!cell.IsValid || cell.Item is not AnalyzedField field)
        {
            return;
        }

        SelectedField = field;

        var header = cell.Column.Header?.ToString() ?? string.Empty;
        SelectedCellText = header switch
        {
            "№" => field.Number.ToString(),
            "Поле" => field.Name,
            "Значение" => field.RawValue,
            "Расшифровка" => field.Interpretation,
            "Проверка" => field.StatusText + (string.IsNullOrWhiteSpace(field.Diagnostic) ? string.Empty : $": {field.Diagnostic}"),
            _ => field.RawValue
        };
    }

    private void FieldsGrid_RightClick(object sender, MouseButtonEventArgs e)
    {
        var cell = FindVisualParent<DataGridCell>(e.OriginalSource as DependencyObject);
        if (cell?.DataContext is not AnalyzedField field)
        {
            return;
        }

        SelectedField = field;
        cell.Focus();
        FieldsGrid.SelectedCells.Clear();
        FieldsGrid.SelectedCells.Add(new DataGridCellInfo(field, cell.Column));
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match)
            {
                return match;
            }
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private async void BulkEditMarking_Click(object sender, RoutedEventArgs e)
    {
        if (IsLoading || !HasLoadedRecords)
        {
            return;
        }

        var products = _records.Where(record => record.IsProductRecord).ToArray();
        var sources = products
            .GroupBy(record => record.ProductTypeCode ?? "0", StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var name = FrontolReferenceCatalog.ProductTypeValues.TryGetValue(group.Key, out var known)
                    ? known
                    : $"Неизвестный код {group.Key}";
                return new BulkMarkingSourceOption(group.Key, name, group.Count());
            });
        var targets = FrontolReferenceCatalog.ProductTypeValues
            .Select(pair => new BulkMarkingTargetOption(pair.Key, pair.Value));
        var dialog = new BulkMarkingEditWindow(sources, targets) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var targetName = FrontolReferenceCatalog.ProductTypeValues.TryGetValue(dialog.TargetCode, out var knownTarget)
            ? knownTarget
            : "произвольное значение";
        var confirm = MessageBox.Show(this,
            $"Изменить поле 55 «Тип номенклатуры / маркировки» у {dialog.AffectedCount:N0} товаров?\n\nНовое значение: {dialog.TargetCode} — {targetName}\n\nИзменения можно проверить до сохранения. При сохранении поверх исходного файла будет создана .bak-копия.",
            "Подтверждение массовой замены", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        await ApplyBulkMarkingEditAsync(dialog.SourceCodes, dialog.TargetCode);
    }

    private async Task ApplyBulkMarkingEditAsync(HashSet<string> sourceCodes, string targetCode)
    {
        var matchingLines = _records
            .Where(record => record.IsProductRecord && sourceCodes.Contains(record.ProductTypeCode ?? "0"))
            .Select(record => record.LineNumber)
            .Distinct()
            .ToArray();
        if (matchingLines.Length == 0)
        {
            StatusMessage = "Нет товаров для массового изменения";
            return;
        }

        try
        {
            IsLoading = true;
            LoadProgressIsIndeterminate = true;
            LoadProgressText = $"Изменение {matchingLines.Length:N0} товаров";
            var updated = _workingLines.ToArray();
            foreach (var lineNumber in matchingLines)
            {
                var parts = updated[lineNumber - 1].Split(';', StringSplitOptions.None).ToList();
                while (parts.Count < 55)
                {
                    parts.Add(string.Empty);
                }
                parts[54] = targetCode;
                updated[lineNumber - 1] = string.Join(';', parts);
            }

            var document = await Task.Run(() => _parser.ParseLines(FilePath, updated, _encodingName));
            _workingLines = updated.ToList();
            foreach (var lineNumber in matchingLines)
            {
                if (lineNumber <= _originalLines.Count && string.Equals(updated[lineNumber - 1], _originalLines[lineNumber - 1], StringComparison.Ordinal))
                {
                    _modifiedLineNumbers.Remove(lineNumber);
                }
                else
                {
                    _modifiedLineNumbers.Add(lineNumber);
                }
            }
            var selectedLine = SelectedRecord?.LineNumber ?? matchingLines[0];
            ApplyParsedDocument(document, selectedLine, 55);
            SetDirty(_modifiedLineNumbers.Count > 0);
            StatusMessage = $"Вид маркировки изменён у {matchingLines.Length:N0} товаров. Файл пока не сохранён";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Ошибка массовой замены", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
            LoadProgressIsIndeterminate = false;
            LoadProgressText = string.Empty;
        }
    }

    private async void EditField_Click(object sender, RoutedEventArgs e)
    {
        if (IsLoading || SelectedRecord is not { Kind: FrontolRecordKind.Data } record || SelectedField is not { } field)
        {
            return;
        }

        var rawValues = record.RawText.Split(';', StringSplitOptions.None);
        var definition = record.Definition?.ResolveFields(rawValues)
            .FirstOrDefault(item => item.Number == field.Number);
        var dialog = new FieldEditWindow(record.LineNumber, field, definition) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Value == field.RawValue)
        {
            return;
        }

        if (dialog.Value.Contains(';') || dialog.Value.Contains('\r') || dialog.Value.Contains('\n'))
        {
            MessageBox.Show(this, "Значение одного поля не может содержать ';' или перевод строки.",
                "Недопустимое значение", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var parts = record.RawText.Split(';', StringSplitOptions.None).ToList();
        while (parts.Count < field.Number)
        {
            parts.Add(string.Empty);
        }
        parts[field.Number - 1] = dialog.Value;
        await ApplyLineEditAsync(record.LineNumber, string.Join(';', parts), field.Number);
    }

    private async void EditRawLine_Click(object sender, RoutedEventArgs e)
    {
        if (IsLoading || SelectedRecord is not { } record)
        {
            return;
        }

        var dialog = new TextEditWindow("Расширенное редактирование строки",
            $"Физическая строка {record.LineNumber:N0}. Точка с запятой разделяет поля; изменение структуры может повлиять на разбор всей команды.", record.RawText)
        { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Value == record.RawText)
        {
            return;
        }

        if (dialog.Value.Contains('\r') || dialog.Value.Contains('\n'))
        {
            MessageBox.Show(this, "В редакторе одной физической строки нельзя добавлять перевод строки.",
                "Недопустимое значение", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await ApplyLineEditAsync(record.LineNumber, dialog.Value, null);
    }

    private async Task ApplyLineEditAsync(int lineNumber, string newRawLine, int? fieldNumber)
    {
        if (lineNumber <= 0 || lineNumber > _workingLines.Count)
        {
            return;
        }

        try
        {
            IsLoading = true;
            var previousLine = _workingLines[lineNumber - 1];
            _workingLines[lineNumber - 1] = newRawLine;
            try
            {
                var snapshot = _workingLines.ToArray();
                var document = await Task.Run(() => _parser.ParseLines(FilePath, snapshot, _encodingName));
                if (lineNumber <= _originalLines.Count && string.Equals(newRawLine, _originalLines[lineNumber - 1], StringComparison.Ordinal))
                {
                    _modifiedLineNumbers.Remove(lineNumber);
                }
                else
                {
                    _modifiedLineNumbers.Add(lineNumber);
                }
                ApplyParsedDocument(document, lineNumber, fieldNumber);
                SetDirty(_modifiedLineNumbers.Count > 0);
                StatusMessage = $"Строка {lineNumber:N0} изменена и повторно проверена";
            }
            catch
            {
                _workingLines[lineNumber - 1] = previousLine;
                throw;
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Ошибка изменения строки", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void CopyText(string? value, string message)
    {
        if (string.IsNullOrEmpty(value))
        {
            StatusMessage = "Копировать нечего: значение пустое";
            return;
        }

        try
        {
            Clipboard.SetText(value);
            StatusMessage = message;
        }
        catch (Exception exception)
        {
            StatusMessage = $"Не удалось скопировать: {exception.Message}";
        }
    }

    private async Task LoadFileAsync(string path)
    {
        var requestedPath = path.Trim().Trim('"');

        try
        {
            IsLoading = true;
            LoadProgressIsIndeterminate = true;
            LoadProgressValue = 0;
            LoadProgressText = "Чтение файла";
            StatusMessage = "Чтение и анализ файла…";

            var progress = new Progress<FrontolParseProgress>(value =>
            {
                LoadProgressIsIndeterminate = value.TotalLines <= 0;
                LoadProgressValue = value.Percent;
                LoadProgressText = value.TotalLines <= 0
                    ? value.Stage
                    : $"{value.Stage}: {value.ProcessedLines:N0} из {value.TotalLines:N0} ({value.Percent}%)";
            });

            var sourceBytes = await File.ReadAllBytesAsync(requestedPath);
            var document = await Task.Run(() => _parser.ParseBytes(requestedPath, sourceBytes, progress));
            _loadedFileHash = Convert.ToHexString(SHA256.HashData(sourceBytes));
            _encodingName = document.EncodingName;
            _sourceEncoding = EncodingFor(document.EncodingName);
            var sourceText = DecodeForLayout(sourceBytes, _sourceEncoding);
            _newLine = DetectNewLine(sourceText);
            _hasTrailingNewLine = sourceText.EndsWith('\n') || sourceText.EndsWith('\r');
            _workingLines = document.Records.Select(record => record.RawText).ToList();
            _originalLines = _workingLines.ToList();
            _modifiedLineNumbers.Clear();

            LoadProgressIsIndeterminate = true;
            LoadProgressText = "Подготовка списка строк";
            _records = new ObservableCollection<ParsedRecord>(document.Records);
            ReplaceRecordsView(_records);
            RebuildMarkingFilters(document.Records);
            RebuildCommandFilters(document.Records);

            FilePath = document.FilePath;
            AddRecentFile(document.FilePath);
            EncodingLabel = $"Кодировка: {document.EncodingName}";
            RecordCountLabel = $"Строк: {document.Records.Count:N0} · данных: {document.DataRecordCount:N0}";
            ProblemCountLabel = $"Ошибок: {document.ErrorCount:N0} · предупреждений: {document.WarningCount:N0}";
            StatusMessage = $"Файл разобран: команд {document.CommandCount:N0}, строк данных {document.DataRecordCount:N0}";
            SelectedRecord = _records.FirstOrDefault(record =>
                                 record.Kind == FrontolRecordKind.Data &&
                                 record.CommandName?.Contains("QUANTITY", StringComparison.OrdinalIgnoreCase) == true)
                             ?? _records.FirstOrDefault(record => record.Kind == FrontolRecordKind.Data)
                             ?? _records.FirstOrDefault();
            SetDirty(false);
            OnPropertyChanged(nameof(HasLoadedRecords));
            OnPropertyChanged(nameof(CanSaveFile));
            if (SelectedRecord is not null)
            {
                await Dispatcher.InvokeAsync(
                    () => RecordsList.ScrollIntoView(SelectedRecord),
                    System.Windows.Threading.DispatcherPriority.Background);
            }
        }
        catch (Exception exception)
        {
            ResetLoadProgress();

            var displayPath = GetDisplayPath(requestedPath);
            var reason = exception switch
            {
                FileNotFoundException or DirectoryNotFoundException => "Файл не найден",
                UnauthorizedAccessException => "Нет доступа к файлу",
                IOException => "Не удалось прочитать файл",
                _ => "Не удалось открыть файл"
            };

            StatusMessage = $"{reason}. Выберите файл заново";
            MessageBox.Show(
                this,
                $"{reason}:\n{displayPath}\n\nНажмите «Открыть» и выберите файл заново.",
                "Не удалось открыть файл",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            ResetLoadProgress();
        }
    }

    private void ResetLoadProgress()
    {
        IsLoading = false;
        LoadProgressIsIndeterminate = false;
        LoadProgressValue = 0;
        LoadProgressText = string.Empty;
    }

    private static string GetDisplayPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception) when (string.IsNullOrWhiteSpace(path))
        {
            return "Путь не указан";
        }
        catch (Exception)
        {
            return path;
        }
    }

    private void ReplaceRecordsView(ObservableCollection<ParsedRecord> records)
    {
        var view = CollectionViewSource.GetDefaultView(records);
        view.Filter = FilterRecord;
        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new SortDescription(nameof(ParsedRecord.SectionGroup), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(ParsedRecord.LineNumber), ListSortDirection.Ascending));
        view.GroupDescriptions.Clear();
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ParsedRecord.SectionGroup)));
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ParsedRecord.CommandGroup)));
        RecordsView = view;
    }

    private void ApplyParsedDocument(AnalysisDocument document, int selectedLine, int? selectedFieldNumber)
    {
        foreach (var record in document.Records.Where(record => _modifiedLineNumbers.Contains(record.LineNumber)))
        {
            record.IsModified = true;
        }
        _records = new ObservableCollection<ParsedRecord>(document.Records);
        ReplaceRecordsView(_records);
        RebuildMarkingFilters(document.Records);
        RebuildCommandFilters(document.Records);
        RecordCountLabel = $"Строк: {document.Records.Count:N0} · данных: {document.DataRecordCount:N0}";
        ProblemCountLabel = $"Ошибок: {document.ErrorCount:N0} · предупреждений: {document.WarningCount:N0}";
        SelectedRecord = _records.FirstOrDefault(record => record.LineNumber == selectedLine) ?? _records.FirstOrDefault();
        if (selectedFieldNumber is { } number)
        {
            SelectedField = _visibleFields.FirstOrDefault(field => field.Number == number) ?? _visibleFields.FirstOrDefault();
        }

        OnPropertyChanged(nameof(HasLoadedRecords));
        OnPropertyChanged(nameof(CanSaveFile));
    }

    private void RebuildCommandFilters(IReadOnlyList<ParsedRecord> records)
    {
        var previous = SelectedCommandFilter?.CommandName;
        var options = records
            .Where(record => !string.IsNullOrWhiteSpace(record.CommandName))
            .GroupBy(record => record.CommandName!, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var definition = group.Select(record => record.Definition).FirstOrDefault(item => item is not null);
                return new CommandFilterOption(
                    group.Key,
                    definition?.DisplayName ?? "Назначение не описано",
                    definition?.Description ?? "Для этой команды пока отсутствует встроенное описание.",
                    group.Count());
            })
            .OrderBy(option => option.CommandName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _commandFilters.Clear();
        _commandFilters.Add(new CommandFilterOption(null, "Все команды", "Не ограничивать строки по команде.", records.Count));
        foreach (var option in options)
        {
            _commandFilters.Add(option);
        }

        SelectedCommandFilter = previous is null
            ? _commandFilters[0]
            : _commandFilters.FirstOrDefault(option => string.Equals(option.CommandName, previous, StringComparison.OrdinalIgnoreCase))
              ?? _commandFilters[0];
    }

    private void RebuildMarkingFilters(IReadOnlyList<ParsedRecord> records)
    {
        var previousCodes = _selectedMarkingCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var productRecords = records.Where(record => record.IsProductRecord).ToArray();
        var options = productRecords
            .GroupBy(record => record.ProductTypeCode ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var code = group.Key;
                var name = FrontolReferenceCatalog.ProductTypeValues.TryGetValue(code, out var knownName)
                    ? knownName
                    : $"Неизвестный код {code}";
                return new MarkingFilterOption(code, name, group.Count());
            })
            .OrderBy(option => int.TryParse(option.Code, out var number) ? number : int.MaxValue)
            .ThenBy(option => option.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        _markingFilters.Clear();
        _markingFilters.Add(new MarkingFilterOption(null, "Все виды маркировки", productRecords.Length));
        foreach (var option in options)
        {
            _markingFilters.Add(option);
        }

        _selectedMarkingCodes.Clear();
        foreach (var code in previousCodes.Where(code => options.Any(option => string.Equals(option.Code, code, StringComparison.OrdinalIgnoreCase))))
        {
            _selectedMarkingCodes.Add(code);
        }
        UpdateMarkingFilterLabel();
    }

    private void UpdateMarkingFilterLabel()
    {
        MarkingFilterLabel = _selectedMarkingCodes.Count switch
        {
            0 => "Все виды",
            1 => _markingFilters.FirstOrDefault(option => _selectedMarkingCodes.Contains(option.Code ?? string.Empty))?.Name ?? "Выбран 1 вид",
            _ => $"Выбрано видов: {_selectedMarkingCodes.Count}"
        };
    }

    private void SelectFirstVisibleRecord()
    {
        if (SelectedRecord is not null && RecordsView.Contains(SelectedRecord))
        {
            return;
        }

        SelectedRecord = RecordsView.Cast<ParsedRecord>().FirstOrDefault();
    }

    private void RefreshVisibleFields()
    {
        var previousNumber = SelectedField?.Number;
        _visibleFields.Clear();

        if (SelectedRecord is null)
        {
            FieldVisibilityLabel = "Поля не выбраны";
            SelectedField = null;
            return;
        }

        var hidden = GetHiddenFields(SelectedRecord.CommandName ?? "UNKNOWN");
        foreach (var field in SelectedRecord.Fields)
        {
            if (hidden.Contains(field.Number))
            {
                continue;
            }

            if (!ShowEmptyFields && string.IsNullOrEmpty(field.RawValue))
            {
                continue;
            }

            _visibleFields.Add(field);
        }

        FieldVisibilityLabel = $"Показано полей: {_visibleFields.Count} из {SelectedRecord.Fields.Count}";
        SelectedField = _visibleFields.FirstOrDefault(field => field.Number == previousNumber) ?? _visibleFields.FirstOrDefault();
    }

    private HashSet<int> GetHiddenFields(string commandName)
    {
        var key = NormalizeCommand(commandName);
        return _settings.HiddenFieldsByCommand.TryGetValue(key, out var hidden) ? hidden.ToHashSet() : [];
    }

    private static string NormalizeCommand(string commandName) => commandName.Trim().TrimStart('$').ToUpperInvariant();

    private void SaveInterfaceSettings()
    {
        foreach (var state in _recordColumns)
        {
            if (state.Column.Width >= state.MinimumWidth)
            {
                _settings.RecordColumnWidths[state.Key] = state.Column.Width;
            }
        }

        if (RecordsColumn.Width.Value > 0)
        {
            _settings.RecordsPaneWidth = RecordsColumn.Width.Value;
        }
        else if (_recordsPaneWidth.Value > 0)
        {
            _settings.RecordsPaneWidth = _recordsPaneWidth.Value;
        }
        _settingsStore.Save(_settings);
    }

    private void AddRecentFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        _settings.RecentFiles.RemoveAll(item => string.Equals(item, fullPath, StringComparison.OrdinalIgnoreCase));
        _settings.RecentFiles.Insert(0, fullPath);
        _settings.RecentFiles = _settings.RecentFiles
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Take(10)
            .ToList();
        RebuildRecentFilesMenu();
        _settingsStore.Save(_settings);
    }

    private void RebuildRecentFilesMenu()
    {
        if (RecentFilesMenu is null)
        {
            return;
        }

        RecentFilesMenu.Items.Clear();
        var paths = _settings.RecentFiles
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Take(10)
            .ToArray();
        if (paths.Length == 0)
        {
            RecentFilesMenu.Items.Add(new MenuItem { Header = "Список пуст", IsEnabled = false });
            return;
        }

        for (var index = 0; index < paths.Length; index++)
        {
            var path = paths[index];
            var item = new MenuItem
            {
                Header = $"_{index + 1}  {Path.GetFileName(path)}",
                ToolTip = path,
                Tag = path
            };
            item.Click += RecentFile_Click;
            RecentFilesMenu.Items.Add(item);
        }
        RecentFilesMenu.Items.Add(new Separator());
        var clearItem = new MenuItem { Header = "Очистить список" };
        clearItem.Click += (_, _) =>
        {
            _settings.RecentFiles.Clear();
            RebuildRecentFilesMenu();
            _settingsStore.Save(_settings);
        };
        RecentFilesMenu.Items.Add(clearItem);
    }

    private async void RecentFile_Click(object sender, RoutedEventArgs e)
    {
        if (!IsLoading && sender is MenuItem { Tag: string path } && ConfirmDiscardChanges() && File.Exists(path))
        {
            await LoadFileAsync(path);
        }
    }

    private bool ConfirmDiscardChanges()
    {
        if (!_isDirty)
        {
            return true;
        }

        return MessageBox.Show(this,
            "В текущем файле есть несохранённые изменения. Открыть другой файл и потерять эти изменения?",
            "Несохранённые изменения", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private void SetDirty(bool value)
    {
        _isDirty = value;
        var fileName = HasCurrentFile ? Path.GetFileName(FilePath) + " — " : string.Empty;
        Title = fileName + "Анализатор файла обмена Frontol" + (value ? " *" : string.Empty);
        OnPropertyChanged(nameof(CanSaveChanges));
    }

    private static Encoding EncodingFor(string encodingName) => encodingName switch
    {
        "UTF-8 с BOM" => new UTF8Encoding(true, true),
        "UTF-16 LE" => new UnicodeEncoding(false, true, true),
        "UTF-16 BE" => new UnicodeEncoding(true, true, true),
        "Windows-1251" => Encoding.GetEncoding(1251, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
        _ => new UTF8Encoding(false, true)
    };

    private static string DecodeForLayout(byte[] bytes, Encoding encoding)
    {
        var preamble = encoding.GetPreamble();
        return preamble.Length > 0 && bytes.AsSpan().StartsWith(preamble)
            ? encoding.GetString(bytes, preamble.Length, bytes.Length - preamble.Length)
            : encoding.GetString(bytes);
    }

    private static string DetectNewLine(string text)
    {
        var crlf = text.IndexOf("\r\n", StringComparison.Ordinal);
        if (crlf >= 0)
        {
            return "\r\n";
        }
        return text.Contains('\n') ? "\n" : text.Contains('\r') ? "\r" : Environment.NewLine;
    }

    private static string ComputeFileHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string Csv(string value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    private static string GroupDisplay(string value) => value.Contains('|') ? value[(value.IndexOf('|') + 1)..] : value;

    private static Dictionary<string, ParsedRecord> ProductMap(IEnumerable<ParsedRecord> records) => records
        .Where(record => record.IsProductRecord)
        .GroupBy(record => string.IsNullOrWhiteSpace(record.CodeText) ? $"#LINE:{record.LineNumber}" : record.CodeText,
            StringComparer.OrdinalIgnoreCase)
        .SelectMany(group => group.Count() == 1
            ? [new KeyValuePair<string, ParsedRecord>(group.Key, group.Single())]
            : group.OrderBy(record => record.LineNumber)
                .Select((record, index) => new KeyValuePair<string, ParsedRecord>($"{group.Key}#DUP:{index + 1}", record)))
        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<CompareRow> BuildComparison(
        IReadOnlyDictionary<string, ParsedRecord> current,
        IReadOnlyDictionary<string, ParsedRecord> other)
    {
        var result = new List<CompareRow>();
        foreach (var key in current.Keys.Union(other.Keys, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase))
        {
            var isDuplicate = key.Contains("#DUP:", StringComparison.Ordinal);
            current.TryGetValue(key, out var oldRecord);
            other.TryGetValue(key, out var newRecord);
            if (oldRecord is null)
            {
                result.Add(new CompareRow(isDuplicate ? "Дубликат" : "Добавлен", newRecord!.CodeText, string.Empty, newRecord.ContentText,
                    isDuplicate ? "Код встречается в файле несколько раз; однозначное сопоставление невозможно" : "Новая товарная строка"));
                continue;
            }
            if (newRecord is null)
            {
                result.Add(new CompareRow(isDuplicate ? "Дубликат" : "Удалён", oldRecord.CodeText, oldRecord.ContentText, string.Empty,
                    isDuplicate ? "Код встречается в файле несколько раз; однозначное сопоставление невозможно" : "Товар отсутствует во втором файле"));
                continue;
            }

            var changes = new List<string>();
            AddChange(changes, "наименование", oldRecord.ContentText, newRecord.ContentText);
            AddChange(changes, "цена", oldRecord.PriceText, newRecord.PriceText);
            AddChange(changes, "штрихкод", oldRecord.BarcodeText, newRecord.BarcodeText);
            AddChange(changes, "маркировка", oldRecord.ProductTypeText, newRecord.ProductTypeText);
            if (isDuplicate)
            {
                changes.Insert(0, "код дублируется; строки сопоставлены по порядку");
                result.Add(new CompareRow("Дубликат", oldRecord.CodeText, oldRecord.ContentText, newRecord.ContentText, string.Join("; ", changes)));
            }
            else if (changes.Count > 0)
            {
                result.Add(new CompareRow("Изменён", oldRecord.CodeText, oldRecord.ContentText, newRecord.ContentText, string.Join("; ", changes)));
            }
        }
        return result;
    }

    private static void AddChange(ICollection<string> changes, string name, string oldValue, string newValue)
    {
        if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            changes.Add($"{name}: «{oldValue}» → «{newValue}»");
        }
    }

    private bool SetField<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void SaveScreenshot(string path)
    {
        SaveWindowScreenshot(this, path);
    }

    private static void SaveWindowScreenshot(Window window, string path)
    {
        var visual = window.Content as FrameworkElement ?? window;
        visual.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(visual.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(visual.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            var bounds = new Rect(0, 0, width, height);
            context.DrawRectangle(window.Background ?? Brushes.White, null, bounds);
            context.DrawRectangle(new VisualBrush(visual), null, bounds);
        }
        bitmap.Render(drawing);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void ApplyRequestedWindowSize(Window window, IReadOnlyList<string> arguments, string option)
    {
        var index = Enumerable.Range(0, arguments.Count)
            .FirstOrDefault(item => arguments[item].Equals(option, StringComparison.OrdinalIgnoreCase), -1);
        if (index < 0 || index + 2 >= arguments.Count ||
            !double.TryParse(arguments[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var width) ||
            !double.TryParse(arguments[index + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out var height))
        {
            return;
        }

        window.WindowState = WindowState.Normal;
        window.Width = Math.Max(window.MinWidth, width);
        window.Height = Math.Max(window.MinHeight, height);
    }
}

public sealed class MarkingFilterOption(string? code, string name, int count)
{
    public string? Code { get; } = code;
    public string Name { get; } = name;
    public int Count { get; } = count;

    public string DisplayText => Code is null
        ? $"{Name} ({Count:N0})"
        : $"{Code} — {Name} ({Count:N0})";
}

public sealed class CommandFilterOption(string? commandName, string displayName, string description, int count)
{
    public string? CommandName { get; } = commandName;
    public string DisplayName { get; } = displayName;
    public string Description { get; } = description;
    public int Count { get; } = count;
    public string DisplayText => CommandName is null
        ? $"Все команды ({Count:N0})"
        : $"$$${CommandName} — {DisplayName} ({Count:N0})";
}

internal sealed record RecordColumnState(
    string Key,
    string Header,
    GridViewColumn Column,
    double DefaultWidth,
    double MinimumWidth);
