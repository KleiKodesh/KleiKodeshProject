namespace KitveiHakodesh.Core.FastDocView;

/// <summary>The local file types the FastDocView page reads (the page's own list in
/// vue-frontend/src/utils/fastDocViewFiles.ts must match).</summary>
public static class FastDocViewFileTypes
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".docm", ".dot", ".dotx", ".dotm", ".odt", ".ott", ".fodt", ".rtf", ".txt",
    };

    public static bool IsSupported(string path) => Extensions.Contains(Path.GetExtension(path));
}
