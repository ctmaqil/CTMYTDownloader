using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
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
using System.Collections.Generic;

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

        private readonly ObservableCollection<DownloadItem> downloadItems;
        private readonly SemaphoreSlim downloadSemaphore;
        private bool isMultipleDownloading = false;

        private readonly Queue<(DateTime Time, long Bytes)> downloadHistory = [];
        private const int HistoryWindowSeconds = 10;

        private static readonly Regex SpaceRegex = new(@"\s+", RegexOptions.Compiled);

        public MainWindow()
        {
            InitializeComponent();

            httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            youtubeClient = new YoutubeClient();
            appSettings = AppSettings.Load();
            downloadItems = [];
            downloadSemaphore = new SemaphoreSlim(2, 10);

            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                InitializeApplication();
                SetupFFmpeg();
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"Error initializing application: {ex.Message}", "Initialization Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InitializeApplication()
        {
            InitializeQualityComboBox();
            SetSavedFormatSelection();

            if (appSettings.IsMultipleMode && MultiModeRadio != null)
            {
                MultiModeRadio.IsChecked = true;
                ShowMultipleMode();
            }
            else if (SingleModeRadio != null)
            {
                SingleModeRadio.IsChecked = true;
                ShowSingleMode();
            }

            if (MaxDownloadsComboBox != null)
            {
                MaxDownloadsComboBox.SelectedIndex = Math.Max(0, Math.Min(4, appSettings.MaxSimultaneousDownloads - 1));
                UpdateDownloadSemaphore();
            }

            if (DownloadStatusList != null)
                DownloadStatusList.ItemsSource = downloadItems;

            if (DownloadButton != null)
                DownloadButton.IsEnabled = false;

            if (VideoInfoGroup != null)
                VideoInfoGroup.Visibility = Visibility.Collapsed;

            SetupOutputFolder();
        }

        private void SetupOutputFolder()
        {
            string outputFolder = appSettings.OutputFolder;

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
                    System.Windows.Application.Current.Shutdown();
                    return;
                }
            }

            if (OutputPathTextBox != null)
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
                    if (OutputPathTextBox != null)
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

        private void UpdateDownloadSemaphore()
        {
            if (MaxDownloadsComboBox == null) return;

            int maxDownloads = MaxDownloadsComboBox.SelectedIndex + 1;
            downloadSemaphore.Release(downloadSemaphore.CurrentCount);
            for (int i = 0; i < maxDownloads; i++)
            {
                downloadSemaphore.Wait(0);
            }
        }

        private void ModeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (SingleModeRadio == null || MultiModeRadio == null) return;

            if (SingleModeRadio.IsChecked == true)
            {
                ShowSingleMode();
                appSettings.IsMultipleMode = false;
            }
            else
            {
                ShowMultipleMode();
                appSettings.IsMultipleMode = true;
            }
            appSettings.Save();
        }

        private void ShowSingleMode()
        {
            if (SingleVideoGroup != null)
                SingleVideoGroup.Visibility = Visibility.Visible;
            if (MultipleVideoGroup != null)
                MultipleVideoGroup.Visibility = Visibility.Collapsed;
            if (MultipleProgressGroup != null)
                MultipleProgressGroup.Visibility = Visibility.Collapsed;
            if (SingleProgressGroup != null)
                SingleProgressGroup.Visibility = Visibility.Visible;

            // FIX 1: Update global selectors state
            UpdateGlobalSelectorsState();
        }

        private void ShowMultipleMode()
        {
            if (SingleVideoGroup != null)
                SingleVideoGroup.Visibility = Visibility.Collapsed;
            if (MultipleVideoGroup != null)
                MultipleVideoGroup.Visibility = Visibility.Visible;
            if (MultipleProgressGroup != null)
                MultipleProgressGroup.Visibility = Visibility.Visible;
            if (SingleProgressGroup != null)
                SingleProgressGroup.Visibility = Visibility.Collapsed;
            if (VideoInfoGroup != null)
                VideoInfoGroup.Visibility = Visibility.Collapsed;

            // FIX 1: Update global selectors state
            UpdateGlobalSelectorsState();
        }

        // FIX 1: Disable global selectors in multiple mode
        private void UpdateGlobalSelectorsState()
        {
            bool isSingleMode = SingleModeRadio?.IsChecked == true;

            if (FormatComboBox != null)
                FormatComboBox.IsEnabled = isSingleMode;

            if (QualityComboBox != null)
                QualityComboBox.IsEnabled = isSingleMode;
        }

        private void FormatComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateQualityOptionsForFormat();
        }

        private void UpdateQualityOptionsForFormat()
        {
            if (FormatComboBox == null || QualityLabel == null) return;

            string selectedFormat = GetSelectedComboBoxText(FormatComboBox);

            if (selectedFormat.Contains("Audio Only"))
            {
                QualityLabel.Text = "Audio Quality:";
                UpdateAudioQualityOptions();
            }
            else
            {
                QualityLabel.Text = "Video Quality:";
                if (currentStreamManifest != null)
                {
                    UpdateQualityOptionsFromVideo();
                }
                else
                {
                    InitializeQualityComboBox();
                }
            }
        }

        private void UpdateAudioQualityOptions()
        {
            if (QualityComboBox == null) return;

            QualityComboBox.Items.Clear();
            QualityComboBox.Items.Add("🏆 Best Available");

            if (currentStreamManifest != null)
            {
                var audioStreams = currentStreamManifest.GetAudioOnlyStreams()
                    .OrderByDescending(s => s.Bitrate)
                    .ToList();

                foreach (var stream in audioStreams.Take(5))
                {
                    string bitrateKbps = $"{stream.Bitrate.KiloBitsPerSecond:F0} kbps";
                    QualityComboBox.Items.Add($"🎵 {bitrateKbps}");
                }
            }
            else
            {
                QualityComboBox.Items.Add("🎵 320 kbps");
                QualityComboBox.Items.Add("🎵 256 kbps");
                QualityComboBox.Items.Add("🎵 192 kbps");
                QualityComboBox.Items.Add("🎵 128 kbps");
            }

            QualityComboBox.SelectedIndex = 0;
        }

        private async void ProcessUrlsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MultipleUrlsTextBox == null || ProcessUrlsButton == null) return;

                string urlsText = MultipleUrlsTextBox.Text.Trim();
                if (string.IsNullOrEmpty(urlsText))
                {
                    WpfMessageBox.Show("Please enter some YouTube URLs", "No URLs",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var urls = urlsText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(url => url.Trim())
                    .Where(url => !string.IsNullOrEmpty(url) && IsValidYouTubeUrl(url))
                    .Select(url => NormalizeYouTubeUrl(url))  // Normalize URLs including Shorts
                    .Take(20)
                    .ToList();

                if (urls.Count == 0)
                {
                    WpfMessageBox.Show("No valid YouTube URLs found", "Invalid URLs",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                downloadItems.Clear();
                ProcessUrlsButton.IsEnabled = false;
                ProcessUrlsButton.Content = "Processing...";
                UpdateOverallProgress();

                foreach (string url in urls)
                {
                    downloadItems.Add(new DownloadItem { Url = url, Title = "Processing...", Status = "Loading" });
                }

                var tasks = downloadItems.Select(async item =>
                {
                    try
                    {
                        // Normalize the URL before processing
                        string normalizedUrl = NormalizeYouTubeUrl(item.Url);

                        var video = await youtubeClient.Videos.GetAsync(normalizedUrl);
                        var streamManifest = await youtubeClient.Videos.Streams.GetManifestAsync(normalizedUrl);

                        item.Video = video;
                        item.StreamManifest = streamManifest;
                        item.Format = "🎬 Video - MP4 (H264)";
                        item.Quality = "🏆 Best Available";
                        item.Status = "Ready";

                        item.OnPropertyChanged(nameof(item.Quality));
                        item.OnPropertyChanged(nameof(item.Format));
                        item.OnPropertyChanged(nameof(item.Status));
                    }
                    catch (Exception ex)
                    {
                        item.Title = $"Failed to load: {ex.Message[..Math.Min(ex.Message.Length, 30)]}...";
                        item.Status = "Error";
                        item.OnPropertyChanged(nameof(item.Title));
                        item.OnPropertyChanged(nameof(item.Status));
                    }
                }).ToArray();

                await Task.WhenAll(tasks);

                if (DownloadButton != null)
                    DownloadButton.IsEnabled = downloadItems.Count(item => item.Status == "Ready") > 0;

                UpdateOverallProgress();
                UpdateGlobalSelectorsState();
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"Error processing URLs: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (ProcessUrlsButton != null)
                {
                    ProcessUrlsButton.IsEnabled = true;
                    ProcessUrlsButton.Content = "📋 Process URLs";
                }
            }
        }


        private void InitializeQualityComboBox()
        {
            if (QualityComboBox == null) return;

            QualityComboBox.Items.Clear();
            QualityComboBox.Items.Add("🏆 Best Available");
            QualityComboBox.SelectedIndex = 0;
        }

        private void SetSavedFormatSelection()
        {
            if (FormatComboBox == null) return;

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
                if (UrlTextBox == null) return;

                string url = UrlTextBox.Text.Trim();
                if (string.IsNullOrEmpty(url) || !IsValidYouTubeUrl(url))
                {
                    WpfMessageBox.Show("Please enter a valid YouTube URL (including Shorts)", "Invalid URL",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Normalize the URL to handle Shorts
                string normalizedUrl = NormalizeYouTubeUrl(url);
                await LoadVideoPreview(normalizedUrl);
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

                var videoTask = youtubeClient.Videos.GetAsync(url, cancellationToken);
                var manifestTask = youtubeClient.Videos.Streams.GetManifestAsync(url, cancellationToken);

                UpdateProgressStatus("Loading video details...", 30);
                currentVideo = await videoTask;

                UpdateProgressStatus("Loading stream information...", 60);
                currentStreamManifest = await manifestTask;

                UpdateProgressStatus("Processing video information...", 80);

                if (TitleTextBlock != null)
                    TitleTextBlock.Text = currentVideo.Title;

                if (DurationTextBlock != null)
                    DurationTextBlock.Text = FormatDuration(currentVideo.Duration);

                if (ViewsTextBlock != null)
                    ViewsTextBlock.Text = FormatViews(currentVideo.Engagement.ViewCount);

                UpdateProgressStatus("Loading thumbnail...", 90);
                _ = LoadThumbnailAsync(currentVideo.Thumbnails.GetWithHighestResolution().Url, cancellationToken);

                UpdateQualityOptionsForFormat();

                if (VideoInfoGroup != null)
                    VideoInfoGroup.Visibility = Visibility.Visible;

                if (DownloadButton != null)
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
            if (currentStreamManifest == null || QualityComboBox == null) return;

            var availableQualities = currentStreamManifest.GetVideoOnlyStreams()
                .Select(s => s.VideoQuality.Label)
                .Distinct()
                .OrderByDescending(GetQualityValue)
                .ToList();

            QualityComboBox.Items.Clear();
            QualityComboBox.Items.Add("🏆 Best Available");

            foreach (var quality in availableQualities)
            {
                string emoji = GetQualityEmoji(quality);
                QualityComboBox.Items.Add($"{emoji} {quality}");
            }

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
                var q when q.Contains("4320") => "🌟",
                var q when q.Contains("2160") => "🎬",
                var q when q.Contains("1440") => "📹",
                var q when q.Contains("1080") => "🎥",
                var q when q.Contains("720") => "📺",
                _ => "📱"
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

                Dispatcher.Invoke(() =>
                {
                    if (ThumbnailImage != null)
                        ThumbnailImage.Source = bitmap;
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                Dispatcher.Invoke(() =>
                {
                    if (ThumbnailImage != null)
                        ThumbnailImage.Source = null;
                });
            }
        }

        private void SetLoadingState(bool isLoading)
        {
            if (PreviewButton != null)
            {
                PreviewButton.IsEnabled = !isLoading;
                PreviewButton.Content = isLoading ? "🔄 Loading..." : "🔍 Preview";
            }

            if (DownloadButton != null && SingleModeRadio != null && MultiModeRadio != null)
            {
                DownloadButton.IsEnabled = !isLoading &&
                    ((SingleModeRadio.IsChecked == true && VideoInfoGroup?.Visibility == Visibility.Visible && currentVideo != null) ||
                     (MultiModeRadio.IsChecked == true && downloadItems.Count(item => item.Status == "Ready") > 0));
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            SelectOutputFolder();
        }

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string folderPath = OutputPathTextBox?.Text ?? "";
                if (Directory.Exists(folderPath))
                {
                    Process.Start("explorer.exe", folderPath);
                }
                else
                {
                    WpfMessageBox.Show("Output folder does not exist.", "Folder Not Found",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"Error opening folder: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            SaveUserPreferences();

            if (SingleModeRadio?.IsChecked == true)
            {
                await HandleSingleDownload();
            }
            else
            {
                await HandleMultipleDownloads();
            }
        }

        private async Task HandleSingleDownload()
        {
            if (isDownloading || currentVideo == null || currentStreamManifest == null) return;

            string outputDir = OutputPathTextBox?.Text ?? "";
            if (!EnsureOutputFolder(outputDir)) return;

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

        private async Task HandleMultipleDownloads()
        {
            if (isMultipleDownloading) return;

            var readyItems = downloadItems.Where(item => item.Status == "Ready").ToList();
            if (readyItems.Count == 0)
            {
                WpfMessageBox.Show("No videos ready for download. Please process URLs first.", "No Videos",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string outputDir = OutputPathTextBox?.Text ?? "";
            if (!EnsureOutputFolder(outputDir)) return;

            try
            {
                isMultipleDownloading = true;
                SetDownloadingState(true);

                UpdateDownloadSemaphore();

                var downloadTasks = readyItems.Select(item => DownloadSingleItemAsync(item, outputDir));
                await Task.WhenAll(downloadTasks);

                WpfMessageBox.Show($"All downloads completed! {readyItems.Count} videos downloaded.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                UpdateOverallProgress();
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"Multiple download error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                isMultipleDownloading = false;
                SetDownloadingState(false);
            }
        }

        private async Task DownloadSingleItemAsync(DownloadItem item, string outputDir)
        {
            await downloadSemaphore.WaitAsync();

            try
            {
                if (item.Video == null || item.StreamManifest == null) return;

                item.Status = "Downloading";
                item.Progress = 0;
                item.OnPropertyChanged(nameof(item.Status));
                item.OnPropertyChanged(nameof(item.Progress));

                string selectedFormat = item.Format;
                string selectedQuality = item.Quality;

                if (selectedFormat.Contains("Audio Only"))
                {
                    await DownloadAudioOnlyMultiple(item, item.StreamManifest, outputDir, selectedFormat, selectedQuality);
                }
                else
                {
                    await DownloadVideoMultiple(item, item.StreamManifest, outputDir, selectedQuality);
                }

                item.Status = "Completed";
                item.Progress = 100;
                item.OnPropertyChanged(nameof(item.Status));
                item.OnPropertyChanged(nameof(item.Progress));
            }
            catch (Exception ex)
            {
                item.Status = $"Error: {ex.Message[..Math.Min(ex.Message.Length, 30)]}...";
                item.Progress = 0;
                item.OnPropertyChanged(nameof(item.Status));
                item.OnPropertyChanged(nameof(item.Progress));
            }
            finally
            {
                downloadSemaphore.Release();
                Dispatcher.Invoke(UpdateOverallProgress);
            }
        }

        private async Task DownloadVideoMultiple(DownloadItem item, StreamManifest streamManifest, string outputDir, string selectedQuality)
        {
            if (item.Video == null) return;

            IVideoStreamInfo videoStream;

            if (selectedQuality.Contains("Best Available"))
            {
                videoStream = streamManifest.GetVideoOnlyStreams().GetWithHighestVideoQuality();
            }
            else if (selectedQuality.Contains("kbps"))
            {
                videoStream = streamManifest.GetVideoOnlyStreams().GetWithHighestVideoQuality();
            }
            else
            {
                string qualityLabel = selectedQuality.Split(' ').Last();
                videoStream = streamManifest.GetVideoOnlyStreams()
                    .Where(s => s.VideoQuality.Label == qualityLabel)
                    .OrderByDescending(s => s.Bitrate)
                    .FirstOrDefault() ?? streamManifest.GetVideoOnlyStreams().GetWithHighestVideoQuality();
            }

            var audioStream = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

            string safeTitle = SanitizeFileName(item.Video.Title);
            string tempVideoPath = Path.Combine(Path.GetTempPath(), $"{item.Video.Id}_video.{videoStream.Container}");
            string tempAudioPath = Path.Combine(Path.GetTempPath(), $"{item.Video.Id}_audio.{audioStream.Container}");
            string outputFilePath = Path.Combine(outputDir, $"{safeTitle}.mp4");

            try
            {
                await DownloadStreamWithProgress(videoStream, tempVideoPath, item, 0, 40);
                await DownloadStreamWithProgress(audioStream, tempAudioPath, item, 40, 70);

                item.Progress = 75;
                item.OnPropertyChanged(nameof(item.Progress));

                await FFMpegArguments
                    .FromFileInput(tempVideoPath)
                    .AddFileInput(tempAudioPath)
                    .OutputToFile(outputFilePath, true, options => options
                        .WithVideoCodec(VideoCodec.LibX264)
                        .WithAudioCodec("aac")
                        .WithVariableBitrate(4)
                        .WithFastStart())
                    .ProcessAsynchronously();

                item.Progress = 95;
                item.OnPropertyChanged(nameof(item.Progress));
            }
            finally
            {
                CleanupTempFiles(tempVideoPath, tempAudioPath);
            }
        }

        // FIXED CS0266: Proper handling without casting issues
        private async Task DownloadAudioOnlyMultiple(DownloadItem item, StreamManifest streamManifest, string outputDir, string selectedFormat, string selectedQuality)
        {
            if (item.Video == null) return;

            // Get all audio streams as a list to avoid LINQ casting issues
            var audioStreamsList = streamManifest.GetAudioOnlyStreams().ToList();
            IAudioStreamInfo audioStream;

            if (selectedQuality.Contains("kbps"))
            {
                string bitrateStr = selectedQuality.Split(' ')[1];
                if (double.TryParse(bitrateStr, out double targetBitrate))
                {
                    // Find closest bitrate using direct list access
                    IAudioStreamInfo? closestStream = null;
                    double closestDifference = double.MaxValue;

                    foreach (var stream in audioStreamsList)
                    {
                        double difference = Math.Abs(stream.Bitrate.KiloBitsPerSecond - targetBitrate);
                        if (difference < closestDifference)
                        {
                            closestDifference = difference;
                            closestStream = stream;
                        }
                    }

                    audioStream = closestStream ?? audioStreamsList.OrderByDescending(s => s.Bitrate).First();
                }
                else
                {
                    audioStream = audioStreamsList.OrderByDescending(s => s.Bitrate).First();
                }
            }
            else
            {
                audioStream = audioStreamsList.OrderByDescending(s => s.Bitrate).First();
            }

            string audioFormat = selectedFormat.Split('-')[1].Trim().ToLower();
            string safeTitle = SanitizeFileName(item.Video.Title);
            string tempAudioPath = Path.Combine(Path.GetTempPath(), $"{item.Video.Id}_audio.{audioStream.Container}");
            string outputFilePath = Path.Combine(outputDir, $"{safeTitle}.{audioFormat}");

            try
            {
                await DownloadStreamWithProgress(audioStream, tempAudioPath, item, 0, 60);

                item.Progress = 70;
                item.OnPropertyChanged(nameof(item.Progress));

                await FFMpegArguments
                    .FromFileInput(tempAudioPath)
                    .OutputToFile(outputFilePath, true, options => options
                        .WithAudioCodec(GetAudioCodec(audioFormat))
                        .WithVariableBitrate(4))
                    .ProcessAsynchronously();

                item.Progress = 95;
                item.OnPropertyChanged(nameof(item.Progress));
            }
            finally
            {
                CleanupTempFiles(tempAudioPath);
            }
        }

        private static async Task DownloadStreamWithProgress(IStreamInfo streamInfo, string filePath, DownloadItem item, double startProgress, double endProgress)
        {
            using var httpClient = new HttpClient();
            using var response = await httpClient.GetAsync(streamInfo.Url, HttpCompletionOption.ResponseHeadersRead);
            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = File.Create(filePath);

            var streamBytes = response.Content.Headers.ContentLength ?? 0;
            var buffer = new byte[8192];
            long streamDownloaded = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                streamDownloaded += bytesRead;

                if (streamBytes > 0)
                {
                    double streamProgress = (double)streamDownloaded / streamBytes;
                    double overallProgress = startProgress + (streamProgress * (endProgress - startProgress));

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        item.Progress = overallProgress;
                        item.OnPropertyChanged(nameof(item.Progress));
                    });
                }
            }
        }

        private void UpdateOverallProgress()
        {
            int total = downloadItems.Count;
            int completed = downloadItems.Count(item => item.Status == "Completed");
            int failed = downloadItems.Count(item => item.Status.StartsWith("Error"));
            int inProgress = downloadItems.Count(item => item.Status == "Downloading");

            if (OverallProgressLabel != null)
            {
                string statusText = $"Overall Progress: {completed}/{total} completed";
                if (inProgress > 0) statusText += $", {inProgress} downloading";
                if (failed > 0) statusText += $", {failed} failed";
                OverallProgressLabel.Text = statusText;
            }

            double totalProgress = downloadItems.Sum(item => item.Progress);
            double percentage = total > 0 ? totalProgress / total : 0;

            if (OverallProgressBar != null)
                OverallProgressBar.Value = percentage;

            if (OverallProgressPercentage != null)
                OverallProgressPercentage.Text = $"{percentage:F0}%";
        }

        private bool EnsureOutputFolder(string outputDir)
        {
            if (string.IsNullOrEmpty(outputDir))
            {
                WpfMessageBox.Show("Please select an output folder first.", "No Output Folder",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                SelectOutputFolder();
                return false;
            }

            try
            {
                Directory.CreateDirectory(outputDir);
                return true;
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"Cannot create output folder: {ex.Message}", "Folder Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void SaveUserPreferences()
        {
            appSettings.LastSelectedQuality = GetSelectedComboBoxText(QualityComboBox);
            appSettings.LastSelectedFormat = GetSelectedComboBoxText(FormatComboBox);
            appSettings.OutputFolder = OutputPathTextBox?.Text ?? "";
            appSettings.MaxSimultaneousDownloads = (MaxDownloadsComboBox?.SelectedIndex ?? 1) + 1;
            appSettings.IsMultipleMode = MultiModeRadio?.IsChecked == true;
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
            downloadHistory.Clear();

            if (SpeedLabel != null) SpeedLabel.Text = "Speed: Calculating...";
            if (SizeLabel != null) SizeLabel.Text = "Size: Calculating...";
            if (EtaLabel != null) EtaLabel.Text = "ETA: Calculating...";
        }

        private void ResetDownloadTracking()
        {
            downloadHistory.Clear();
            if (SpeedLabel != null) SpeedLabel.Text = "Speed: --";
            if (SizeLabel != null) SizeLabel.Text = "Size: --";
            if (EtaLabel != null) EtaLabel.Text = "ETA: --";
        }

        private void UpdateDownloadProgress(long bytesDownloaded, long totalFileSize)
        {
            downloadedBytes = bytesDownloaded;
            if (totalBytes == 0) totalBytes = totalFileSize;

            var now = DateTime.Now;
            downloadHistory.Enqueue((now, downloadedBytes));

            while (downloadHistory.Count > 0 && (now - downloadHistory.Peek().Time).TotalSeconds > HistoryWindowSeconds)
            {
                downloadHistory.Dequeue();
            }

            if (downloadHistory.Count >= 2)
            {
                var (oldestTime, oldestBytes) = downloadHistory.First();
                var (newestTime, newestBytes) = downloadHistory.Last();

                var timeSpan = newestTime - oldestTime;
                var bytesInWindow = newestBytes - oldestBytes;

                if (timeSpan.TotalSeconds > 0 && bytesInWindow > 0)
                {
                    double speed = bytesInWindow / timeSpan.TotalSeconds;
                    double speedMBps = speed / (1024 * 1024);

                    if (SpeedLabel != null)
                        SpeedLabel.Text = speedMBps > 1 ? $"Speed: {speedMBps:F1} MB/s" : $"Speed: {speed / 1024:F1} KB/s";

                    if (totalBytes > downloadedBytes && speed > 0)
                    {
                        double remainingSeconds = (totalBytes - downloadedBytes) / speed;
                        if (EtaLabel != null)
                            EtaLabel.Text = $"ETA: {FormatTime(TimeSpan.FromSeconds(remainingSeconds))}";
                    }
                    else
                    {
                        if (EtaLabel != null)
                            EtaLabel.Text = "ETA: Almost done";
                    }
                }
            }

            if (SizeLabel != null)
            {
                SizeLabel.Text = totalBytes > 0 ?
                    $"Size: {FormatBytes(downloadedBytes)}/{FormatBytes(totalBytes)}" :
                    $"Size: {FormatBytes(downloadedBytes)}";
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

        private static string GetSelectedComboBoxText(System.Windows.Controls.ComboBox? comboBox)
        {
            if (comboBox?.SelectedItem is System.Windows.Controls.ComboBoxItem item)
            {
                return item.Content?.ToString() ?? "";
            }
            return comboBox?.SelectedItem?.ToString() ?? "";
        }

        private async Task DownloadVideo(string selectedQuality)
        {
            if (currentVideo == null || currentStreamManifest == null) return;

            try
            {
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

                string safeTitle = SanitizeFileName(currentVideo.Title);
                string outputDir = OutputPathTextBox?.Text ?? "";
                string tempVideoPath = Path.Combine(Path.GetTempPath(), $"{currentVideo.Id}_video.{videoStream.Container}");
                string tempAudioPath = Path.Combine(Path.GetTempPath(), $"{currentVideo.Id}_audio.{audioStream.Container}");
                string outputFilePath = Path.Combine(outputDir, $"{safeTitle}.mp4");

                totalBytes = videoStream.Size.Bytes + audioStream.Size.Bytes;

                UpdateProgressStatus("Downloading video stream...", 20);
                await DownloadWithProgress(videoStream, tempVideoPath, 0.6);

                UpdateProgressStatus("Downloading audio stream...", 70);
                await DownloadWithProgress(audioStream, tempAudioPath, 0.9);

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

                string audioFormat = selectedFormat.Split('-')[1].Trim().ToLower();
                string safeTitle = SanitizeFileName(currentVideo.Title);
                string outputDir = OutputPathTextBox?.Text ?? "";

                string tempAudioPath = Path.Combine(Path.GetTempPath(), $"{currentVideo.Id}_audio.{audioStream.Container}");
                string outputFilePath = Path.Combine(outputDir, $"{safeTitle}.{audioFormat}");

                totalBytes = audioStream.Size.Bytes;

                UpdateProgressStatus("Downloading audio stream...", 30);
                await DownloadWithProgress(audioStream, tempAudioPath, 0.7);

                UpdateProgressStatus($"Converting to {audioFormat.ToUpper()}...", 80);
                await FFMpegArguments
                    .FromFileInput(tempAudioPath)
                    .OutputToFile(outputFilePath, true, options => options
                        .WithAudioCodec(GetAudioCodec(audioFormat))
                        .WithVariableBitrate(4))
                    .ProcessAsynchronously();

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

            while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                streamDownloaded += bytesRead;

                UpdateDownloadProgress(streamDownloaded, streamBytes);

                if (streamBytes > 0)
                {
                    double streamProgress = (double)streamDownloaded / streamBytes;
                    double overallProgress = streamProgress * progressWeight * 100;

                    if (ProgressBar != null)
                        ProgressBar.Value = Math.Min(overallProgress, progressWeight * 100);

                    if (ProgressPercentage != null)
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

            sanitized = SpaceRegex.Replace(sanitized, " ").Trim();
            if (sanitized.Length > 100)
                sanitized = sanitized[..100];

            return sanitized;
        }

        private void SetDownloadingState(bool isDownloading)
        {
            bool downloading = isDownloading || isMultipleDownloading;

            if (DownloadButton != null)
            {
                DownloadButton.IsEnabled = !downloading;
                DownloadButton.Content = downloading ? "⏳ Downloading..." : "⬇️ Download";
            }

            if (PreviewButton != null)
                PreviewButton.IsEnabled = !downloading;

            if (ProcessUrlsButton != null)
                ProcessUrlsButton.IsEnabled = !downloading;

            if (BrowseButton != null)
                BrowseButton.IsEnabled = !downloading;

            if (QualityComboBox != null)
                QualityComboBox.IsEnabled = !downloading;

            if (FormatComboBox != null)
                FormatComboBox.IsEnabled = !downloading;

            if (MaxDownloadsComboBox != null)
                MaxDownloadsComboBox.IsEnabled = !downloading;
        }

        private void UpdateProgressStatus(string message, double progress)
        {
            if (ProgressLabel != null)
                ProgressLabel.Text = message;

            if (ProgressBar != null)
                ProgressBar.Value = progress;

            if (ProgressPercentage != null)
                ProgressPercentage.Text = $"{progress:F0}%";
        }

        private void ResetProgressStatus(string message)
        {
            UpdateProgressStatus(message, 0);
            ResetDownloadTracking();
        }

        private static bool IsValidYouTubeUrl(string url)
        {
            return url.Contains("youtube.com/watch") ||
                   url.Contains("youtu.be/") ||
                   url.Contains("youtube.com/embed/") ||
                   url.Contains("youtube.com/v/") ||
                   url.Contains("youtube.com/shorts/") ||  // Add support for Shorts
                   url.Contains("m.youtube.com/watch") ||  // Mobile URLs
                   url.Contains("youtube.com/live/");      // Live streams
        }

        private static string NormalizeYouTubeUrl(string url)
        {
            try
            {
                // Handle YouTube Shorts URLs
                if (url.Contains("youtube.com/shorts/"))
                {
                    var shortsMatch = System.Text.RegularExpressions.Regex.Match(url, @"youtube\.com/shorts/([a-zA-Z0-9_-]{11})");
                    if (shortsMatch.Success)
                    {
                        string videoId = shortsMatch.Groups[1].Value;
                        return $"https://www.youtube.com/watch?v={videoId}";
                    }
                }

                // Handle youtu.be URLs
                if (url.Contains("youtu.be/"))
                {
                    var shortMatch = System.Text.RegularExpressions.Regex.Match(url, @"youtu\.be/([a-zA-Z0-9_-]{11})");
                    if (shortMatch.Success)
                    {
                        string videoId = shortMatch.Groups[1].Value;
                        return $"https://www.youtube.com/watch?v={videoId}";
                    }
                }

                // Handle mobile URLs
                if (url.Contains("m.youtube.com/watch"))
                {
                    return url.Replace("m.youtube.com", "www.youtube.com");
                }

                // Return original URL if it's already in standard format
                return url;
            }
            catch
            {
                return url; // Return original if parsing fails
            }
        }


        // Add this method to MainWindow.xaml.cs

        // Add these methods to your MainWindow.xaml.cs

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                // Open the URL in the default browser
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"Unable to open link: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

      


        private void QualityComboBox_DropDownOpened(object sender, EventArgs e)
        {
            if (sender is System.Windows.Controls.ComboBox comboBox)
            {
                // Ensure dropdown is wide enough to show full text
                comboBox.SetValue(System.Windows.Controls.ComboBox.MinWidthProperty, 280.0);
            }
        }


        protected override void OnClosed(EventArgs e)
        {
            cancellationTokenSource?.Cancel();
            cancellationTokenSource?.Dispose();
            httpClient?.Dispose();
            downloadSemaphore?.Dispose();
            base.OnClosed(e);
        }
    }
}
