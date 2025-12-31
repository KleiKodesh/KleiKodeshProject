using Newtonsoft.Json;
using Microsoft.Win32;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace KleiKodesh.Helpers
{
    public class GitHubUpdateChecker
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private const string REGISTRY_KEY = @"SOFTWARE\KleiKodesh";
        
        static GitHubUpdateChecker() => httpClient.DefaultRequestHeaders.Add("User-Agent", "KleiKodesh-UpdateChecker");

        public async Task<bool> IsUpdateAvailableAsync()
        {
            try
            {
                var currentVersion = GetCurrentVersionFromRegistry();
                if (string.IsNullOrEmpty(currentVersion)) return false;

                var response = await httpClient.GetStringAsync("https://api.github.com/repos/KleiKodesh/KleiKodeshProject/releases/latest");
                var release = JsonConvert.DeserializeObject<GitHubRelease>(response);
                
                return release?.TagName != null && !string.Equals(release.TagName, currentVersion, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private string GetCurrentVersionFromRegistry()
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
    }

    public class GitHubRelease
    {
        [JsonProperty("tag_name")] public string TagName { get; set; } = string.Empty;
    }
}
