using System;
using System.Threading.Tasks;
using UpdateCheckerLib;

namespace TestUpdateCheck
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== KleiKodesh Update Checker Test ===");
            Console.WriteLine();

            try
            {
                var updateChecker = new UpdateChecker();

                // Test 1: Get current version from registry
                Console.WriteLine("1. Testing registry version retrieval...");
                var currentVersion = updateChecker.GetCurrentVersionFromRegistry();
                Console.WriteLine($"   Current version from registry: {currentVersion ?? "Not found"}");
                Console.WriteLine();

                // Test 2: Get latest release from GitHub
                Console.WriteLine("2. Testing GitHub API...");
                GitHubRelease latestRelease = null;
                try
                {
                    latestRelease = await updateChecker.GetLatestReleaseAsync();
                    if (latestRelease != null)
                    {
                        Console.WriteLine($"   Latest version from GitHub: {latestRelease.TagName}");
                        Console.WriteLine($"   Release name: {latestRelease.Name}");
                        Console.WriteLine($"   Published: {latestRelease.PublishedAt:yyyy-MM-dd}");
                        Console.WriteLine($"   URL: {latestRelease.HtmlUrl}");
                    }
                    else
                    {
                        Console.WriteLine("   Failed to get latest release from GitHub");
                    }
                }
                catch (InvalidOperationException ex)
                {
                    Console.WriteLine($"   GitHub API Error: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Console.WriteLine($"   Inner Error: {ex.InnerException.Message}");
                    }
                    Console.WriteLine("   → You need to create the GitHub repository or update the API URL");
                }
                Console.WriteLine();

                // Test 3: Version comparison
                if (!string.IsNullOrEmpty(currentVersion) && latestRelease?.TagName != null)
                {
                    Console.WriteLine("3. Testing version comparison...");
                    var comparison = updateChecker.CompareVersions(latestRelease.TagName, currentVersion);
                    Console.WriteLine($"   Comparison result: {comparison}");
                    if (comparison > 0)
                        Console.WriteLine("   → Update available!");
                    else if (comparison == 0)
                        Console.WriteLine("   → You have the latest version");
                    else
                        Console.WriteLine("   → You have a newer version than released");
                    Console.WriteLine();
                }

                // Test 4: Check if update is available
                Console.WriteLine("4. Testing update availability check...");
                var isUpdateAvailable = await updateChecker.IsUpdateAvailableAsync();
                Console.WriteLine($"   Update available: {isUpdateAvailable}");
                Console.WriteLine();

                // Test 5: Full update check with prompt (if update available)
                if (isUpdateAvailable)
                {
                    Console.WriteLine("5. Testing full update check with Hebrew prompt...");
                    Console.WriteLine("   (This will show Hebrew dialog if update is available)");
                    
                    // Custom close action for testing
                    await updateChecker.CheckAndPromptForUpdateAsync(() => 
                    {
                        Console.WriteLine("   Test app would close here (but we won't actually close)");
                    });
                }
                else
                {
                    Console.WriteLine("5. Skipping update prompt test (no update available)");
                }

                Console.WriteLine();
                Console.WriteLine("=== Test completed successfully! ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during testing: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }

            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}