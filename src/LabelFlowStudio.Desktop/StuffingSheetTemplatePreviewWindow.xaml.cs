using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Desktop.BoxProcessing;
using LabelFlowStudio.Desktop.Printing;
using LabelFlowStudio.Desktop.Templates;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.IO;
using System.Text;
using System.Windows;

namespace LabelFlowStudio.Desktop;

/// <summary>
/// Окно предпросмотра и печати листа сброса
/// </summary>
public partial class StuffingSheetTemplatePreviewWindow : Window
{
    private readonly BoxProcessingResponse _response;
    private readonly string _tenam;

    private readonly SemaphoreSlim _previewGate = new(1, 1);

    private Task? _initializeWebViewTask;
    private bool _isPreviewReady;

    /// <summary>
    /// Создает окно предпросмотра листа сброса
    /// </summary>
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

    // Инициализирует предпросмотр при загрузке окна
    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        Loaded -= OnLoaded;
        await LoadPreviewAsync();
    }

    // Загружает HTML шаблон и отображает его в WebView
    private async Task LoadPreviewAsync()
    {
        if (!await _previewGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            SetPreviewState(isReady: false, status: "Загрузка предпросмотра");

            var hasWeight = BoxProcessingResponseInspector.HasWeight(_response);

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

    // Подготавливает WebView2 к загрузке предпросмотра
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

    // Инициализирует окружение WebView2
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

    // Возвращает путь до папки пользовательских данных WebView2
    private static string GetWebViewUserDataFolder()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "LabelFlowStudio", "WebView2", $"pid-{Environment.ProcessId}");
    }

    // Обновляет состояние интерфейса после загрузки предпросмотра
    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs eventArgs)
    {
        if (eventArgs.IsSuccess)
        {
            SetPreviewState(isReady: true, status: string.Empty);
            return;
        }

        SetPreviewState(isReady: false, status: "Ошибка загрузки предпросмотра");
    }

    // Запускает печать листа сброса
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
                PreviewWebView.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
                StatusText.Text = "Открыто окно печати";
                return;
            }

            var copies = Math.Max(1, settings?.StuffingSheetCopies ?? 1);
            var isPrinted = await TryPrintSilentAsync(PreviewWebView.CoreWebView2, printerName, copies, CancellationToken.None);

            StatusText.Text = isPrinted
                ? $"Отправлено на принтер: {printerName}"
                : "Не удалось отправить задание на принтер";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Ошибка печати", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Печатает документ в фоне без системного диалога
    private static async Task<bool> TryPrintSilentAsync(CoreWebView2 webView, string printerName, int copies, CancellationToken cancellationToken)
    {
        for (var i = 0; i < copies; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var printSettings = webView.Environment.CreatePrintSettings();
            printSettings.PrinterName = printerName;
            printSettings.ShouldPrintBackgrounds = true;
            printSettings.ShouldPrintHeaderAndFooter = false;

            var status = await webView.PrintAsync(printSettings);
            if (status != CoreWebView2PrintStatus.Succeeded)
            {
                return false;
            }

            await Task.Delay(100, cancellationToken);
        }

        return true;
    }

    // Закрывает окно предпросмотра
    private void OnCloseClick(object sender, RoutedEventArgs eventArgs)
    {
        Close();
    }

    // Сохраняет HTML предпросмотра во временный файл
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

    // Обновляет индикаторы готовности предпросмотра
    private void SetPreviewState(bool isReady, string status)
    {
        _isPreviewReady = isReady;
        StatusText.Text = status;
    }
}
