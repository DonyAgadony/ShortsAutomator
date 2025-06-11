using System.Diagnostics;
using Serilog;

namespace ShortsAutomator
{
    public static class FFmpegService
    {
        public static async Task RunFFmpegCommand(string arguments)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ConfigurationService.Settings?.FFmpegPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            try
            {
                process.Start();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    var error = await process.StandardError.ReadToEndAsync();
                    throw new Exception($"FFmpeg command failed with exit code: {process.ExitCode}. Error: {error}");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error running FFmpeg command");
                throw;
            }
        }

        public static async Task<TimeSpan> GetAudioDuration(string audioPath)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ConfigurationService.Settings?.FFprobePath,
                    Arguments = $"-v quiet -show_entries format=duration -of default=noprint_wrappers=1:nokey=1:print_section=0 \"{audioPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            try
            {
                process.Start();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    var error = await process.StandardError.ReadToEndAsync();
                    Log.Error($"ffprobe exited with error code {process.ExitCode}: {error}");
                    return TimeSpan.Zero;
                }

                var result = await process.StandardOutput.ReadToEndAsync();
                return double.TryParse(result, out var duration) ? TimeSpan.FromSeconds(duration) : TimeSpan.Zero;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Error getting audio duration for {audioPath}");
                return TimeSpan.Zero;
            }
        }
    }
}