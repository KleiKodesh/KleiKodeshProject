using Microsoft.Win32;
using System;
using System.IO;
using System.Threading.Tasks;

namespace KleiKodeshVstoInstallerWpf
{
    /// <summary>
    /// Handles detection and removal of old installations from Program Files
    /// before installing to the new App Data location
    /// </summary>
    public static class OldInstallationCleaner
    {
        const string AppName = "KleiKodesh";
        const string OldInstallFolderName = "KleiKodesh";
        
        // Old installation paths
        static string OldInstallPathX86 => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), OldInstallFolderName);
        static string OldInstallPathX64 => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), OldInstallFolderName);
        static string OldAddinRegistryPath => $@"Software\Microsoft\Office\Word\Addins\{AppName}";

        /// <summary>
        /// Checks if any old installations exist and removes them
        /// </summary>
        /// <returns>True if old installations were found and removed, false if none existed</returns>
        public static async Task<bool> CheckAndRemoveOldInstallations()
        {
            bool foundOldInstallation = false;

            try
            {
                // Check and remove old Program Files (x86) installation
                if (Directory.Exists(OldInstallPathX86))
                {
                    await RemoveOldInstallation(OldInstallPathX86);
                    foundOldInstallation = true;
                }

                // Check and remove old Program Files installation
                if (Directory.Exists(OldInstallPathX64))
                {
                    await RemoveOldInstallation(OldInstallPathX64);
                    foundOldInstallation = true;
                }

                // Clean up old registry entries
                if (foundOldInstallation)
                {
                    await CleanupOldRegistryEntries();
                }

                return foundOldInstallation;
            }
            catch (Exception)
            {
                // Don't fail the installation if old cleanup fails
                // Just return false to indicate we couldn't clean up
                return false;
            }
        }

        /// <summary>
        /// Removes files from an old installation directory
        /// </summary>
        /// <param name="oldPath">Path to the old installation directory</param>
        private static async Task RemoveOldInstallation(string oldPath)
        {
            try
            {
                // Remove all files and subdirectories
                await Task.Run(() =>
                {
                    if (Directory.Exists(oldPath))
                    {
                        Directory.Delete(oldPath, recursive: true);
                    }
                });
            }
            catch (UnauthorizedAccessException)
            {
                // If we can't delete due to permissions, try to delete individual files
                await TryRemoveIndividualFiles(oldPath);
            }
            catch (IOException)
            {
                // Files might be in use, try individual file removal
                await TryRemoveIndividualFiles(oldPath);
            }
        }

        /// <summary>
        /// Attempts to remove individual files when directory deletion fails
        /// </summary>
        /// <param name="directoryPath">Directory path to clean up</param>
        private static async Task TryRemoveIndividualFiles(string directoryPath)
        {
            try
            {
                await Task.Run(() =>
                {
                    if (!Directory.Exists(directoryPath))
                        return;

                    // Try to remove individual files
                    string[] files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
                    foreach (string file in files)
                    {
                        try
                        {
                            File.SetAttributes(file, FileAttributes.Normal);
                            File.Delete(file);
                        }
                        catch
                        {
                            // Skip files that can't be deleted
                        }
                    }

                    // Try to remove empty directories
                    string[] directories = Directory.GetDirectories(directoryPath, "*", SearchOption.AllDirectories);
                    Array.Reverse(directories); // Delete from deepest first
                    
                    foreach (string dir in directories)
                    {
                        try
                        {
                            if (Directory.GetFiles(dir).Length == 0 && Directory.GetDirectories(dir).Length == 0)
                            {
                                Directory.Delete(dir);
                            }
                        }
                        catch
                        {
                            // Skip directories that can't be deleted
                        }
                    }

                    // Finally try to remove the main directory if empty
                    try
                    {
                        if (Directory.GetFiles(directoryPath).Length == 0 && Directory.GetDirectories(directoryPath).Length == 0)
                        {
                            Directory.Delete(directoryPath);
                        }
                    }
                    catch
                    {
                        // Skip if can't delete main directory
                    }
                });
            }
            catch
            {
                // Ignore errors in individual file cleanup
            }
        }

        /// <summary>
        /// Removes old registry entries for the Office add-in from HKLM (machine-wide)
        /// since we're now using HKCU (current user) registration
        /// </summary>
        private static async Task CleanupOldRegistryEntries()
        {
            await Task.Run(() =>
            {
                try
                {
                    // Clean up old 64-bit HKLM registry entries
                    using (RegistryKey key64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                    {
                        key64.DeleteSubKey(OldAddinRegistryPath, throwOnMissingSubKey: false);
                    }
                }
                catch
                {
                    // Ignore registry cleanup errors
                }

                try
                {
                    // Clean up old 32-bit HKLM registry entries
                    using (RegistryKey key32 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
                    {
                        key32.DeleteSubKey(OldAddinRegistryPath, throwOnMissingSubKey: false);
                    }
                }
                catch
                {
                    // Ignore registry cleanup errors
                }
            });
        }

        /// <summary>
        /// Checks if any old installations exist without removing them
        /// </summary>
        /// <returns>True if old installations are detected</returns>
        public static bool HasOldInstallations()
        {
            return Directory.Exists(OldInstallPathX86) || Directory.Exists(OldInstallPathX64);
        }

        /// <summary>
        /// Gets information about detected old installations
        /// </summary>
        /// <returns>String describing found old installations</returns>
        public static string GetOldInstallationInfo()
        {
            var info = new System.Text.StringBuilder();
            
            if (Directory.Exists(OldInstallPathX86))
            {
                info.AppendLine($"Found old installation: {OldInstallPathX86}");
            }
            
            if (Directory.Exists(OldInstallPathX64))
            {
                info.AppendLine($"Found old installation: {OldInstallPathX64}");
            }

            return info.ToString().TrimEnd();
        }
    }
}