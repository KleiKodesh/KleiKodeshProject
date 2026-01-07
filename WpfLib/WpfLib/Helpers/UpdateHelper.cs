using Microsoft.VisualBasic;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace WpfLib.Helpers
{
    public static class UpdateHelper
    {
        public static event EventHandler<PropertyChangedEventArgs> StaticPropertyChanged;

        private static void OnStaticPropertyChanged(string propertyName)
        {
            StaticPropertyChanged?.Invoke(null, new PropertyChangedEventArgs(propertyName));
        }

        static string updateQuestion = "Updates_were_found_Would_you_like_to_download_them?";

        static int updateInterval = 0;
        static string _currentVersion = "v0";

        public static string CurrentVersion 
        {
            get => _currentVersion; 
            set 
            {
                if (value != _currentVersion) 
                {
                    _currentVersion = value;
                    OnStaticPropertyChanged(nameof(CurrentVersion)); 
                }
            }
        }

        public static async void Update(string repoOwner, string repoName, string currentVersion,
            int interval, string updateMessage = "")
        {
            if (interval >= 0)
                updateInterval = interval;

            if (!string.IsNullOrEmpty(updateMessage))
                updateQuestion = updateMessage;

            CurrentVersion = currentVersion;

            try
            {
                await Task.Run( async () =>
                {
                    await Task.Delay(5000);
                    if (IsUpdateTime())
                    {
                        var result = await CheckForUpdates(repoOwner, repoName, currentVersion);

                        if (result.IsUpdateAvailable)
                        {
                            var dialogResult = MsgBox.Question(updateQuestion, repoName);

                            if (dialogResult == MessageBoxResult.Yes)
                                Process.Start(result.UpdateUrl);
                        }
                    }
                });
            }
            catch {}
        }

        static bool IsUpdateTime()
        {
            string domainName = AppDomain.CurrentDomain.FriendlyName;
            string nextUpdateString = Interaction.GetSetting(domainName, "Updates", "NextUpdateCheck", DateTime.Now.ToString("yyyy-MM-dd"));
            DateTime nextUpdateCheck = DateTime.Parse(nextUpdateString);

            DateTime nextCheckDate = DateTime.Now.AddDays(updateInterval); // Save setting to check for updates again in set interval
            Interaction.SaveSetting(domainName, "Updates", "NextUpdateCheck", nextCheckDate.ToString("yyyy-MM-dd"));

            return DateTime.Now >= nextUpdateCheck;
        }

        public static async Task<UpdateItemModel> CheckForUpdates(string repoOwner, string repoName, string currentVersion)
        {
            try
            {
                string jsonResponse = await FetchLatestReleaseJson(repoOwner, repoName);
                return ParseAndCompareRelease(jsonResponse, currentVersion);
            }
            catch (HttpRequestException ex)
                { return CreateErrorModel($"Network error: {ex.Message}"); }
            catch (JsonException ex)
                { return CreateErrorModel($"Error parsing response: {ex.Message}"); }
            catch (Exception ex)
                { return CreateErrorModel($"Unexpected error: {ex.Message}");}
        }

        private static async Task<string> FetchLatestReleaseJson(string repoOwner, string repoName)
        {
            string url = $"https://api.github.com/repos/{repoOwner}/{repoName}/releases/latest";
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("MyApp");
                HttpResponseMessage response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"Unable to fetch release info. Status Code: {response.StatusCode}");

                return await response.Content.ReadAsStringAsync();
            }
        }

        private static UpdateItemModel ParseAndCompareRelease(string jsonResponse, string currentVersion)
        {
            using (JsonDocument jsonDocument = JsonDocument.Parse(jsonResponse))
            {
                JsonElement root = jsonDocument.RootElement;

                string latestVersion = root.GetProperty("tag_name").GetString();
                string downloadUrl = root.GetProperty("html_url").GetString();

                if (string.Compare(latestVersion, currentVersion, StringComparison.OrdinalIgnoreCase) > 0)
                {
                    return new UpdateItemModel
                    {
                        Message = $"New version available: {latestVersion}. Download it here: {downloadUrl}",
                        IsUpdateAvailable = true,
                        UpdateUrl = downloadUrl,
                        NewVersionId = latestVersion
                    };
                }

                return new UpdateItemModel
                {
                    Message = "You are using the latest version."
                };
            }             
        }

        private static UpdateItemModel CreateErrorModel(string message)
        {
            return new UpdateItemModel
            {
                Message = message,
                Error = true
            };
        }
    }

    public class UpdateItemModel
    {
        public bool IsUpdateAvailable { get; set; }
        public bool Error { get; set; }
        public string Message { get; set; }
        public string UpdateUrl {get; set;}
        public string NewVersionId { get; set; }
    }
}
