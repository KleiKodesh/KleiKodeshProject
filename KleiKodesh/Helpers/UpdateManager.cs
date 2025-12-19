using KleiKodesh;
using KleiKodesh.Helpers;
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
    public static class UpdateManager
    {
        public static event EventHandler<PropertyChangedEventArgs> StaticPropertyChanged;

        private static void OnStaticPropertyChanged(string propertyName)
        {
            StaticPropertyChanged?.Invoke(null, new PropertyChangedEventArgs(propertyName));
        }

        static string updateQuestion = "Updates_were_found_Would_you_like_to_download_them?";
        static int updateInterval = 0;
        const string Version = "v3.0.0";

        public static async void CheckForUpdates(
            string repoOwner,
            string repoName,
            string updateMessage,
            int updateInterval = 0)
        {
            if (!string.IsNullOrEmpty(updateMessage))
                updateQuestion = updateMessage;

            try
            {
                await Task.Run(async () =>
                {
                    await Task.Delay(5000);
                    if (IsUpdateTime(updateInterval))
                    {
                        var result = await CheckForUpdates(repoOwner, repoName, Version);

                        if (result.IsUpdateAvailable && MsgBox.Question(updateQuestion, repoName))
                                Process.Start(result.UpdateUrl);
                    }
                });
            }
            catch { }
        }

        static bool IsUpdateTime(int updateInterval)
        {
            const string Section = "Updates";
            const string Key = "NextUpdateCheck";

            var nextCheck = DateTime.Parse(
                SettingsManager.Get(Section, Key, DateTime.Today.ToString("yyyy-MM-dd")));

            SettingsManager.Save(
                Section,
                Key,
                DateTime.Today.AddDays(updateInterval).ToString("yyyy-MM-dd"));

            return DateTime.Now >= nextCheck;
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
            { return CreateErrorModel($"Unexpected error: {ex.Message}"); }
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
        public string UpdateUrl { get; set; }
        public string NewVersionId { get; set; }
    }
}
