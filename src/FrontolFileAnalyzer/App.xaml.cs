using System.Windows;
using System.Windows.Threading;
using System.IO;

namespace FrontolFileAnalyzer;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += HandleUnhandledException;
    }

    private static void HandleUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FrontolFileAnalyzer");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}\n\n");
        }
        catch
        {
            // Ошибка записи журнала не должна вызывать второе исключение.
        }

        MessageBox.Show(
            "Операцию не удалось выполнить. Приложение продолжит работу.\n\n" + e.Exception.Message,
            "Ошибка приложения", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
