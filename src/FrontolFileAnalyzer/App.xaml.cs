using System.Windows;
using System.Windows.Threading;
using System.IO;

namespace FrontolFileAnalyzer;

public partial class App : Application
{
    public static string ErrorLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FrontolFileAnalyzer",
        "error.log");

    public App()
    {
        DispatcherUnhandledException += HandleUnhandledException;
    }

    private static void HandleUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            var folder = Path.GetDirectoryName(ErrorLogPath)!;
            Directory.CreateDirectory(folder);
            File.AppendAllText(ErrorLogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}\n\n");
        }
        catch
        {
            // Ошибка записи журнала не должна вызывать второе исключение.
        }

        MessageBox.Show(
            "Операцию не удалось выполнить. Приложение продолжит работу.\n\n" + e.Exception.Message +
            $"\n\nПодробности записаны в:\n{ErrorLogPath}",
            "Ошибка приложения", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
