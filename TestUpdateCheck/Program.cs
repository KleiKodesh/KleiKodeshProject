using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace TestUpdateCheck
{
    class Program
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private const string REGISTRY_KEY = @"SOFTWARE\KleiKodesh";
        
        static async Task Main(string[] args)
        {
            try
            {
                // Set User-Agent header (required by GitHub API)
                httpClient.DefaultRequestHeaders.Add("User-Agent", "KleiKodesh-UpdateChecker");
                
                // Get current version from registry
                var currentVersion = GetCurrentVersionFromRegistry();
                Console.WriteLine($"Current version from registry: {currentVersion ?? "Not found"}");
                
                // Get latest version from GitHub
                string apiUrl = "https://api.github.com/repos/KleiKodesh/KleiKodeshProject/releases/latest";
                string response = await httpClient.GetStringAsync(apiUrl);
                var release = JsonConvert.DeserializeObject<GitHubRelease>(response);
                
                if (release != null && !string.IsNullOrEmpty(release.TagName))
                {
                    Console.WriteLine($"Latest version from GitHub: {release.TagName}");
                    
                    if (!string.IsNullOrEmpty(currentVersion))
                    {
                        var comparison = CompareVersions(release.TagName, currentVersion);
                        if (comparison > 0)
                        {
                            Console.WriteLine("✓ Update available!");
                            Console.WriteLine($"Download: {release.HtmlUrl}");
                            
                            // Show Hebrew MessageBox
                            var hebrewMessage = $"גרסה חדשה זמינה: {release.TagName}\n" +
                                              $"הגרסה הנוכחית שלך: {currentVersion}\n\n" +
                                              "האם ברצונך להוריד ולהתקין את הגרסה החדשה?";
                            
                            var result = System.Windows.Forms.MessageBox.Show(
                                hebrewMessage,
                                "עדכון זמין - כלי קודש",
                                System.Windows.Forms.MessageBoxButtons.YesNo,
                                System.Windows.Forms.MessageBoxIcon.Question,
                                System.Windows.Forms.MessageBoxDefaultButton.Button1,
                                System.Windows.Forms.MessageBoxOptions.RtlReading | System.Windows.Forms.MessageBoxOptions.RightAlign
                            );
                            
                            if (result == System.Windows.Forms.DialogResult.Yes)
                            {
                                Console.WriteLine("User clicked Yes - would download and run installer");
                                // Here you would download and run the installer
                            }
                            else
                            {
                                Console.WriteLine("User clicked No - update cancelled");
                            }
                        }
                        else if (comparison == 0)
                        {
                            Console.WriteLine("✓ You have the latest version");
                        }
                        else
                        {
                            Console.WriteLine("✓ You have a newer version than released");
                        }
                    }
                    else
                    {
                        Console.WriteLine("⚠ Cannot compare - no current version in registry");
                    }
                }
                else
                {
                    Console.WriteLine("No version found on GitHub");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        private static string GetCurrentVersionFromRegistry()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(REGISTRY_KEY))
                {
                    return key?.GetValue("Version")?.ToString();
                }
            }
            catch { return null; }
        }

        private static int CompareVersions(string githubVersion, string registryVersion)
        {
            // Normalize versions by removing 'v' prefix if present
            var normalizedGithub = githubVersion?.TrimStart('v') ?? "";
            var normalizedRegistry = registryVersion?.TrimStart('v') ?? "";

            // Try to parse as semantic versions (e.g., "1.0.31")
            if (Version.TryParse(normalizedGithub, out var githubVer) && 
                Version.TryParse(normalizedRegistry, out var registryVer))
            {
                return githubVer.CompareTo(registryVer);
            }

            // Fallback to string comparison if not valid semantic versions
            return string.Compare(normalizedGithub, normalizedRegistry, StringComparison.OrdinalIgnoreCase);
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