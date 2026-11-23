using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using KitveiHakodesh.Core.Common;

namespace KitveiHakodesh.Core.HebrewBooks
{
    /// <summary>
    /// Where a book's PDF can be fetched from, in the order to try: the official hebrewbooks
    /// handler first, then a public PDF mirror for when the handler does not yield a PDF on
    /// this network — a Cloudflare challenge or a 403.
    ///
    /// The mirror's address lives in <c>HebrewBooks\hb-mirror.json</c>, which ships beside the
    /// catalog and is NOT in git. A missing file or blank value just drops that fallback; the
    /// official handler always remains.
    /// </summary>
    internal sealed class HebrewBooksDownloadSources
    {
        private const string HandlerUrlFormat =
            "https://download.hebrewbooks.org/downloadhandler.ashx?req={0}";

        private const string ConfigRelativePath = "HebrewBooks\\hb-mirror.json";

        private readonly string? _publicMirrorUrlFormat;

        private HebrewBooksDownloadSources(JsonElement? config)
        {
            if (config is not JsonElement root) return;
            _publicMirrorUrlFormat = Read(root, "publicMirrorUrlFormat");
        }

        /// <summary>Reads hb-mirror.json once. A missing or malformed file yields the official
        /// handler alone — the download still works wherever hebrewbooks.org does.</summary>
        public static HebrewBooksDownloadSources Load()
        {
            string? path = AppFileLocator.FindFile(ConfigRelativePath);
            if (path == null) return new HebrewBooksDownloadSources(null);
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                return new HebrewBooksDownloadSources(document.RootElement.Clone());
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                return new HebrewBooksDownloadSources(null);
            }
        }

        /// <summary>The requests to try for a book, official handler first. Each is freshly
        /// built because a request message is single-use.</summary>
        public IEnumerable<HttpRequestMessage> RequestsFor(string bookId)
        {
            yield return new HttpRequestMessage(HttpMethod.Get,
                string.Format(CultureInfo.InvariantCulture, HandlerUrlFormat, bookId));

            if (_publicMirrorUrlFormat != null)
                yield return new HttpRequestMessage(HttpMethod.Get,
                    string.Format(CultureInfo.InvariantCulture, _publicMirrorUrlFormat, bookId));
        }

        private static string? Read(JsonElement root, string name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()!.Trim()
                : null;
    }
}
