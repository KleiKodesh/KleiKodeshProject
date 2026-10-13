#nullable enable
using System;
using System.Diagnostics;
using System.IO;

namespace KitveiHakodesh.Core.LocalFiles;

/// <summary>
/// The rules for opening a local file in the app viewer or the OS: what name the served file
/// carries (its extension tells the client which viewer to use), whether it is HTML (which
/// needs its folder served too, for sibling CSS/JS/images), whether that folder is an Otzaria
/// addin, and handing a file to its default program.
/// </summary>
public static class LocalFileOpenRules
{
    // Types served DIRECTLY (the file's own bytes): the viewer renders these as-is.
    private static readonly string[] DirectExtensions = { ".pdf", ".htm", ".html", ".txt" };

    // Types RENDERED first (Word -> PDF). Deliberately NOT supported: MS Works (.wps), MHTML
    // web archives (.mht/.mhtml) and XPS - they are absent here, so they are rejected at every
    // open entry point (pick, Open-With, restore) of both hosts.
    private static readonly string[] ConvertExtensions =
        { ".doc", ".docx", ".docm", ".dot", ".dotx", ".dotm", ".odt", ".rtf" };

    /// <summary>True when the app can open <paramref name="path"/> at all; <paramref name="needsConvert"/>
    /// says whether it must be rendered (Word family) before it is served.</summary>
    public static bool IsSupported(string path, out bool needsConvert)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        needsConvert = Array.IndexOf(ConvertExtensions, ext) >= 0;
        return needsConvert || Array.IndexOf(DirectExtensions, ext) >= 0;
    }

    /// <summary>True for a plain-text file, which the viewer shows from its decoded text.</summary>
    public static bool IsText(string path) =>
        Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase);

    /// <summary>The display/served file name: the original name, or, for a rendered Word-family
    /// document, the original base name with the rendered extension (.html or .pdf).</summary>
    public static string ServedFileName(string sourcePath, bool rendered, bool renderedAsHtml) =>
        rendered
            ? Path.GetFileNameWithoutExtension(sourcePath) + (renderedAsHtml ? ".html" : ".pdf")
            : Path.GetFileName(sourcePath);

    /// <summary>True when the served file is HTML (.html / .htm).</summary>
    public static bool IsHtml(string fileName)
    {
        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext == ".html" || ext == ".htm";
    }

    /// <summary>The folder an HTML file's sibling assets load from.</summary>
    public static string AssetFolderOf(string sourcePath) => Path.GetDirectoryName(sourcePath) ?? sourcePath;

    /// <summary>Otzaria addins carry a manifest.json in the same folder as their HTML entry.</summary>
    public static bool IsOtzariaAddinFolder(string folder) =>
        File.Exists(Path.Combine(folder, "manifest.json"));

    /// <summary>Hands <paramref name="fullPath"/> to the OS's registered default program
    /// (shell-execute). Returns null on success, or the error message. The caller validates the
    /// path first.</summary>
    public static string? OpenInDefaultApp(string fullPath)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo { FileName = fullPath, UseShellExecute = true });
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
