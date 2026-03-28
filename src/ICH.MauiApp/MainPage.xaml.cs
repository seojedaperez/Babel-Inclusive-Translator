using ICH.MauiApp.ViewModels;
#if WINDOWS
using Microsoft.Web.WebView2.Core;
#endif

namespace ICH.MauiApp;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _viewModel;
    private readonly ICH.MauiApp.Services.SignalRService _signalRService;
    private bool _webViewConfigured = false;

    public MainPage(MainViewModel viewModel, ICH.MauiApp.Services.SignalRService signalRService)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _signalRService = signalRService;
        BindingContext = _viewModel;

        // Wire up SignalR events (if backend is available)
        WireUpSignalREvents();

        // Handle navigation events from JS (maui://startSession, maui://stopSession)
        HubWebView.Navigating += OnWebViewNavigating;

#if WINDOWS
        // On Windows: use virtual host mapping for proper origin (fixes mic/speech permissions)
        HubWebView.HandlerChanged += OnHandlerChanged;
#else
        // Fallback for non-Windows: load HTML inline
        LoadHtmlUI();
#endif
    }

#if WINDOWS
    private async void OnHandlerChanged(object? sender, EventArgs e)
    {
        if (_webViewConfigured) return;

        if (HubWebView.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 nativeWebView)
        {
            try
            {
                // Wait for the CoreWebView2 to be fully initialized
                await nativeWebView.EnsureCoreWebView2Async();
                var core = nativeWebView.CoreWebView2;
                if (core == null) return;

                _webViewConfigured = true;

                // ── 1. Auto-grant microphone (and any other) permissions ──
                core.PermissionRequested += (_, permArgs) =>
                {
                    permArgs.State = CoreWebView2PermissionState.Allow;
                    System.Diagnostics.Debug.WriteLine($"[WebView2] Permission auto-granted: {permArgs.PermissionKind}");
                };

                // ── 1b. Listen for postMessage from JS (safe channel, no navigation side-effects) ──
                core.WebMessageReceived += OnWebMessageReceived;

                // ── 2. Find the folder that contains hub.html ──
                string assetsPath = FindAssetsFolder();
                System.Diagnostics.Debug.WriteLine($"[WebView2] Assets path resolved to: {assetsPath}");

                // ── 3. Map a virtual host name to that folder ──
                //   This gives the page a real origin (https://inclusive-hub.local)
                //   so getUserMedia, SpeechRecognition, enumerateDevices all work properly.
                core.SetVirtualHostNameToFolderMapping(
                    "inclusive-hub.local",
                    assetsPath,
                    CoreWebView2HostResourceAccessKind.Allow);

                // ── 4. Navigate to the HTML via the virtual host ──
                core.Navigate("https://inclusive-hub.local/hub.html");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WebView2] Setup failed, falling back to inline HTML: {ex.Message}");
                LoadHtmlUI();
            }
        }
    }

    /// <summary>
    /// Search common locations for the folder containing hub.html.
    /// MAUI puts MauiAsset files in different spots depending on how the app runs.
    /// </summary>
    private static string FindAssetsFolder()
    {
        string[] candidates =
        [
            AppContext.BaseDirectory,
            Path.Combine(AppContext.BaseDirectory, "AppX"),
            Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? "",
        ];

        foreach (var dir in candidates)
        {
            if (!string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "hub.html")))
            {
                return dir;
            }
        }

        // Last resort: walk up from base directory
        var search = AppContext.BaseDirectory;
        for (int i = 0; i < 4; i++)
        {
            if (File.Exists(Path.Combine(search, "hub.html"))) return search;
            var parent = Path.GetDirectoryName(search);
            if (parent == null || parent == search) break;
            search = parent;
        }

        return AppContext.BaseDirectory; // give up, use base
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var message = e.TryGetWebMessageAsString();
            System.Diagnostics.Debug.WriteLine($"[WebView2] PostMessage received: {message}");
            switch (message?.ToLowerInvariant())
            {
                case "startsession":
                    _viewModel.StartPipelineCommand.Execute(null);
                    System.Diagnostics.Debug.WriteLine("[MainPage] Session started via postMessage");
                    break;
                case "stopsession":
                    _viewModel.StopPipelineCommand.Execute(null);
                    System.Diagnostics.Debug.WriteLine("[MainPage] Session stopped via postMessage");
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebView2] PostMessage error: {ex.Message}");
        }
    }
#endif

    /// <summary>
    /// Fallback loader: reads hub.html and injects it as inline HTML.
    /// Only used if virtual host mapping is not available.
    /// </summary>
    private async void LoadHtmlUI()
    {
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync("hub.html");
            using var reader = new StreamReader(stream);
            var html = await reader.ReadToEndAsync();
            HubWebView.Source = new HtmlWebViewSource { Html = html };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load HTML: {ex.Message}");
            HubWebView.Source = new HtmlWebViewSource
            {
                Html = $"<html><body style='background:#0b1326;color:#dae2fd;font-family:Inter,sans-serif;display:flex;align-items:center;justify-content:center;height:100vh;'><h1>Loading error: {ex.Message}</h1></body></html>"
            };
        }
    }

    private void WireUpSignalREvents()
    {
        _signalRService.SubtitleReceived += (original, translated, source, target, isFinal) =>
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await CallJs($"updateSubtitles('{Escape(original)}', '{Escape(translated)}')");
                await CallJs($"updateLanguages('{Escape(source)}', '{Escape(target)}')");
            });
        };

        _signalRService.ConnectionStatusChanged += (isConnected) =>
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await CallJs($"updateConnectionStatus({(isConnected ? "true" : "false")})");
            });
        };

        _signalRService.PipelineStatusReceived += (sessionId, inputActive, outputActive, inputLatency, outputLatency, transcriptCount) =>
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                var active = inputActive || outputActive;
                var latency = Math.Max(inputLatency, outputLatency);
                await CallJs($"updateLatency({latency:F0})");
                await CallJs($"updatePipelineStatus({(active ? "true" : "false")}, {transcriptCount})");
            });
        };

        _signalRService.SignLanguageGestureReceived += (word, animationId, durationMs, index) =>
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await CallJs($"updateSignWordLocal('{Escape(word)}')");
            });
        };

        _signalRService.TranscriptReceived += (original, translated, source, target, speakerId, confidence, emotion) =>
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await CallJs($"updateSubtitles('{Escape(original)}', '{Escape(translated)}')");
            });
        };
    }

    private void OnWebViewNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (e.Url.StartsWith("maui://"))
        {
            e.Cancel = true;

            var command = e.Url.Replace("maui://", "").ToLowerInvariant();
            switch (command)
            {
                case "startsession":
                    _viewModel.StartPipelineCommand.Execute(null);
                    System.Diagnostics.Debug.WriteLine("[MainPage] Session started from WebView");
                    break;
                case "stopsession":
                    _viewModel.StopPipelineCommand.Execute(null);
                    System.Diagnostics.Debug.WriteLine("[MainPage] Session stopped from WebView");
                    break;
            }
        }
    }

    private async Task CallJs(string js)
    {
        try
        {
            await HubWebView.EvaluateJavaScriptAsync(js);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"JS call failed: {ex.Message}");
        }
    }

    private static string Escape(string? input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        return input
            .Replace("\\", "\\\\")
            .Replace("'", "\\'")
            .Replace("\n", "\\n")
            .Replace("\r", "");
    }
}
