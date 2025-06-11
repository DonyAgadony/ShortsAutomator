namespace ShortsAutomator
{
        public class AppSettings
        {
                public string? FFmpegPath { get; set; }
                public string? FFprobePath { get; set; }
                public string? PostsDirectory { get; set; }
                public string? MP3Directory { get; set; }
                public string? BackgroundMusicFolder { get; set; }
                public RedditSettings? RedditSettings { get; set; }
        }

        public class RedditSettings
        {
                public string? ClientId { get; set; }
                public string? ClientSecret { get; set; }
                public string? Username { get; set; }
                public string? Password { get; set; }
        }
}