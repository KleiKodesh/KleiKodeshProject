using KitveiHakodesh.Core.SeforimDbCatalog;

namespace KitveiHakodesh.Core.LocalFiles;

/// <summary>
/// The WORDS of a file path as the catalog ranking sees them: the file name (no extension)
/// and the folder names, split on file-name separators and run through the catalog's own
/// tokenizer. ONE copy, used both when the file index writes a document (its name_words /
/// folder_words fields) and when the UserSpace search measures a file's ranking keys - so the
/// words Lucene orders by are exactly the words the final ranking compares.
/// </summary>
internal static class LocalFileNameWords
{
    /// <summary>The file name without extension, tokenized.</summary>
    public static List<string> NameTokens(string path)
        => SeforimDbCatalogTextNormalizer.Tokenize(SeparatorsToSpaces(Path.GetFileNameWithoutExtension(path)));

    /// <summary>Folder names (a "\"-separated run of them, e.g. "c:\talmud\bavli"), tokenized.</summary>
    public static List<string> FolderTokens(string folders)
        => SeforimDbCatalogTextNormalizer.Tokenize(SeparatorsToSpaces(folders.Replace('\\', ' ')));

    /// <summary>File names use "_", "-", "+", "," and brackets as word breaks
    /// ("bava_kamma_daf_5"); the catalog tokenizer only splits on whitespace and would glue
    /// them into one word, so the file could never match a typed word whole.</summary>
    public static string SeparatorsToSpaces(string s)
    {
        var chars = s.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (chars[i] is '_' or '-' or '+' or ',' or '(' or ')' or '[' or ']' or '{' or '}' or '\u05BE' or '\u2013' or '\u2014')
                chars[i] = ' ';
        return new string(chars);
    }
}
