using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using FFMpegCore;
using FFMpegCore.Enums;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using YoutubeExplode.Videos;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

// Use alias to avoid ambiguity with MessageBox
using WpfMessageBox = System.Windows.MessageBox;
using YoutubeExplode.Common;

namespace YouTubeDownloader
{
    public partial class MainWindow : Window
    {
        private readonly HttpClient httpClient;
        private readonly YoutubeClient youtubeClient;
        private readonly AppSettings appSettings;
        private bool isDownloading = false;
        private Video? currentVideo;
        private StreamManifest? currentStreamManifest;
        private CancellationTokenSource? cancellationTokenSource;
        private DateTime downloadStartTime;
        private long totalBytes;
        private long downloadedBytes;


        // Remove the GeneratedRegex lines completely and replace with:
        private static readonly Regex SpaceRegex = new(@"\s+", RegexOptions.Compiled);


        public MainWindow()
        {
            InitializeComponent();

            // Initialize fields
            httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            youtubeClient = new YoutubeClient();
            appSettings = AppSettings.Load();

            InitializeApplication();
            SetupFFmpeg();
        }

        private void InitializeApplication()
        {
            // Initialize quality combobox with default values
            InitializeQualityComboBox();

            // Set saved format selection
            SetSavedFormatSelection();

            // Set initial UI state
            DownloadButton.IsEnabled = false;
            VideoInfoGroup.Visibility = Visibility.Collapsed;

            // Setup output folder
            SetupOutputFolder();
        }

        private void SetupOutputFolder()
        {
            string outputFolder = appSettings.OutputFolder;

            // If no saved folder or folder doesn't exist, prompt user
            if (string.IsNullOrEmpty(outputFolder) || !Directory.Exists(outputFolder))
            {
                var result = WpfMessageBox.Show(
                    "Please select a folder where downloaded videos will be saved.",
                    "Select Output Folder",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.OK)
                {
                    if (SelectOutputFolder())
                    {
                        outputFolder = appSettings.OutputFolder;
                    }
                    else
                    {
                        // User cancelled, use default but don't create it
                        outputFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "YouTube Downloads");
                        appSettings.OutputFolder = outputFolder;

                        WpfMessageBox.Show(
                            $"Default output folder set to: {outputFolder}\nYou can change this anytime using the Browse button.",
                            "Default Folder Set",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
                else
                {
                    // User doesn't want to select folder, close application
                    System.Windows.Application.Current.Shutdown();
                    return;
                }
            }

            OutputPathTextBox.Text = outputFolder;
        }

        private bool SelectOutputFolder()
        {
            try
            {
                var dialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Select folder for downloaded videos",
                    ShowNewFolderButton = true
                };

                if (!string.IsNullOrEmpty(appSettings.OutputFolder))
                {
                    dialog.SelectedPath = appSettings.OutputFolder;
                }

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    appSettings.OutputFolder = dialog.SelectedPath;
                    appSettings.Save();
                    OutputPathTextBox.Text = dialog.SelectedPath;
                    return true;
                }
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"Error selecting folder: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return false;
        }

        private void InitializeQualityComboBox()
        {
            QualityComboBox.Items.Clear();
            QualityComboBox.Items.Add("🏆 Best Available");
            QualityComboBox.SelectedIndex = 0;
        }

        private void SetSavedFormatSelection()
        {
            // Set saved format selection
            for (int i = 0; i < FormatComboBox.Items.Count; i++)
            {
                if (FormatComboBox.Items[i] is System.Windows.Controls.ComboBoxItem item)
                {
                    string content = item.Content?.ToString() ?? "";
                    if (content.Contains(appSettings.LastSelectedFormat))
                    {
                        FormatComboBox.SelectedIndex = i;
                        break;
                    }
                }
            }

            if (FormatComboBox.SelectedIndex == -1)
                FormatComboBox.SelectedIndex = 0;
        }

        private void SetupFFmpeg()
        {
            try
            {
                string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string ffmpegPath = Path.Combine(appDirectory, "ffmpeg");

                string ffmpegExe = Path.Combine(ffmpegPath, "ffmpeg.exe");
                string ffprobeExe = Path.Combine(ffmpegPath, "ffprobe.exe");

                if (File.Exists(ffmpegExe) && File.Exists(ffprobeExe))
                {
                    GlobalFFOptions.Configure(new FFOptions
                    {
                        BinaryFolder = ffmpegPath,
                        TemporaryFilesFolder = Path.GetTempPath()
                    });

                    UpdateProgressStatus("Application ready. Enter a YouTube URL to begin.", 0);
                }
                else
                {
                    UpdateProgressStatus("FFmpeg not found - video conversion may not work.", 0);
                }
            }
            catch (Exception ex)
            {
                UpdateProgressStatus($"FFmpeg setup failed: {ex.Message}", 0);
            }
        }

        private async void PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string url = UrlTextBox.Text.Trim();
                if (string.IsNullOrEmpty(url) || !IsValidYouTubeUrl(url))
                {
                    WpfMessageBox.Show("Please enter a valid YouTube URL", "Invalid URL",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                await LoadVideoPreview(url);
            }
            catch (OperationCanceledException)
            {
                UpdateProgressStatus("Operation canceled.", 0);
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"Error loading video preview: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                ResetProgressStatus("Error loading video information.");
            }
        }

        private async Task LoadVideoPreview(string url)
        {
            cancellationTokenSource?.Cancel();
            cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = cancellationTokenSource.Token;

            try
            {
                SetLoadingState(true);
                UpdateProgressStatus("Fetching video information...", 10);

                // Get video metadata with timeout and cancellation
                var videoTask = youtubeClient.Videos.GetAsync(url, cancellationToken);
                var manifestTask = youtubeClient.Videos.Streams.GetManifestAsync(url, cancellationToken);

                UpdateProgressStatus("Loading video details...", 30);
                currentVideo = await videoTask;

                UpdateProgressStatus("Loading stream information...", 60);
                currentStreamManifest = await manifestTask;

                UpdateProgressStatus("Processing video information...", 80);

                // Update UI with video information
                TitleTextBlock.Text = currentVideo.Title;
                DurationTextBlock.Text = FormatDuration(currentVideo.Duration);
                ViewsTextBlock.Text = FormatViews(currentVideo.Engagement.ViewCount);

                // Load thumbnail asynchronously
                UpdateProgressStatus("Loading thumbnail...", 90);
                _ = LoadThumbnailAsync(currentVideo.Thumbnails.GetWithHighestResolution().Url, cancellationToken);

                // Update quality options based on available streams
                UpdateQualityOptionsFromVideo();

                VideoInfoGroup.Visibility = Visibility.Visible;
                DownloadButton.IsEnabled = true;
                UpdateProgressStatus("Video loaded successfully. Ready to download.", 100);
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private void UpdateQualityOptionsFromVideo()
        {
            if (currentStreamManifest == null) return;

            // Get actually available video qualities from the current video
            var availableQualities = currentStreamManifest.GetVideoOnlyStreams()
                .Select(s => s.VideoQuality.Label)
                .Distinct()
                .OrderByDescending(GetQualityValue)
                .ToList();

            QualityComboBox.Items.Clear();
            QualityComboBox.Items.Add("🏆 Best Available");

            // Only add qualities that are actually available for this video
            foreach (var quality in availableQualities)
            {
                string emoji = GetQualityEmoji(quality);
                QualityComboBox.Items.Add($"{emoji} {quality}");
            }

            // Try to select the previously saved quality if available
            bool foundSavedQuality = false;
            for (int i = 0; i < QualityComboBox.Items.Count; i++)
            {
                string item = QualityComboBox.Items[i]?.ToString() ?? "";
                if (item.Contains(appSettings.LastSelectedQuality))
                {
                    QualityComboBox.SelectedIndex = i;
                    foundSavedQuality = true;
                    break;
                }
            }

            if (!foundSavedQuality)
                QualityComboBox.SelectedIndex = 0;
        }

        private static string GetQualityEmoji(string quality)
        {
            return quality switch
            {
                var q when q.Contains("4320") => "🌟", // 8K
                var q when q.Contains("2160") => "🎬", // 4K
                var q when q.Contains("1440") => "📹", // 2K
                var q when q.Contains("1080") => "🎥", // Full HD
                var q when q.Contains("720") => "📺",  // HD
                _ => "📱" // Others
            };
        }

        private static int GetQualityValue(string quality)
        {
            if (quality.Contains("4320")) return 4320;
            if (quality.Contains("2160")) return 2160;
            if (quality.Contains("1440")) return 1440;
            if (quality.Contains("1080")) return 1080;
            if (quality.Contains("720")) return 720;
            if (quality.Contains("480")) return 480;
            if (quality.Contains("360")) return 360;
            if (quality.Contains("240")) return 240;
            if (quality.Contains("144")) return 144;
            return 0;
        }

        private static string FormatDuration(TimeSpan? duration)
        {
            if (!duration.HasValue) return "--:--";
            var d = duration.Value;
            if (d.TotalHours >= 1)
                return $"{(int)d.TotalHours}:{d.Minutes:D2}:{d.Seconds:D2}";
            else
                return $"{d.Minutes}:{d.Seconds:D2}";
        }

        private static string FormatViews(long views)
        {
            return views switch
            {
                >= 1_000_000_000 => $"{views / 1_000_000_000.0:F1}B",
                >= 1_000_000 => $"{views / 1_000_000.0:F1}M",
                >= 1_000 => $"{views / 1_000.0:F1}K",
                _ => views.ToString()
            };
        }

        private async Task LoadThumbnailAsync(string thumbnailUrl, CancellationToken cancellationToken)
        {
            try
            {
                byte[] imageData = await httpClient.GetByteArrayAsync(thumbnailUrl, cancellationToken);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = new MemoryStream(imageData);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                Dispatcher.Invoke(() => ThumbnailImage.Source = bitmap);
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation
            }
            catch
            {
                Dispatcher.Invoke(() => ThumbnailImage.Source = null);
            }
        }

        private void SetLoadingState(bool isLoading)
        {
            PreviewButton.IsEnabled = !isLoading;
            PreviewButton.Content = isLoading ? "🔄 Loading..." : "🔍 Preview";
            DownloadButton.IsEnabled = !isLoading && VideoInfoGroup.Visibility == Visibility.Visible && currentVideo != null;
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            SelectOutputFolder();
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (isDownloading || currentVideo == null || currentStreamManifest == null) return;

            // Ensure output folder exists before downloading
            string outputDir = OutputPathTextBox.Text;
            if (string.IsNullOrEmpty(outputDir))
            {
                WpfMessageBox.Show("Please select an output folder first.", "No Output Folder",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                SelectOutputFolder();
                return;
            }

            try
            {
                // Create output directory if it doesn't exist
                Directory.CreateDirectory(outputDir);
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"Cannot create output folder: {ex.Message}", "Folder Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Save user preferences
            SaveUserPreferences();

            try
            {
                isDownloading = true;
                SetDownloadingState(true);

                await StartActualDownload();

                WpfMessageBox.Show("Download completed successfully!", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                UpdateProgressStatus("Download completed successfully!", 100);
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"Download error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateProgressStatus("Download failed.", 0);
            }
            finally
            {
                isDownloading = false;
                SetDownloadingState(false);
                ResetDownloadTracking();
            }
        }

        private void SaveUserPreferences()
        {
            appSettings.LastSelectedQuality = GetSelectedComboBoxText(QualityComboBox);
            appSettings.LastSelectedFormat = GetSelectedComboBoxText(FormatComboBox);
            appSettings.OutputFolder = OutputPathTextBox.Text;
            appSettings.Save();
        }

        private async Task StartActualDownload()
        {
            string selectedFormat = GetSelectedComboBoxText(FormatComboBox);
            string selectedQuality = GetSelectedComboBoxText(QualityComboBox);

            UpdateProgressStatus($"Starting download: {selectedQuality}, {selectedFormat}", 0);
            InitializeDownloadTracking();

            if (selectedFormat.Contains("Audio Only"))
            {
                await DownloadAudioOnly(selectedFormat);
            }
            else
            {
                await DownloadVideo(selectedQuality);
            }
        }

        private void InitializeDownloadTracking()
        {
            downloadStartTime = DateTime.Now;
            totalBytes = 0;
            downloadedBytes = 0;
            SpeedLabel.Text = "Speed: Calculating...";
            SizeLabel.Text = "Size: Calculating...";
            EtaLabel.Text = "ETA: Calculating...";
        }

        private void ResetDownloadTracking()
        {
            SpeedLabel.Text = "Speed: --";
            SizeLabel.Text = "Size: --";
            EtaLabel.Text = "ETA: --";
        }

        private void UpdateDownloadProgress(long bytesDownloaded, long totalFileSize)
        {
            downloadedBytes = bytesDownloaded;
            if (totalBytes == 0) totalBytes = totalFileSize;

            var elapsed = DateTime.Now - downloadStartTime;
            if (elapsed.TotalSeconds > 0)
            {
                double speed = downloadedBytes / elapsed.TotalSeconds; // bytes per second
                double speedMBps = speed / (1024 * 1024); // MB per second

                SpeedLabel.Text = speedMBps > 1 ? $"Speed: {speedMBps:F1} MB/s" : $"Speed: {speed / 1024:F1} KB/s";
                SizeLabel.Text = $"Size: {FormatBytes(downloadedBytes)}/{FormatBytes(totalBytes)}";

                if (speed > 0 && totalBytes > downloadedBytes)
                {
                    double remainingSeconds = (totalBytes - downloadedBytes) / speed;
                    EtaLabel.Text = $"ETA: {FormatTime(TimeSpan.FromSeconds(remainingSeconds))}";
                }
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            return $"{number:n1} {suffixes[counter]}";
        }

        private static string FormatTime(TimeSpan timeSpan)
        {
            if (timeSpan.TotalHours >= 1)
                return $"{(int)timeSpan.TotalHours}h {timeSpan.Minutes}m";
            else if (timeSpan.TotalMinutes >= 1)
                return $"{(int)timeSpan.TotalMinutes}m {timeSpan.Seconds}s";
            else
                return $"{timeSpan.Seconds}s";
        }

        private static string GetSelectedComboBoxText(System.Windows.Controls.ComboBox comboBox)
        {
            if (comboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item)
            {
                return item.Content?.ToString() ?? "";
            }
            return comboBox.SelectedItem?.ToString() ?? "";
        }

        private async Task DownloadVideo(string selectedQuality)
        {
            if (currentVideo == null || currentStreamManifest == null) return;

            try
            {
                // Get the best video and audio streams
                IVideoStreamInfo videoStream;

                if (selectedQuality.Contains("Best Available"))
                {
                    videoStream = currentStreamManifest.GetVideoOnlyStreams().GetWithHighestVideoQuality();
                }
                else
                {
                    string qualityLabel = selectedQuality.Split(' ').Last();
                    videoStream = currentStreamManifest.GetVideoOnlyStreams()
                        .Where(s => s.VideoQuality.Label == qualityLabel)
                        .OrderByDescending(s => s.Bitrate)
                        .FirstOrDefault() ?? currentStreamManifest.GetVideoOnlyStreams().GetWithHighestVideoQuality();
                }

                var audioStream = currentStreamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

                // Create file paths
                string safeTitle = SanitizeFileName(currentVideo.Title);
                string outputDir = OutputPathTextBox.Text;
                string tempVideoPath = Path.Combine(Path.GetTempPath(), $"{currentVideo.Id}_video.{videoStream.Container}");
                string tempAudioPath = Path.Combine(Path.GetTempPath(), $"{currentVideo.Id}_audio.{audioStream.Container}");
                string outputFilePath = Path.Combine(outputDir, $"{safeTitle}.mp4");

                // Fixed: Calculate total size for progress tracking - FileSize is struct, not nullable
                totalBytes = videoStream.Size.Bytes + audioStream.Size.Bytes;

                // Download video stream with progress tracking
                UpdateProgressStatus("Downloading video stream...", 20);
                await DownloadWithProgress(videoStream, tempVideoPath, 0.6); // 60% of total progress

                // Download audio stream with progress tracking
                UpdateProgressStatus("Downloading audio stream...", 70);
                await DownloadWithProgress(audioStream, tempAudioPath, 0.9); // 90% of total progress

                // Merge video and audio using FFmpeg
                UpdateProgressStatus("Merging video and audio...", 90);
                await FFMpegArguments
                    .FromFileInput(tempVideoPath)
                    .AddFileInput(tempAudioPath)
                    .OutputToFile(outputFilePath, true, options => options
                        .WithVideoCodec(VideoCodec.LibX264)
                        .WithAudioCodec("aac")
                        .WithVariableBitrate(4)
                        .WithFastStart())
                    .ProcessAsynchronously();

                // Clean up temp files
                UpdateProgressStatus("Cleaning up...", 95);
                CleanupTempFiles(tempVideoPath, tempAudioPath);

                UpdateProgressStatus($"Video saved: {Path.GetFileName(outputFilePath)}", 100);
            }
            catch (Exception ex)
            {
                throw new Exception($"Video download failed: {ex.Message}");
            }
        }

        private async Task DownloadAudioOnly(string selectedFormat)
        {
            if (currentVideo == null || currentStreamManifest == null) return;

            try
            {
                var audioStream = currentStreamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

                // Extract audio format from selection
                string audioFormat = selectedFormat.Split('-')[1].Trim().ToLower();
                string safeTitle = SanitizeFileName(currentVideo.Title);
                string outputDir = OutputPathTextBox.Text;

                string tempAudioPath = Path.Combine(Path.GetTempPath(), $"{currentVideo.Id}_audio.{audioStream.Container}");
                string outputFilePath = Path.Combine(outputDir, $"{safeTitle}.{audioFormat}");

                // Fixed: Set total size for progress tracking - FileSize is struct, not nullable
                totalBytes = audioStream.Size.Bytes;

                // Download audio stream with progress tracking
                UpdateProgressStatus("Downloading audio stream...", 30);
                await DownloadWithProgress(audioStream, tempAudioPath, 0.7); // 70% of total progress

                // Convert to desired format using FFmpeg
                UpdateProgressStatus($"Converting to {audioFormat.ToUpper()}...", 80);
                await FFMpegArguments
                    .FromFileInput(tempAudioPath)
                    .OutputToFile(outputFilePath, true, options => options
                        .WithAudioCodec(GetAudioCodec(audioFormat))
                        .WithVariableBitrate(4))
                    .ProcessAsynchronously();

                // Clean up temp file
                UpdateProgressStatus("Cleaning up...", 95);
                CleanupTempFiles(tempAudioPath);

                UpdateProgressStatus($"Audio saved: {Path.GetFileName(outputFilePath)}", 100);
            }
            catch (Exception ex)
            {
                throw new Exception($"Audio download failed: {ex.Message}");
            }
        }

        private async Task DownloadWithProgress(IStreamInfo streamInfo, string filePath, double progressWeight)
        {
            using var httpClient = new HttpClient();
            using var response = await httpClient.GetAsync(streamInfo.Url, HttpCompletionOption.ResponseHeadersRead);
            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = File.Create(filePath);

            var streamBytes = response.Content.Headers.ContentLength ?? 0;
            var buffer = new byte[8192];
            long streamDownloaded = 0;
            int bytesRead;

            var streamStartTime = DateTime.Now;

            while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                streamDownloaded += bytesRead;

                // Update progress tracking
                var elapsed = DateTime.Now - streamStartTime;
                if (elapsed.TotalSeconds > 0)
                {
                    UpdateDownloadProgress(streamDownloaded, streamBytes);
                }

                // Update overall progress bar
                if (streamBytes > 0)
                {
                    double streamProgress = (double)streamDownloaded / streamBytes;
                    double overallProgress = streamProgress * progressWeight * 100;
                    ProgressBar.Value = Math.Min(overallProgress, progressWeight * 100);
                    ProgressPercentage.Text = $"{overallProgress:F0}%";
                }
            }
        }

        private static void CleanupTempFiles(params string[] files)
        {
            foreach (string file in files)
            {
                try
                {
                    if (File.Exists(file))
                        File.Delete(file);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }

        private static string GetAudioCodec(string format)
        {
            return format.ToLower() switch
            {
                "mp3" => "libmp3lame",
                "wav" => "pcm_s16le",
                "aac" => "aac",
                "flac" => "flac",
                _ => "libmp3lame"
            };
        }

        private static string SanitizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = string.Concat(fileName.Where(c => !invalidChars.Contains(c)));

            // Remove extra spaces and limit length
            sanitized = SpaceRegex.Replace(sanitized, " ").Trim(); // Remove () from SpaceRegex()
            if (sanitized.Length > 100)
                sanitized = sanitized[..100];

            return sanitized;
        }


        private void SetDownloadingState(bool isDownloading)
        {
            DownloadButton.IsEnabled = !isDownloading;
            DownloadButton.Content = isDownloading ? "⏳ Downloading..." : "⬇️ Download";
            PreviewButton.IsEnabled = !isDownloading;
            BrowseButton.IsEnabled = !isDownloading;
            QualityComboBox.IsEnabled = !isDownloading;
            FormatComboBox.IsEnabled = !isDownloading;
        }

        private void UpdateProgressStatus(string message, double progress)
        {
            ProgressLabel.Text = message;
            ProgressBar.Value = progress;
            ProgressPercentage.Text = $"{progress:F0}%";
        }

        private void ResetProgressStatus(string message)
        {
            UpdateProgressStatus(message, 0);
            ResetDownloadTracking();
        }

        private static bool IsValidYouTubeUrl(string url)
        {
            return url.Contains("youtube.com/watch") || url.Contains("youtu.be/") ||
                   url.Contains("youtube.com/embed/") || url.Contains("youtube.com/v/");
        }

        protected override void OnClosed(EventArgs e)
        {
            cancellationTokenSource?.Cancel();
            cancellationTokenSource?.Dispose();
            httpClient?.Dispose();
            base.OnClosed(e);
        }
    }
}
