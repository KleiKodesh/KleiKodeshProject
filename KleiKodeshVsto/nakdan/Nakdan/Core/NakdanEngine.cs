using Nakdan.WdStyles;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Nakdan.Core
{
    public class NakdanEngine
    {
        private const int DictaMaxChars = 5000;

        private readonly DictaApiClient _dictaApi =
            new DictaApiClient();

        /// <summary>
        /// Callback for progress updates during vowelization.
        /// Called with stage name and optional details.
        /// </summary>
        public Action<string> OnProgress { get; set; }

        public async Task<string> VowelizeOoxmlAsync(
            string ooxml,
            NakdanOptions opts,
            CancellationToken cancellationToken = default)
        {
            if (opts == null)
                opts = new NakdanOptions();

            OnProgress?.Invoke("📄 ניתוח מבנה המסמך");
            XDocument doc = XDocument.Parse(ooxml);

            OnProgress?.Invoke("🏷️ חילוץ סגנונות");
            var styleIdToName = StyleExtractor.ExtractStylesFromOoxml(doc);

            var ignored = new HashSet<string>(
                (opts.IgnoredStyles ?? Enumerable.Empty<string>())
                .Select(s => s.ToLowerInvariant()));

            OnProgress?.Invoke("📝 איסוף קטעי טקסט");
            List<RunInfo> runs =
                CollectRuns(doc, ignored, styleIdToName);

            if (runs.Count == 0)
                return ooxml;

            OnProgress?.Invoke($"🔤 בניית זרם אותיות - {runs.Count} קטעים");
            List<Token> tokens =
                BuildTokenStream(runs);

            OnProgress?.Invoke($"✂️ חלוקת טקסט לחלקים - {tokens.Count} אותיות");
            List<List<Token>> chunks =
                TokenChunker.Chunk(
                    tokens,
                    DictaMaxChars);

            string genre =
                opts.Genre.ToString().ToLowerInvariant();

            OnProgress?.Invoke($"🌐 שליחה ל-Dicta - {chunks.Count} בקשות");
            Task<string>[] tasks = chunks
                .Select(c => _dictaApi.NakdanAsync(
                    TokenTextConverter.ToPlainText(c),
                    genre,
                    cancellationToken))
                .ToArray();

            string[] vowelizedChunks =
                await Task.WhenAll(tasks);

            OnProgress?.Invoke("🎯 הוספת ניקוד לטקסט");
            for (int i = 0; i < chunks.Count; i++)
            {
                TokenTextConverter.FillVowels(
                    chunks[i],
                    vowelizedChunks[i]);
            }

            OnProgress?.Invoke("💾 כתיבת התוצאה למסמך");
            RunWriter.WriteTokensToRuns(tokens, runs);

            OnProgress?.Invoke("✅ ניקוד הושלם בהצלחה");
            return doc.ToString(
                SaveOptions.DisableFormatting);
        }

        private List<RunInfo> CollectRuns(
            XDocument doc,
            HashSet<string> ignored,
            Dictionary<string, string> styleIdToName)
        {
            var runs = new List<RunInfo>();

            int idx = 0;

            foreach (XElement para in OoxmlHelper.GetParagraphs(doc))
            {
                string styleId = OoxmlHelper.GetParagraphStyleId(para);

                // If no explicit style, use the default style "a"
                if (string.IsNullOrWhiteSpace(styleId))
                    styleId = "a";

                // Check if this style ID should be ignored
                if (ignored.Contains(styleId))
                    continue;

                foreach (XElement run in OoxmlHelper.GetRuns(para))
                {
                    XElement textEl = OoxmlHelper.GetTextElement(run);

                    if (textEl == null)
                        continue;

                    // Get display name for the run info
                    string styleName = styleId;
                    if (styleIdToName.TryGetValue(styleId, out var name))
                        styleName = name;

                    runs.Add(new RunInfo
                    {
                        Index = idx++,
                        Element = run,
                        TextEl = textEl,
                        OrigText = textEl.Value,
                        StyleName = styleName
                    });
                }
            }

            return runs;
        }

        private List<Token> BuildTokenStream(
            List<RunInfo> runs)
        {
            var tokens = new List<Token>();

            foreach (RunInfo run in runs)
            {
                string stripped = run.OrigText.StripNikkud();

                int pos = 0;

                foreach (char c in stripped)
                {
                    tokens.Add(new Token
                    {
                        Base = c,
                        VowelsAfter = string.Empty,
                        RunIndex = run.Index,
                        PosInRun = pos++
                    });
                }
            }

            return tokens;
        }
    }
}
