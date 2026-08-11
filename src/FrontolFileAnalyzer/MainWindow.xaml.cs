using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
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
    private ObservableCollection<ParsedRecord> _records = [];
    private ICollectionView _recordsView = null!;
    private string _filePath = "Файл не выбран";
    private string _encodingLabel = "Кодировка: —";
    private string _recordCountLabel = "Строк: 0";
    private string _problemCountLabel = "Замечаний: 0";
    private string _statusMessage = "Откройте или перетащите файл обмена Frontol";
    private string _fieldVisibilityLabel = "Поля не выбраны";
    private string _loadProgressText = string.Empty;
    private ParsedRecord? _selectedRecord;
    private AnalyzedField? _selectedField;
    private string _searchText = string.Empty;
    private int _filterIndex;
    private int _loadProgressValue;
    private bool _loadProgressIsIndeterminate;
    private bool _isLoading;
    private bool _showEmptyFields;
    private GridLength _recordsPaneWidth = new(540);

    public MainWindow()
    {
        _settings = _settingsStore.Load();
        _showEmptyFields = _settings.ShowEmptyFields;
        ReplaceRecordsView(_records);
        InitializeComponent();
        DataContext = this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICollectionView RecordsView
    {
        get => _recordsView;
        private set => SetField(ref _recordsView, value);
    }

    public ObservableCollection<AnalyzedField> VisibleFields => _visibleFields;
    public string VersionLabel => ApplicationInfo.VersionLabel;

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
            }
        }
    }

    public AnalyzedField? SelectedField
    {
        get => _selectedField;
        set => SetField(ref _selectedField, value);
    }

    public bool HasCurrentFile => File.Exists(FilePath);
    public bool CanStartLoad => !IsLoading;
    public bool CanOpenCurrentFile => !IsLoading && HasCurrentFile;
    public bool CanConfigureFields => !IsLoading && SelectedRecord?.Fields.Count > 0;

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
        if (IsLoading)
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
            ? (_recordsPaneWidth.Value > 0 ? _recordsPaneWidth : new GridLength(540))
            : new GridLength(0);
        RecordsPanel.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        ToggleRecordsPaneButton.Content = isVisible ? "◀" : "▶";
        ToggleRecordsPaneButton.ToolTip = isVisible
            ? "Скрыть список строк (Ctrl+M)"
            : "Показать список строк (Ctrl+M)";
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = !IsLoading && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!IsLoading && e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0 && File.Exists(files[0]))
        {
            await LoadFileAsync(files[0]);
        }
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = (sender as TextBox)?.Text?.Trim() ?? string.Empty;
        RecordsView.Refresh();
    }

    private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _filterIndex = (sender as ComboBox)?.SelectedIndex ?? 0;
        RecordsView.Refresh();
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

        return _filterIndex switch
        {
            1 => record.Severity is IssueSeverity.Warning or IssueSeverity.Error,
            2 => record.Severity == IssueSeverity.Error,
            3 => record.Kind == FrontolRecordKind.Data,
            _ => true
        };
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

            LoadProgressIsIndeterminate = true;
            LoadProgressText = "Подготовка списка строк";
            _records = new ObservableCollection<ParsedRecord>(document.Records);
            ReplaceRecordsView(_records);

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
        RecordsView = view;
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
