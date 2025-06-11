using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ShortsAutomator
{
    public class GeminiService
    {
        string filePath;
        public GeminiService(string filePath)
        {
            this.filePath = filePath;
        }
        public static async Task<bool?> IsMan(string filePathW)
        {
            Console.WriteLine("Tries to approach Gemini");
            try
            {
                string GeminiApi = File.ReadAllLines("secrets.txt").First();
                string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={GeminiApi}";
                string prompt = File.ReadAllText(filePathW);

                prompt = JsonEncodedText.Encode(prompt).ToString();

                string jsonData = @"{
            ""contents"": [{
                ""parts"": [{
                    ""text"": ""Is this text written by a man or a woman? reply in 'W' for woman and 'M'for man. if you cant tell reply with 'N'. reply only with one letter from the options above. The text: " + prompt + @"""
                }]
            }]
        }";

                using HttpClient client = new HttpClient();
                var content = new StringContent(jsonData, Encoding.UTF8, "application/json");
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage response = await client.PostAsync(url, content);
                string responseString = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Response from Gemini: {responseString}");

                if (response.IsSuccessStatusCode)
                {
                    using JsonDocument document = JsonDocument.Parse(responseString);
                    var text = document.RootElement
                        .GetProperty("candidates")[0]
                        .GetProperty("content")
                        .GetProperty("parts")[0]
                        .GetProperty("text")
                        .GetString()
                        ?.Trim();

                    if (string.IsNullOrEmpty(text))
                    {
                        Console.WriteLine("Received empty response text");
                        return null;
                    }

                    Console.WriteLine($"Extracted response: '{text}'");

                    // New logic to handle all three cases
                    switch (text.ToUpper())
                    {
                        case "M":
                            return true;
                        case "W":
                            return false;
                        case "N":
                            return null;
                        default:
                            Console.WriteLine($"Unexpected response: {text}");
                            return null;
                    }
                }
                else
                {
                    Console.WriteLine($"Error: {response.StatusCode}");
                    Console.WriteLine($"Response content: {responseString}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in IsMan: {ex.Message}");
                return null;
            }
        }
        public async Task<string?> GenerateDescription()
        {
            {
                string GeminiApi = File.ReadAllLines("secrets.txt").First();
                string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={GeminiApi}";
                string prompt = File.ReadAllText(filePath);

                prompt = JsonEncodedText.Encode(prompt).ToString();

                string jsonData = @"{
            ""contents"": [{
                ""parts"": [{
                    ""text"": ""Create a description for this video script:" + prompt + @"""
                }]
            }]
        }";

                using HttpClient client = new HttpClient();
                var content = new StringContent(jsonData, Encoding.UTF8, "application/json");
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage response = await client.PostAsync(url, content);
                string responseString = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Response from Gemini: {responseString}");

                if (response.IsSuccessStatusCode)
                {
                    using JsonDocument document = JsonDocument.Parse(responseString);
                    var text = document.RootElement
                        .GetProperty("candidates")[0]
                        .GetProperty("content")
                        .GetProperty("parts")[0]
                        .GetProperty("text")
                        .GetString()
                        ?.Trim();

                    if (string.IsNullOrEmpty(text))
                    {
                        Console.WriteLine("Received empty response text");
                        return null;
                    }

                    Console.WriteLine($"Extracted response: '{text}'");

                    return text;
                }
                return "";
            }
        }
        public static async Task<string?> FormatSentence(string sentence)
        {
            string GeminiApi = File.ReadAllLines("secrets.txt").First();
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={GeminiApi}";

            sentence = JsonEncodedText.Encode(sentence).ToString();

            string jsonData = @"{
            ""contents"": [{
                ""parts"": [{
                    ""text"": ""remove any not family friendly words or any other words that tiktok doesnt allow and replace them with another substitute. for example: Died, Unalived. and for words like: sex,(or any other words you cant find a good substitute) turn them to s*x. return only the fixed sentence. The sentence:" + sentence + @"""
                }]
            }]
        }";

            using HttpClient client = new HttpClient();
            var content = new StringContent(jsonData, Encoding.UTF8, "application/json");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            HttpResponseMessage response = await client.PostAsync(url, content);
            string responseString = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Response from Gemini: {responseString}");

            if (response.IsSuccessStatusCode)
            {
                using JsonDocument document = JsonDocument.Parse(responseString);
                var text = document.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString()
                    ?.Trim();

                if (string.IsNullOrEmpty(text))
                {
                    Console.WriteLine("Received empty response text");
                    return null;
                }

                Console.WriteLine($"Extracted response: '{text}'");

                return text;
            }
            else
            {
                Thread.Sleep(120000);
                await FormatSentence(sentence);
            }
            return "";
        }
    }
}
