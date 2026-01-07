using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace WpfLib.Helpers
{
    public static class ResourcesHelper
    {
        static string _baseDirectory;
        public static string BaseDirectory
        {
            get
            {
                if (string.IsNullOrEmpty(_baseDirectory))
                    _baseDirectory = GetBaseDirectory();
                return _baseDirectory;
            }
        }

        public static string ResourcesDirectory => Path.Combine(BaseDirectory, "Resources");

        public static void OpenResource(string fileName)
        {
            try
            {
                string filePath = Path.Combine(ResourcesDirectory, fileName);
                if (File.Exists(filePath))
                    Process.Start(filePath);
                else
                    MessageBox.Show("Missing_File" + " " + fileName, "", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        public static string GetBaseDirectory()
        {
            string baseDirectory = Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory);
            baseDirectory = GetDirectoryRecursive(baseDirectory);
            return baseDirectory;
        }

        static string GetDirectoryRecursive(string directory)
        {
            try
            {
                string versionFile = Path.Combine(directory, UpdateHelper.CurrentVersion);

                if (File.Exists(versionFile))
                    return directory;

                foreach (string entry in Directory.GetDirectories(directory))
                {
                    string found = GetDirectoryRecursive(entry);
                    if (found != null)
                        return found;
                }
            }
            catch { return null; }

            return null;
        }
    }
}
