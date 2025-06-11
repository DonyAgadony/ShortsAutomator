using Serilog;
namespace ShortsAutomator
{
    public class VideoMetadata
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public string OutputPath { get; set; }

        public VideoMetadata(string title, string description, string outputPath)
        {
            Title = title;
            Description = description;
            OutputPath = outputPath;
        }
    }

    public class Program
    {
        public static async Task Main(string[] args)
        {
            // Initialize logging
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console()
                .WriteTo.File("log.txt", rollingInterval: RollingInterval.Day)
                .CreateLogger();

            // Setup directories
            EnsureDirectoriesExist();

            // Clean up MP3 files before starting
            CleanupDirectory("MP3");

            try
            {
                Console.WriteLine("[Debug] Starting program...");

                // Initialize services
                ConfigurationService.Initialize();
                FFmpegConfig.Initialize();

                // Check if specific post ID was provided
                bool processSpecificPost = false;
                string? specificPostId = null;

                if (args.Length > 0)
                {
                    if (args[0] == "--post-id" && args.Length > 1)
                    {
                        specificPostId = args[1];
                        processSpecificPost = true;
                        Console.WriteLine($"[Debug] Processing specific post with ID: {specificPostId}");
                    }
                    else if (args[0] == "--help")
                    {
                        ShowHelp();
                        return;
                    }
                }

                // Process Reddit posts (either specific post or default behavior)
                if (processSpecificPost && specificPostId != null)
                {
                    await RedditService.ProcessSpecificRedditPost(specificPostId);
                }
                else
                {
                    await RedditService.ProcessRedditPosts();
                }

                // Process all posts found in the Posts directory
                await ProcessPostsToVideos();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Debug] Fatal error: {ex.Message}");
                Log.Fatal(ex, "Fatal error occurred during processing");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        private static void ShowHelp()
        {
            Console.WriteLine("Reddit Video Creator Help");
            Console.WriteLine("------------------------");
            Console.WriteLine("Usage:");
            Console.WriteLine("  --help           : Show this help message");
            Console.WriteLine("  --post-id <id>   : Process a specific Reddit post by ID");
            Console.WriteLine("                     (ID format example: \"abcd123\", without the \"t3_\" prefix)");
            Console.WriteLine("\nExample:");
            Console.WriteLine("  dotnet run -- --post-id abcd123");
        }

        private static void EnsureDirectoriesExist()
        {
            string[] requiredDirs = ["MP3", "Posts", "BeforeCaptions", "finished"];
            foreach (var dir in requiredDirs)
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }
        }

        private static void CleanupDirectory(string directory)
        {
            if (Directory.Exists(directory))
            {
                foreach (var file in Directory.GetFiles(directory))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, $"Failed to delete file {file}");
                    }
                }
            }
        }

        private static async Task ProcessPostsToVideos()
        {
            var videoMetadataList = new List<VideoMetadata>();

            // Process posts and collect metadata
            foreach (var redditPost in Directory.GetFiles("Posts"))
            {
                var postContent = await File.ReadAllTextAsync(redditPost);
                var title = Path.GetFileNameWithoutExtension(redditPost);
                await TextToSpeechService.TurnTextToSpeech(redditPost);

                var gemini = new GeminiService(redditPost);
                var description = await gemini.GenerateDescription();

                videoMetadataList.Add(new VideoMetadata(title, description ?? "", "")); // Store metadata
                File.Delete(redditPost);
            }

            // Process all audio files
            var audioFiles = Directory.GetFiles("MP3");
            Console.WriteLine($"[Debug] Found {audioFiles.Length} audio files");
            for (int i = 0; i < audioFiles.Length; i++)
            {
                var audioFile = audioFiles[i];
                Console.WriteLine($"[Debug] Creating TikTok video for: {audioFile}");
                CreateVideo createVideo = new(audioFile);
                File.Delete(audioFile);
            }

            // Process videos and update output paths
            var videoFiles = Directory.GetFiles("BeforeCaptions");
            Console.WriteLine($"[Debug] Found {videoFiles.Length} video files");

            // Make sure we have metadata for each video
            while (videoMetadataList.Count < videoFiles.Length)
            {
                var title = Path.GetFileNameWithoutExtension(videoFiles[videoMetadataList.Count]);
                videoMetadataList.Add(new VideoMetadata(title, "Generated video", ""));
            }

            Console.WriteLine($"[Debug] Processing {videoFiles.Length} videos...");

            for (int i = 0; i < videoFiles.Length; i++)
            {
                var videoFile = videoFiles[i];
                Console.WriteLine($"[Debug] Processing video {i + 1} of {videoFiles.Length}: {videoFile}");

                try
                {
                    VideoProcessor processor = new(videoFile, "finished");
                    string outputFileName = Path.GetFileName(videoFile);
                    string outputPath = Path.Combine("finished", outputFileName);

                    await processor.ProcessVideo(outputFileName);
                    videoMetadataList[i].OutputPath = outputPath;
                    File.Delete(videoFile);

                    Console.WriteLine($"[Debug] Successfully processed video: {outputFileName}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Error] Failed to process video {videoFile}: {ex.Message}");
                    continue; // Continue with next video even if one fails
                }
            }

            Console.WriteLine($"[Debug] Video processing completed. Created {videoMetadataList.Count} videos in the 'finished' directory.");
        }
    }
}