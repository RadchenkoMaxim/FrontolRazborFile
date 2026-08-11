using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FrontolFileAnalyzer.Core;

namespace FrontolFileAnalyzer;

public partial class MarkingCodesWindow : Window
{
    private readonly ObservableCollection<FrontolCodeReference> _codes = new(FrontolReferenceCatalog.ProductTypes);
    private string _searchText = string.Empty;

    public MarkingCodesWindow()
    {
        CodesView = CollectionViewSource.GetDefaultView(_codes);
        CodesView.Filter = FilterCode;
        RelatedCodes = FrontolReferenceCatalog.RelatedMarkingCodes;
        InitializeComponent();
        DataContext = this;
    }

    public ICollectionView CodesView { get; }
    public IReadOnlyList<FrontolCodeReference> RelatedCodes { get; }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = (sender as TextBox)?.Text?.Trim() ?? string.Empty;
        CodesView.Refresh();
    }

    private bool FilterCode(object item) => item is FrontolCodeReference code &&
        (_searchText.Length == 0 || code.Code.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
         code.Name.Contains(_searchText, StringComparison.CurrentCultureIgnoreCase));

    private void CopySelected_Click(object sender, RoutedEventArgs e)
    {
        if (CodesGrid.SelectedItem is FrontolCodeReference code)
        {
            Clipboard.SetText($"{code.Code} - {code.Name}");
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
