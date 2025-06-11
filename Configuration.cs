using FFMpegCore;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace ShortsAutomator
{
    public static class ConfigurationService
    {
        public static IConfiguration? Configuration { get; private set; }
        public static AppSettings? Settings { get; private set; }

        public static void Initialize()
        {
            SetupConfiguration();
            SetupLogging();
            InitializeSettings();
            InitializeFFmpeg(); // Add this line
        }
        private static void SetupConfiguration()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            Configuration = builder.Build();
        }

        private static void SetupLogging()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day)
                .CreateLogger();
        }

        private static void InitializeSettings()
        {
            Log.Debug("Reading FFmpeg configuration...");
            Log.Debug("Binary Folder: {Folder}", Configuration?["FFmpeg:BinaryFolder"]);
            Log.Debug("FFmpeg Path: {Path}", Configuration?["FFmpeg:FFmpegPath"]);
            Log.Debug("FFprobe Path: {Path}", Configuration?["FFmpeg:FFprobePath"]);
#pragma warning disable CS8604 // Possible null reference argument.
            Settings = new AppSettings
            {
                FFmpegPath = Path.Combine(Configuration!["FFmpeg:BinaryFolder"], "ffmpeg.exe"),
                FFprobePath = Path.Combine(Configuration["FFmpeg:BinaryFolder"], "ffprobe.exe"),
                PostsDirectory = Configuration["Output:PostsDirectory"],
                MP3Directory = Configuration["Output:MP3Directory"],
                BackgroundMusicFolder = Configuration["BackgroundMusic:Folder"],
                RedditSettings = new RedditSettings
                {
                    ClientId = Configuration["Reddit:ClientId"],
                    ClientSecret = Configuration["Reddit:ClientSecret"],
                    Username = Configuration["Reddit:Username"],
                    Password = Configuration["Reddit:Password"]
                }
            };
#pragma warning restore CS8604 // Possible null reference argument.

            ValidateSettings();
        }

        private static void ValidateSettings()
        {
            if (string.IsNullOrEmpty(Settings!.FFmpegPath) || string.IsNullOrEmpty(Settings.FFprobePath))
            {
                throw new InvalidOperationException("FFmpeg paths are not properly configured in appsettings.json");
            }

            // Check if the files actually exist
            if (!File.Exists(Settings.FFmpegPath))
            {
                throw new FileNotFoundException($"FFmpeg executable not found at: {Settings.FFmpegPath}");
            }

            if (!File.Exists(Settings.FFprobePath))
            {
                throw new FileNotFoundException($"FFprobe executable not found at: {Settings.FFprobePath}");
            }

            Directory.CreateDirectory(Settings.PostsDirectory!);
            Directory.CreateDirectory(Settings.MP3Directory!);
        }

        private static void InitializeFFmpeg()
        {
            string ffmpegExePath = Settings!.FFmpegPath!;
            string ffprobeExePath = Settings.FFprobePath!;

            Log.Debug("Current Directory: {Dir}", Directory.GetCurrentDirectory());
            Log.Debug("FFmpeg exists: {Exists}", File.Exists(ffmpegExePath));
            Log.Debug("FFprobe exists: {Exists}", File.Exists(ffprobeExePath));

            // Try to list files in the bin directory
            string binDirectory = Path.GetDirectoryName(ffmpegExePath)!;
            if (Directory.Exists(binDirectory))
            {
                Log.Debug("Files in {Dir}:", binDirectory);
                foreach (var file in Directory.GetFiles(binDirectory))
                {
                    Log.Debug("Found file: {File}", file);
                }
            }
            else
            {
                Log.Error("Directory does not exist: {Dir}", binDirectory);
            }

            if (!File.Exists(ffmpegExePath) || !File.Exists(ffprobeExePath))
            {
                throw new FileNotFoundException(
                    "FFmpeg executables not found. Please ensure both ffmpeg.exe and ffprobe.exe are present in the configured path.");
            }

            GlobalFFOptions.Configure(new FFOptions { BinaryFolder = binDirectory! });
            Log.Information("FFmpeg initialized successfully from {Path}", binDirectory);
        }
    }
    public static class FFmpegConfig
    {
        private const string FFmpegPath = @"C:\ffmpeg\bin";

        public static void Initialize()
        {
            if (!ValidateFFmpegInstallation())
            {
                throw new FileNotFoundException(
                    "FFmpeg executables not found. Please ensure both ffmpeg.exe and ffprobe.exe are present in C:\\ffmpeg\\bin");
            }

            GlobalFFOptions.Configure(new FFOptions { BinaryFolder = FFmpegPath });
            Log.Information("FFmpeg initialized successfully from {Path}", FFmpegPath);
        }

        private static bool ValidateFFmpegInstallation()
        {
            return File.Exists(Path.Combine(FFmpegPath, "ffmpeg.exe")) &&
                   File.Exists(Path.Combine(FFmpegPath, "ffprobe.exe"));
        }
    }

}
