using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    private List<string> _workingLines = [];
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
    private MarkingFilterOption? _selectedMarkingFilter;
    private GridLength _recordsPaneWidth = new(655);

    public MainWindow()
    {
        _settings = _settingsStore.Load();
        _showEmptyFields = _settings.ShowEmptyFields;
        ReplaceRecordsView(_records);
        InitializeComponent();
        InitializeRecordColumns();
        _recordsPaneWidth = new GridLength(Math.Max(360, _settings.RecordsPaneWidth));
        RecordsColumn.Width = _recordsPaneWidth;
        DataContext = this;
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

    public MarkingFilterOption? SelectedMarkingFilter
    {
        get => _selectedMarkingFilter;
        set
        {
            if (SetField(ref _selectedMarkingFilter, value))
            {
                RecordsView.Refresh();
                SelectFirstVisibleRecord();
            }
        }
    }

    public string FilePath
    {
        get => _filePath;
        private set
        {
            if (SetField(ref _filePath, value))
            {
                OnPropertyChanged(nameof(HasCurrentFile));
                OnPropertyChanged(nameof(CanOpenCurrentFile));
            }
        }
    }

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

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var aboutScreenshotIndex = Array.FindIndex(arguments, argument => argument.Equals("--screenshot-about", StringComparison.OrdinalIgnoreCase));
        if (aboutScreenshotIndex >= 0 && arguments.Length > aboutScreenshotIndex + 1)
        {
            var window = new AboutWindow { Owner = this };
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
                window.Show();
                await Dispatcher.InvokeAsync(window.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                SaveWindowScreenshot(window, arguments[fieldsScreenshotIndex + 1]);
                window.Close();
            }

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
                SelectedMarkingFilter = MarkingFilters.FirstOrDefault(option =>
                    string.Equals(option.Code, requestedCode, StringComparison.OrdinalIgnoreCase))
                    ?? SelectedMarkingFilter;
            }
            var searchIndex = Array.FindIndex(arguments, argument =>
                argument.Equals("--search", StringComparison.OrdinalIgnoreCase));
            if (searchIndex >= 0 && arguments.Length > searchIndex + 1)
            {
                SearchBox.Text = arguments[searchIndex + 1];
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

        var argument = arguments.FirstOrDefault(File.Exists);
        if (argument is not null)
        {
            await LoadFileAsync(argument);
        }
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
        if (CanSaveFile && HasCurrentFile)
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
            var text = string.Join(_newLine, _workingLines) + (_hasTrailingNewLine ? _newLine : string.Empty);
            var fullTargetPath = Path.GetFullPath(targetPath);
            var targetDirectory = Path.GetDirectoryName(fullTargetPath)!;
            Directory.CreateDirectory(targetDirectory);
            var tempPath = Path.Combine(targetDirectory, $".{Path.GetFileName(fullTargetPath)}.{Guid.NewGuid():N}.tmp");
            var backupPath = fullTargetPath + ".bak";

            await Task.Run(() => File.WriteAllText(tempPath, text, _sourceEncoding));
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
            new RecordColumnOption(state.Key, state.Header, visibleKeys.Contains(state.Key)))) { Owner = this };
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

        if (e.Key == Key.M && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            ToggleRecordsPane_Click(sender, new RoutedEventArgs());
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

        RecordsColumn.MinWidth = isVisible ? 360 : 0;
        RecordsColumn.Width = isVisible
            ? (_recordsPaneWidth.Value > 0 ? _recordsPaneWidth : new GridLength(655))
            : new GridLength(0);
        RecordsPanel.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        ToggleRecordsPaneButton.Content = isVisible ? "◀" : "▶";
        ToggleRecordsPaneButton.ToolTip = isVisible
            ? "Скрыть список строк (Ctrl+M)"
            : "Показать список строк (Ctrl+M)";
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
        SelectedMarkingFilter = _markingFilters.FirstOrDefault();
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

        if (SelectedMarkingFilter?.Code is { } productTypeCode &&
            !string.Equals(record.ProductTypeCode, productTypeCode, StringComparison.OrdinalIgnoreCase))
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

    private async void EditField_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRecord is not { Kind: FrontolRecordKind.Data } record || SelectedField is not { } field)
        {
            return;
        }

        var dialog = new TextEditWindow("Изменить значение поля",
            $"Строка {record.LineNumber}, поле {field.Number} «{field.Name}». Символ ';' является разделителем полей.",
            field.RawValue) { Owner = this };
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
        if (SelectedRecord is not { } record)
        {
            return;
        }

        var dialog = new TextEditWindow("Изменить исходную строку",
            $"Физическая строка {record.LineNumber}. Поля данных разделяются точкой с запятой.", record.RawText) { Owner = this };
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
                ApplyParsedDocument(document, lineNumber, fieldNumber);
                SetDirty(true);
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

            var document = await Task.Run(() => _parser.ParseFile(path, progress));

            var sourceBytes = await File.ReadAllBytesAsync(path);
            _encodingName = document.EncodingName;
            _sourceEncoding = EncodingFor(document.EncodingName);
            var sourceText = DecodeForLayout(sourceBytes, _sourceEncoding);
            _newLine = DetectNewLine(sourceText);
            _hasTrailingNewLine = sourceText.EndsWith('\n') || sourceText.EndsWith('\r');
            _workingLines = document.Records.Select(record => record.RawText).ToList();

            LoadProgressIsIndeterminate = true;
            LoadProgressText = "Подготовка списка строк";
            _records = new ObservableCollection<ParsedRecord>(document.Records);
            ReplaceRecordsView(_records);
            RebuildMarkingFilters(document.Records);
            RebuildCommandFilters(document.Records);

            FilePath = document.FilePath;
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
            StatusMessage = "Не удалось прочитать файл";
            MessageBox.Show(this, exception.Message, "Ошибка открытия файла", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
            LoadProgressIsIndeterminate = false;
            LoadProgressValue = 100;
            LoadProgressText = string.Empty;
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
            .Select(group => new CommandFilterOption(group.Key, group.Count()))
            .OrderBy(option => option.CommandName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _commandFilters.Clear();
        _commandFilters.Add(new CommandFilterOption(null, records.Count));
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
        var previousCode = SelectedMarkingFilter?.Code;
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

        SelectedMarkingFilter = previousCode is null
            ? _markingFilters[0]
            : _markingFilters.FirstOrDefault(option =>
                string.Equals(option.Code, previousCode, StringComparison.OrdinalIgnoreCase)) ?? _markingFilters[0];
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
        Title = value ? "Анализатор файла обмена Frontol *" : "Анализатор файла обмена Frontol";
    }

    private static Encoding EncodingFor(string encodingName) => encodingName switch
    {
        "UTF-8 с BOM" => new UTF8Encoding(true),
        "UTF-16 LE" => new UnicodeEncoding(false, true),
        "Windows-1251" => Encoding.GetEncoding(1251),
        _ => new UTF8Encoding(false)
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

    private static string Csv(string value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    private static string GroupDisplay(string value) => value.Contains('|') ? value[(value.IndexOf('|') + 1)..] : value;

    private static Dictionary<string, ParsedRecord> ProductMap(IEnumerable<ParsedRecord> records) => records
        .Where(record => record.IsProductRecord)
        .GroupBy(record => string.IsNullOrWhiteSpace(record.CodeText) ? $"#LINE:{record.LineNumber}" : record.CodeText,
            StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<CompareRow> BuildComparison(
        IReadOnlyDictionary<string, ParsedRecord> current,
        IReadOnlyDictionary<string, ParsedRecord> other)
    {
        var result = new List<CompareRow>();
        foreach (var key in current.Keys.Union(other.Keys, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase))
        {
            current.TryGetValue(key, out var oldRecord);
            other.TryGetValue(key, out var newRecord);
            if (oldRecord is null)
            {
                result.Add(new CompareRow("Добавлен", newRecord!.CodeText, string.Empty, newRecord.ContentText, "Новая товарная строка"));
                continue;
            }
            if (newRecord is null)
            {
                result.Add(new CompareRow("Удалён", oldRecord.CodeText, oldRecord.ContentText, string.Empty, "Товар отсутствует во втором файле"));
                continue;
            }

            var changes = new List<string>();
            AddChange(changes, "наименование", oldRecord.ContentText, newRecord.ContentText);
            AddChange(changes, "цена", oldRecord.PriceText, newRecord.PriceText);
            AddChange(changes, "штрихкод", oldRecord.BarcodeText, newRecord.BarcodeText);
            AddChange(changes, "маркировка", oldRecord.ProductTypeText, newRecord.ProductTypeText);
            if (changes.Count > 0)
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
        var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        encoder.Save(stream);
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

public sealed class CommandFilterOption(string? commandName, int count)
{
    public string? CommandName { get; } = commandName;
    public int Count { get; } = count;
    public string DisplayText => CommandName is null ? $"Все команды ({Count:N0})" : $"$$${CommandName} ({Count:N0})";
}

internal sealed record RecordColumnState(
    string Key,
    string Header,
    GridViewColumn Column,
    double DefaultWidth,
    double MinimumWidth);
