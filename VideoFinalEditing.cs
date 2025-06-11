using System.Diagnostics;
using Whisper.net;
using System.Text;

namespace ShortsAutomator
{
    public class VideoProcessor
    {
        private readonly string _defaultPath;
        private readonly string _inputPath;
        private readonly string _outputDirectory;
        private readonly string _ffmpegPath;
        private readonly string _modelPath;
        private readonly SubtitleSettings _subtitleSettings;

        public class SubtitleSettings
        {
            public string FontName { get; set; } = "Kids Magazine";
            public int FontSize { get; set; } = 24;
            public string TextColor { get; set; } = "white";
            public int OutlineWidth { get; set; } = 3;
            public string OutlineColor { get; set; } = "black";
            public int VerticalMargin { get; set; } = 150;
            public int HorizontalMargin { get; set; } = 20;
        }


        public class Caption
        {
            public required string Text { get; set; }
            public TimeSpan StartTime { get; set; }
            public TimeSpan Duration { get; set; }
        }

        public VideoProcessor(string inputPath, string outputDirectory = "finished", SubtitleSettings? subtitleSettings = null)
        {
            _inputPath = inputPath;
            _outputDirectory = Path.GetFullPath(outputDirectory);
            _defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "finished");
            _ffmpegPath = Path.Combine(@"C:\ffmpeg\bin", "ffmpeg.exe");
            _modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "ggml-base.bin");
            _subtitleSettings = subtitleSettings ?? new SubtitleSettings();

            Directory.CreateDirectory(_outputDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(_modelPath)!);
        }

        public async Task ProcessVideo(string outputFileName)
        {
            try
            {
                Console.WriteLine($"[Debug] Starting video processing for file: {_inputPath}");
                Console.WriteLine($"[Debug] Output directory: {_outputDirectory}");
                Console.WriteLine($"[Debug] FFmpeg path: {_ffmpegPath}");

                string outputPath;
                if (string.IsNullOrEmpty(outputFileName))
                {
                    outputPath = Path.Combine(_outputDirectory,
                        $"processed_{Path.GetFileNameWithoutExtension(_inputPath)}{Path.GetExtension(_inputPath)}");
                }
                else
                {
                    outputPath = Path.Combine(_outputDirectory, outputFileName);
                }
                Console.WriteLine($"[Debug] Final output path: {outputPath}");

                string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
                Directory.CreateDirectory(tempDir);
                Console.WriteLine($"[Debug] Created temp directory: {tempDir}");

                try
                {
                    string audioPath = Path.Combine(tempDir, "audio.wav");
                    Console.WriteLine($"[Debug] About to extract audio to: {audioPath}");
                    await ExtractAudioHighQuality(_inputPath, audioPath);
                    Console.WriteLine("[Debug] Audio extraction completed");

                    string subtitlesPath = Path.Combine(tempDir, "subtitles.srt");
                    Console.WriteLine($"[Debug] About to generate subtitles to: {subtitlesPath}");
                    await GenerateSubtitlesWithSpeechDetection(audioPath, subtitlesPath);
                    Console.WriteLine("[Debug] Subtitles generation completed");

                    Console.WriteLine("[Debug] About to burn subtitles into video");
                    await BurnSubtitlesToVideo(_inputPath, outputPath, subtitlesPath);
                    Console.WriteLine("[Debug] Subtitle burning completed");

                    Console.WriteLine($"[Debug] Video processing completed successfully! Output saved to: {outputPath}");
                }
                finally
                {
                    Console.WriteLine($"[Debug] Cleaning up temp directory: {tempDir}");
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Debug] Error processing video: {ex.Message}");
                Console.WriteLine($"[Debug] Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private async Task ExtractAudioHighQuality(string videoPath, string audioPath)
        {
            Console.WriteLine($"[Debug] ExtractAudioHighQuality started");
            Console.WriteLine($"[Debug] Video path exists: {File.Exists(videoPath)}");
            Console.WriteLine($"[Debug] FFmpeg path exists: {File.Exists(_ffmpegPath)}");

            if (!File.Exists(_ffmpegPath))
            {
                throw new FileNotFoundException($"FFmpeg not found at path: {_ffmpegPath}");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = $"-i \"{videoPath}\" -vn -acodec pcm_s16le -ar 16000 -ac 1 \"{audioPath}\"",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Console.WriteLine($"[Debug] Starting FFmpeg with arguments: {startInfo.Arguments}");

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                throw new Exception("Failed to start FFmpeg process");
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

            var outputTask = Task.Run(async () =>
            {
                while (!process.StandardOutput.EndOfStream)
                {
                    var line = await process.StandardOutput.ReadLineAsync();
                    Console.WriteLine($"[FFmpeg Output] {line}");
                }
            }, cts.Token);

            var errorTask = Task.Run(async () =>
            {
                while (!process.StandardError.EndOfStream)
                {
                    var line = await process.StandardError.ReadLineAsync();
                    Console.WriteLine($"[FFmpeg Error] {line}");
                }
            }, cts.Token);

            Console.WriteLine("[Debug] Waiting for FFmpeg process to complete...");

            try
            {
                await process.WaitForExitAsync(cts.Token);
                await Task.WhenAll(outputTask, errorTask);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("[Debug] FFmpeg process timed out after 2 minutes");
                process.Kill();
                throw new TimeoutException("FFmpeg process timed out after 2 minutes");
            }

            Console.WriteLine($"[Debug] FFmpeg process completed with exit code: {process.ExitCode}");

            if (process.ExitCode != 0)
            {
                throw new Exception($"Failed to extract audio from video. FFmpeg exit code: {process.ExitCode}");
            }
        }

        private async Task GenerateSubtitlesWithSpeechDetection(string audioPath, string subtitlesPath)
        {
            if (!File.Exists(_modelPath))
            {
                Console.WriteLine("Downloading Whisper model...");
                await DownloadModel();
            }

            using var whisperFactory = WhisperFactory.FromPath(_modelPath);
            using var processor = whisperFactory.CreateBuilder()
                .WithLanguageDetection()
                .Build();

            using var fileStream = File.OpenRead(audioPath);
            var segments = processor.ProcessAsync(fileStream);

            using var writer = new StreamWriter(subtitlesPath);
            int index = 1;

            try
            {
                await foreach (var segment in segments)
                {
                    var text = segment.Text.Trim().ToUpper(); // Convert to uppercase here
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                    for (int i = 0; i < words.Length; i += 3)
                    {
                        var wordChunk = words.Skip(i).Take(3).ToArray();
                        if (wordChunk.Length == 0) continue;

                        var chunkText = string.Join(" ", wordChunk);

                        var totalDuration = segment.End - segment.Start;
                        var chunkDuration = totalDuration * (wordChunk.Length / (double)words.Length);
                        var chunkStart = segment.Start + (totalDuration * (i / (double)words.Length));
                        var chunkEnd = chunkStart + chunkDuration;

                        await writer.WriteLineAsync(index.ToString());
                        await writer.WriteLineAsync($"{FormatTimestamp(chunkStart)} --> {FormatTimestamp(chunkEnd)}");
                        await writer.WriteLineAsync(chunkText);
                        await writer.WriteLineAsync();

                        index++;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing segments: {ex.Message}");
                throw;
            }
        }
        private async Task DownloadModel()
        {
            try
            {
                using var httpClient = new System.Net.Http.HttpClient();
                Console.WriteLine("Downloading Whisper model from HuggingFace...");
                var modelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin";

                var response = await httpClient.GetAsync(modelUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var buffer = new byte[8192];
                var totalBytesRead = 0L;

                using var fileStream = File.Create(_modelPath);
                using var stream = await response.Content.ReadAsStreamAsync();

                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    totalBytesRead += bytesRead;

                    if (totalBytes > 0)
                    {
                        var percentage = (int)((totalBytesRead * 100) / totalBytes);
                        Console.Write($"\rDownloading model: {percentage}%");
                    }
                }
                Console.WriteLine("\nModel download completed!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError downloading model: {ex.Message}");
                throw;
            }
        }

        private async Task BurnSubtitlesToVideo(string inputPath, string outputPath, string subtitlesPath)
        {
            Console.WriteLine($"[Debug] BurnSubtitlesToVideo started");

            foreach (var file in new[] { (inputPath, "Input video"), (subtitlesPath, "Subtitles"), (_ffmpegPath, "FFmpeg") })
            {
                if (!File.Exists(file.Item1))
                {
                    throw new FileNotFoundException($"{file.Item2} file not found: {file.Item1}");
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            var dimensionsMatch = System.Text.RegularExpressions.Regex.Match(
                await GetVideoInfo(inputPath),
                @"(\d{2,4})x(\d{2,4})"
            );

            if (!dimensionsMatch.Success)
            {
                throw new Exception("Could not determine video dimensions");
            }

            var videoDimensions = dimensionsMatch.Value;
            var escapedSubtitlesPath = subtitlesPath.Replace(@"\", "/").Replace(":", "\\:").Replace(" ", "\\ ");

            var subtitleStyle = $"Fontname={_subtitleSettings.FontName}," +
                    $"Fontsize={_subtitleSettings.FontSize}," +
                    $"PrimaryColour=&H{ColorToASS(_subtitleSettings.TextColor)}," +
                    $"OutlineColour=&H{ColorToASS(_subtitleSettings.OutlineColor)}," +
                    $"Outline={_subtitleSettings.OutlineWidth}," +
                    $"MarginV={_subtitleSettings.VerticalMargin}," +
                    $"MarginH={_subtitleSettings.HorizontalMargin}," +
                    "Alignment=2," +  // 2 = centered
                    "BorderStyle=1";   // 1 = outline only, no box

            var startInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = $"-y -i \"{inputPath}\" " +
                           $"-vf \"subtitles='{escapedSubtitlesPath}':" +
                           $"original_size={videoDimensions}:" +
                           $"force_style='{subtitleStyle}'\" " +
                           $"-c:v libx264 -preset ultrafast " +
                           $"-c:a copy " +
                           $"\"{outputPath}\"",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Console.WriteLine($"[Debug] Starting FFmpeg with command: {startInfo.Arguments}");

            var processCompletionSource = new TaskCompletionSource<bool>();
            using var process = new Process { StartInfo = startInfo };

            process.EnableRaisingEvents = true;
            process.Exited += (sender, args) => processCompletionSource.TrySetResult(true);

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();
            var lastProgressTime = DateTime.UtcNow;
            var hasProgress = false;

            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    Console.WriteLine($"[FFmpeg Output] {e.Data}");
                    outputBuilder.AppendLine(e.Data);
                    lastProgressTime = DateTime.UtcNow;
                    hasProgress = true;
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    Console.WriteLine($"[FFmpeg Error] {e.Data}");
                    errorBuilder.AppendLine(e.Data);

                    if (e.Data.Contains("time=") || e.Data.Contains("frame="))
                    {
                        lastProgressTime = DateTime.UtcNow;
                        hasProgress = true;
                    }
                }
            };

            if (!process.Start())
            {
                throw new Exception("Failed to start FFmpeg process");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(30));

                var progressMonitoringTask = Task.Run(async () =>
                {
                    while (!process.HasExited)
                    {
                        if (cts.Token.IsCancellationRequested)
                        {
                            throw new OperationCanceledException("Operation was canceled.");
                        }

                        await Task.Delay(1000, cts.Token);

                        if (hasProgress && DateTime.UtcNow - lastProgressTime > TimeSpan.FromMinutes(2))
                        {
                            throw new TimeoutException("FFmpeg process stalled - no progress for 2 minutes");
                        }
                    }
                }, cts.Token);

                try
                {
                    await Task.WhenAny(processCompletionSource.Task, progressMonitoringTask);
                }
                catch (OperationCanceledException)
                {
                    process.Kill();
                    throw new TimeoutException("FFmpeg process timed out after 30 minutes");
                }

                if (!process.HasExited)
                {
                    process.Kill();
                    throw new TimeoutException("FFmpeg process timed out");
                }

                if (process.ExitCode != 0)
                {
                    throw new Exception(
                        $"FFmpeg process failed with exit code: {process.ExitCode}\n" +
                        $"Error output:\n{errorBuilder}\n" +
                        $"Standard output:\n{outputBuilder}");
                }

                if (!File.Exists(outputPath))
                {
                    throw new Exception($"Output file was not created at: {outputPath}");
                }

                var fileInfo = new FileInfo(outputPath);
                if (fileInfo.Length == 0)
                {
                    throw new Exception($"Output file was created but is empty: {outputPath}");
                }

                Console.WriteLine($"[Debug] Successfully created output file: {outputPath} (size: {fileInfo.Length:N0} bytes)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Debug] Exception during video processing: {ex.Message}");
                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill();
                        Console.WriteLine("[Debug] Killed FFmpeg process");
                    }
                    catch (Exception killEx)
                    {
                        Console.WriteLine($"[Debug] Error killing process: {killEx.Message}");
                    }
                }
                throw;
            }
        }

        private async Task<string> GetVideoInfo(string videoPath)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = $"-i \"{videoPath}\"",
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            var output = new StringBuilder();
            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                    output.AppendLine(e.Data);
            };

            process.Start();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            return output.ToString();
        }

        private string FormatTimestamp(TimeSpan time)
        {
            return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00},{time.Milliseconds:000}";
        }

        private static string ColorToASS(string color)
        {
            // Convert common color names to ASS format (AABBGGRR)
            return color.ToLower() switch
            {
                "white" => "FFFFFF",
                "black" => "000000",
                "yellow" => "00FFFF",
                "red" => "0000FF",
                "green" => "00FF00",
                "blue" => "FF0000",
                _ => color.TrimStart('#') // Assume it's already a hex color
            };
        }
    }
}