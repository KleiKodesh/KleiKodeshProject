using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace KleiKodesh.Helpers
{
    /// <summary>
    /// Simple update checker - compares registry version against GitHub releases
    /// No automatic updates - just notifies user and opens download page
    /// </summary>
    public static class UpdateChecker
    {
        private const string AppName = "כלי קודש";
        private const string ReleasesPageUrl = "https://github.com/KleiKodesh/KleiKodeshProject/releases/latest";
        private const string GitHubApiUrl = "https://api.github.com/repos/KleiKodesh/KleiKodeshProject/releases/latest";
        private static readonly string AddinRegistryPath = $@"Software\Microsoft\Office\Word\Addins\{AppName}";
        
        /// <summary>
        /// Gets the installed version from registry
        /// </summary>
        private static string GetInstalledVersion()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(AddinRegistryPath))
                {
                    return key?.GetValue("Version") as string;
                }
            }
            catch
            {
                return null;
            }
        }
        
        /// <summary>
        /// Checks for updates by comparing registry version against GitHub releases
        /// Uses external PowerShell script to bypass NetFree restrictions
        /// </summary>
        public static async Task CheckForUpdatesAsync()
        {
            try {
                MessageBox.Show("DEBUG: Update check started!", "Debug", MessageBoxButtons.OK, MessageBoxIcon.Information);
                
                var currentVersion = GetInstalledVersion();
                
                Debug.WriteLine($"[UpdateChecker] Current version: {currentVersion}");
                MessageBox.Show($"DEBUG: Current version: {currentVersion ?? "NULL"}", "Debug", MessageBoxButtons.OK, MessageBoxIcon.Information);
                
                if (string.IsNullOrEmpty(currentVersion))
                {
                    Debug.WriteLine("[UpdateChecker] No version in registry - skipping update check");
                    MessageBox.Show("DEBUG: No version in registry", "Debug", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                
                // Get latest version using PowerShell script (NetFree bypass)
                var remoteVersion = await GetRemoteVersionAsync(currentVersion);
                
                MessageBox.Show($"DEBUG: Remote version: {remoteVersion ?? "NULL"}", "Debug", MessageBoxButtons.OK, MessageBoxIcon.Information);
                
                if (string.IsNullOrEmpty(remoteVersion))
                {
                    Debug.WriteLine("[UpdateChecker] Failed to get remote version");
                    return;
                }
                
                Debug.WriteLine($"[UpdateChecker] Comparing: Current={currentVersion}, Remote={remoteVersion}");
                
                if (IsNewerVersion(remoteVersion, currentVersion))
                {
                    Debug.WriteLine("[UpdateChecker] Update available! Showing notification...");
                    var result = MessageBox.Show(
                        $"נמצא עדכון חדש לכלי קודש (גרסה {remoteVersion})\nגרסה נוכחית: {currentVersion}\n\nהאם להוריד ולהתקין את העדכון?",
                        AppName,
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question,
                        MessageBoxDefaultButton.Button1,
                        MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
                        
                    if (result == DialogResult.Yes)
                    {
                        await RunUpdateScriptAsync(currentVersion);
                    }
                }
                else
                {
                    Debug.WriteLine("[UpdateChecker] No update available");
                    MessageBox.Show("DEBUG: No update available", "Debug", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateChecker] Error: {ex.Message}");
                MessageBox.Show($"DEBUG: Exception - {ex.Message}", "Debug", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        /// <summary>
        /// Gets remote version using PowerShell script to bypass NetFree
        /// </summary>
        private static async Task<string> GetRemoteVersionAsync(string currentVersion)
        {
            try
            {
                var scriptPath = GetUpdateScriptPath();
                MessageBox.Show($"DEBUG: Script path: {scriptPath}\nExists: {System.IO.File.Exists(scriptPath)}", "Debug", MessageBoxButtons.OK, MessageBoxIcon.Information);
                
                if (!System.IO.File.Exists(scriptPath))
                {
                    Debug.WriteLine("[UpdateChecker] Update script not found");
                    return null;
                }
                
                Debug.WriteLine("[UpdateChecker] Using PowerShell script for update check");
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\" -CurrentVersion \"{currentVersion}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                
                using (var process = Process.Start(startInfo))
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    var error = await process.StandardError.ReadToEndAsync();
                    process.WaitForExit();
                    
                    Debug.WriteLine($"[UpdateChecker] Script output: {output}");
                    Debug.WriteLine($"[UpdateChecker] Script exit code: {process.ExitCode}");
                    if (!string.IsNullOrEmpty(error))
                    {
                        Debug.WriteLine($"[UpdateChecker] Script error: {error}");
                    }
                    
                    MessageBox.Show($"DEBUG: PowerShell Output:\n{output}\n\nErrors:\n{error}\n\nExit Code: {process.ExitCode}", "Debug", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    
                    // Parse output to extract remote version
                    var lines = output.Split('\n');
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("Latest version:"))
                        {
                            return line.Substring("Latest version:".Length).Trim();
                        }
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateChecker] Script failed: {ex.Message}");
                MessageBox.Show($"DEBUG: PowerShell Exception: {ex.Message}", "Debug", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }
        
        /// <summary>
        /// Runs the update script to download and install updates
        /// </summary>
        private static async Task RunUpdateScriptAsync(string currentVersion)
        {
            try
            {
                var scriptPath = GetUpdateScriptPath();
                if (!System.IO.File.Exists(scriptPath))
                {
                    MessageBox.Show("Update script not found", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\" -CurrentVersion \"{currentVersion}\"",
                    UseShellExecute = true,
                    CreateNoWindow = false
                };
                
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to run update script: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        /// <summary>
        /// Gets the path to the update script
        /// </summary>
        private static string GetUpdateScriptPath()
        {
            var assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var assemblyDir = System.IO.Path.GetDirectoryName(assemblyLocation);
            return System.IO.Path.Combine(assemblyDir, "UpdateKleiKodesh.ps1");
        }
        
        private static bool IsNewerVersion(string remoteVersion, string currentVersion)
        {
            try
            {
                var remote = new Version(remoteVersion);
                var current = new Version(currentVersion);
                return remote > current;
            }
            catch
            {
                return false;
            }
        }
    }
    
    public class GitHubRelease
    {
        public string tag_name { get; set; }
        public string html_url { get; set; }
        public string name { get; set; }
        public bool prerelease { get; set; }
    }
}