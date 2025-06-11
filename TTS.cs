using RestSharp;
using Newtonsoft.Json;
using ShortsAutomator;
using NAudio.Wave;
using NAudio.Lame;

public static class TextToSpeechService
{
    public static async Task TurnTextToSpeech(string PathToFile)
    {
        try
        {
            string text = File.ReadAllText(PathToFile).Trim();
            if (string.IsNullOrWhiteSpace(text))
                throw new Exception("Text content is empty");

            string APIKey = File.ReadLines(@"Secrets.txt")
                .Skip(8).First().Trim();
            if (string.IsNullOrWhiteSpace(APIKey))
                throw new Exception("API key is empty or not found");

            // Determine if the text is from a man or woman using Gemini
            bool? isMan = await GeminiService.IsMan(PathToFile);
            string voiceId;

            if (isMan.HasValue)
            {
                voiceId = isMan.Value ? "Daniel" : "Melody";
                Console.WriteLine($"Gemini determined gender: {(isMan.Value ? "Male" : "Female")}, using voice: {voiceId}");
            }
            else
            {
                voiceId = "Daniel"; // Default to Daniel if gender detection fails
                Console.WriteLine("Gender detection was inconclusive, defaulting to Daniel voice");
            }

            string[] Sentences = TurnTextIntoSentences(text);
            List<byte[]> audioChunks = new List<byte[]>();

            Console.WriteLine($"Processing {Sentences.Length} sentences with voice {voiceId}");

            for (int i = 0; i < Sentences.Length; i++)
            {
                var client2 = new RestClient(new RestClientOptions("https://api.v8.unrealspeech.com/stream")
                {
                    Timeout = TimeSpan.FromSeconds(30)
                });

                var request2 = new RestRequest("", Method.Post);
                request2.AddHeader("Content-Type", "application/json");
                request2.AddHeader("accept", "text/plain");
                request2.AddHeader("Authorization", $"Bearer {APIKey}");

                var requestBody2 = new
                {
                    Text = Sentences[i],
                    VoiceId = voiceId,
                    Bitrate = "192k",
                    Speed = "0.4",
                    Pitch = "1",
                    TimestampType = "sentence"
                };

                string requestBodyJson2 = JsonConvert.SerializeObject(requestBody2, Formatting.Indented);
                Console.WriteLine($"Processing sentence {i + 1}/{Sentences.Length}");

                request2.AddParameter("application/json", requestBodyJson2, ParameterType.RequestBody);

                var response2 = await client2.ExecuteAsync(request2);

                if (response2.IsSuccessful && response2.RawBytes != null && response2.RawBytes.Length > 0)
                {
                    audioChunks.Add(response2.RawBytes);
                    Console.WriteLine($"Successfully processed sentence {i + 1}, received {response2.RawBytes.Length} bytes");
                }
                else
                {
                    Console.WriteLine($"Error processing sentence {i + 1}: {response2.StatusCode}, {response2.Content}");
                }
            }

            if (audioChunks.Count == 0)
            {
                throw new Exception("No audio was generated from the API");
            }

            // Combine all audio chunks
            byte[] combinedAudio = CombineAudioChunks(audioChunks);
            Console.WriteLine($"Combined {audioChunks.Count} audio chunks into {combinedAudio.Length} bytes");

            // Split files if needed and save
            await SplitAndSaveAudio(PathToFile, combinedAudio, voiceId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in TurnTextToSpeech: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }

    private static byte[] CombineAudioChunks(List<byte[]> audioChunks)
    {
        int totalLength = audioChunks.Sum(chunk => chunk.Length);
        byte[] combinedAudio = new byte[totalLength];
        int offset = 0;

        foreach (byte[] chunk in audioChunks)
        {
            Buffer.BlockCopy(chunk, 0, combinedAudio, offset, chunk.Length);
            offset += chunk.Length;
        }

        return combinedAudio;
    }

    private static async Task SplitAndSaveAudio(string originalFilePath, byte[] audioData, string voiceId)
    {
        string baseFileName = Path.GetFileNameWithoutExtension(originalFilePath);
        string outputDir = Path.Combine(@"C:\Users\donyb\Desktop\HandasatTochna\ShortsAutomator\MP3");

        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        // Create a temporary file to analyze the duration
        string tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mp3");
        File.WriteAllBytes(tempFile, audioData);

        try
        {
            double totalDuration = GetAudioDurationInMinutes(tempFile);
            Console.WriteLine($"Total audio duration: {totalDuration:F2} minutes");

            // Only split if longer than 3 minutes
            List<byte[]> splitAudioFiles;
            if (totalDuration > 3.0)
            {
                Console.WriteLine("Audio exceeds 3 minutes, will split into parts");
                // Split the files recursively
                splitAudioFiles = await SplitAudioRecursively(tempFile, audioData, voiceId);
            }
            else
            {
                Console.WriteLine("Audio is under 3 minutes, no splitting required");
                splitAudioFiles = new List<byte[]> { audioData };
            }

            // Only add part numbers if we have more than one part
            bool needPartNumbers = splitAudioFiles.Count > 1;

            // Save all split files
            for (int i = 0; i < splitAudioFiles.Count; i++)
            {
                string outputFileName;

                if (needPartNumbers)
                {
                    outputFileName = $"{baseFileName}_part{i + 1}.mp3";
                }
                else
                {
                    outputFileName = $"{baseFileName}.mp3";
                }

                string outputPath = Path.Combine(outputDir, outputFileName);
                File.WriteAllBytes(outputPath, splitAudioFiles[i]);
                Console.WriteLine($"Created {(needPartNumbers ? "split " : "")}file: {outputPath}");
            }
        }
        finally
        {
            // Clean up temp file
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    // Fixed recursive function that was incorrectly implemented
    private static async Task Temp(int i)
    {
        await Task.CompletedTask; // Proper async placeholder
    }

    private static async Task<List<byte[]>> SplitAudioRecursively(string tempFile, byte[] audioData, string voiceId)
    {
        await Temp(0); // Just to keep the async signature
        List<byte[]> result = new List<byte[]>();
        Queue<byte[]> chunksToProcess = new Queue<byte[]>();
        chunksToProcess.Enqueue(audioData);

        Console.WriteLine("Starting recursive audio splitting");

        while (chunksToProcess.Count > 0)
        {
            byte[] currentChunk = chunksToProcess.Dequeue();

            // Write current chunk to temp file for analysis
            File.WriteAllBytes(tempFile, currentChunk);

            // Check duration
            double durationInMinutes = GetAudioDurationInMinutes(tempFile);
            Console.WriteLine($"Chunk duration: {durationInMinutes:F2} minutes");

            if (durationInMinutes > 3.0)
            {
                Console.WriteLine($"Splitting chunk of {durationInMinutes:F2} minutes (exceeds 3-minute limit)");
                // Split in half
                var (firstHalf, secondHalf) = SplitAudioInHalf(currentChunk);
                chunksToProcess.Enqueue(firstHalf);
                chunksToProcess.Enqueue(secondHalf);
            }
            else
            {
                Console.WriteLine($"Keeping chunk of {durationInMinutes:F2} minutes (under 3-minute limit)");
                result.Add(currentChunk);
            }
        }

        Console.WriteLine($"Finished splitting into {result.Count} chunks");

        // If we have more than one part, add "for the next part" audio to all parts except the last one
        if (result.Count > 1)
        {
            Console.WriteLine($"Adding transition audio to {result.Count - 1} chunks");
            List<byte[]> finalResult = new List<byte[]>();

            // Get the appropriate transition audio file based on the voice
            string nextPartAudioPath = voiceId == "Daniel"
                ? @"C:\Users\donyb\Desktop\HandasatTochna\ShortsAutomator\For the next part Audio\Daniel.mp3"
                : @"C:\Users\donyb\Desktop\HandasatTochna\ShortsAutomator\For the next part Audio\Melody.mp3";

            // Check if the transition audio file exists
            if (!File.Exists(nextPartAudioPath))
            {
                Console.WriteLine($"Warning: Transition audio file not found at: {nextPartAudioPath}");
                Console.WriteLine("Creating the directory if it doesn't exist");

                // Create the directory if it doesn't exist
                string directory = Path.GetDirectoryName(nextPartAudioPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    Console.WriteLine($"Created directory: {directory}");
                }

                // Since the file doesn't exist, we'll continue without adding transition audio
                return result;
            }

            byte[] nextPartAudio = File.ReadAllBytes(nextPartAudioPath);
            Console.WriteLine($"Loaded transition audio: {nextPartAudio.Length} bytes from {nextPartAudioPath}");

            for (int i = 0; i < result.Count; i++)
            {
                if (i < result.Count - 1)
                {
                    // Add "for the next part" audio
                    byte[] combinedWithNextPart = new byte[result[i].Length + nextPartAudio.Length];
                    Buffer.BlockCopy(result[i], 0, combinedWithNextPart, 0, result[i].Length);
                    Buffer.BlockCopy(nextPartAudio, 0, combinedWithNextPart, result[i].Length, nextPartAudio.Length);

                    finalResult.Add(combinedWithNextPart);
                    Console.WriteLine($"Added transition audio to part {i + 1}");
                }
                else
                {
                    // Last part, no need to add "for the next part" audio
                    finalResult.Add(result[i]);
                    Console.WriteLine($"No transition audio added to final part {i + 1}");
                }
            }

            return finalResult;
        }
        else
        {
            // Only one part, no need to add "for the next part" audio
            Console.WriteLine("Only one part, no transition audio needed");
            return result;
        }
    }

    private static double GetAudioDurationInMinutes(string filePath)
    {
        using (var reader = new Mp3FileReader(filePath))
        {
            return reader.TotalTime.TotalMinutes;
        }
    }

    private static (byte[] firstHalf, byte[] secondHalf) SplitAudioInHalf(byte[] audioData)
    {
        string tempInputFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mp3");
        string tempWavFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.wav");
        string tempFirstHalfWav = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_first.wav");
        string tempSecondHalfWav = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_second.wav");
        string tempFirstHalfMp3 = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_first.mp3");
        string tempSecondHalfMp3 = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_second.mp3");

        try
        {
            Console.WriteLine("Starting to split audio file in half");

            // Write the audio data to a temporary file
            File.WriteAllBytes(tempInputFile, audioData);

            // First convert MP3 to WAV for easier processing
            using (var reader = new Mp3FileReader(tempInputFile))
            using (var writer = new WaveFileWriter(tempWavFile, reader.WaveFormat))
            {
                byte[] buffer = new byte[4096];
                int bytesRead;
                while ((bytesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    writer.Write(buffer, 0, bytesRead);
                }
            }

            Console.WriteLine("Converted MP3 to WAV for processing");

            // Now split the WAV file at the halfway point
            using (var reader = new WaveFileReader(tempWavFile))
            {
                int sampleCount = (int)reader.Length;
                int halfSampleCount = sampleCount / 2;

                // Create first half WAV
                using (var writer = new WaveFileWriter(tempFirstHalfWav, reader.WaveFormat))
                {
                    byte[] buffer = new byte[4096];
                    int bytesRead;
                    int totalBytesRead = 0;

                    while ((bytesRead = reader.Read(buffer, 0, Math.Min(buffer.Length, halfSampleCount - totalBytesRead))) > 0)
                    {
                        writer.Write(buffer, 0, bytesRead);
                        totalBytesRead += bytesRead;

                        if (totalBytesRead >= halfSampleCount)
                            break;
                    }
                }

                Console.WriteLine($"Created first half WAV ({halfSampleCount} bytes)");

                // Create second half WAV
                using (var writer = new WaveFileWriter(tempSecondHalfWav, reader.WaveFormat))
                {
                    byte[] buffer = new byte[4096];
                    int bytesRead;

                    while ((bytesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        writer.Write(buffer, 0, bytesRead);
                    }
                }

                Console.WriteLine("Created second half WAV");
            }

            // Convert WAVs back to MP3 using LAME
            ConvertWavToMp3(tempFirstHalfWav, tempFirstHalfMp3);
            ConvertWavToMp3(tempSecondHalfWav, tempSecondHalfMp3);

            Console.WriteLine("Converted split WAVs back to MP3 format");

            // Read the converted MP3 files
            byte[] firstHalfData = File.ReadAllBytes(tempFirstHalfMp3);
            byte[] secondHalfData = File.ReadAllBytes(tempSecondHalfMp3);

            Console.WriteLine($"Split complete: First half: {firstHalfData.Length} bytes, Second half: {secondHalfData.Length} bytes");

            return (firstHalfData, secondHalfData);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error splitting audio: {ex.Message}");
            throw;
        }
        finally
        {
            // Clean up temporary files
            if (File.Exists(tempInputFile)) File.Delete(tempInputFile);
            if (File.Exists(tempWavFile)) File.Delete(tempWavFile);
            if (File.Exists(tempFirstHalfWav)) File.Delete(tempFirstHalfWav);
            if (File.Exists(tempSecondHalfWav)) File.Delete(tempSecondHalfWav);
            if (File.Exists(tempFirstHalfMp3)) File.Delete(tempFirstHalfMp3);
            if (File.Exists(tempSecondHalfMp3)) File.Delete(tempSecondHalfMp3);

            Console.WriteLine("Cleaned up temporary files");
        }
    }

    // Fixed WAV to MP3 conversion method
    private static void ConvertWavToMp3(string wavFilePath, string mp3FilePath)
    {
        try
        {
            using (var reader = new WaveFileReader(wavFilePath))
            using (var writer = new LameMP3FileWriter(mp3FilePath, reader.WaveFormat, 128))
            {
                reader.CopyTo(writer);
            }

            // Verify the MP3 file was created
            if (!File.Exists(mp3FilePath) || new FileInfo(mp3FilePath).Length == 0)
            {
                throw new Exception("MP3 conversion failed");
            }

            // No longer copying WAV file over MP3
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error converting WAV to MP3: {ex.Message}");
            throw;
        }
    }

    public static string[] TurnTextIntoSentences(string text)
    {
        // Improved sentence splitting to handle punctuation properly
        string[] sentences = text.Split(new[] { ". ", "? ", "! ", ".\n", "?\n", "!\n" },
            StringSplitOptions.RemoveEmptyEntries);

        // Make sure each sentence ends with appropriate punctuation
        for (int i = 0; i < sentences.Length; i++)
        {
            string sentence = sentences[i].Trim();
            if (!sentence.EndsWith(".") && !sentence.EndsWith("?") && !sentence.EndsWith("!"))
            {
                sentences[i] = sentence + ".";
            }
            else
            {
                sentences[i] = sentence;
            }
        }

        return sentences.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
    }

    // Original SaveMP3File method kept for reference but not used in the new workflow
    public static void SaveMP3File(string fileName, byte[] newContent)
    {
        string destinationFolder = @"MP3";
        string destinationFilePath = Path.Combine(@"C:\Users\donyb\Desktop\HandasatTochna\ShortsAutomator\MP3", $"{Path.GetFileNameWithoutExtension(fileName)}.mp3");

        try
        {
            if (!Directory.Exists(destinationFolder))
            {
                Directory.CreateDirectory(destinationFolder);
            }

            if (File.Exists(destinationFilePath))
            {
                byte[] existingContent = File.ReadAllBytes(destinationFilePath);
                byte[] combinedContent = new byte[existingContent.Length + newContent.Length];
                Buffer.BlockCopy(existingContent, 0, combinedContent, 0, existingContent.Length);
                Buffer.BlockCopy(newContent, 0, combinedContent, existingContent.Length, newContent.Length);
                File.WriteAllBytes(destinationFilePath, combinedContent);
                Console.WriteLine($"Successfully appended content to existing file: {destinationFilePath}");
            }
            else
            {
                File.WriteAllBytes(destinationFilePath, newContent);
                Console.WriteLine($"Created new file at: {destinationFilePath}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving/appending MP3 file: {ex.Message}");
        }
    }
}