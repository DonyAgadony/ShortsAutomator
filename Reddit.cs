using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Serilog;
using static ShortsAutomator.Models;

namespace ShortsAutomator
{
    public static class RedditService
    {
        // Add a HashSet to track processed post IDs
        private static HashSet<string> processedPostIds = new HashSet<string>();

        public static string SanitizeName(string title)
        {
            char[] chars = ['.', '/', '?', '!', '"'];
            for (int i = 0; i < title.Length; i++)
            {
                if (chars.Contains(title[i]))
                {
                    title = title.Replace(title[i], ' ');
                }
            }
            return title;
        }

        private static async Task<string> FormatText(string text)
        {
            // Split text into sentences using basic punctuation
            string[] sentences = text.Split(new[] { ". ", "! ", "? " }, StringSplitOptions.RemoveEmptyEntries);
            List<string> formattedSentences = new List<string>();

            foreach (var sentence in sentences)
            {
                string? formattedSentence = await GeminiService.FormatSentence(sentence.Trim());
                if (!string.IsNullOrEmpty(formattedSentence))
                {
                    formattedSentences.Add(formattedSentence);
                }
            }

            // Rejoin sentences with proper punctuation
            return string.Join(". ", formattedSentences) + ".";
        }

        public static async Task ProcessRedditPosts()
        {
            // Clear the processed posts tracking for a new run
            processedPostIds.Clear();

            string[] subreddit = ["AmItheAsshole", "AITAH", "TwoHotTakes", "sillyconfession", "confessions", "tifu", "cheating_stories", "adultery"];
            string postsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "posts");

            if (!Directory.Exists(postsDirectory))
            {
                Directory.CreateDirectory(postsDirectory);
            }

            try
            {
                // Get an access token once and reuse it for all requests
                string? accessToken = await GetAccessToken();
                if (accessToken == null)
                {
                    Log.Error("Failed to get access token. Stopping Reddit post processing.");
                    return;
                }

                for (int i = 0; i < subreddit.Length; i++)
                {
                    Log.Information($"Processing subreddit: {subreddit[i]}");

                    // Get top posts
                    var topPostsJson = await GetRedditData(subreddit[i], "top", accessToken);
                    if (topPostsJson != null)
                    {
                        Log.Information($"Successfully retrieved top posts for r/{subreddit[i]}");
                        await FormatAndSaveFile(topPostsJson, postsDirectory);
                    }

                    // Get hot posts
                    var hotPostsJson = await GetRedditData(subreddit[i], "hot", accessToken);
                    if (hotPostsJson != null)
                    {
                        Log.Information($"Successfully retrieved hot posts for r/{subreddit[i]}");
                        await FormatAndSaveFile(hotPostsJson, postsDirectory);
                    }
                }

                Log.Information($"Total unique posts processed: {processedPostIds.Count}");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error processing subreddits");
            }
        }

        // New method to process a specific Reddit post by ID
        public static async Task ProcessSpecificRedditPost(string postId)
        {
            // Clear the processed posts tracking for a new run
            processedPostIds.Clear();

            string postsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "posts");

            if (!Directory.Exists(postsDirectory))
            {
                Directory.CreateDirectory(postsDirectory);
            }

            try
            {
                // Get an access token
                string? accessToken = await GetAccessToken();
                if (accessToken == null)
                {
                    Log.Error("Failed to get access token. Stopping Reddit post processing.");
                    return;
                }

                // Get the specific post by ID
                var postJson = await GetSpecificRedditPost(postId, accessToken);
                if (postJson != null)
                {
                    Log.Information($"Successfully retrieved post with ID: {postId}");
                    await FormatAndSaveFile(postJson, postsDirectory);
                    Log.Information($"Successfully processed post with ID: {postId}");
                }
                else
                {
                    Log.Error($"Failed to retrieve post with ID: {postId}");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Error processing specific Reddit post with ID: {postId}");
            }
        }

        private static async Task<string?> GetAccessToken()
        {
            using HttpClient client = new();
            try
            {
                client.DefaultRequestHeaders.Clear();
                var settings = ConfigurationService.Settings!.RedditSettings;
                var authString = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings!.ClientId}:{settings.ClientSecret}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authString);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("RedditBot/1.0");
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var tokenRequestContent = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "password"),
                    new KeyValuePair<string, string>("username", settings.Username!),
                    new KeyValuePair<string, string>("password", settings.Password!),
                });

                Log.Information("Attempting to get Reddit access token...");

                var tokenResponse = await client.PostAsync("https://www.reddit.com/api/v1/access_token", tokenRequestContent);
                var tokenJson = await tokenResponse.Content.ReadAsStringAsync();

                if (!tokenResponse.IsSuccessStatusCode)
                {
                    Log.Error($"Failed to get access token. Status: {tokenResponse.StatusCode}");
                    Log.Error($"Response: {tokenJson}");
                    return null;
                }

                var tokenData = JsonSerializer.Deserialize<JsonElement>(tokenJson);
                if (!tokenData.TryGetProperty("access_token", out var accessTokenElement))
                {
                    Log.Error("Access token not found in response");
                    Log.Error($"Full response: {tokenJson}");
                    return null;
                }

                string accessToken = accessTokenElement.GetString()!;
                Log.Information("Successfully obtained access token");

                return accessToken;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error getting Reddit access token");
                return null;
            }
        }

        // New method to get a specific Reddit post by ID
        private static async Task<string?> GetSpecificRedditPost(string postId, string accessToken)
        {
            using HttpClient client = new();
            try
            {
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("RedditBot/1.0");

                // Format for getting a specific post by ID
                // Note: "t3_" prefix is for posts
                var response = await client.GetAsync($"https://oauth.reddit.com/api/info?id=t3_{postId}");
                var jsonResponse = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Log.Error($"Failed to fetch post with ID: {postId}. Status: {response.StatusCode}");
                    Log.Error($"Error content: {jsonResponse}");
                    return null;
                }

                Log.Information($"Successfully fetched post with ID: {postId}");
                return jsonResponse;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Error fetching post with ID: {postId}");
                return null;
            }
        }

        private static async Task<string?> GetRedditData(string subreddit, string sortType, string accessToken)
        {
            using HttpClient client = new();
            try
            {
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("RedditBot/1.0");

                var response = await client.GetAsync($"https://oauth.reddit.com/r/{subreddit}/{sortType}?limit=10&t=day");
                var jsonResponse = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Log.Error($"Failed to fetch {sortType} posts for r/{subreddit}. Status: {response.StatusCode}");
                    Log.Error($"Error content: {jsonResponse}");
                    return null;
                }

                Log.Information($"Successfully fetched {sortType} posts for r/{subreddit}");
                return jsonResponse;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Error fetching {sortType} data from subreddit {subreddit}");
                return null;
            }
        }

        private static async Task FormatAndSaveFile(string postsJson, string postsDirectory)
        {
            if (postsJson != null)
            {
                var root = JsonSerializer.Deserialize<Root>(postsJson);
                if (root?.data?.children != null)
                {
                    foreach (var child in root.data.children)
                    {
                        // Skip if no selftext or if length is outside our bounds
                        if (string.IsNullOrEmpty(child.data?.selftext) ||
                            child.data.selftext.Length < 1000 ||
                            child.data.selftext.Length > 30000)
                        {
                            continue;
                        }

                        // Skip if we don't have a title
                        if (string.IsNullOrEmpty(child.data.title))
                        {
                            continue;
                        }

                        // Get post ID for duplicate checking
                        string postId = child.data.id ?? Guid.NewGuid().ToString();

                        // Check if we already processed this post ID
                        if (processedPostIds.Contains(postId))
                        {
                            Log.Information($"Skipping duplicate post (ID: {postId}): {child.data.title}");
                            continue;
                        }

                        // Add to our tracking HashSet
                        processedPostIds.Add(postId);

                        // Format the title and content
                        string? formattedTitle = await GeminiService.FormatSentence(child.data.title);
                        string formattedContent = await FormatText(child.data.selftext);

                        if (string.IsNullOrEmpty(formattedTitle) || string.IsNullOrEmpty(formattedContent))
                        {
                            Log.Warning($"Skipping post due to formatting failure: {child.data.title}");
                            continue;
                        }

                        string sanitizedTitle = SanitizeName(formattedTitle);
                        string filename = $"{sanitizedTitle}_{postId}.txt";
                        string filePath = Path.Combine(postsDirectory, filename);

                        // Save the formatted content to the file
                        await File.WriteAllTextAsync(filePath, formattedTitle + "\n" + formattedContent);
                        Log.Information($"Saved formatted post: {filename}");
                    }
                }
            }
        }
    }
}