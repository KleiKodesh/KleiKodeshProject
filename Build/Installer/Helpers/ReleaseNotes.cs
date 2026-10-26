using System.IO;
using System.Text;
using System.Windows;

namespace KleiKodeshVstoInstallerWpf
{
    /// <summary>
    /// Release notes embedded by CI (see the ReleaseNotes ItemGroup in the csproj):
    /// "ReleaseNotes.he.md" and "ReleaseNotes.en.md". Picks the file matching the
    /// installer UI language and falls back to the other one. Null when none is embedded.
    /// </summary>
    public sealed class ReleaseNotes
    {
        public string Markdown { get; private set; }
        public FlowDirection FlowDirection { get; private set; }

        public static ReleaseNotes Load()
        {
            string preferred = UiLanguage.Current;
            string other     = preferred == UiLanguage.English ? UiLanguage.Hebrew : UiLanguage.English;
            return TryLoad(preferred) ?? TryLoad(other);
        }

        static ReleaseNotes TryLoad(string lang)
        {
            using (var stream = typeof(ReleaseNotes).Assembly.GetManifestResourceStream("ReleaseNotes." + lang + ".md"))
            {
                if (stream == null) return null;
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string text = reader.ReadToEnd();
                    if (string.IsNullOrWhiteSpace(text)) return null;
                    return new ReleaseNotes
                    {
                        Markdown      = text,
                        FlowDirection = lang == UiLanguage.Hebrew ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                    };
                }
            }
        }
    }
}
