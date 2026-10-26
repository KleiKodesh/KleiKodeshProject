using System;
using System.IO;

namespace KitveiHakodesh.Core.SeforimLibraryDownload
{
    /// <summary>Cleanup of the download's temporary files, where a failure to delete is not worth failing over.</summary>
    internal static class SeforimLibraryDownloadFiles
    {
        public static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* next run cleans up */ }
        }
    }
}
