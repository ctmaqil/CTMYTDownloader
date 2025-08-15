using System.ComponentModel;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace YouTubeDownloader
{
    public class DownloadItem : INotifyPropertyChanged
    {
        private string _title = "";
        private string _status = "Queued";
        private double _progress = 0;
        private Video? _video;
        private StreamManifest? _streamManifest;
        private string _quality = "🏆 Best Available";
        private string _format = "🎬 Video - MP4 (H264)";

        public string Url { get; set; } = "";

        public string Title
        {
            get => _title;
            set
            {
                _title = value;
                OnPropertyChanged();
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged();
            }
        }

        public double Progress
        {
            get => _progress;
            set
            {
                _progress = value;
                OnPropertyChanged();
            }
        }

        public string Quality
        {
            get => _quality;
            set
            {
                _quality = value;
                OnPropertyChanged();
            }
        }

        public string Format
        {
            get => _format;
            set
            {
                _format = value;
                OnPropertyChanged();
            }
        }

        public Video? Video
        {
            get => _video;
            set
            {
                _video = value;
                if (value != null)
                {
                    Title = value.Title.Length > 35 ? value.Title[..35] + "..." : value.Title;
                }
                OnPropertyChanged();
            }
        }

        public StreamManifest? StreamManifest
        {
            get => _streamManifest;
            set
            {
                _streamManifest = value;
                OnPropertyChanged();
                PopulateAvailableQualities();
            }
        }

        public List<string> AvailableQualities { get; set; } = [];
        public List<string> AvailableFormats { get; set; } = [
            "🎬 Video - MP4 (H264)",
            "🎵 Audio Only - MP3",
            "🎵 Audio Only - WAV",
            "🎵 Audio Only - AAC",
            "🎵 Audio Only - FLAC"
        ];

        private void PopulateAvailableQualities()
        {
            AvailableQualities.Clear();
            AvailableQualities.Add("🏆 Best Available");

            if (_streamManifest != null)
            {
                var videoQualities = _streamManifest.GetVideoOnlyStreams()
                    .Select(s => s.VideoQuality.Label)
                    .Distinct()
                    .OrderByDescending(GetQualityValue)
                    .ToList();

                foreach (var quality in videoQualities)
                {
                    string emoji = GetQualityEmoji(quality);
                    AvailableQualities.Add($"{emoji} {quality}");
                }

                var audioQualities = _streamManifest.GetAudioOnlyStreams()
                    .OrderByDescending(s => s.Bitrate)
                    .Take(3)
                    .Select(s => $"🎵 {s.Bitrate.KiloBitsPerSecond:F0} kbps")
                    .ToList();

                AvailableQualities.AddRange(audioQualities);
            }

            OnPropertyChanged(nameof(AvailableQualities));
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

        public event PropertyChangedEventHandler? PropertyChanged;

        // FIX 3: Proper PropertyChanged implementation
        public void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
