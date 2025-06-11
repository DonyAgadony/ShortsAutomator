namespace ShortsAutomator
{
        public static class Models
        {

                public class Root
                {
                        public string? kind { get; set; }
                        public Data? data { get; set; }
                }

                public class Data
                {

                        public string? after { get; set; }
                        public int dist { get; set; }
                        public object? modhash { get; set; }

                        public object? geo_filter { get; set; }

                        public List<Child>? children { get; set; } = new List<Child>(); // Initialize to avoid null reference

                        public object? before { get; set; }
                }

                public class Child
                {
                        public string? kind { get; set; }
                        public PostData? data { get; set; }
                }

                public class PostData
                {
                        public string? title { get; set; }
                        public string? selftext { get; set; }
                        public string? id { get; set; }
                        // Add other properties as needed
                }

        }
}