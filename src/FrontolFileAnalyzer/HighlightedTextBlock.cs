using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace FrontolFileAnalyzer;

public sealed class HighlightedTextBlock : TextBlock
{
    public static readonly DependencyProperty HighlightTextProperty = DependencyProperty.Register(
        nameof(HighlightText), typeof(string), typeof(HighlightedTextBlock),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender, Refresh));

    public static readonly DependencyProperty SearchTextProperty = DependencyProperty.Register(
        nameof(SearchText), typeof(string), typeof(HighlightedTextBlock),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender, Refresh));

    public string HighlightText
    {
        get => (string)GetValue(HighlightTextProperty);
        set => SetValue(HighlightTextProperty, value);
    }

    public string SearchText
    {
        get => (string)GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    private static void Refresh(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs _) =>
        ((HighlightedTextBlock)dependencyObject).RebuildInlines();

    private void RebuildInlines()
    {
        Inlines.Clear();
        var value = HighlightText ?? string.Empty;
        var query = SearchText?.Trim() ?? string.Empty;
        if (query.Length == 0)
        {
            Inlines.Add(new Run(value));
            return;
        }

        var start = 0;
        while (start < value.Length)
        {
            var match = value.IndexOf(query, start, StringComparison.CurrentCultureIgnoreCase);
            if (match < 0)
            {
                Inlines.Add(new Run(value[start..]));
                break;
            }

            if (match > start)
            {
                Inlines.Add(new Run(value[start..match]));
            }

            Inlines.Add(new Run(value.Substring(match, query.Length))
            {
                Background = Brushes.Gold,
                Foreground = Brushes.Black,
                FontWeight = FontWeights.SemiBold
            });
            start = match + query.Length;
        }

        if (value.Length == 0)
        {
            Inlines.Add(new Run());
        }
    }
}
