using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Desktop.Printing;
using LabelFlowStudio.Desktop.Templates;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.IO;
using System.Text;
using System.Windows;

namespace LabelFlowStudio.Desktop;

public partial class StuffingSheetTemplatePreviewWindow : Window
{
    private readonly BoxProcessingResponse _response;
    private readonly string _tenam;

    private readonly SemaphoreSlim _previewGate = new(1, 1);

    private Task? _initializeWebViewTask;
    private bool _isPreviewReady;

    public StuffingSheetTemplatePreviewWindow(BoxProcessingResponse response, string tenam)
    {
        _response = response ?? throw new ArgumentNullException(nameof(response));

        if (string.IsNullOrWhiteSpace(tenam))
        {
            throw new ArgumentException("Tenam is required", nameof(tenam));
        }

        _tenam = tenam;

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
        if (!await _previewGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            SetPreviewState(isReady: false, status: "Загрузка предпросмотра");

            var hasWeight = HasWeight(_response);

            string html;

            if (!hasWeight)
            {
                html = await EmptyPageTemplateStore.LoadOrCreateAsync(CancellationToken.None);
            }
            else
            {
                var template = await StuffingSheetTemplateStore.LoadOrCreateAsync(CancellationToken.None);
                html = StuffingSheetHtmlTemplateRenderer.Render(template, _response, _tenam);
            }

            var previewFilePath = await SavePreviewHtmlAsync(html, CancellationToken.None);

            await EnsureWebViewReadyAsync();

            PreviewWebView.Source = new Uri(previewFilePath, UriKind.Absolute);
        }
        catch (TimeoutException exception)
        {
            SetPreviewState(isReady: false, status: "WebView2 не инициализировался");
            MessageBox.Show(this, exception.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception exception)
        {
            SetPreviewState(isReady: false, status: "Ошибка предпросмотра");
            MessageBox.Show(this, exception.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _previewGate.Release();
        }
    }

    private static bool HasWeight(BoxProcessingResponse response)
    {
        if (response.Weight.HasValue && response.Weight.Value > 0)
        {
            return true;
        }

        if (response.Records.Count == 0)
        {
            return false;
        }

        var brutto = response.Records[0].Brutto;
        return brutto.HasValue && brutto.Value > 0;
    }

    private async Task EnsureWebViewReadyAsync()
    {
        if (PreviewWebView.CoreWebView2 is not null)
        {
            return;
        }

        if (_initializeWebViewTask is null || _initializeWebViewTask.IsFaulted || _initializeWebViewTask.IsCanceled)
        {
            _initializeWebViewTask = InitializeWebViewAsync();
        }

        try
        {
            await _initializeWebViewTask.WaitAsync(TimeSpan.FromSeconds(60));
        }
        catch (TimeoutException)
        {
            _initializeWebViewTask = null;
            throw new TimeoutException("WebView2 не успел инициализироваться за 60 секунд");
        }
    }

    private async Task InitializeWebViewAsync()
    {
        var userDataFolder = GetWebViewUserDataFolder();
        Directory.CreateDirectory(userDataFolder);

        PreviewWebView.CreationProperties ??= new CoreWebView2CreationProperties
        {
            UserDataFolder = userDataFolder
        };

        await PreviewWebView.EnsureCoreWebView2Async();
    }

    private static string GetWebViewUserDataFolder()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "LabelFlowStudio", "WebView2", $"pid-{Environment.ProcessId}");
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

    private async void OnPrintClick(object sender, RoutedEventArgs eventArgs)
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

            var settings = PrintSettingsStore.TryLoad();
            var printerName = settings?.StuffingSheetPrinterName ?? string.Empty;

            if (string.IsNullOrWhiteSpace(printerName) || !PrinterDiscovery.IsPrinterInstalled(printerName))
            {
                // Настроек/принтера нет — оставляем старое поведение
                PreviewWebView.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
                return;
            }

            var copies = settings?.StuffingSheetCopies ?? 1;
            if (copies < 1)
            {
                copies = 1;
            }

            for (var i = 0; i < copies; i++)
            {
                var printSettings = PreviewWebView.CoreWebView2.Environment.CreatePrintSettings();
                printSettings.PrinterName = printerName;
                printSettings.ShouldPrintBackgrounds = true;
                printSettings.ShouldPrintHeaderAndFooter = false;

                var status = await PreviewWebView.CoreWebView2.PrintAsync(printSettings);
                if (status != CoreWebView2PrintStatus.Succeeded)
                {
                    MessageBox.Show(this, "Не удалось отправить задание на принтер", "Печать", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                await Task.Delay(120);
            }

            MessageBox.Show(this, $"Отправлено на принтер: {printerName}", "Печать", MessageBoxButton.OK, MessageBoxImage.Information);
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

        var filePath = Path.Combine(folder, "stuffing-sheet-preview.html");

        await File.WriteAllTextAsync(
            filePath,
            html,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken
        );

        return filePath;
    }

    private void SetPreviewState(bool isReady, string status)
    {
        _isPreviewReady = isReady;
        PrintButton.IsEnabled = isReady;
        StatusText.Text = status;
    }
}
