using System.Windows;

namespace FrontolFileAnalyzer;

public partial class CompareWindow : Window
{
    public CompareWindow(IReadOnlyList<CompareRow> rows)
    {
        Rows = rows;
        Summary = $"Различий: {rows.Count:N0} · добавлено: {rows.Count(row => row.Status == "Добавлен"):N0} · удалено: {rows.Count(row => row.Status == "Удалён"):N0} · изменено: {rows.Count(row => row.Status == "Изменён"):N0}";
        InitializeComponent();
        DataContext = this;
    }

    public IReadOnlyList<CompareRow> Rows { get; }
    public string Summary { get; }
}

public sealed record CompareRow(string Status, string Code, string OldName, string NewName, string Changes);
