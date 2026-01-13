using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TestSilentCleanup
{
    /// <summary>
    /// Test program to verify silent mode self-deleting functionality
    /// Simulates the UpdateChecker cleanup behavior
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Testing Silent Mode Self-Deleting Cleanup ===\n");

            // Create a fake installer file to test with
            var fakeInstallerPath = CreateFakeInstaller();
            Console.WriteLine($"Created fake installer: {fakeInstallerPath}");

            // Test the cleanup script creation and execution
            await TestCleanupScript(fakeInstallerPath);

            Console.WriteLine("\nTest completed. Press any key to exit...");
            Console.ReadKey();
        }

        static string CreateFakeInstaller()
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"FakeKleiKodeshSetup-v1.0.99.exe");
            
            // Create a fake installer that just waits and exits
            var fakeInstallerContent = @"@echo off
echo Fake installer running in silent mode...
echo Simulating installation process...
timeout /t 10 /nobreak >NUL
echo Installation complete!
";
            
            // Create as .bat file for testing (easier than creating actual .exe)
            var batPath = Path.ChangeExtension(tempPath, ".bat");
            File.WriteAllText(batPath, fakeInstallerContent);
            
            return batPath;
        }

        static async Task TestCleanupScript(string installerPath)
        {
            Console.WriteLine("\n--- Testing Cleanup Script ---");
            
            // Create cleanup script (copied from UpdateChecker logic)
            var cleanupScript = CreateCleanupScript(installerPath);
            Console.WriteLine($"Created cleanup script: {cleanupScript}");

            // Verify files exist before test
            Console.WriteLine($"Installer exists: {File.Exists(installerPath)}");
            Console.WriteLine($"Cleanup script exists: {File.Exists(cleanupScript)}");

            // Start the fake installer
            Console.WriteLine("\nStarting fake installer...");
            var installerProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{installerPath}\"",
                UseShellExecute = false,
                CreateNoWindow = false // Show window for testing
            });

            // Start the cleanup script
            Console.WriteLine("Starting cleanup script...");
            var cleanupProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{cleanupScript}\"",
                UseShellExecute = false,
                CreateNoWindow = false // Show window for testing
            });

            // Wait for installer to complete
            Console.WriteLine("Waiting for installer to complete...");
            await Task.Run(() => installerProcess?.WaitForExit());
            Console.WriteLine("Installer completed!");

            // Wait a bit for cleanup to happen
            Console.WriteLine("Waiting for cleanup to complete...");
            await Task.Delay(8000); // Wait 8 seconds for cleanup

            // Check if files were cleaned up
            Console.WriteLine("\n--- Cleanup Results ---");
            Console.WriteLine($"Installer still exists: {File.Exists(installerPath)}");
            Console.WriteLine($"Cleanup script still exists: {File.Exists(cleanupScript)}");

            if (!File.Exists(installerPath) && !File.Exists(cleanupScript))
            {
                Console.WriteLine("✅ SUCCESS: Both files were cleaned up!");
            }
            else
            {
                Console.WriteLine("❌ FAILURE: Some files were not cleaned up");
                
                // Manual cleanup for test
                try
                {
                    if (File.Exists(installerPath)) File.Delete(installerPath);
                    if (File.Exists(cleanupScript)) File.Delete(cleanupScript);
                    Console.WriteLine("Manual cleanup completed");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Manual cleanup failed: {ex.Message}");
                }
            }
        }

        static string CreateCleanupScript(string installerPath)
        {
            try
            {
                var installerName = Path.GetFileNameWithoutExtension(installerPath);
                var scriptPath = Path.Combine(Path.GetTempPath(), $"cleanup_{installerName}_{DateTime.Now.Ticks}.bat");

                var scriptContent = $@"@echo off
echo Starting cleanup script for: {Path.GetFileName(installerPath)}

REM Wait for installer process to finish
:wait
tasklist /FI ""IMAGENAME eq {Path.GetFileName(installerPath)}"" 2>NUL | find /I ""{Path.GetFileName(installerPath)}"" >NUL
if ""%%ERRORLEVEL""==""0"" (
    echo Waiting for installer to finish...
    timeout /t 2 /nobreak >NUL
    goto wait
)

echo Installer process finished, waiting additional 5 seconds...
REM Wait a bit more to ensure installer is completely done
timeout /t 5 /nobreak >NUL

REM Delete the installer file
if exist ""{installerPath}"" (
    echo Deleting installer file: {installerPath}
    del /f /q ""{installerPath}""
    if exist ""{installerPath}"" (
        echo Failed to delete installer file
    ) else (
        echo Successfully deleted installer file
    )
) else (
    echo Installer file not found: {installerPath}
)

echo Cleanup script will now self-delete...
REM Self-delete this script (this command deletes the currently running batch file)
(goto) 2>nul & del ""%~f0""
";

                File.WriteAllText(scriptPath, scriptContent);
                return scriptPath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to create cleanup script: {ex.Message}");
                return null;
            }
        }
    }
}