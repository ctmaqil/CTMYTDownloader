using System;
using System.IO;
using System.Text.Json;

namespace YouTubeDownloader
{
    public class AppSettings
    {
        public string OutputFolder { get; set; } = string.Empty;
        public string LastSelectedQuality { get; set; } = "Best Available";
        public string LastSelectedFormat { get; set; } = "Video - MP4 (H264)";
        public int MaxSimultaneousDownloads { get; set; } = 2;
        public bool IsMultipleMode { get; set; } = false;

        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "YouTubeDownloader",
            "settings.json"
        );

        // Cache JsonSerializerOptions for better performance
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch
            {
                // If loading fails, return default settings
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                string json = JsonSerializer.Serialize(this, JsonOptions);
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // Ignore save errors
            }
        }
    }
}
