using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Maui.Storage;

#if WINDOWS
using System.Runtime.InteropServices;
#endif

namespace ytDownloader;

public class VideoFormatInfo
{
    public string FormatId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public override string ToString() => DisplayName;
}

public class PlaylistItem : INotifyPropertyChanged
{
    public int Index { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Url => $"https://www.youtube.com/watch?v={Id}";

    public string Thumbnail => $"https://img.youtube.com/vi/{Id}/hqdefault.jpg";

    private bool _isAudioMode;
    public bool IsAudioMode
    {
        get => _isAudioMode;
        set { _isAudioMode = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ModeIcon))); }
    }
    public string ModeIcon => IsAudioMode ? "🎵" : "🎬";

    public string CustomFormatId { get; set; } = string.Empty;
    public string CustomFormatName { get; set; } = string.Empty;
    public string SelectedSubtitle { get; set; } = string.Empty;

    public string StatusText
    {
        get
        {
            string txt = "";
            if (!string.IsNullOrEmpty(CustomFormatName)) txt += $"{CustomFormatName} ";
            if (!string.IsNullOrEmpty(SelectedSubtitle)) txt += $"[Altyazı: {SelectedSubtitle}] ";
            return txt;
        }
    }
    public void RefreshStatus() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));

    private bool _isSelected = true;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); } }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class MainPage : ContentPage
{
    private readonly string _appDir;
    private readonly string _ytDlpPath;
    private string _downloadDir;
    private string _safeAppDir;
    private string _lastPastedLink = string.Empty;
    private string _currentVideoUrl = string.Empty;
    private string _singleVideoTitle = "Bilinmeyen Başlık";
    private string _currentPlaylistTitle = "Oynatma_Listesi";
    private bool _isInitializing = true;

    private Process? _currentDownloadProcess;
    private bool _isCancelled = false;

    private string _singleCustomFormatId = "";
    private string _singleCustomFormatName = "";
    private string _singleSelectedSubtitle = "";

    private ObservableCollection<PlaylistItem> _playlistItems = new();

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
        CodecPicker.SelectedIndex = Preferences.Default.Get("DefaultCodec", 0);

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

    private void OnCodecChanged(object? sender, EventArgs e)
    {
        if (_isInitializing) return;
        Preferences.Default.Set("DefaultCodec", CodecPicker.SelectedIndex);
    }

    private void ApplyThemeAndColor()
    {
        switch (ThemePicker.SelectedIndex)
        {
            case 0:
                Resources["BgColor"] = Color.FromArgb("#F3F4F6");
                Resources["PanelBgColor"] = Color.FromArgb("#FFFFFF");
                Resources["CardBgColor"] = Color.FromArgb("#F9FAFB");
                Resources["TextColor"] = Color.FromArgb("#111827");
                Resources["SubTextColor"] = Color.FromArgb("#6B7280");
                Resources["EntryBgColor"] = Color.FromArgb("#E5E7EB");
                Resources["BorderColor"] = Color.FromArgb("#D1D5DB");
                break;
            case 2:
                Resources["BgColor"] = Color.FromArgb("#000000");
                Resources["PanelBgColor"] = Color.FromArgb("#0A0A0A");
                Resources["CardBgColor"] = Color.FromArgb("#111111");
                Resources["TextColor"] = Color.FromArgb("#FFFFFF");
                Resources["SubTextColor"] = Color.FromArgb("#9CA3AF");
                Resources["EntryBgColor"] = Color.FromArgb("#1A1A1A");
                Resources["BorderColor"] = Color.FromArgb("#262626");
                break;
            case 3:
                Resources["BgColor"] = Color.FromArgb("#0D0D0D");
                Resources["PanelBgColor"] = Color.FromArgb("#121212");
                Resources["CardBgColor"] = Color.FromArgb("#1A1A1A");
                Resources["TextColor"] = Color.FromArgb("#00FF41");
                Resources["SubTextColor"] = Color.FromArgb("#008F11");
                Resources["EntryBgColor"] = Color.FromArgb("#1A1A1A");
                Resources["BorderColor"] = Color.FromArgb("#008F11");
                break;
            default:
                Resources["BgColor"] = Color.FromArgb("#18181B");
                Resources["PanelBgColor"] = Color.FromArgb("#27272A");
                Resources["CardBgColor"] = Color.FromArgb("#3F3F46");
                Resources["TextColor"] = Color.FromArgb("#F9FAFB");
                Resources["SubTextColor"] = Color.FromArgb("#A1A1AA");
                Resources["EntryBgColor"] = Color.FromArgb("#3F3F46");
                Resources["BorderColor"] = Color.FromArgb("#52525B");
                break;
        }

        string accentHex = ColorPicker.SelectedIndex switch
        {
            1 => "#06B6D4",
            2 => "#10B981",
            3 => "#8B5CF6",
            4 => "#F97316",
            5 => "#EC4899",
            6 => "#3B82F6",
            7 => "#EAB308",
            _ => "#EF4444"
        };
        if (ThemePicker.SelectedIndex == 3 && ColorPicker.SelectedIndex == 0) accentHex = "#00FF41";
        Resources["AccentColor"] = Color.FromArgb(accentHex);
    }

    private void OnSettingsClicked(object? sender, EventArgs e) => SettingsPanel.IsVisible = !SettingsPanel.IsVisible;

    private void OnTogglePlaylistClicked(object? sender, EventArgs e)
    {
        PlaylistContentContainer.IsVisible = !PlaylistContentContainer.IsVisible;
        TogglePlaylistBtn.Text = PlaylistContentContainer.IsVisible ? "➖" : "➕";
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
        if (QualityPicker != null) LoadQualityOptions(RadioMp3?.IsChecked ?? true);

        bool isMp3 = RadioMp3?.IsChecked ?? true;
        foreach (var item in _playlistItems) item.IsAudioMode = isMp3;
    }

    private void LoadQualityOptions(bool isMp3)
    {
        if (QualityPicker == null) return;
        QualityPicker.Items.Clear();

        if (isMp3)
        {
            QualityPicker.Items.Add("320 Kbps");
            QualityPicker.Items.Add("192 Kbps");
            QualityPicker.Items.Add("128 Kbps");
            QualityPicker.SelectedIndex = Preferences.Default.Get("DefaultAudio", 0);
        }
        else
        {
            QualityPicker.Items.Add("En İyi (Max)");
            QualityPicker.Items.Add("2160p (4K)");
            QualityPicker.Items.Add("1440p (2K)");
            QualityPicker.Items.Add("1080p (FHD)");
            QualityPicker.Items.Add("720p (HD)");
            QualityPicker.Items.Add("480p (SD)");
            QualityPicker.SelectedIndex = Preferences.Default.Get("DefaultVideo", 0);
        }
    }

    private async void OnPasteClicked(object? sender, EventArgs e)
    {
        if (Clipboard.Default.HasText)
        {
            string? text = await Clipboard.Default.GetTextAsync();
            if (!string.IsNullOrWhiteSpace(text))
            {
                UrlEntry.Text = text.Trim();
                await FetchVideoInfoAsync(text.Trim());
            }
        }
    }

    private async void OnThumbnailTapped(object? sender, TappedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentVideoUrl)) await Launcher.Default.OpenAsync(_currentVideoUrl);
    }

    private void OnSelectAllChanged(object? sender, CheckedChangedEventArgs e)
    {
        bool isChecked = e.Value;
        foreach (var item in _playlistItems) item.IsSelected = isChecked;
    }

    private void OnToggleItemModeClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.BindingContext is PlaylistItem item)
        {
            item.IsAudioMode = !item.IsAudioMode;
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
                    var window = App.Current?.Windows?.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
                    if (window != null)
                    {
                        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
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

        var window = App.Current?.Windows?.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
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
        if (Directory.Exists(_downloadDir)) Process.Start("explorer.exe", _downloadDir);
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

        _currentVideoUrl = link;
        bool isPlaylist = link.Contains("list=");

        // YENİLİK: Eski videonun hafızasını tamamen sıfırla ki isim çakışması olmasın!
        _singleCustomFormatId = "";
        _singleCustomFormatName = "";
        _singleSelectedSubtitle = "";

        MainThread.BeginInvokeOnMainThread(() =>
        {
            PlaylistSelectionBorder.IsVisible = false;
            SingleVideoExtrasPanel.IsVisible = false;
            SingleStatusLabel.Text = "";
            PlaylistCountLabel.Text = "Taranıyor...";

            // Format kutusunu tekrar aktif et
            FormatQualityPanel.IsEnabled = true;
            FormatQualityPanel.Opacity = 1.0;
            TotalProgressText.IsVisible = false;
        });

        await Task.Run(() =>
        {
#if WINDOWS
            try
            {
                string arguments = isPlaylist
                    ? $"--dump-json --flat-playlist --no-warnings \"{link}\""
                    : $"--dump-json --playlist-end 1 --no-warnings \"{link}\"";

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

                processInfo.EnvironmentVariables["PYTHONUTF8"] = "1";
                processInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

                using var process = Process.Start(processInfo);
                string jsonOutput = process.StandardOutput.ReadToEnd() ?? string.Empty;
                process.WaitForExit();

                if (!string.IsNullOrEmpty(jsonOutput))
                {
                    var lines = jsonOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                    if (isPlaylist)
                    {
                        var tempList = new List<PlaylistItem>();
                        int index = 1;
                        bool defaultMp3 = RadioMp3?.IsChecked ?? true;

                        string pTitle = "Oynatma_Listesi";
                        if (lines.Length > 0)
                        {
                            try
                            {
                                using JsonDocument tempDoc = JsonDocument.Parse(lines[0]);
                                if (tempDoc.RootElement.TryGetProperty("playlist_title", out var ptEl) && ptEl.ValueKind == JsonValueKind.String)
                                {
                                    pTitle = ptEl.GetString() ?? "Oynatma_Listesi";
                                }
                            }
                            catch { }
                        }
                        _currentPlaylistTitle = string.IsNullOrEmpty(pTitle) ? "Oynatma_Listesi" : pTitle;

                        foreach (var line in lines)
                        {
                            try
                            {
                                using JsonDocument doc = JsonDocument.Parse(line);
                                JsonElement root = doc.RootElement;
                                string id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                                string title = root.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? "Bilinmeyen Başlık" : "Bilinmeyen Başlık";
                                tempList.Add(new PlaylistItem { Index = index, Id = id, Title = title, IsAudioMode = defaultMp3 });
                                index++;
                            }
                            catch { }
                        }

                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            _playlistItems.Clear();
                            foreach (var item in tempList) _playlistItems.Add(item);

                            PlaylistCollectionView.ItemsSource = _playlistItems;
                            PlaylistSelectionBorder.IsVisible = true;
                            PlaylistContentContainer.IsVisible = true;
                            TogglePlaylistBtn.Text = "➖";
                            PlaylistCountLabel.Text = $"({_playlistItems.Count} Kayıt)";

                            if (_playlistItems.Count > 0)
                            {
                                VideoTitleLabel.Text = _currentPlaylistTitle;
                                ThumbnailImage.Source = ImageSource.FromUri(new Uri(_playlistItems[0].Thumbnail));
                                VideoDurationLabel.Text = "⏱ Liste";
                            }
                        });
                    }
                    else
                    {
                        using JsonDocument doc = JsonDocument.Parse(lines[0]);
                        JsonElement root = doc.RootElement;
                        string title = root.TryGetProperty("title", out JsonElement tEl) ? tEl.GetString() ?? "Bilinmeyen Başlık" : "Bilinmeyen Başlık";
                        string thumbnail = root.TryGetProperty("thumbnail", out JsonElement thEl) ? thEl.GetString() ?? "" : "";
                        string durationText = "--:--";
                        if (root.TryGetProperty("duration_string", out JsonElement durEl)) durationText = durEl.GetString() ?? "--:--";

                        string hqThumb = thumbnail;
                        if (!string.IsNullOrEmpty(hqThumb)) hqThumb = hqThumb.Replace("hqdefault.jpg", "maxresdefault.jpg");

                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            VideoTitleLabel.Text = title;
                            _singleVideoTitle = title;
                            VideoDurationLabel.Text = $"⏱ {durationText}";
                            if (!string.IsNullOrEmpty(thumbnail)) ThumbnailImage.Source = ImageSource.FromUri(new Uri(thumbnail));
                            SingleVideoExtrasPanel.IsVisible = true;
                        });
                    }
                }
            }
            catch { }
#endif
        });

        FetchInfoBtn.Text = "BUL";
        FetchInfoBtn.IsEnabled = true;
    }

    private async void OnFetchSubtitleClicked(object? sender, EventArgs e)
    {
        string url = _currentVideoUrl;
        PlaylistItem? pItem = null;

        if (sender is Button btn && btn.BindingContext is PlaylistItem item)
        {
            url = item.Url;
            pItem = item;
            btn.Text = "⏳...";
        }
        else if (sender is Button sBtn)
        {
            sBtn.Text = "⏳...";
        }

        List<string> subLangs = new();
        await Task.Run(() =>
        {
#if WINDOWS
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = _ytDlpPath,
                    Arguments = $"--dump-json --no-warnings \"{url}\"",
                    WorkingDirectory = _safeAppDir,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };
                processInfo.EnvironmentVariables["PYTHONUTF8"] = "1";
                using var process = Process.Start(processInfo);
                string jsonOutput = process.StandardOutput.ReadToEnd() ?? string.Empty;
                process.WaitForExit();

                if (!string.IsNullOrEmpty(jsonOutput))
                {
                    using JsonDocument doc = JsonDocument.Parse(jsonOutput);
                    if (doc.RootElement.TryGetProperty("subtitles", out JsonElement subs))
                    {
                        foreach (var prop in subs.EnumerateObject()) subLangs.Add(prop.Name);
                    }
                    if (doc.RootElement.TryGetProperty("automatic_captions", out JsonElement autoSubs))
                    {
                        foreach (var prop in autoSubs.EnumerateObject())
                        {
                            if (!subLangs.Contains(prop.Name)) subLangs.Add(prop.Name + " (Oto)");
                        }
                    }
                }
            }
            catch { }
#endif
        });

        if (sender is Button resetBtn) resetBtn.Text = (pItem != null) ? "💬" : "💬 Altyazı";

        if (subLangs.Count == 0)
        {
            await DisplayAlert("Hata", "Bu videoda gömülü veya otomatik altyazı bulunamadı.", "Tamam");
            return;
        }

        subLangs.Insert(0, "Sıfırla (İptal)");
        string action = await DisplayActionSheet("Altyazı Seçin (Gömülü İnecek)", "Kapat", null, subLangs.ToArray());

        if (action == "Kapat" || string.IsNullOrEmpty(action)) return;

        // YENİLİK: Artık (Oto) yazısını silmiyoruz, indirme motoru ona göre auto-subs komutunu seçecek.
        string selCode = action == "Sıfırla (İptal)" ? "" : action;

        if (pItem != null)
        {
            pItem.SelectedSubtitle = selCode;
            pItem.RefreshStatus();
        }
        else
        {
            _singleSelectedSubtitle = selCode;
            SingleStatusLabel.Text = string.IsNullOrEmpty(selCode) ? "" : $"[Altyazı: {selCode}] ";
        }
    }

    private async void OnFetchItemFormatClicked(object? sender, EventArgs e)
    {
        string url = _currentVideoUrl;
        PlaylistItem? pItem = null;

        if (sender is Button btn && btn.BindingContext is PlaylistItem item)
        {
            url = item.Url;
            pItem = item;
            btn.Text = "⏳...";
        }
        else if (sender is Button sBtn)
        {
            sBtn.Text = "⏳...";
        }

        List<VideoFormatInfo> parsedFormats = new();

        await Task.Run(() =>
        {
#if WINDOWS
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = _ytDlpPath,
                    Arguments = $"--dump-json --no-warnings \"{url}\"",
                    WorkingDirectory = _safeAppDir,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };
                processInfo.EnvironmentVariables["PYTHONUTF8"] = "1";
                using var process = Process.Start(processInfo);
                string jsonOutput = process.StandardOutput.ReadToEnd() ?? string.Empty;
                process.WaitForExit();

                if (!string.IsNullOrEmpty(jsonOutput))
                {
                    using JsonDocument doc = JsonDocument.Parse(jsonOutput);
                    if (doc.RootElement.TryGetProperty("formats", out JsonElement formatsArray))
                    {
                        foreach (var fmt in formatsArray.EnumerateArray())
                        {
                            string vcodec = fmt.TryGetProperty("vcodec", out JsonElement vc) ? vc.GetString() ?? "none" : "none";
                            if (vcodec == "none" || vcodec.Contains("images")) continue;

                            string ext = fmt.TryGetProperty("ext", out JsonElement ex) ? ex.GetString() ?? "" : "";
                            string height = fmt.TryGetProperty("height", out JsonElement h) && h.ValueKind == JsonValueKind.Number ? h.GetInt32().ToString() + "p" : "";
                            if (string.IsNullOrEmpty(height)) continue;

                            double filesize = 0;
                            if (fmt.TryGetProperty("filesize", out JsonElement fs) && fs.ValueKind == JsonValueKind.Number)
                                filesize = fs.GetDouble() / (1024 * 1024);
                            else if (fmt.TryGetProperty("filesize_approx", out JsonElement fsa) && fsa.ValueKind == JsonValueKind.Number)
                                filesize = fsa.GetDouble() / (1024 * 1024);

                            string codecShort = vcodec.Contains("avc") ? "H264" : (vcodec.Contains("vp9") ? "VP9" : (vcodec.Contains("av01") ? "AV1" : vcodec));
                            string sizeStr = filesize > 0 ? $"~{filesize:F1} MB" : "Bilinmiyor";

                            string formatId = fmt.TryGetProperty("format_id", out var fIdEl) ? fIdEl.GetString() ?? "" : "";

                            parsedFormats.Add(new VideoFormatInfo
                            {
                                FormatId = formatId,
                                DisplayName = $"{height} | {ext.ToUpper()} ({codecShort}) | {sizeStr}"
                            });
                        }
                    }
                }
            }
            catch { }
#endif
        });

        if (sender is Button resetBtn) resetBtn.Text = (pItem != null) ? "⚙️" : "⚙️ Özel Format";

        if (parsedFormats.Count == 0)
        {
            await DisplayAlert("Hata", "Bu video için format bulunamadı.", "Tamam");
            return;
        }

        parsedFormats.Reverse();
        var formatNames = parsedFormats.Select(f => f.DisplayName).ToArray();
        string action = await DisplayActionSheet($"Format Seç", "Kapat", "Sıfırla (Genel Ayar)", formatNames);

        if (action == "Kapat" || string.IsNullOrEmpty(action)) return;

        string targetFormatId = "";
        string displayStatus = "";

        if (action != "Sıfırla (Genel Ayar)")
        {
            var selectedFormat = parsedFormats.FirstOrDefault(f => f.DisplayName == action);
            if (selectedFormat != null)
            {
                targetFormatId = selectedFormat.FormatId;
                displayStatus = $"[{selectedFormat.DisplayName}]";
            }
        }

        if (pItem != null)
        {
            pItem.CustomFormatId = targetFormatId;
            pItem.CustomFormatName = displayStatus; // Hafızaya al

            // YENİLİK: Playlistte özel video formatı seçtiğinde, şarkıyı otomatik "Video" moduna geçir!
            if (!string.IsNullOrEmpty(targetFormatId)) pItem.IsAudioMode = false;

            pItem.RefreshStatus();
        }
        else
        {
            _singleCustomFormatId = targetFormatId;
            _singleCustomFormatName = displayStatus; // Hafızaya al
            string existingSub = string.IsNullOrEmpty(_singleSelectedSubtitle) ? "" : $"[Altyazı: {_singleSelectedSubtitle}] ";
            SingleStatusLabel.Text = $"{existingSub}{displayStatus}";

            // YENİLİK: Tekli videoda özel format seçildiğinde Genel Kalite Paneli KİLİTLENİR VE KARARTILIR (Çakışma olmaz)
            if (!string.IsNullOrEmpty(targetFormatId))
            {
                RadioMp4.IsChecked = true; // Radyo butonunu MP4'e otomatik geçir
                FormatQualityPanel.IsEnabled = false;
                FormatQualityPanel.Opacity = 0.5;
            }
            else
            {
                FormatQualityPanel.IsEnabled = true;
                FormatQualityPanel.Opacity = 1.0;
            }
        }
    }

    private void OnCancelClicked(object? sender, EventArgs e)
    {
        if (_currentDownloadProcess != null && !_currentDownloadProcess.HasExited)
        {
            _isCancelled = true;
            try { _currentDownloadProcess.Kill(true); } catch { }

            CurrentFileLabel.Text = "❌ İşlem iptal edildi.";
            ProgressText.Text = "Kullanıcı durdurdu.";
            ProgressText.TextColor = Colors.Red;

            DownloadBtn.IsEnabled = true;
            UrlEntry.IsEnabled = true;
            CancelBtn.IsVisible = false;
        }
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

        bool isPlaylistDownload = PlaylistSelectionBorder.IsVisible;
        var selectedItems = isPlaylistDownload ? _playlistItems.Where(x => x.IsSelected).ToList() : new List<PlaylistItem>();

        if (isPlaylistDownload && selectedItems.Count == 0)
        {
            await DisplayAlert("Uyarı", "Lütfen listeden indirilecek en az 1 video/şarkı seçin.", "Tamam");
            return;
        }

        bool isGlobalMp3 = RadioMp3?.IsChecked ?? true;
        string globalQuality = QualityPicker?.SelectedItem?.ToString() ?? "";

        _isCancelled = false;
        DownloadBtn.IsEnabled = false;
        UrlEntry.IsEnabled = false;
        CancelBtn.IsVisible = true;
        ProgressContainer.IsVisible = true;
        DownloadProgressBar.Progress = 0;

        if (isPlaylistDownload)
        {
            PlaylistContentContainer.IsVisible = false;
            TogglePlaylistBtn.Text = "➕";
            TotalProgressText.IsVisible = true;
            TotalProgressText.Text = $"📁 Toplam İlerleme: 0 / {selectedItems.Count} Video Tamamlandı";
        }
        else
        {
            TotalProgressText.IsVisible = false;
        }

        if (isPlaylistDownload)
        {
            int total = selectedItems.Count;
            int current = 1;

            foreach (var item in selectedItems)
            {
                if (_isCancelled) break;
                MainThread.BeginInvokeOnMainThread(() => {
                    ThumbnailImage.Source = ImageSource.FromUri(new Uri(item.Thumbnail));
                    VideoTitleLabel.Text = item.Title;
                    TotalProgressText.Text = $"📁 Toplam İlerleme: {current - 1} / {total} Video Tamamlandı";
                });

                // ÖNEMLİ: Özel format varsa ses modunu pas geçiyoruz (Zaten video formatıdır)
                bool processAudio = item.IsAudioMode;
                if (!string.IsNullOrEmpty(item.CustomFormatId)) processAudio = false;

                await StartDownloadProcessAsync(item.Url, processAudio, globalQuality, item.CustomFormatId, item.CustomFormatName, item.SelectedSubtitle, true, current, total, item.Title);

                if (!_isCancelled)
                {
                    MainThread.BeginInvokeOnMainThread(() => TotalProgressText.Text = $"📁 Toplam İlerleme: {current} / {total} Video Tamamlandı");
                }
                current++;
            }
        }
        else
        {
            // TEKLİ VİDEO İNDİRME
            bool processAudio = isGlobalMp3;
            if (!string.IsNullOrEmpty(_singleCustomFormatId)) processAudio = false;

            await StartDownloadProcessAsync(link, processAudio, globalQuality, _singleCustomFormatId, _singleCustomFormatName, _singleSelectedSubtitle, false, 1, 1, _singleVideoTitle);
        }

        if (_isCancelled) return;

        DownloadBtn.IsEnabled = true;
        UrlEntry.IsEnabled = true;
        CancelBtn.IsVisible = false;
        CurrentFileLabel.Text = "✅ Tüm İşlemler Başarıyla Tamamlandı!";
        ProgressText.Text = "İndirme Bitti!";
        ProgressText.TextColor = (Color)Resources["AccentColor"];

        if (Preferences.Default.Get("AutoOpenFolder", false))
        {
#if WINDOWS
            if (Directory.Exists(_downloadDir)) Process.Start("explorer.exe", _downloadDir);
#endif
        }
    }

    private Task StartDownloadProcessAsync(string link, bool isMp3, string globalQuality, string? customFormatId, string? customFormatName, string? subtitleLang, bool isPlaylist, int currentIndex, int totalCount, string videoTitle)
    {
        return Task.Run(() =>
        {
#if WINDOWS
            string safeAppDir = _appDir.TrimEnd('\\');
            string fastArgs = "";
            string fileSuffix = "";

            // YENİLİK: İsim çakışmalarına kesin çözüm! Özel formattan kalite çekilip Format ID'si eklenir. (Örn: 144p_133)
            bool isCustomFormat = !string.IsNullOrEmpty(customFormatId);

            if (isCustomFormat)
            {
                var match = Regex.Match(customFormatName ?? "", @"\d{3,4}p");
                string res = match.Success ? match.Value : "Ozel";
                fileSuffix = $"{res}_{customFormatId}";
            }
            else if (isMp3)
            {
                string kbpsNum = Regex.Match(globalQuality, @"\d+").Value;
                fileSuffix = $"Ses_{kbpsNum}K";
            }
            else
            {
                if (globalQuality.Contains("En İyi")) fileSuffix = "Maksimum";
                else fileSuffix = Regex.Match(globalQuality, @"\d{3,4}").Value + "p";
            }

            string safePlaylistTitle = string.IsNullOrEmpty(_currentPlaylistTitle) ? "Arsiv" : string.Join("_", _currentPlaylistTitle.Split(Path.GetInvalidFileNameChars()));

            string outputTemplate = isPlaylist
                ? $"-o \"{_downloadDir}\\Playlist_Arsivleri\\{safePlaylistTitle}\\{currentIndex:00} - %(title)s ({fileSuffix}).%(ext)s\""
                : $"-o \"{_downloadDir}\\Tekli_Indirmeler\\%(title)s ({fileSuffix}).%(ext)s\"";

            string modeArgs = "";
            string subtitleArgs = "";
            string mergeFmt = "mp4"; // Varsayılan video birleştirme formatı

            // YENİLİK: KUSURSUZ ALTYAZI GÖMME MOTORU
            if (!string.IsNullOrEmpty(subtitleLang) && !isMp3)
            {
                bool isAuto = subtitleLang.Contains("(Oto)");
                string cleanLang = subtitleLang.Replace(" (Oto)", "").Trim();

                string subType = isAuto ? "--write-auto-subs" : "--write-subs";
                // convert-subs srt : Otomatik oluşturulan VTT formatını bozmadan SRT'ye çevirip gömer
                subtitleArgs = $"{subType} --sub-langs \"{cleanLang}\" --convert-subs srt --embed-subs --compat-options no-keep-subs ";
                mergeFmt = "mkv"; // MKV, altyazıları kusursuz destekler
            }

            if (isMp3)
            {
                fastArgs = $"--newline --no-color --no-warnings -N 16 --http-chunk-size 10M " +
                           $"--extractor-args \"youtube:player_client=android,web\" " +
                           $"--continue --windows-filenames --sponsorblock-remove all " +
                           $"--no-write-info-json --clean-info-json " +
                           $"--ffmpeg-location \"{safeAppDir}\"";

                string kbps = Regex.Match(globalQuality, @"\d+").Value;
                if (string.IsNullOrEmpty(kbps)) kbps = "192";
                modeArgs = $"-f bestaudio/best -x --audio-format mp3 --audio-quality {kbps}K --embed-metadata --embed-thumbnail";
            }
            else
            {
                fastArgs = $"--newline --no-color --no-warnings -N 8 --http-chunk-size 5M " +
                           $"--continue --windows-filenames --sponsorblock-remove all " +
                           $"--no-write-info-json --clean-info-json " +
                           $"--ffmpeg-location \"{safeAppDir}\"";

                if (isCustomFormat)
                {
                    modeArgs = $"-f {customFormatId}+bestaudio[ext=m4a]/bestvideo[ext=mp4]+bestaudio[ext=m4a]/best --merge-output-format {mergeFmt} --embed-metadata {subtitleArgs}";
                }
                else
                {
                    int codecPref = Preferences.Default.Get("DefaultCodec", 0);
                    string cRule = codecPref switch
                    {
                        1 => "vcodec^=avc1", // H264
                        2 => "vcodec^=vp9",  // VP9
                        3 => "vcodec^=av01", // AV1
                        _ => "" // Otomatik
                    };
                    string cStr = string.IsNullOrEmpty(cRule) ? "" : $"[{cRule}]";

                    string formatStr;
                    if (globalQuality.Contains("En İyi"))
                    {
                        formatStr = $"bestvideo{cStr}[ext=mp4]+bestaudio[ext=m4a]/bestvideo[ext=mp4]+bestaudio[ext=m4a]/best";
                    }
                    else
                    {
                        string res = Regex.Match(globalQuality, @"\d{3,4}").Value;
                        if (!string.IsNullOrEmpty(res))
                        {
                            formatStr = $"bestvideo{cStr}[height<={res}][ext=mp4]+bestaudio[ext=m4a]/" +
                                        $"bestvideo[height<={res}][ext=mp4]+bestaudio[ext=m4a]/" +
                                        $"best[height<={res}]/best";
                        }
                        else
                        {
                            formatStr = $"bestvideo{cStr}[ext=mp4]+bestaudio[ext=m4a]/bestvideo[ext=mp4]+bestaudio[ext=m4a]/best";
                        }
                    }
                    modeArgs = $"-f \"{formatStr}\" --merge-output-format {mergeFmt} --embed-metadata {subtitleArgs}";
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
                StandardOutputEncoding = System.Text.Encoding.UTF8 // TÜRKÇE KARAKTERLER İÇİN KESİN ÇÖZÜM
            };

            processInfo.EnvironmentVariables["PYTHONUTF8"] = "1";
            processInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

            _currentDownloadProcess = new Process { StartInfo = processInfo };

            var regexProgress = new Regex(@"\[download\]\s+([\d.]+)%(?:.*?at\s+([\d.\w]+/?s))?(?:.*?ETA\s+([\d:]+))?");
            string currentIcon = isMp3 ? "🎵" : "🎬";

            // YENİLİK: C# içinden oluşturulan temiz isim (Konsol bozulmasına karşı korumalı)
            string displayTitle = isPlaylist ? $"{currentIndex:00} - {videoTitle}" : videoTitle;
            MainThread.BeginInvokeOnMainThread(() => CurrentFileLabel.Text = $"⬇ {displayTitle}");

            _currentDownloadProcess.OutputDataReceived += (sender, e) =>
            {
                if (_isCancelled) return;
                string outputLine = e.Data ?? string.Empty;

                if (!string.IsNullOrEmpty(outputLine))
                {
                    if (outputLine.Contains("has already been downloaded"))
                    {
                        MainThread.BeginInvokeOnMainThread(() => CurrentFileLabel.Text = $"✅ Mevcut: {displayTitle}");
                    }

                    var match = regexProgress.Match(outputLine);
                    if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double progress))
                    {
                        string speed = match.Groups[2].Success ? match.Groups[2].Value : "";
                        string eta = match.Groups[3].Success ? match.Groups[3].Value : "";

                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            if (_isCancelled) return;
                            DownloadProgressBar.Progress = progress / 100.0;

                            string progressInfo = "";
                            if (isPlaylist) progressInfo += $"[{currentIcon} {currentIndex}/{totalCount}] ";

                            progressInfo += $"% {progress:F1}";
                            if (!string.IsNullOrEmpty(speed)) progressInfo += $" | {speed}";
                            if (!string.IsNullOrEmpty(eta)) progressInfo += $" | Kalan: {eta}";

                            ProgressText.Text = progressInfo;
                        });
                    }
                    else if (outputLine.Contains("[ExtractAudio]") || outputLine.Contains("[Merger]") || outputLine.Contains("[EmbedSubtitle]"))
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            if (_isCancelled) return;
                            DownloadProgressBar.Progress = 1.0;
                            string prefix = isPlaylist ? $"[{currentIcon} {currentIndex}/{totalCount}] " : "";
                            ProgressText.Text = $"{prefix}İşleniyor (Format/Altyazı Ayarlanıyor)...";
                        });
                    }
                }
            };

            _currentDownloadProcess.ErrorDataReceived += (sender, e) =>
            {
                if (_isCancelled) return;
                string errorLine = e.Data ?? string.Empty;
                if (!string.IsNullOrEmpty(errorLine))
                {
                    MainThread.BeginInvokeOnMainThread(() => ProgressText.Text = $"Uyarı: {errorLine}");
                }
            };

            try
            {
                _currentDownloadProcess.Start();
                _currentDownloadProcess.BeginOutputReadLine();
                _currentDownloadProcess.BeginErrorReadLine();
                _currentDownloadProcess.WaitForExit();
            }
            catch { }
            finally
            {
                _currentDownloadProcess = null;
            }
#endif
        });
    }
}