# Konsolun GERÇEK kod sayfasını UTF-8'e zorla (65001).
chcp 65001 | Out-Null

# SENİN ORİJİNAL ENCODING AYARLARIN (Burası en sağlıklısıymış)
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
[Console]::InputEncoding  = New-Object System.Text.UTF8Encoding($false)
$OutputEncoding = New-Object System.Text.UTF8Encoding($false)

# yt-dlp Python ortam değişkenleri
$env:PYTHONUTF8 = "1"
$env:PYTHONIOENCODING = "utf-8"

# İnternet indirme protokolü
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$currentDir = $PSScriptRoot
Set-Location $currentDir

Clear-Host
Write-Host "Sistem dosyaları kontrol ediliyor..." -ForegroundColor DarkGray

if (-not (Test-Path "$currentDir\yt-dlp.exe")) {
    Write-Host "yt-dlp bulunamadı, indiriliyor..." -ForegroundColor Yellow
    Invoke-WebRequest -Uri "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe" -OutFile "$currentDir\yt-dlp.exe"
}

if (-not (Test-Path "$currentDir\ffmpeg.exe")) {
    Write-Host "UYARI: ffmpeg.exe bu klasörde bulunamadı! MP3 ve MP4 dönüştürme işlemleri başarısız olabilir." -ForegroundColor Red
    Start-Sleep -Seconds 3
}

# Güncelleme kontrolü
Write-Host "yt-dlp güncelleniyor..." -ForegroundColor DarkGray
Start-Process -FilePath ".\yt-dlp.exe" -ArgumentList "-U" -NoNewWindow -Wait

$txtFiles = Get-ChildItem -Path $currentDir -Filter *.txt

Clear-Host
Write-Host " ╔══════════════════════════════════════════════╗ " -ForegroundColor Cyan
Write-Host " ║      YOUTUBE PRO İNDİRİCİ (ULTRA FAST)       ║ " -ForegroundColor White
Write-Host " ║              by_bozkurt                      ║ " -ForegroundColor Yellow
Write-Host " ╚══════════════════════════════════════════════╝ " -ForegroundColor Cyan
Write-Host ""
Write-Host "  [ 1 ] - Tekli Linkleri MP3 Olarak İndir (Klasördeki .txt'ler)" -ForegroundColor Cyan
Write-Host "  [ 2 ] - Tekli Linkleri MP4 Olarak İndir (Klasördeki .txt'ler)" -ForegroundColor Cyan
Write-Host "  [ 3 ] - Playlist'i MP3 Olarak İndir (Link Yapıştır)" -ForegroundColor Magenta
Write-Host "  [ 4 ] - Playlist'i MP4 Olarak İndir (Link Yapıştır)" -ForegroundColor Magenta
Write-Host "  [ 0 ] - Çıkış Yap" -ForegroundColor Red
Write-Host ""
$mainSecim = Read-Host "  ► Seçiminiz"

if ($mainSecim -eq "0" -or $mainSecim -eq "") { exit }

# Kalite Seçimleri
if ($mainSecim -eq "1" -or $mainSecim -eq "3") {
    $modeArgs = @("-x", "--audio-format", "mp3", "--audio-quality", "192K", "--embed-metadata", "--embed-thumbnail")
} else {
    $modeArgs = @("-f", "bestvideo+bestaudio/best", "--merge-output-format", "mp4", "--embed-metadata")
}

# HIZ, TEMİZLİK VE TARAMA PARAMETRELERİ
$fastArgs = @(
    "--no-color",
    "--no-warnings",
    "-i",
    "--ffmpeg-location", $currentDir,
    "-N", "16",
    "--http-chunk-size", "10M",
    "--extractor-args", "youtube:player_client=android,web",
    "--no-overwrites",
    "--continue",
    "--windows-filenames",
    "--sponsorblock-remove", "all",
    "--no-write-info-json",
    "--no-write-playlist-metafiles",
    "--clean-info-json",
    "--lazy-playlist",
    "--newline"
)

# ------------------------------------------------------------------
# Gerçek indirme çubuğunu gösteren yardımcı fonksiyon
# ------------------------------------------------------------------
function Invoke-YtDlpWithProgress {
    param(
        [string[]]$Arguments,
        [string]$Activity
    )

    $progressRegex = '\[download\]\s+([\d.]+)%(?:\s+of\s+~?([\d.]+\w+))?(?:\s+at\s+([\d.]+\w+/s|Unknown speed))?(?:\s+ETA\s+([\d:]+|Unknown))?'
    $currentFile = ""

    & ".\yt-dlp.exe" @Arguments 2>&1 | ForEach-Object {
        
        # GERÇEK ÇÖZÜM: Sadece görünmez ASCII kontrol karakterlerini (0-31 ve 127) siliyoruz.
        # Bu işlem \r, \n, ve Terminal renk kodlarını siler, TÜRKÇE karakterlere DOKUNMAZ!
        $line = $_.ToString() -replace '[\x00-\x1F\x7F]', ''

        if ($line -match '\[download\]\s+Destination:\s+(.+)$') {
            $currentFile = Split-Path $matches[1].Trim() -Leaf
        }
        elseif ($line -match '\[download\]\s+(.+?) has already been downloaded') {
            $currentFile = Split-Path $matches[1].Trim() -Leaf
        }

        if ($line -match $progressRegex) {
            $percent = [double]$matches[1]
            $size    = $matches[2]
            $speed   = $matches[3]
            $eta     = $matches[4]

            $statusParts = @()
            if ($currentFile) { $statusParts += $currentFile }
            if ($size)        { $statusParts += "Boyut: $size" }
            if ($speed)       { $statusParts += "Hız: $speed" }
            if ($eta)         { $statusParts += "ETA: $eta" }
            $status = if ($statusParts.Count -gt 0) { $statusParts -join " | " } else { "İndiriliyor..." }

            Write-Progress -Activity $Activity -Status $status -PercentComplete ([Math]::Min([Math]::Max($percent, 0), 100))
        }
        elseif ($line -match '\[Merger\]|\[ExtractAudio\]|\[Metadata\]|\[EmbedThumbnail\]') {
            Write-Progress -Activity $Activity -Status "İşleniyor: $currentFile" -PercentComplete 100
        }

        # Ham satırı konsola orijinal haliyle yaz
        Write-Host $line
    }

    Write-Progress -Activity $Activity -Completed
}

Clear-Host

if ($mainSecim -eq "1" -or $mainSecim -eq "2") {
    foreach ($file in $txtFiles) {
        $folderName = $file.BaseName
        $outputFolder = Join-Path $currentDir $folderName
        if (-not (Test-Path $outputFolder)) { New-Item -Path $outputFolder -ItemType Directory | Out-Null }

        Write-Host "`n>>> İŞLENİYOR: $folderName" -ForegroundColor Yellow
        Write-Host ">>> Turbo mod aktif, işlem başlatılıyor..." -ForegroundColor DarkGray

        $fullArgs = $fastArgs + $modeArgs + @("--batch-file", $file.FullName, "-o", "$outputFolder\%(playlist_index)02d - %(title)s.%(ext)s")
        Invoke-YtDlpWithProgress -Arguments $fullArgs -Activity "İndiriliyor: $folderName"
    }
}
elseif ($mainSecim -eq "3" -or $mainSecim -eq "4") {
    $pLink = Read-Host "  ► Playlist Linkini Yapıştırın"
    $pFolder = Read-Host "  ► Klasör Adı"
    $outputFolder = Join-Path $currentDir $pFolder
    if (-not (Test-Path $outputFolder)) { New-Item -Path $outputFolder -ItemType Directory | Out-Null }

    Write-Host "`n>>> PLAYLIST BAŞLATILIYOR: $pFolder" -ForegroundColor Yellow
    Write-Host ">>> Hızlı Tarama Aktif: İşlem doğrudan başlatılıyor..." -ForegroundColor DarkGray
    Write-Host "-------------------------------------------------------------------------------" -ForegroundColor Cyan

    $fullArgs = $fastArgs + $modeArgs + @("-o", "$outputFolder\%(playlist_index)02d - %(title)s.%(ext)s", $pLink)
    Invoke-YtDlpWithProgress -Arguments $fullArgs -Activity "Playlist indiriliyor: $pFolder"
}

Write-Host "`n-------------------------------------------------------------------------------" -ForegroundColor Cyan
Write-Host "TÜM İŞLEMLER BAŞARIYLA TAMAMLANDI!" -ForegroundColor Green
Pause