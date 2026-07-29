using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Maui.Storage;

#if WINDOWS
using System.Runtime.InteropServices;
#endif

namespace ytDownloader;

public partial class MainPage : ContentPage
{
    private readonly string _appDir;
    private readonly string _ytDlpPath;
    private string _downloadDir;
    private string _safeAppDir;
    private string _lastPastedLink = string.Empty;
    private bool _isInitializing = true;

#if WINDOWS
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_RESTORE = 9;
#endif

    public MainPage()
    {
        InitializeComponent();

        _appDir = AppDomain.CurrentDomain.BaseDirectory;
        _safeAppDir = _appDir.TrimEnd('\\');
        _ytDlpPath = Path.Combine(_safeAppDir, "yt-dlp.exe");

        string defaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ytDownloader_İndirilenler");
        _downloadDir = Preferences.Default.Get("DownloadDir", defaultDir);
        if (!Directory.Exists(_downloadDir)) Directory.CreateDirectory(_downloadDir);
        FolderEntry.Text = _downloadDir;

        AutoPasteSwitch.IsToggled = Preferences.Default.Get("AutoPaste", true);
        BringToFrontSwitch.IsToggled = Preferences.Default.Get("BringToFront", true);
        AutoOpenFolderSwitch.IsToggled = Preferences.Default.Get("AutoOpenFolder", false);

        AutoPasteSwitch.Toggled += (s, e) => Preferences.Default.Set("AutoPaste", e.Value);
        BringToFrontSwitch.Toggled += (s, e) => Preferences.Default.Set("BringToFront", e.Value);
        AutoOpenFolderSwitch.Toggled += (s, e) => Preferences.Default.Set("AutoOpenFolder", e.Value);

        ThemePicker.SelectedIndex = Preferences.Default.Get("AppTheme", 1);
        ColorPicker.SelectedIndex = Preferences.Default.Get("AppColor", 0);
        DefaultAudioPicker.SelectedIndex = Preferences.Default.Get("DefaultAudio", 0);
        DefaultVideoPicker.SelectedIndex = Preferences.Default.Get("DefaultVideo", 0);

        ApplyThemeAndColor();

        _isInitializing = false;
        LoadQualityOptions(true);
        _ = InitializeSystemAsync();

        Clipboard.Default.ClipboardContentChanged += OnClipboardContentChanged;
        _ = CheckClipboardOnStartupAsync();
    }

    private void OnThemeOrColorChanged(object? sender, EventArgs e)
    {
        if (_isInitializing) return;

        Preferences.Default.Set("AppTheme", ThemePicker.SelectedIndex);
        Preferences.Default.Set("AppColor", ColorPicker.SelectedIndex);
        ApplyThemeAndColor();
    }

    private void ApplyThemeAndColor()
    {
        switch (ThemePicker.SelectedIndex)
        {
            case 0:
                Resources["BgColor"] = Color.FromArgb("#F5F5F5");
                Resources["PanelBgColor"] = Color.FromArgb("#FFFFFF");
                Resources["TextColor"] = Color.FromArgb("#000000");
                Resources["SubTextColor"] = Color.FromArgb("#555555");
                Resources["EntryBgColor"] = Color.FromArgb("#EAEAEA");
                Resources["BorderColor"] = Color.FromArgb("#DDDDDD");
                break;
            case 2:
                Resources["BgColor"] = Color.FromArgb("#000000");
                Resources["PanelBgColor"] = Color.FromArgb("#0A0A0A");
                Resources["TextColor"] = Color.FromArgb("#FFFFFF");
                Resources["SubTextColor"] = Color.FromArgb("#999999");
                Resources["EntryBgColor"] = Color.FromArgb("#111111");
                Resources["BorderColor"] = Color.FromArgb("#222222");
                break;
            case 3:
                Resources["BgColor"] = Color.FromArgb("#0D0D0D");
                Resources["PanelBgColor"] = Color.FromArgb("#121212");
                Resources["TextColor"] = Color.FromArgb("#00FF41");
                Resources["SubTextColor"] = Color.FromArgb("#008F11");
                Resources["EntryBgColor"] = Color.FromArgb("#1A1A1A");
                Resources["BorderColor"] = Color.FromArgb("#008F11");
                break;
            default:
                Resources["BgColor"] = Color.FromArgb("#1E1E1E");
                Resources["PanelBgColor"] = Color.FromArgb("#2A2A2A");
                Resources["TextColor"] = Color.FromArgb("#FFFFFF");
                Resources["SubTextColor"] = Color.FromArgb("#CCCCCC");
                Resources["EntryBgColor"] = Color.FromArgb("#333333");
                Resources["BorderColor"] = Color.FromArgb("#444444");
                break;
        }

        string accentHex = ColorPicker.SelectedIndex switch
        {
            1 => "#00ADB5",
            2 => "#1DB954",
            3 => "#8A2BE2",
            4 => "#FF8C00",
            5 => "#FF69B4",
            6 => "#1E90FF",
            7 => "#FFD700",
            _ => "#E50914"
        };

        if (ThemePicker.SelectedIndex == 3 && ColorPicker.SelectedIndex == 0)
            accentHex = "#00FF41";

        Resources["AccentColor"] = Color.FromArgb(accentHex);
    }

    private void OnSettingsClicked(object? sender, EventArgs e)
    {
        SettingsPanel.IsVisible = !SettingsPanel.IsVisible;
    }

    private void OnDefaultQualityChanged(object? sender, EventArgs e)
    {
        if (_isInitializing) return;
        Preferences.Default.Set("DefaultAudio", DefaultAudioPicker.SelectedIndex);
        Preferences.Default.Set("DefaultVideo", DefaultVideoPicker.SelectedIndex);
        LoadQualityOptions(RadioMp3?.IsChecked ?? true);
    }

    private void OnFormatChanged(object? sender, CheckedChangedEventArgs e)
    {
        if (QualityPicker != null)
        {
            LoadQualityOptions(RadioMp3?.IsChecked ?? true);
        }
    }

    private void LoadQualityOptions(bool isMp3)
    {
        if (QualityPicker == null) return;
        QualityPicker.Items.Clear();

        if (isMp3)
        {
            QualityPicker.Items.Add("320 Kbps (Yüksek)");
            QualityPicker.Items.Add("192 Kbps (Standart)");
            QualityPicker.Items.Add("128 Kbps (Düşük)");
            QualityPicker.SelectedIndex = Preferences.Default.Get("DefaultAudio", 0);
        }
        else
        {
            QualityPicker.Items.Add("En İyi (Maksimum)");
            QualityPicker.Items.Add("1080p (FHD)");
            QualityPicker.Items.Add("720p (HD)");
            QualityPicker.Items.Add("480p (SD)");
            QualityPicker.SelectedIndex = Preferences.Default.Get("DefaultVideo", 0);
        }
    }

    private async Task CheckClipboardOnStartupAsync()
    {
        if (Clipboard.Default.HasText)
        {
            string? text = await Clipboard.Default.GetTextAsync();
            ProcessClipboardText(text);
        }
    }

    private async void OnClipboardContentChanged(object? sender, EventArgs e)
    {
        if (Clipboard.Default.HasText)
        {
            await Task.Delay(100);
            string? text = await Clipboard.Default.GetTextAsync();
            ProcessClipboardText(text);
        }
    }

    private void ProcessClipboardText(string? text)
    {
        if (!Preferences.Default.Get("AutoPaste", true)) return;
        if (string.IsNullOrWhiteSpace(text)) return;

        text = text.Trim();
        string ytRegex = @"^(https?://)?(www\.|m\.)?(youtube\.com|youtu\.be)/.+$";

        if (Regex.IsMatch(text, ytRegex, RegexOptions.IgnoreCase))
        {
            if (text == _lastPastedLink) return;
            _lastPastedLink = text;

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (Preferences.Default.Get("BringToFront", true))
                {
#if WINDOWS
                    var platformWindow = App.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
                    if (platformWindow != null)
                    {
                        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(platformWindow);
                        ShowWindow(hwnd, SW_RESTORE);
                        SetForegroundWindow(hwnd);
                    }
#endif
                }
                UrlEntry.Text = text;
                await FetchVideoInfoAsync(text);
            });
        }
    }

    private async Task InitializeSystemAsync()
    {
        try
        {
            DownloadBtn.IsEnabled = false;
            FetchInfoBtn.IsEnabled = false;

            if (!File.Exists(_ytDlpPath))
            {
                StatusLabel.Text = "yt-dlp bulunamadı, indiriliyor...";
                using var client = new HttpClient();
                var bytes = await client.GetByteArrayAsync("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe");
                await File.WriteAllBytesAsync(_ytDlpPath, bytes);
            }

            StatusLabel.Text = "Sistem güncelleniyor...";

#if WINDOWS
            var processInfo = new ProcessStartInfo
            {
                FileName = _ytDlpPath,
                Arguments = "-U",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var process = Process.Start(processInfo);
            if (process != null) await process.WaitForExitAsync();
#endif
            StatusLabel.Text = "Sistem hazır! Sürüm güncel.";
            DownloadBtn.IsEnabled = true;
            FetchInfoBtn.IsEnabled = true;
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Hata: {ex.Message}";
        }
    }

    private async void OnSelectFolderClicked(object? sender, EventArgs e)
    {
#if WINDOWS
        var folderPicker = new Windows.Storage.Pickers.FolderPicker();
        folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads;
        folderPicker.FileTypeFilter.Add("*");

        var window = App.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
        if (window != null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
        }

        var result = await folderPicker.PickSingleFolderAsync();
        if (result != null)
        {
            _downloadDir = result.Path;
            FolderEntry.Text = _downloadDir;
            Preferences.Default.Set("DownloadDir", _downloadDir);
        }
#else
        await Task.CompletedTask;
#endif
    }

    private void OnOpenFolderClicked(object? sender, EventArgs e)
    {
#if WINDOWS
        if (Directory.Exists(_downloadDir))
        {
            Process.Start("explorer.exe", _downloadDir);
        }
#endif
    }

    private async void OnFetchInfoClicked(object? sender, EventArgs e)
    {
        string link = UrlEntry.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(link)) return;

        _lastPastedLink = link;
        await FetchVideoInfoAsync(link);
    }

    private async Task FetchVideoInfoAsync(string link)
    {
        FetchInfoBtn.IsEnabled = false;
        FetchInfoBtn.Text = "...";

        await Task.Run(() =>
        {
#if WINDOWS
            try
            {
                string arguments = $"--dump-json --playlist-items 1 --no-warnings \"{link}\"";
                var processInfo = new ProcessStartInfo
                {
                    FileName = _ytDlpPath,
                    Arguments = arguments,
                    WorkingDirectory = _safeAppDir,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };

                using var process = Process.Start(processInfo);
                string jsonOutput = process.StandardOutput.ReadToEnd() ?? string.Empty;
                process.WaitForExit();

                if (!string.IsNullOrEmpty(jsonOutput))
                {
                    using JsonDocument doc = JsonDocument.Parse(jsonOutput);
                    JsonElement root = doc.RootElement;

                    string title = root.GetProperty("title").GetString() ?? "Bilinmeyen Başlık";
                    string thumbnail = root.GetProperty("thumbnail").GetString() ?? string.Empty;

                    string durationText = "--:--";
                    if (root.TryGetProperty("duration_string", out JsonElement durationElement))
                    {
                        durationText = durationElement.GetString() ?? "--:--";
                    }

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        VideoTitleLabel.Text = title;
                        VideoDurationLabel.Text = $"⏱ Süre: {durationText}";
                        if (!string.IsNullOrEmpty(thumbnail))
                        {
                            ThumbnailImage.Source = ImageSource.FromUri(new Uri(thumbnail));
                        }
                        TestVideoBtn.IsVisible = true;
                    });
                }
            }
            catch { }
#endif
        });

        FetchInfoBtn.Text = "BUL";
        FetchInfoBtn.IsEnabled = true;
    }

    private async void OnDownloadClicked(object? sender, EventArgs e)
    {
        string link = UrlEntry.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(link))
        {
            await DisplayAlert("Hata", "Lütfen geçerli bir YouTube linki yapıştırın.", "Tamam");
            return;
        }

        string ffmpegPath = Path.Combine(_safeAppDir, "ffmpeg.exe");
        if (!File.Exists(ffmpegPath))
        {
            await DisplayAlert("Kritik Hata", "ffmpeg.exe klasörde bulunamadı.", "Tamam");
            return;
        }

        bool isMp3 = RadioMp3?.IsChecked ?? true;
        string selectedQuality = QualityPicker?.SelectedItem?.ToString() ?? "";
        DownloadBtn.IsEnabled = false;
        UrlEntry.IsEnabled = false;
        ProgressContainer.IsVisible = true;
        DownloadProgressBar.Progress = 0;
        ProgressText.Text = "Bağlantı kuruluyor...";

        ProgressText.TextColor = (Color)Resources["TextColor"];

        await Task.Run(() => StartDownload(link, isMp3, selectedQuality));

        DownloadBtn.IsEnabled = true;
        UrlEntry.IsEnabled = true;
        ProgressText.Text = "İşlem Başarıyla Tamamlandı!";

        if (Preferences.Default.Get("AutoOpenFolder", false))
        {
#if WINDOWS
            if (Directory.Exists(_downloadDir))
                Process.Start("explorer.exe", _downloadDir);
#endif
        }
        else
        {
            await DisplayAlert("Başarılı", $"İndirme tamamlandı!\nDosyalar şuraya kaydedildi:\n{_downloadDir}", "Tamam");
        }
    }

    private void StartDownload(string link, bool isMp3, string qualityString)
    {
#if WINDOWS
        string safeAppDir = _appDir.TrimEnd('\\');
        string fastArgs = "";

        if (isMp3)
        {
            fastArgs = $"--newline --no-color --no-warnings -N 16 --http-chunk-size 10M " +
                       $"--extractor-args \"youtube:player_client=android,web\" " +
                       $"--no-overwrites --continue --windows-filenames " +
                       $"--sponsorblock-remove all --no-write-info-json " +
                       $"--clean-info-json --lazy-playlist " +
                       $"--ffmpeg-location \"{safeAppDir}\"";
        }
        else
        {
            fastArgs = $"--newline --no-color --no-warnings -N 4 " +
                       $"--sleep-requests 1 --sleep-interval 2 " +
                       $"--no-overwrites --continue --windows-filenames " +
                       $"--sponsorblock-remove all --no-write-info-json " +
                       $"--clean-info-json --lazy-playlist " +
                       $"--ffmpeg-location \"{safeAppDir}\"";
        }

        string outputTemplate = $"-o \"{_downloadDir}\\%(playlist_title|Tekli_Indirmeler)s\\%(title)s.%(ext)s\"";
        string modeArgs = "";

        if (isMp3)
        {
            string kbps = Regex.Match(qualityString, @"\d+").Value;
            if (string.IsNullOrEmpty(kbps)) kbps = "192";
            modeArgs = $"-f bestaudio/best -x --audio-format mp3 --audio-quality {kbps}K --embed-metadata --embed-thumbnail";
        }
        else
        {
            if (qualityString.Contains("En İyi"))
            {
                modeArgs = "-f bv*+ba/b -S ext:mp4:m4a,vcodec:h264 --merge-output-format mp4 --embed-metadata";
            }
            else
            {
                string res = Regex.Match(qualityString, @"\d{3,4}").Value;
                modeArgs = $"-f bv*+ba/b -S res:{res},ext:mp4:m4a,vcodec:h264 --merge-output-format mp4 --embed-metadata";
            }
        }

        string arguments = $"{fastArgs} {modeArgs} {outputTemplate} \"{link}\"";

        var processInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(safeAppDir, "yt-dlp.exe"),
            Arguments = arguments,
            WorkingDirectory = safeAppDir,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8
        };

        using var process = new Process { StartInfo = processInfo };
        var regex = new Regex(@"\[download\]\s+([\d.]+)%");

        process.OutputDataReceived += (sender, e) =>
        {
            // Null başvuru (CS8602) uyarıları tamamen giderildi!
            string outputLine = e.Data ?? string.Empty;

            if (!string.IsNullOrEmpty(outputLine))
            {
                var match = regex.Match(outputLine);
                if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double progress))
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        DownloadProgressBar.Progress = progress / 100.0;
                        ProgressText.Text = $"İndiriliyor: %{progress:F1}";
                    });
                }
                else if (outputLine.Contains("[ExtractAudio]") || outputLine.Contains("[Merger]"))
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        DownloadProgressBar.Progress = 1.0;
                        ProgressText.Text = "Dönüştürülüyor ve kalite ayarlanıyor (Lütfen bekleyin)...";
                    });
                }
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            string errorLine = e.Data ?? string.Empty;

            if (!string.IsNullOrEmpty(errorLine))
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ProgressText.Text = $"UYARI/HATA: {errorLine}";
                });
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();
#endif
    }

    private async void OnTestVideoClicked(object? sender, EventArgs e)
    {
        string link = UrlEntry.Text?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(link))
        {
            await Launcher.Default.OpenAsync(link);
        }
    }
}