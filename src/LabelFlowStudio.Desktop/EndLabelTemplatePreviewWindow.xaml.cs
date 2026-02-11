using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Desktop.Templates;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Drawing.Printing;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;

namespace LabelFlowStudio.Desktop;

public partial class EndLabelTemplatePreviewWindow : Window
{
    private readonly BoxProcessingResponse _response;
    private readonly string _tenam;

    private readonly SemaphoreSlim _previewGate = new(1, 1);

    private Task? _initializeWebViewsTask;
    private CancellationTokenSource? _renderDebounceCts;

    private bool _isPreviewReady;
    private bool _isEditorReady;
    private bool _isDirty;

    private string _templateText = string.Empty;

    private int _previewVersion;
    private double _zoomFactor = 1.0;

    private const string PreferredPrinterName = "zebra_torec";
    private const int PreferredCopies = 2;

    private static readonly TemplateToken[] Tokens =
    {
        new("{{Tenam}}", "{{Tenam}}", "Код магазина"),
        new("{{TENAM}}", "{{TENAM}}", "Код магазина"),
        new("{{BarcodeDataUri}}", "{{BarcodeDataUri}}", "Штрихкод (data:image/png;base64,...)"),
        new("{{BARCODE_DATA_URL}}", "{{BARCODE_DATA_URL}}", "Штрихкод (data:image/png;base64,...)"),
        new("{{Lfakdnr}}", "{{Lfakdnr}}", "Код заказа"),
        new("{{Gpbez}}", "{{Gpbez}}", "Адрес доставки строка 1"),
        new("{{Gport1}}", "{{Gport1}}", "Адрес доставки строка 2"),
        new("{{Gpstrasse}}", "{{Gpstrasse}}", "Адрес доставки строка 3"),
        new("{{Bstchgnam5}}", "{{Bstchgnam5}}", "Грузополучатель"),
        new("{{Brutto}}", "{{Brutto}}", "Вес брутто (кг)"),
        new("{{CountBst}}", "{{CountBst}}", "Короб в заказ"),
        new("{{SumBst}}", "{{SumBst}}", "Изделий в коробе")
    };

    public EndLabelTemplatePreviewWindow(BoxProcessingResponse response, string tenam)
    {
        _response = response ?? throw new ArgumentNullException(nameof(response));

        if (string.IsNullOrWhiteSpace(tenam))
        {
            throw new ArgumentException("Tenam is required", nameof(tenam));
        }

        _tenam = tenam;

        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        Loaded -= OnLoaded;

        try
        {
            SetStatus("Загрузка шаблона");

            _templateText = await EndLabelTemplateStore.LoadOrCreateAsync(CancellationToken.None);

            await EnsureWebViewsReadyAsync();

            NavigateEditor();

            ScheduleRenderPreview();
            ApplyModeLayout();
            ApplyZoom(_zoomFactor);

            SetStatus(string.Empty);
        }
        catch (TimeoutException exception)
        {
            SetStatus("WebView2 не инициализировался");
            MessageBox.Show(this, exception.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception exception)
        {
            SetStatus("Ошибка загрузки");
            MessageBox.Show(this, exception.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        _renderDebounceCts?.Cancel();
        _renderDebounceCts?.Dispose();
        _renderDebounceCts = null;
    }

    private async Task EnsureWebViewsReadyAsync()
    {
        if (PreviewWebView.CoreWebView2 is not null && EditorWebView.CoreWebView2 is not null)
        {
            return;
        }

        if (_initializeWebViewsTask is null || _initializeWebViewsTask.IsFaulted || _initializeWebViewsTask.IsCanceled)
        {
            _initializeWebViewsTask = InitializeWebViewsAsync();
        }

        try
        {
            await _initializeWebViewsTask.WaitAsync(TimeSpan.FromSeconds(60));
        }
        catch (TimeoutException)
        {
            _initializeWebViewsTask = null;
            throw new TimeoutException("WebView2 не успел инициализироваться за 60 секунд");
        }
    }

    private async Task InitializeWebViewsAsync()
    {
        var userDataFolder = GetWebViewUserDataFolder();
        Directory.CreateDirectory(userDataFolder);

        PreviewWebView.CreationProperties ??= new CoreWebView2CreationProperties
        {
            UserDataFolder = userDataFolder
        };

        EditorWebView.CreationProperties ??= new CoreWebView2CreationProperties
        {
            UserDataFolder = userDataFolder
        };

        await PreviewWebView.EnsureCoreWebView2Async();
        await EditorWebView.EnsureCoreWebView2Async();

        EditorWebView.CoreWebView2.WebMessageReceived -= OnEditorWebMessageReceived;
        EditorWebView.CoreWebView2.WebMessageReceived += OnEditorWebMessageReceived;

        var assetsRoot = GetAssetsRoot();
        if (Directory.Exists(assetsRoot))
        {
            EditorWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "app",
                assetsRoot,
                CoreWebView2HostResourceAccessKind.Allow
            );
        }
    }

    private static string GetAssetsRoot()
    {
        return Path.Combine(AppContext.BaseDirectory, "Assets");
    }

    private static string GetWebViewUserDataFolder()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "LabelFlowStudio", "WebView2", $"pid-{Environment.ProcessId}");
    }

    private void NavigateEditor()
    {
        _isEditorReady = false;
        UpdateSaveButtonState();

        var editorUrl = "https://app/TemplateEditor/editor.html";
        EditorWebView.Source = new Uri(editorUrl, UriKind.Absolute);
    }

    private void OnEditorWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        try
        {
            using var document = JsonDocument.Parse(eventArgs.WebMessageAsJson);
            var root = document.RootElement;

            if (!root.TryGetProperty("type", out var typeElement))
            {
                return;
            }

            var type = typeElement.GetString();
            if (string.IsNullOrWhiteSpace(type))
            {
                return;
            }

            if (string.Equals(type, "ready", StringComparison.Ordinal))
            {
                _isEditorReady = true;

                var initPayload = new
                {
                    type = "init",
                    value = _templateText,
                    tokens = Tokens.Select(token => new
                    {
                        label = token.Label,
                        insertText = token.InsertText,
                        detail = token.Detail
                    })
                };

                var json = JsonSerializer.Serialize(initPayload);
                EditorWebView.CoreWebView2.PostWebMessageAsJson(json);

                UpdateSaveButtonState();
                return;
            }

            if (string.Equals(type, "contentChanged", StringComparison.Ordinal))
            {
                if (!root.TryGetProperty("value", out var valueElement))
                {
                    return;
                }

                _templateText = valueElement.GetString() ?? string.Empty;
                _isDirty = true;

                UpdateSaveButtonState();
                ScheduleRenderPreview();
                return;
            }

            if (string.Equals(type, "saveRequested", StringComparison.Ordinal))
            {
                _ = SaveTemplateAsync();
                return;
            }
        }
        catch
        {
            // ignore editor message parsing errors
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

    private void OnModeChanged(object sender, RoutedEventArgs eventArgs)
    {
        ApplyModeLayout();
    }

    private void ApplyModeLayout()
    {
        if (ModeCode.IsChecked == true)
        {
            EditorWebView.Visibility = Visibility.Visible;
            PreviewWebView.Visibility = Visibility.Collapsed;

            EditorColumn.Width = new GridLength(1, GridUnitType.Star);
            SplitterColumn.Width = new GridLength(0, GridUnitType.Pixel);
            PreviewColumn.Width = new GridLength(0, GridUnitType.Pixel);

            return;
        }

        if (ModePreview.IsChecked == true)
        {
            EditorWebView.Visibility = Visibility.Collapsed;
            PreviewWebView.Visibility = Visibility.Visible;

            EditorColumn.Width = new GridLength(0, GridUnitType.Pixel);
            SplitterColumn.Width = new GridLength(0, GridUnitType.Pixel);
            PreviewColumn.Width = new GridLength(1, GridUnitType.Star);

            return;
        }

        EditorWebView.Visibility = Visibility.Visible;
        PreviewWebView.Visibility = Visibility.Visible;

        EditorColumn.Width = new GridLength(1, GridUnitType.Star);
        SplitterColumn.Width = new GridLength(6, GridUnitType.Pixel);
        PreviewColumn.Width = new GridLength(1, GridUnitType.Star);
    }

    private void OnPreviewFlagsChanged(object sender, RoutedEventArgs eventArgs)
    {
        ScheduleRenderPreview();
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs eventArgs)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Windows))
        {
            var delta = eventArgs.Delta > 0 ? 0.1 : -0.1;
            SetZoom(_zoomFactor + delta);
            eventArgs.Handled = true;
        }
    }

    private void OnZoomInClick(object sender, RoutedEventArgs eventArgs)
    {
        SetZoom(_zoomFactor + 0.1);
    }

    private void OnZoomOutClick(object sender, RoutedEventArgs eventArgs)
    {
        SetZoom(_zoomFactor - 0.1);
    }

    private void OnZoomResetClick(object sender, RoutedEventArgs eventArgs)
    {
        SetZoom(1.0);
    }

    private void SetZoom(double zoomFactor)
    {
        var clamped = Math.Clamp(zoomFactor, 0.25, 4.0);
        _zoomFactor = Math.Round(clamped, 2);
        ApplyZoom(_zoomFactor);
    }

    private void ApplyZoom(double zoomFactor)
    {
        PreviewWebView.ZoomFactor = zoomFactor;

        var percent = (int)Math.Round(zoomFactor * 100, MidpointRounding.AwayFromZero);
        ZoomText.Text = $"{percent}%";
    }

    private void ScheduleRenderPreview()
    {
        _renderDebounceCts?.Cancel();
        _renderDebounceCts?.Dispose();

        var cts = new CancellationTokenSource();
        _renderDebounceCts = cts;

        _ = RenderPreviewDebouncedAsync(cts.Token);
    }

    private async Task RenderPreviewDebouncedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(250, cancellationToken);
            await RenderPreviewAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
    }

    private async Task RenderPreviewAsync(CancellationToken cancellationToken)
    {
        await _previewGate.WaitAsync(cancellationToken);

        try
        {
            SetPreviewState(isReady: false, status: "Обновление предпросмотра");

            await EnsureWebViewsReadyAsync();

            var html = EndLabelHtmlTemplateRenderer.Render(_templateText, _response, _tenam);
            html = InjectPreviewCss(html);

            var previewFilePath = await SavePreviewHtmlAsync(html, cancellationToken);

            var baseUri = new Uri(previewFilePath, UriKind.Absolute);
            var builder = new UriBuilder(baseUri)
            {
                Query = $"v={Interlocked.Increment(ref _previewVersion)}"
            };

            PreviewWebView.Source = builder.Uri;

            ApplyZoom(_zoomFactor);
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch
        {
            SetPreviewState(isReady: false, status: "Ошибка предпросмотра");
        }
        finally
        {
            _previewGate.Release();
        }
    }

    private string InjectPreviewCss(string html)
    {
        var showGrid = ShowGridCheckBox.IsChecked == true;
        var showBounds = ShowBoundsCheckBox.IsChecked == true;

        var bodyClass = "lf-preview";
        if (showGrid)
        {
            bodyClass += " lf-grid";
        }

        if (!showBounds)
        {
            bodyClass += " lf-hide-bounds";
        }

        var css = $@"
<style>
@media screen {{
    html, body {{
        height: 100%;
    }}

    body {{
        margin: 0;
        padding: 24px;
        background: #ffffff;
        overflow: auto;
    }}

    body.lf-grid {{
        background-image:
            repeating-linear-gradient(0deg, rgba(0,0,0,0.08) 0, rgba(0,0,0,0.08) 1px, transparent 1px, transparent 16px),
            repeating-linear-gradient(90deg, rgba(0,0,0,0.08) 0, rgba(0,0,0,0.08) 1px, transparent 1px, transparent 16px);
    }}

    .label {{
        margin: 24px auto;
        background: #ffffff;
        box-shadow: 0 10px 28px rgba(0,0,0,0.14);
    }}

    body.lf-hide-bounds .label {{
        outline: none !important;
    }}

    body:not(.lf-hide-bounds) .label {{
        outline: 2px solid rgba(0,0,0,0.85);
        outline-offset: 0;
    }}
}}

@media print {{
    body {{
        background: #ffffff !important;
    }}

    body.lf-grid {{
        background-image: none !important;
    }}
}}
</style>
";

        var result = html;

        if (result.Contains("<head>", StringComparison.OrdinalIgnoreCase) && result.Contains("</head>", StringComparison.OrdinalIgnoreCase))
        {
            result = ReplaceFirstIgnoreCase(result, "<head>", "<head>" + css);
        }
        else
        {
            result = css + result;
        }

        if (result.Contains("<body", StringComparison.OrdinalIgnoreCase))
        {
            result = AddBodyClass(result, bodyClass);
        }

        return result;
    }

    private static string ReplaceFirstIgnoreCase(string input, string search, string replace)
    {
        var index = input.IndexOf(search, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return input;
        }

        return input.Substring(0, index) + replace + input.Substring(index + search.Length);
    }

    private static string AddBodyClass(string html, string classToAdd)
    {
        var bodyIndex = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
        if (bodyIndex < 0)
        {
            return html;
        }

        var tagEndIndex = html.IndexOf(">", bodyIndex, StringComparison.Ordinal);
        if (tagEndIndex < 0)
        {
            return html;
        }

        var bodyTag = html.Substring(bodyIndex, tagEndIndex - bodyIndex + 1);

        if (bodyTag.Contains("class=", StringComparison.OrdinalIgnoreCase))
        {
            var classIndex = bodyTag.IndexOf("class=", StringComparison.OrdinalIgnoreCase);
            var quoteIndex = bodyTag.IndexOf('"', classIndex);
            if (quoteIndex < 0)
            {
                return html;
            }

            var quoteEndIndex = bodyTag.IndexOf('"', quoteIndex + 1);
            if (quoteEndIndex < 0)
            {
                return html;
            }

            var existing = bodyTag.Substring(quoteIndex + 1, quoteEndIndex - quoteIndex - 1);
            var combined = string.IsNullOrWhiteSpace(existing) ? classToAdd : $"{existing} {classToAdd}";
            var updatedBodyTag = bodyTag.Substring(0, quoteIndex + 1) + combined + bodyTag.Substring(quoteEndIndex);

            return html.Substring(0, bodyIndex) + updatedBodyTag + html.Substring(tagEndIndex + 1);
        }

        var insertPos = bodyTag.Length - 1;
        var updated = bodyTag.Substring(0, insertPos) + $" class=\"{classToAdd}\"" + bodyTag.Substring(insertPos);
        return html.Substring(0, bodyIndex) + updated + html.Substring(tagEndIndex + 1);
    }

    private async void OnSaveClick(object sender, RoutedEventArgs eventArgs)
    {
        await SaveTemplateAsync();
    }

    private async Task SaveTemplateAsync()
    {
        if (!_isEditorReady)
        {
            return;
        }

        try
        {
            SaveButton.IsEnabled = false;
            SetStatus("Сохранение");

            var templatePath = EndLabelTemplateStore.GetTemplatePath();
            var directory = Path.GetDirectoryName(templatePath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(
                templatePath,
                _templateText,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                CancellationToken.None
            );

            _isDirty = false;
            UpdateSaveButtonState();

            SetStatus("Сохранено");
            _ = ClearStatusLaterAsync();
        }
        catch (Exception exception)
        {
            SetStatus("Ошибка сохранения");
            MessageBox.Show(this, exception.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task ClearStatusLaterAsync()
    {
        try
        {
            await Task.Delay(1500);
            if (!_isDirty)
            {
                SetStatus(string.Empty);
            }
        }
        catch
        {
            // ignore
        }
    }

    private void UpdateSaveButtonState()
    {
        SaveButton.IsEnabled = _isEditorReady && _isDirty;
    }

    private void OnPrintClick(object sender, RoutedEventArgs eventArgs)
    {
        _ = PrintAsync();
    }

    private async Task PrintAsync()
    {
        try
        {
            if (!_isPreviewReady)
            {
                SetStatus("Предпросмотр загружается");
                return;
            }

            if (PreviewWebView.CoreWebView2 is null)
            {
                return;
            }

            PrintButton.IsEnabled = false;
            SetStatus("Печать");

            var didPrint = await TryPrintToPreferredPrinterAsync(PreviewWebView, CancellationToken.None);

            if (!didPrint)
            {
                PreviewWebView.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Ошибка печати", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            PrintButton.IsEnabled = _isPreviewReady;

            if (_isPreviewReady && string.Equals(StatusText.Text, "Печать", StringComparison.Ordinal))
            {
                SetStatus(string.Empty);
            }
        }
    }

    private static async Task<bool> TryPrintToPreferredPrinterAsync(WebView2 webView, CancellationToken cancellationToken)
    {
        if (webView.CoreWebView2 is null)
        {
            return false;
        }

        if (!IsPrinterInstalled(PreferredPrinterName))
        {
            return false;
        }

        for (var i = 0; i < PreferredCopies; i++)
        {
            var settings = webView.CoreWebView2.Environment.CreatePrintSettings();
            settings.PrinterName = PreferredPrinterName;

            var status = await webView.CoreWebView2.PrintAsync(settings);
            if (status != CoreWebView2PrintStatus.Succeeded)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPrinterInstalled(string printerName)
    {
        foreach (var installedPrinter in PrinterSettings.InstalledPrinters)
        {
            if (installedPrinter is string name
                && string.Equals(name, printerName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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
        SetStatus(status);
    }

    private void SetStatus(string status)
    {
        StatusText.Text = status;
    }

    private sealed record TemplateToken(string Label, string InsertText, string Detail);
}
