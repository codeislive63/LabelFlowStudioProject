using System.IO;
using System.Text;
using System.Windows;
using LabelFlowStudio.Desktop.Templates;
using Microsoft.Web.WebView2.Core;

namespace LabelFlowStudio.Desktop;

public partial class EndLabelTemplatePreviewWindow : Window
{
    private readonly string _tenam;
    private readonly decimal? _weight;

    private bool _isPreviewReady;

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
            SetPreviewState(isReady: false, status: "Загрузка предпросмотра");

            var template = await EndLabelTemplateStore.LoadOrCreateAsync(CancellationToken.None);
            var html = EndLabelHtmlTemplateRenderer.Render(template, _tenam, _weight);

            var previewFilePath = await SavePreviewHtmlAsync(html, CancellationToken.None);

            await PreviewWebView.EnsureCoreWebView2Async();

            // навигация именно на file:///… даёт стабильную печать и предсказуемую загрузку
            PreviewWebView.Source = new Uri(previewFilePath, UriKind.Absolute);
        }
        catch (Exception exception)
        {
            SetPreviewState(isReady: false, status: "Ошибка предпросмотра");
            MessageBox.Show(this, exception.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs eventArgs)
    {
        if (eventArgs.IsSuccess)
        {
            SetPreviewState(isReady: true, status: string.Empty);
            return;
        }

        SetPreviewState(isReady: false, status: "Ошибка загрузки предпросмотра");
    }

    private void OnReloadClick(object sender, RoutedEventArgs eventArgs)
    {
        _ = LoadPreviewAsync();
    }

    private void OnPrintClick(object sender, RoutedEventArgs eventArgs)
    {
        try
        {
            if (!_isPreviewReady)
            {
                StatusText.Text = "Предпросмотр загружается";
                return;
            }

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

    private static async Task<string> SavePreviewHtmlAsync(string html, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(Path.GetTempPath(), "LabelFlowStudio");
        Directory.CreateDirectory(folder);

        var filePath = Path.Combine(folder, "end-label-preview.html");

        await File.WriteAllTextAsync(filePath, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);

        return filePath;
    }

    private void SetPreviewState(bool isReady, string status)
    {
        _isPreviewReady = isReady;
        PrintButton.IsEnabled = isReady;
        StatusText.Text = status;
    }
}
