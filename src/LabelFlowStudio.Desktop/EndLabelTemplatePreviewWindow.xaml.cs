using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Desktop.Templates;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

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
    private bool _isSaving;
    private bool _pendingPreviewFlagsApply;

    private string _templateText = string.Empty;

    private double _zoomFactor = 1.0;

    private DispatcherTimer? _toastTimer;

    private const string PreferredPrinterName = "zebra_torec";
    private const int PreferredCopies = 2;

    // UI state (Tokens panel)
    private bool _tokensHidden;

    private static readonly TemplateToken[] Tokens =
    {
        new("{{Tenam}}", "{{Tenam}}", "Код магазина"),
        new("{{BarcodeDataUri}}", "{{BarcodeDataUri}}", "Штрихкод (data:image/png;base64,...)"),
        new("{{Lfakdnr}}", "{{Lfakdnr}}", "Код заказа"),
        new("{{Gpbez}}", "{{Gpbez}}", "Адрес доставки строка 1"),
        new("{{Gport1}}", "{{Gport1}}", "Адрес доставки строка 2"),
        new("{{Gpstrasse}}", "{{Gpstrasse}}", "Адрес доставки строка 3"),
        new("{{Bstchgnam5}}", "{{Bstchgnam5}}", "Грузополучатель"),
        new("{{Brutto}}", "{{Brutto}}", "Вес брутто (кг)"),
        new("{{CountBst}}", "{{CountBst}}", "Короб в заказ"),
        new("{{SumBst}}", "{{SumBst}}", "Изделий в коробе")
    };

    private enum ViewMode
    {
        Preview,
        Split,
        Code
    }

    private ViewMode _mode = ViewMode.Preview;

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

        CommandBindings.Add(new CommandBinding(ApplicationCommands.Open, (_, _) => OnImportClick(this, new RoutedEventArgs())));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.SaveAs, (_, _) => OnExportClick(this, new RoutedEventArgs())));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Print, async (_, _) => await PrintAsync()));

        InputBindings.Add(new KeyBinding(ApplicationCommands.Open, new KeyGesture(Key.O, ModifierKeys.Control)));
        InputBindings.Add(new KeyBinding(ApplicationCommands.SaveAs, new KeyGesture(Key.E, ModifierKeys.Control)));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Print, new KeyGesture(Key.P, ModifierKeys.Control)));
    }

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        Loaded -= OnLoaded;

        try
        {
            _tokensHidden = LoadUiStateTokensHidden();

            _templateText = await EndLabelTemplateStore.LoadOrCreateAsync(CancellationToken.None);

            await EnsureWebViewsReadyAsync();

            NavigateEditor();

            SetViewMode(ViewMode.Preview);

            ScheduleRenderPreview();
            ApplyZoom(_zoomFactor);

            UpdateWindowTitle();
        }
        catch (TimeoutException exception)
        {
            ShowToast("WebView2 не инициализировался");
            MessageBox.Show(this, exception.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception exception)
        {
            ShowToast("Ошибка загрузки");
            MessageBox.Show(this, exception.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        _renderDebounceCts?.Cancel();
        _renderDebounceCts?.Dispose();
        _renderDebounceCts = null;

        _toastTimer?.Stop();
        _toastTimer = null;
    }

    // ===========================
    // Window chrome handlers
    // ===========================

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (eventArgs.ClickCount == 2)
        {
            OnMaximizeRestoreClick(sender, new RoutedEventArgs());
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
            // ignore
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs eventArgs) => WindowState = WindowState.Minimized;

    private void OnMaximizeRestoreClick(object sender, RoutedEventArgs eventArgs)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnCloseClick(object sender, RoutedEventArgs eventArgs) => Close();

    // ===========================
    // View mode menu
    // ===========================

    private void OnViewModeMenuClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender == ViewPreviewMenuItem)
        {
            SetViewMode(ViewMode.Preview);
            return;
        }

        if (sender == ViewSplitMenuItem)
        {
            SetViewMode(ViewMode.Split);
            return;
        }

        SetViewMode(ViewMode.Code);
    }

    private void SetViewMode(ViewMode mode)
    {
        _mode = mode;

        ViewPreviewMenuItem.IsChecked = mode == ViewMode.Preview;
        ViewSplitMenuItem.IsChecked = mode == ViewMode.Split;
        ViewCodeMenuItem.IsChecked = mode == ViewMode.Code;

        ApplyModeLayout(mode);
    }

    private void ApplyModeLayout(ViewMode mode)
    {
        if (EditorWebView is null || PreviewWebView is null)
        {
            return;
        }

        if (mode == ViewMode.Code)
        {
            EditorWebView.Visibility = Visibility.Visible;
            PreviewHost.Visibility = Visibility.Collapsed;

            EditorColumn.Width = new GridLength(1, GridUnitType.Star);
            SplitterColumn.Width = new GridLength(0, GridUnitType.Pixel);
            PreviewColumn.Width = new GridLength(0, GridUnitType.Pixel);

            ToolsColumn.Width = new GridLength(0, GridUnitType.Pixel);
            PreviewToolsPanel.Visibility = Visibility.Collapsed;

            return;
        }

        if (mode == ViewMode.Preview)
        {
            EditorWebView.Visibility = Visibility.Collapsed;
            PreviewHost.Visibility = Visibility.Visible;

            EditorColumn.Width = new GridLength(0, GridUnitType.Pixel);
            SplitterColumn.Width = new GridLength(0, GridUnitType.Pixel);
            PreviewColumn.Width = new GridLength(1, GridUnitType.Star);

            ToolsColumn.Width = GridLength.Auto;
            PreviewToolsPanel.Visibility = Visibility.Visible;

            return;
        }

        // Split
        EditorWebView.Visibility = Visibility.Visible;
        PreviewHost.Visibility = Visibility.Visible;

        EditorColumn.Width = new GridLength(1, GridUnitType.Star);
        SplitterColumn.Width = new GridLength(6, GridUnitType.Pixel);
        PreviewColumn.Width = new GridLength(1, GridUnitType.Star);

        ToolsColumn.Width = GridLength.Auto;
        PreviewToolsPanel.Visibility = Visibility.Visible;

        Dispatcher.BeginInvoke(() =>
        {
            EditorWebView.Focus();
            Keyboard.Focus(EditorWebView);
        }, DispatcherPriority.Background);
    }

    // ===========================
    // WebView init
    // ===========================

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

        // ВАЖНО: чтобы не было "прозрачных дыр" при снапе/полуэкране
        PreviewWebView.DefaultBackgroundColor = Color.White;
        EditorWebView.DefaultBackgroundColor = Color.White;

        // Отключаем встроенный zoom Edge (он как раз зумит, но UI не знает)
        PreviewWebView.CoreWebView2.Settings.IsZoomControlEnabled = false;

        EditorWebView.CoreWebView2.WebMessageReceived -= OnEditorWebMessageReceived;
        EditorWebView.CoreWebView2.WebMessageReceived += OnEditorWebMessageReceived;

        EditorWebView.CoreWebView2.NavigationCompleted -= OnEditorNavigationCompleted;
        EditorWebView.CoreWebView2.NavigationCompleted += OnEditorNavigationCompleted;

        var contentRoot = AppContext.BaseDirectory;

        EditorWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "app",
            contentRoot,
            CoreWebView2HostResourceAccessKind.Allow
        );

        PreviewWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "app",
            contentRoot,
            CoreWebView2HostResourceAccessKind.Allow
        );
    }

    private static string GetWebViewUserDataFolder()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "LabelFlowStudio", "WebView2", $"pid-{Environment.ProcessId}");
    }

    private void NavigateEditor()
    {
        _isEditorReady = false;
        _isSaving = false;

        var editorUrl = "https://app/Editor/Editor.html";
        EditorWebView.Source = new Uri(editorUrl, UriKind.Absolute);
    }

    private void OnEditorNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs eventArgs)
    {
        if (!eventArgs.IsSuccess)
        {
            ShowToast("Не удалось открыть редактор");
        }
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
                    }),
                    tokensHidden = _tokensHidden
                };

                EditorWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(initPayload));
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
                UpdateWindowTitle();

                ScheduleRenderPreview();
                return;
            }

            if (string.Equals(type, "saveRequested", StringComparison.Ordinal))
            {
                _ = SaveTemplateAsync();
                return;
            }

            if (string.Equals(type, "tokensPanelToggled", StringComparison.Ordinal))
            {
                if (root.TryGetProperty("hidden", out var hiddenEl) && hiddenEl.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    _tokensHidden = hiddenEl.GetBoolean();
                    SaveUiStateTokensHidden(_tokensHidden);
                }

                return;
            }
        }
        catch
        {
            // ignore
        }
    }

    // ===========================
    // Menu actions
    // ===========================

    private async void OnImportClick(object sender, RoutedEventArgs eventArgs)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "HTML (*.html;*.htm)|*.html;*.htm|Все файлы (*.*)|*.*",
            Title = "Импорт шаблона"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var text = await File.ReadAllTextAsync(dialog.FileName, Encoding.UTF8);
            _templateText = text;

            _isDirty = true;
            UpdateWindowTitle();

            if (_isEditorReady && EditorWebView.CoreWebView2 is not null)
            {
                var payload = new { type = "setValue", value = _templateText };
                EditorWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload));
            }

            ScheduleRenderPreview();
            ShowToast("Импорт выполнен");
        }
        catch (Exception exception)
        {
            ShowToast("Ошибка импорта");
            MessageBox.Show(this, exception.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnExportClick(object sender, RoutedEventArgs eventArgs)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "HTML (*.html)|*.html|Все файлы (*.*)|*.*",
            Title = "Экспорт шаблона",
            FileName = "endlabel.html"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(dialog.FileName, _templateText, new UTF8Encoding(false));
            ShowToast("Экспорт выполнен");
        }
        catch (Exception exception)
        {
            ShowToast("Ошибка экспорта");
            MessageBox.Show(this, exception.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnPrintClick(object sender, RoutedEventArgs eventArgs) => _ = PrintAsync();

    private void OnEditorFlagsChanged(object sender, RoutedEventArgs eventArgs)
    {
        if (!_isPreviewReady)
        {
            _pendingPreviewFlagsApply = true;
            return;
        }

        _ = UpdatePreviewFlagsAsync();
    }

    private async Task UpdatePreviewFlagsAsync()
    {
        if (PreviewWebView.CoreWebView2 is null)
        {
            return;
        }

        var showGrid = ShowGridMenuItem.IsChecked;
        var showBounds = ShowBoundsMenuItem.IsChecked;

        var script =
            "(function () {" +
            "  const body = document.body;" +
            "  if (!body) return;" +
            "  body.classList.toggle('lf-grid', " + (showGrid ? "true" : "false") + ");" +
            "  body.classList.toggle('lf-hide-bounds', " + (showBounds ? "false" : "true") + ");" +
            "})();";

        try
        {
            await PreviewWebView.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch
        {
            // ignore
        }
    }

    // ===========================
    // Preview zoom (мы владеем зумом полностью)
    // ===========================

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs eventArgs)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Windows))
        {
            var delta = eventArgs.Delta > 0 ? 0.1 : -0.1;
            SetZoom(_zoomFactor + delta);
            eventArgs.Handled = true;
        }
    }

    private void OnZoomInClick(object sender, RoutedEventArgs eventArgs) => SetZoom(_zoomFactor + 0.1);
    private void OnZoomOutClick(object sender, RoutedEventArgs eventArgs) => SetZoom(_zoomFactor - 0.1);
    private void OnZoomResetClick(object sender, RoutedEventArgs eventArgs) => SetZoom(1.0);

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

    private void OnPreviewNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs eventArgs)
    {
        _isPreviewReady = eventArgs.IsSuccess;

        if (!_isPreviewReady)
        {
            ShowToast("Ошибка загрузки предпросмотра");
            return;
        }

        if (_pendingPreviewFlagsApply)
        {
            _pendingPreviewFlagsApply = false;
            _ = UpdatePreviewFlagsAsync();
        }

        ApplyZoom(_zoomFactor);
    }

    // ===========================
    // Render preview
    // ===========================

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
            await EnsureWebViewsReadyAsync();

            var html = EndLabelHtmlTemplateRenderer.Render(_templateText, _response, _tenam);
            html = InjectPreviewCss(html);

            if (PreviewWebView.CoreWebView2 is null)
            {
                return;
            }

            _pendingPreviewFlagsApply = true;
            PreviewWebView.CoreWebView2.NavigateToString(html);
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch
        {
            ShowToast("Ошибка предпросмотра");
        }
        finally
        {
            _previewGate.Release();
        }
    }

    private static string InjectPreviewCss(string html)
    {
        var css =
            "<style>\r\n" +
            "@media screen {\r\n" +
            "  html, body { height: 100%; }\r\n" +
            "  body {\r\n" +
            "    margin: 0;\r\n" +
            "    padding: 24px;\r\n" +
            "    background: #ffffff;\r\n" +
            "    overflow: auto;\r\n" +
            "  }\r\n" +
            "  body.lf-grid {\r\n" +
            "    background-image:\r\n" +
            "      repeating-linear-gradient(0deg, rgba(0,0,0,0.08) 0, rgba(0,0,0,0.08) 1px, transparent 1px, transparent 16px),\r\n" +
            "      repeating-linear-gradient(90deg, rgba(0,0,0,0.08) 0, rgba(0,0,0,0.08) 1px, transparent 1px, transparent 16px);\r\n" +
            "  }\r\n" +
            "  .label {\r\n" +
            "    margin: 24px auto;\r\n" +
            "    background: #ffffff;\r\n" +
            "    box-shadow: 0 10px 28px rgba(0,0,0,0.14);\r\n" +
            "  }\r\n" +
            "  body.lf-hide-bounds .label { outline: none !important; }\r\n" +
            "  body:not(.lf-hide-bounds) .label {\r\n" +
            "    outline: 2px solid rgba(0,0,0,0.85);\r\n" +
            "    outline-offset: 0;\r\n" +
            "  }\r\n" +
            "}\r\n" +
            "@media print {\r\n" +
            "  body { background: #ffffff !important; }\r\n" +
            "  body.lf-grid { background-image: none !important; }\r\n" +
            "}\r\n" +
            "</style>\r\n";

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
            result = AddBodyClass(result, "lf-preview");
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

    // ===========================
    // Save + title
    // ===========================

    private async Task SaveTemplateAsync()
    {
        if (!_isEditorReady)
        {
            return;
        }

        if (_isSaving)
        {
            return;
        }

        _isSaving = true;

        try
        {
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
            UpdateWindowTitle();

            ShowToast("Сохранено");
        }
        catch (Exception exception)
        {
            ShowToast("Ошибка сохранения");
            MessageBox.Show(this, exception.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isSaving = false;
        }
    }

    private void UpdateWindowTitle()
    {
        Title = _isDirty ? "Торцевая этикетка *" : "Торцевая этикетка";
    }

    // ===========================
    // Print
    // ===========================

    private async Task PrintAsync()
    {
        try
        {
            if (!_isPreviewReady)
            {
                ShowToast("Предпросмотр ещё загружается");
                return;
            }

            if (PreviewWebView.CoreWebView2 is null)
            {
                return;
            }

            var didPrint = await TryPrintToPreferredPrinterAsync(PreviewWebView, CancellationToken.None);

            if (!didPrint)
            {
                PreviewWebView.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
                ShowToast("Открыто окно печати");
                return;
            }

            ShowToast("Печать отправлена");
        }
        catch (Exception exception)
        {
            ShowToast("Ошибка печати");
            MessageBox.Show(this, exception.Message, "Ошибка печати", MessageBoxButton.OK, MessageBoxImage.Error);
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
            if (installedPrinter is string name && string.Equals(name, printerName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // ===========================
    // UI state persistence
    // ===========================

    private static string GetUiStatePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(localAppData, "LabelFlowStudio");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "endlabel-editor-state.json");
    }

    private static bool LoadUiStateTokensHidden()
    {
        try
        {
            var path = GetUiStatePath();
            if (!File.Exists(path))
            {
                return false;
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("tokensHidden", out var el) && el.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return el.GetBoolean();
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static void SaveUiStateTokensHidden(bool hidden)
    {
        try
        {
            var path = GetUiStatePath();
            var payload = JsonSerializer.Serialize(new { tokensHidden = hidden });
            File.WriteAllText(path, payload, new UTF8Encoding(false));
        }
        catch
        {
            // ignore
        }
    }

    // ===========================
    // Toast
    // ===========================

    private void ShowToast(string message, int milliseconds = 1600)
    {
        ToastText.Text = message;
        ToastPanel.Visibility = Visibility.Visible;

        _toastTimer?.Stop();

        _toastTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(milliseconds)
        };

        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer?.Stop();
            ToastPanel.Visibility = Visibility.Collapsed;
        };

        _toastTimer.Start();
    }

    private sealed record TemplateToken(string Label, string InsertText, string Detail);
}
