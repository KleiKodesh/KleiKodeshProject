namespace KitveiHakodesh.Core.FastDocView;

/// <summary>The local file types the FastDocView page reads (the page's own list in
/// vue-frontend/src/utils/fastDocViewFiles.ts must match).</summary>
public static class FastDocViewFileTypes
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".docm", ".dot", ".dotx", ".dotm", ".odt", ".ott", ".fodt", ".rtf", ".txt", ".xml",   // .xml: Word XML (Flat OPC / Word 2003), told apart by content
    };

    public static bool IsSupported(string path) => Extensions.Contains(Path.GetExtension(path));
}
