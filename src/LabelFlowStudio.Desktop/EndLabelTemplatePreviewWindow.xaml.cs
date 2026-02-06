using System.Windows;
using LabelFlowStudio.Desktop.Templates;
using Microsoft.Web.WebView2.Core;

namespace LabelFlowStudio.Desktop;

public partial class EndLabelTemplatePreviewWindow : Window
{
    private readonly string _tenam;
    private readonly decimal? _weight;

    public EndLabelTemplatePreviewWindow(string tenam, decimal? weight)
    {
        if (string.IsNullOrWhiteSpace(tenam))
        {
            throw new ArgumentException("Tenam is required", nameof(tenam));
        }

        _tenam = tenam;
        _weight = weight;

        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        Loaded -= OnLoaded;
        await LoadPreviewAsync();
    }

    private async Task LoadPreviewAsync()
    {
        try
        {
            var template = await EndLabelTemplateStore.LoadOrCreateAsync(CancellationToken.None);
            var html = EndLabelHtmlTemplateRenderer.Render(template, _tenam, _weight);

            await PreviewWebView.EnsureCoreWebView2Async();
            PreviewWebView.NavigateToString(html);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnReloadClick(object sender, RoutedEventArgs eventArgs)
    {
        _ = LoadPreviewAsync();
    }

    private void OnPrintClick(object sender, RoutedEventArgs eventArgs)
    {
        try
        {
            if (PreviewWebView.CoreWebView2 is null)
            {
                return;
            }

            PreviewWebView.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.System);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Ошибка печати", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs eventArgs)
    {
        Close();
    }
}
