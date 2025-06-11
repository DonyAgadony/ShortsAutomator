using System.Diagnostics;

namespace ShortsAutomator
{
    class CreateVideo
    {
        string AudioPath;
        const double MAX_VIDEO_LENGTH = 180; // 3 minutes in seconds
        const string MALE_TRANSITION = @"For the next part Audio\Dan.mp3";
        const string FEMALE_TRANSITION = @"For the next part Audio\scarlett.mp3";
        private static bool ValidateAudioFile(string audioPath)
        {
            try
            {
                // First convert the MP3 to a temporary WAV file to validate it
                string tempWav = Path.Combine(Path.GetTempPath(), $"temp_{Guid.NewGuid()}.wav");
                string validateCmd = $"-i \"{audioPath}\" -c:a pcm_s16le \"{tempWav}\" -y";

                bool success = RunFFmpegWithResult(validateCmd);

                if (File.Exists(tempWav))
                {
                    File.Delete(tempWav);
                }

                return success;
            }
            catch
            {
                return false;
            }
        }
        public static string GetRandomVideo()
        {
            Random random = new();
            int videoNum = random.Next(1, 4);
            return videoNum switch
            {
                1 => @"Videos\Minecraft Parkour 17 Minutes No Copyright Gameplay _ Vertical _ 64 1080.mp4",
                2 => @"Videos\SubwayGameplay_reencoded_phone.mp4",
                3 => @"Videos\TikTok Format Minecraft Parkour _ 15 Minutes No Copyright Gameplay _ 1440p 60FPS _ 42 1080.mp4",
                _ => @"Videos\Minecraft Parkour 17 Minutes No Copyright Gameplay _ Vertical _ 64 1080.mp4",
            };
        }


        public CreateVideo(string AudioPath)
        {
            this.AudioPath = AudioPath;
            try
            {
                if (!ValidateAudioFile(AudioPath))
                {
                    throw new Exception($"Invalid or corrupted audio file: {AudioPath}");
                }
                Main();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing video: {ex.Message}");
                throw;
            }
        }

        private bool IsMaleSpeaker(string audioPath)
        {
            // Use FFmpeg to analyze audio frequency characteristics
            // Male voices typically have fundamental frequencies between 85-180 Hz
            // Female voices typically have fundamental frequencies between 165-255 Hz
            string ffmpegCommand = $"-i \"{audioPath}\" -af \"silenceremove=1:0:-50dB,astats=metadata=1:reset=1,\" -f null -";

            ProcessStartInfo startInfo = new()
            {
                FileName = @"C:\ffmpeg\bin\ffmpeg.exe",
                Arguments = ffmpegCommand,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using Process process = new Process { StartInfo = startInfo };
            process.Start();
            string output = process.StandardError.ReadToEnd();
            process.WaitForExit();

            // Parse the output to get the mean frequency
            double meanFreq = ParseMeanFrequency(output);

            // If mean frequency is below 165 Hz, likely male voice
            return meanFreq < 165;
        }

        private double ParseMeanFrequency(string ffmpegOutput)
        {
            try
            {
                // Look for mean_frequency in the output
                string freqMarker = "mean_frequency=";
                int startIndex = ffmpegOutput.IndexOf(freqMarker);
                if (startIndex != -1)
                {
                    string freqString = ffmpegOutput.Substring(startIndex + freqMarker.Length);
                    int endIndex = freqString.IndexOf('\n');
                    if (endIndex != -1)
                    {
                        freqString = freqString.Substring(0, endIndex).Trim();
                        if (double.TryParse(freqString, out double frequency))
                        {
                            return frequency;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing frequency: {ex.Message}");
            }

            // Default to male voice if parsing fails
            return 120;
        }

        private string GetTransitionAudio()
        {
            bool isMale = IsMaleSpeaker(AudioPath);
            return isMale ? MALE_TRANSITION : FEMALE_TRANSITION;
        }

        // Rest of the validation and utility methods remain the same...

        void Main()
        {
            string inputVideo = GetRandomVideo();
            string inputAudio = AudioPath;
            string bgMusic = @"BackgroundMusic\BackgroundMusic.mp3";
            string transitionAudio = GetTransitionAudio();
            string outputFolder = "BeforeCaptions";
            string tempFolder = Path.Combine(outputFolder, "temp");

            Directory.CreateDirectory(outputFolder);
            Directory.CreateDirectory(tempFolder);

            double mp3Duration = GetMediaDuration(inputAudio);
            double videoDuration = GetMediaDuration(inputVideo);
            double transitionDuration = GetMediaDuration(transitionAudio);

            if (mp3Duration <= 0 || videoDuration <= 0)
            {
                throw new Exception("Error retrieving media duration. Ensure the files exist and are valid.");
            }

            Random random = new Random();
            double maxStartTime = videoDuration - mp3Duration;
            double startTime = maxStartTime > 0 ? random.NextDouble() * maxStartTime : 0;

            string FileName = Path.GetFileNameWithoutExtension(AudioPath);

            if (mp3Duration > MAX_VIDEO_LENGTH)
            {
                // Calculate segment duration, accounting for transition audio
                double segmentDuration = (mp3Duration - transitionDuration) / 2;

                // Process first part
                string outputVideo1 = Path.Combine(outputFolder, $"{FileName}_part1.mp4");
                ProcessVideoSegment(inputVideo, inputAudio, bgMusic, outputVideo1, startTime, 0, segmentDuration);

                // Process transition part
                string transitionVideo = Path.Combine(tempFolder, $"{FileName}_transition.mp4");
                ProcessTransitionSegment(inputVideo, transitionAudio, bgMusic, transitionVideo,
                    startTime + segmentDuration, transitionDuration);

                // Process second part
                string outputVideo2 = Path.Combine(outputFolder, $"{FileName}_part2.mp4");
                ProcessVideoSegment(inputVideo, inputAudio, bgMusic, outputVideo2,
                    startTime + segmentDuration + transitionDuration,
                    segmentDuration + transitionDuration,
                    segmentDuration);

                // Concatenate all parts
                string[] inputFiles = { outputVideo1, transitionVideo, outputVideo2 };
                string finalOutput = Path.Combine(outputFolder, $"{FileName}_final.mp4");
                ConcatenateVideos(inputFiles, finalOutput);

                // Cleanup temporary files
                File.Delete(transitionVideo);
                File.Delete(outputVideo1);
                File.Delete(outputVideo2);

                Console.WriteLine($"Final video created: {finalOutput}");
            }
            else
            {
                string outputVideo = Path.Combine(outputFolder, $"{FileName}.mp4");
                ProcessVideoSegment(inputVideo, inputAudio, bgMusic, outputVideo, startTime, 0, mp3Duration);
                Console.WriteLine($"Processing complete! Video saved in {outputFolder}.");
            }
        }

        private void ProcessTransitionSegment(string inputVideo, string transitionAudio, string bgMusic,
            string outputVideo, double videoStartTime, double duration)
        {
            string ffmpegCommand = $"-ss {videoStartTime} -i \"{inputVideo}\" " + // Input video
                        $"-i \"{transitionAudio}\" " + // Transition audio
                        $"-i \"{bgMusic}\" " + // Background music
                        "-filter_complex " +
                        "\"[1:a]aformat=sample_fmts=s16:sample_rates=44100:channel_layouts=stereo[a1];" +
                        "[2:a]aformat=sample_fmts=s16:sample_rates=44100:channel_layouts=stereo,volume=0.1[a2];" +
                        "[a1][a2]amix=inputs=2:duration=first[aout]\" " +
                        "-map 0:v -map \"[aout]\" " +
                        $"-t {duration} " +
                        "-c:v copy " +
                        "-c:a aac -b:a 192k " +
                        "-y " +
                        $"\"{outputVideo}\"";

            if (!RunFFmpegWithResult(ffmpegCommand))
            {
                throw new Exception($"Error during transition video processing: {outputVideo}");
            }
        }

        private void ConcatenateVideos(string[] inputFiles, string outputFile)
        {
            // Create a temporary file list
            string listPath = Path.Combine(Path.GetTempPath(), "filelist.txt");
            File.WriteAllLines(listPath, inputFiles.Select(f => $"file '{f}'"));

            string ffmpegCommand = $"-f concat -safe 0 -i \"{listPath}\" -c copy -y \"{outputFile}\"";

            if (!RunFFmpegWithResult(ffmpegCommand))
            {
                throw new Exception("Error concatenating video segments");
            }

            File.Delete(listPath);
        }

      private void ProcessVideoSegment(string inputVideo, string inputAudio, string bgMusic, string outputVideo,
        double videoStartTime, double audioStartTime, double duration)
{
    string ffmpegCommand = $"-ss {videoStartTime} -i \"{inputVideo}\" " + // Input video
                $"-ss {audioStartTime} -i \"{inputAudio}\" " + // Main audio
                $"-i \"{bgMusic}\" " + // Background music
                "-filter_complex " +
                "\"[1:a]aformat=sample_fmts=s16:sample_rates=44100:channel_layouts=stereo[a1];" +
                "[2:a]aformat=sample_fmts=s16:sample_rates=44100:channel_layouts=stereo, volume=0.05[a2];" + // Reduced from 0.1 to 0.05
                "[a1][a2]amix=inputs=2:duration=first[aout]\" " +
                "-map 0:v -map \"[aout]\" " +
                $"-t {duration} " +
                "-c:v copy " +
                "-c:a aac -b:a 192k " +
                "-y " +
                $"\"{outputVideo}\"";

    if (!RunFFmpegWithResult(ffmpegCommand))
    {
        throw new Exception($"Error during video processing for segment: {outputVideo}");
    }
}

        static double GetMediaDuration(string filePath)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = @"C:\ffmpeg\bin\ffmpeg.exe",
                    Arguments = $"-i \"{filePath}\"",
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using Process process = new Process { StartInfo = startInfo };
                process.Start();
                string output = process.StandardError.ReadToEnd();
                process.WaitForExit();

                string durationTag = "Duration: ";
                int index = output.IndexOf(durationTag);
                if (index != -1)
                {
                    string durationString = output.Substring(index + durationTag.Length, 11);
                    TimeSpan duration = TimeSpan.Parse(durationString);
                    return duration.TotalSeconds;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting duration: {ex.Message}");
            }

            return -1;
        }

        static bool RunFFmpegWithResult(string ffmpegCommand)
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = @"C:\ffmpeg\bin\ffmpeg.exe",
                Arguments = ffmpegCommand,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using Process process = new Process { StartInfo = startInfo };
            process.OutputDataReceived += (sender, args) => Console.WriteLine(args.Data);
            process.ErrorDataReceived += (sender, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data) &&
                    !args.Data.Contains("Header missing") &&
                    !args.Data.Contains("Error submitting packet"))
                {
                    Console.WriteLine(args.Data);
                }
            };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FFmpeg process error: {ex.Message}");
                return false;
            }
        }
    }
}