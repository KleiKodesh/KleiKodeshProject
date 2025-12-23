using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace TestUpdateCheck
{
    class Program
    {
        private static readonly HttpClient httpClient = new HttpClient();
        
        static async Task Main(string[] args)
        {
            try
            {
                // Set User-Agent header (required by GitHub API)
                httpClient.DefaultRequestHeaders.Add("User-Agent", "KleiKodesh-UpdateChecker");
                
                string apiUrl = "https://api.github.com/repos/KleiKodesh/KleiKodeshProject/releases/latest";
                
                string response = await httpClient.GetStringAsync(apiUrl);
                var release = JsonConvert.DeserializeObject<GitHubRelease>(response);
                
                if (release != null && !string.IsNullOrEmpty(release.TagName))
                {
                    Console.WriteLine(release.TagName);
                }
                else
                {
                    Console.WriteLine("No version found");
                }
            }
            catch (Exception)
            {
                Console.WriteLine("Error retrieving version");
            }
        }
    }
    
    public class GitHubRelease
    {
        [JsonProperty("tag_name")]
        public string TagName { get; set; } = string.Empty;
        
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;
        
        [JsonProperty("body")]
        public string Body { get; set; } = string.Empty;
        
        [JsonProperty("published_at")]
        public DateTime PublishedAt { get; set; }
        
        [JsonProperty("prerelease")]
        public bool Prerelease { get; set; }
        
        [JsonProperty("draft")]
        public bool Draft { get; set; }
        
        [JsonProperty("html_url")]
        public string HtmlUrl { get; set; } = string.Empty;
    }
}