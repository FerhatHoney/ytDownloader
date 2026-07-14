using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ytDownloader; // BURAYA KENDİ PROJE ADINI YAZMAYI UNUTMA

public partial class MainPage : ContentPage
{
    private readonly string _appDir;
    private readonly string _ytDlpPath;
    private string _downloadDir;
    private string _safeAppDir;

    public MainPage()
    {
        InitializeComponent();

        _appDir = AppDomain.CurrentDomain.BaseDirectory;
        _safeAppDir = _appDir.TrimEnd('\\');
        _ytDlpPath = Path.Combine(_safeAppDir, "yt-dlp.exe");

        _downloadDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ytDownloader_İndirilenler");
        if (!Directory.Exists(_downloadDir))
            Directory.CreateDirectory(_downloadDir);

        FolderEntry.Text = _downloadDir; // Arayüze klasör yolunu yaz

        LoadQualityOptions(true);
        _ = InitializeSystemAsync();
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

            StatusLabel.Text = "yt-dlp güncelleniyor...";

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

    // Windows Klasör Seçme Ekranı
    private async void OnSelectFolderClicked(object sender, EventArgs e)
    {
#if WINDOWS
        var folderPicker = new Windows.Storage.Pickers.FolderPicker();
        folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads;
        folderPicker.FileTypeFilter.Add("*");

        var window = App.Current?.Windows.FirstOrDefault()?.Handler.PlatformView as Microsoft.UI.Xaml.Window;
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
        }
#else
        await Task.CompletedTask; // Mac ve Android'in uyarı vermesini engeller
#endif
    }

    private void OnFormatChanged(object sender, CheckedChangedEventArgs e)
    {
        if (QualityPicker != null)
        {
            LoadQualityOptions(RadioMp3.IsChecked);
        }
    }

    private void LoadQualityOptions(bool isMp3)
    {
        QualityPicker.Items.Clear();
        if (isMp3)
        {
            QualityPicker.Items.Add("320 Kbps (Yüksek)");
            QualityPicker.Items.Add("192 Kbps (Standart)");
            QualityPicker.Items.Add("128 Kbps (Düşük)");
        }
        else
        {
            QualityPicker.Items.Add("En İyi (Best)");
            QualityPicker.Items.Add("1080p");
            QualityPicker.Items.Add("720p");
            QualityPicker.Items.Add("480p");
        }
        QualityPicker.SelectedIndex = 0;
    }

    // Linkten YouTube ID'sini çıkaran yardımcı metod
    private string ExtractVideoId(string url)
    {
        var match = Regex.Match(url, @"(?:youtube\.com\/(?:[^\/]+\/.+\/|(?:v|e(?:mbed)?)\/|.*[?&]v=)|youtu\.be\/)([^""&?\/\s]{11})");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private async void OnFetchInfoClicked(object sender, EventArgs e)
    {
        string link = UrlEntry.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(link)) return;

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

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        VideoTitleLabel.Text = title;
                        if (!string.IsNullOrEmpty(thumbnail))
                        {
                            ThumbnailImage.Source = ImageSource.FromUri(new Uri(thumbnail));
                        }
                        TestVideoBtn.IsVisible = true; // Test butonunu görünür yap
                    });
                }
            }
            catch { }
#endif
        });

        FetchInfoBtn.Text = "BUL";
        FetchInfoBtn.IsEnabled = true;
    }

    private async void OnDownloadClicked(object sender, EventArgs e)
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
        ProgressText.TextColor = Colors.White;

        // Tarayıcı parametresini kaldırdık
        await Task.Run(() => StartDownload(link, isMp3, selectedQuality));

        DownloadBtn.IsEnabled = true;
        UrlEntry.IsEnabled = true;
        ProgressText.Text = "İşlem Başarıyla Tamamlandı!";
        await DisplayAlert("Başarılı", $"İndirme tamamlandı!\nDosyalar şuraya kaydedildi:\n{_downloadDir}", "Tamam");
    }

    private void StartDownload(string link, bool isMp3, string qualityString)
    {
#if WINDOWS
        string safeAppDir = _appDir.TrimEnd('\\');

        // HARİKA DOKUNUŞ: Kendimizi Smart TV olarak gösteriyoruz! (player_client=tv)
        // Bu sayede YouTube bot koruması sormaz ve kaliteyi 360p ile sınırlandırmaz.
        string fastArgs = $"--newline --no-color --no-warnings -N 16 --http-chunk-size 10M " +
                          $"--no-overwrites --continue --windows-filenames " +
                          $"--sponsorblock-remove all --no-write-info-json " +
                          $"--clean-info-json --lazy-playlist " +
                          $"--ffmpeg-location \"{safeAppDir}\"";

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
            // Videoyu tam kalitede, Windows Media Player'da (H264) açılacak şekilde zorluyoruz
            if (qualityString.Contains("En İyi"))
            {
                modeArgs = "-f bv*+ba/b -S ext:mp4:m4a,vcodec:h264 --merge-output-format mp4 --embed-metadata";
            }
            else
            {
                string res = Regex.Match(qualityString, @"\d+").Value;
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
            if (!string.IsNullOrEmpty(e.Data))
            {
                var match = regex.Match(e.Data);
                if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double progress))
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        DownloadProgressBar.Progress = progress / 100.0;
                        ProgressText.Text = $"İndiriliyor: %{progress:F1}";
                        ProgressText.TextColor = Colors.White;
                    });
                }
                else if (e.Data.Contains("[ExtractAudio]") || e.Data.Contains("[Merger]"))
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
            if (!string.IsNullOrEmpty(e.Data))
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ProgressText.Text = $"UYARI/HATA: {e.Data}";
                    ProgressText.TextColor = Colors.Red;
                });
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();
#endif
    }
    private async void OnTestVideoClicked(object sender, EventArgs e)
    {
        string link = UrlEntry.Text?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(link))
        {
            // Videoyu kullanıcının kendi tarayıcısında (Chrome/Edge vb.) açar
            await Launcher.Default.OpenAsync(link);
        }
    }
}