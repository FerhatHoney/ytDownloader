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

public class PlaylistItem : INotifyPropertyChanged
{
    public int Index { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Thumbnail => $"https://img.youtube.com/vi/{Id}/mqdefault.jpg";

    private bool _isSelected = true;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
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
    private bool _isInitializing = true;

    private Process? _currentDownloadProcess;
    private bool _isCancelled = false;

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

        if (ThemePicker.SelectedIndex == 3 && ColorPicker.SelectedIndex == 0)
            accentHex = "#00FF41";

        Resources["AccentColor"] = Color.FromArgb(accentHex);
    }

    private void OnSettingsClicked(object? sender, EventArgs e)
    {
        SettingsPanel.IsVisible = !SettingsPanel.IsVisible;
    }

    // YENİ: Playlist Göster/Gizle (+ / -) Butonu Komutu
    private void OnTogglePlaylistClicked(object? sender, EventArgs e)
    {
        if (PlaylistContentContainer.IsVisible)
        {
            PlaylistContentContainer.IsVisible = false;
            TogglePlaylistBtn.Text = "➕";
        }
        else
        {
            PlaylistContentContainer.IsVisible = true;
            TogglePlaylistBtn.Text = "➖";
        }
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
        foreach (var item in _playlistItems)
        {
            item.IsSelected = isChecked;
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

        MainThread.BeginInvokeOnMainThread(() =>
        {
            PlaylistSelectionBorder.IsVisible = false;
            PlaylistCountLabel.Text = "Taranıyor...";
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
                        foreach (var line in lines)
                        {
                            try
                            {
                                using JsonDocument doc = JsonDocument.Parse(line);
                                JsonElement root = doc.RootElement;
                                string id = root.GetProperty("id").GetString() ?? "";
                                string title = root.GetProperty("title").GetString() ?? "Bilinmeyen Başlık";
                                tempList.Add(new PlaylistItem { Index = index, Id = id, Title = title });
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
                            // Taranınca listeyi otomatik açık göster [-]
                            PlaylistContentContainer.IsVisible = true;
                            TogglePlaylistBtn.Text = "➖";
                            PlaylistCountLabel.Text = $"({_playlistItems.Count} Kayıt)";

                            if (_playlistItems.Count > 0)
                            {
                                VideoTitleLabel.Text = "Çoklu Oynatma Listesi Seçildi";
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

                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            VideoTitleLabel.Text = title;
                            VideoDurationLabel.Text = $"⏱ {durationText}";
                            if (!string.IsNullOrEmpty(thumbnail)) ThumbnailImage.Source = ImageSource.FromUri(new Uri(thumbnail));
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
        string itemsArg = "";

        if (isPlaylistDownload)
        {
            var selectedIndices = _playlistItems.Where(x => x.IsSelected).Select(x => x.Index).ToList();
            if (selectedIndices.Count == 0)
            {
                await DisplayAlert("Uyarı", "Lütfen listeden indirilecek en az 1 video/şarkı seçin.", "Tamam");
                return;
            }
            itemsArg = string.Join(",", selectedIndices);

            // YENİLİK: İndirme başladığı an Playlist menüsünü EKRADAN GİZLE (Otomatik Minimize)
            PlaylistContentContainer.IsVisible = false;
            TogglePlaylistBtn.Text = "➕";
        }

        bool isMp3 = RadioMp3?.IsChecked ?? true;
        string selectedQuality = QualityPicker?.SelectedItem?.ToString() ?? "";

        _isCancelled = false;
        DownloadBtn.IsEnabled = false;
        UrlEntry.IsEnabled = false;
        CancelBtn.IsVisible = true;
        ProgressContainer.IsVisible = true;
        DownloadProgressBar.Progress = 0;

        CurrentFileLabel.Text = "Bağlantı kuruluyor...";
        ProgressText.Text = "Lütfen bekleyin...";
        ProgressText.TextColor = (Color)Resources["SubTextColor"];

        await Task.Run(() => StartDownload(link, isMp3, selectedQuality, isPlaylistDownload, itemsArg));

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
        else
        {
            await DisplayAlert("Başarılı", $"İndirme tamamlandı!\nDosyalar şuraya kaydedildi:\n{_downloadDir}", "Tamam");
        }
    }

    private void StartDownload(string link, bool isMp3, string qualityString, bool isPlaylistDownload, string itemsArg)
    {
#if WINDOWS
        string safeAppDir = _appDir.TrimEnd('\\');
        string fastArgs = "";

        string playlistArgs = isPlaylistDownload ? $"--yes-playlist --playlist-items {itemsArg}" : "--no-playlist";

        string fileSuffix = "";
        if (isMp3)
        {
            string kbpsNum = Regex.Match(qualityString, @"\d+").Value;
            fileSuffix = $"Ses_{kbpsNum}K";
        }
        else
        {
            if (qualityString.Contains("En İyi")) fileSuffix = "Maksimum";
            else fileSuffix = Regex.Match(qualityString, @"\d{3,4}").Value + "p";
        }

        string outputTemplate = "";
        if (isPlaylistDownload)
        {
            outputTemplate = $"-o \"{_downloadDir}\\%(playlist_title|Playlist_İndirmeleri)s\\%(playlist_index)02d - %(title)s ({fileSuffix}).%(ext)s\"";
        }
        else
        {
            outputTemplate = $"-o \"{_downloadDir}\\Tekli_İndirmeler\\%(title)s ({fileSuffix}).%(ext)s\"";
        }

        string modeArgs = "";

        if (isMp3)
        {
            fastArgs = $"--newline --no-color --no-warnings -N 16 --http-chunk-size 10M {playlistArgs} " +
                       $"--extractor-args \"youtube:player_client=android,web\" " +
                       $"--continue --windows-filenames " +
                       $"--sponsorblock-remove all --no-write-info-json " +
                       $"--clean-info-json --lazy-playlist " +
                       $"--ffmpeg-location \"{safeAppDir}\"";

            string kbps = Regex.Match(qualityString, @"\d+").Value;
            if (string.IsNullOrEmpty(kbps)) kbps = "192";
            modeArgs = $"-f bestaudio/best -x --audio-format mp3 --audio-quality {kbps}K --embed-metadata --embed-thumbnail";
        }
        else
        {
            fastArgs = $"--newline --no-color --no-warnings -N 8 --http-chunk-size 5M {playlistArgs} " +
                       $"--continue --windows-filenames " +
                       $"--sponsorblock-remove all --no-write-info-json " +
                       $"--clean-info-json --lazy-playlist " +
                       $"--ffmpeg-location \"{safeAppDir}\"";

            string formatStr;
            if (qualityString.Contains("En İyi"))
            {
                formatStr = "bestvideo[vcodec^=avc1][ext=mp4]+bestaudio[ext=m4a]/bestvideo[ext=mp4]+bestaudio[ext=m4a]/best";
            }
            else
            {
                string res = Regex.Match(qualityString, @"\d{3,4}").Value;
                if (!string.IsNullOrEmpty(res))
                {
                    formatStr = $"bestvideo[vcodec^=avc1][height<={res}][ext=mp4]+bestaudio[ext=m4a]/bestvideo[height<={res}][ext=mp4]+bestaudio[ext=m4a]/best[height<={res}]/best";
                }
                else
                {
                    formatStr = "bestvideo[vcodec^=avc1][ext=mp4]+bestaudio[ext=m4a]/bestvideo[ext=mp4]+bestaudio[ext=m4a]/best";
                }
            }
            modeArgs = $"-f \"{formatStr}\" --merge-output-format mp4 --embed-metadata";
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

        _currentDownloadProcess = new Process { StartInfo = processInfo };

        var regexProgress = new Regex(@"\[download\]\s+([\d.]+)%(?:.*?at\s+([\d.\w]+/?s))?(?:.*?ETA\s+([\d:]+))?");
        var regexPlaylist = new Regex(@"\[download\] Downloading video (\d+) of (\d+)");
        var regexDest = new Regex(@"\[download\] Destination:\s+(.+)$");
        var regexAlready = new Regex(@"\[download\]\s+(.+?)\s+has already been downloaded");
        var regexYtId = new Regex(@"\[youtube\]\s+([A-Za-z0-9_-]{11}):");

        string currentVideoIndex = "";
        string totalVideos = "";
        string currentIcon = isMp3 ? "🎵" : "🎬";

        _currentDownloadProcess.OutputDataReceived += (sender, e) =>
        {
            if (_isCancelled) return;
            string outputLine = e.Data ?? string.Empty;

            if (!string.IsNullOrEmpty(outputLine))
            {
                var idMatch = regexYtId.Match(outputLine);
                if (idMatch.Success && isPlaylistDownload)
                {
                    string currentId = idMatch.Groups[1].Value;
                    var currentItem = _playlistItems.FirstOrDefault(x => x.Id == currentId);
                    if (currentItem != null)
                    {
                        MainThread.BeginInvokeOnMainThread(() => {
                            VideoTitleLabel.Text = currentItem.Title;
                            ThumbnailImage.Source = ImageSource.FromUri(new Uri(currentItem.Thumbnail));
                        });
                    }
                }

                var pMatch = regexPlaylist.Match(outputLine);
                if (pMatch.Success)
                {
                    currentVideoIndex = pMatch.Groups[1].Value;
                    totalVideos = pMatch.Groups[2].Value;
                }

                var destMatch = regexDest.Match(outputLine);
                if (destMatch.Success)
                {
                    string fileName = Path.GetFileName(destMatch.Groups[1].Value);
                    MainThread.BeginInvokeOnMainThread(() => CurrentFileLabel.Text = $"⬇ {fileName}");
                }

                var alreadyMatch = regexAlready.Match(outputLine);
                if (alreadyMatch.Success)
                {
                    string fileName = Path.GetFileName(alreadyMatch.Groups[1].Value);
                    MainThread.BeginInvokeOnMainThread(() => CurrentFileLabel.Text = $"✅ Mevcut: {fileName}");
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
                        if (isPlaylistDownload && !string.IsNullOrEmpty(currentVideoIndex))
                            progressInfo += $"[{currentIcon} {currentVideoIndex}/{totalVideos}] ";

                        progressInfo += $"% {progress:F1}";
                        if (!string.IsNullOrEmpty(speed)) progressInfo += $" | {speed}";
                        if (!string.IsNullOrEmpty(eta)) progressInfo += $" | Kalan: {eta}";

                        ProgressText.Text = progressInfo;
                    });
                }
                else if (outputLine.Contains("[ExtractAudio]") || outputLine.Contains("[Merger]"))
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (_isCancelled) return;
                        DownloadProgressBar.Progress = 1.0;
                        string prefix = (isPlaylistDownload && !string.IsNullOrEmpty(currentVideoIndex)) ? $"[{currentIcon} {currentVideoIndex}/{totalVideos}] " : "";
                        ProgressText.Text = $"{prefix}İşleniyor ve birleştiriliyor...";
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
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ProgressText.Text = $"Uyarı: {errorLine}";
                });
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
    }
}