# CTM YT Downloader

A professional, feature-rich YouTube video and Shorts downloader built with C# and WPF. Download single videos or multiple videos simultaneously with individual quality settings and real-time progress tracking. 

## 🌟 Features

### Core Functionality
- **Single Video Download** - Download individual YouTube videos and Shorts
- **Multiple Video Download** - Batch download up to 20 videos simultaneously
- **YouTube Shorts Support** - Full support for YouTube Shorts URLs
- **High Quality Downloads** - Support for 4K, 8K, and various quality options into H.264 MP4 (works with Adobe Apps)
- **Audio Extraction** - Extract audio in MP3, WAV, AAC, and FLAC formats

### Advanced Features
- **Individual Video Settings** - Set different quality and format for each video in batch downloads
- **Real-time Progress Tracking** - Live progress bars with speed, ETA, and size information
- **Concurrent Downloads** - Configurable simultaneous download limits (1-5)
- **Smart URL Processing** - Automatic conversion of Shorts and mobile URLs to standard format
- **Settings Persistence** - Remembers your preferences between sessions

### User Interface
- **Modern WPF Interface** - Clean, professional design with intuitive navigation
- **Responsive Layout** - Adaptive UI that works at different window sizes
- **Dark Mode Ready** - Professional color scheme and styling
- **Progress Visualization** - Individual and overall progress tracking

## 📋 Requirements

### System Requirements
- **Operating System**: Windows 10 or later
- **.NET Runtime**: .NET 8.0 or later
- **FFmpeg**: Required for video/audio processing (included in releases)

### Dependencies
- **YoutubeExplode** - YouTube API interaction
- **FFMpegCore** - Video and audio processing
- **System.Text.Json** - Settings management

## 🚀 Installation

###  Download Release (Recommended)
1. Go to the [Releases](https://github.com/ctmaqil/CTMYTDownloader/releases) page
2. Download the latest `CTMYTDownloader-v1.0.zip`
3. Extract to your desired location
4. Run `YouTubeDownloader.exe`



## 📖 Usage

### Single Video Download
1. Select "Single Video" mode
2. Paste YouTube video or Shorts URL
3. Click "Preview" to load video information
4. Choose quality and format
5. Select output folder
6. Click "Download"

### Multiple Video Download
1. Select "Multiple Videos" mode
2. Enter URLs (one per line, up to 20 videos)
3. Set maximum simultaneous downloads
4. Click "Process URLs"
5. Adjust individual quality/format settings for each video
6. Click "Download" to start batch processing

### Supported URL Formats

https://www.youtube.com/watch?v=VIDEO_ID

https://youtu.be/VIDEO_ID

https://www.youtube.com/shorts/VIDEO_ID

https://m.youtube.com/watch?v=VIDEO_ID

https://www.youtube.com/embed/VIDEO_ID

https://www.youtube.com/live/VIDEO_ID


### Quality Options
- **Video**: Best Available, 8K, 4K, 2K, 1080p, 720p, 480p, 360p, 240p, 144p
- **Audio**: Best Available, 320kbps, 256kbps, 192kbps, 128kbps

### Format Options
- **Video**: MP4 (H.264) 
- **Audio Only**: MP3, WAV, AAC, FLAC

## 🛠️ Configuration

### Settings Location
Settings are automatically saved to:

%APPDATA%\YouTubeDownloader\settings.json


### Configurable Options
- Default output folder
- Preferred quality and format settings
- Maximum simultaneous downloads
- Download mode preference

## 🔧 Troubleshooting

### Common Issues

**Q: "FFmpeg not found" error**
- **A**: Ensure FFmpeg binaries are in the `ffmpeg` folder next to the executable

**Q: Downloads fail or are slow**
- **A**: Check your internet connection and try reducing simultaneous downloads

**Q: Some videos can't be downloaded**
- **A**: The video may be region-locked, private, or have download restrictions

**Q: Audio extraction fails**
- **A**: Ensure FFmpeg is properly installed and accessible

### Performance Tips
- Use 2-3 simultaneous downloads for optimal speed
- Close other bandwidth-intensive applications
- Ensure adequate disk space for downloads

## 🏗️ Technical Details

### Architecture
- **Frontend**: WPF with MVVM pattern
- **Backend**: C# .NET 8
- **YouTube API**: YoutubeExplode library
- **Media Processing**: FFMpegCore
- **Configuration**: JSON-based settings

### Key Components
- `MainWindow.xaml/.cs` - Main UI and application logic
- `DownloadItem.cs` - Individual download item model
- `AppSettings.cs` - Settings management
- `FFmpeg/` - Media processing binaries

## 🤝 Contributing

Contributions are welcome! Please feel free to submit pull requests, create issues, or suggest new features.

### Development Setup
1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

### Code Style
- Follow C# naming conventions
- Use meaningful variable and method names
- Add comments for complex logic
- Ensure proper error handling

## 📄 License

NIL

## 🙏 Acknowledgments

- **YoutubeExplode** - For providing an excellent YouTube API wrapper
- **FFMpegCore** - For seamless media processing capabilities
- **Microsoft** - For the .NET framework and WPF

## 📞 Support

- **Website**: [CTM STUDIOS](https://ctmstudios.org)
- **Issues**: [GitHub Issues](https://github.com/ctmaqil/CTMYTDownloader/issues)
- **Discussions**: [GitHub Discussions](https://github.com/ctmaqil/CTMYTDownloader/discussions)

## 📊 Project Stats

![GitHub release (latest by date)](https://img.shields.io/github/v/release/ctmaqil/CTMYTDownloader)
![GitHub downloads](https://img.shields.io/github/downloads/ctmaqil/CTMYTDownloader/total)
![GitHub license](https://img.shields.io/github/license/ctmaqil/CTMYTDownloader)
![GitHub stars](https://img.shields.io/github/stars/ctmaqil/CTMYTDownloader)

---

**Made with ❤️ by [CTM STUDIOS](https://ctmstudios.org)**

*Download responsibly and respect content creators' rights.*
