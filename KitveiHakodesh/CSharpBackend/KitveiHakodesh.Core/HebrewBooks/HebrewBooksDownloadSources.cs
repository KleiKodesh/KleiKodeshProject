using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KitveiHakodesh.Core.Common;

namespace KitveiHakodesh.Core.HebrewBooks
{
    /// <summary>
    /// Where a book's PDF can be fetched from, in the order to try: the official hebrewbooks
    /// handler first, then the Otzaria-org mirrors (a public PDF mirror, then the org's R2
    /// bucket) for when the handler does not yield a PDF on this network — a Cloudflare
    /// challenge or a 403.
    ///
    /// The mirrors' addresses and the R2 keys live in <c>HebrewBooks\hb-mirror.json</c>, which
    /// ships beside the catalog and is NOT in git. A missing file or blank value just drops that
    /// fallback; the official handler always remains.
    ///
    /// The R2 request is AWS-SigV4 signed here with HMACSHA256 — no SDK, no curl, no reflection —
    /// so it works under the service's native AOT.
    /// </summary>
    internal sealed class HebrewBooksDownloadSources
    {
        private const string HandlerUrlFormat =
            "https://download.hebrewbooks.org/downloadhandler.ashx?req={0}";

        private const string ConfigRelativePath = "HebrewBooks\\hb-mirror.json";

        private const string EmptyBodySha256 =
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        private readonly string? _publicMirrorUrlFormat;
        private readonly string? _r2Host;
        private readonly string? _r2Bucket;
        private readonly string? _r2ObjectKeyFormat;
        private readonly string _r2Region = "auto";
        private readonly string? _r2AccessKey;
        private readonly string? _r2SecretKey;

        private HebrewBooksDownloadSources(JsonElement? config)
        {
            if (config is not JsonElement root) return;
            _publicMirrorUrlFormat = Read(root, "publicMirrorUrlFormat");
            _r2Host = Read(root, "r2Host");
            _r2Bucket = Read(root, "r2Bucket");
            _r2ObjectKeyFormat = Read(root, "r2ObjectKeyFormat");
            _r2Region = Read(root, "r2Region") ?? _r2Region;
            _r2AccessKey = Read(root, "r2AccessKey");
            _r2SecretKey = Read(root, "r2SecretKey");
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
        /// built because a request message is single-use; the R2 one is signed at build time,
        /// so it is only built if the earlier sources failed.</summary>
        public IEnumerable<HttpRequestMessage> RequestsFor(string bookId)
        {
            yield return new HttpRequestMessage(HttpMethod.Get,
                string.Format(CultureInfo.InvariantCulture, HandlerUrlFormat, bookId));

            if (_publicMirrorUrlFormat != null)
                yield return new HttpRequestMessage(HttpMethod.Get,
                    string.Format(CultureInfo.InvariantCulture, _publicMirrorUrlFormat, bookId));

            if (_r2Host != null && _r2Bucket != null && _r2ObjectKeyFormat != null
                && _r2AccessKey != null && _r2SecretKey != null)
                yield return SignedR2Request(bookId);
        }

        private HttpRequestMessage SignedR2Request(string bookId)
        {
            string objectKey = string.Format(CultureInfo.InvariantCulture, _r2ObjectKeyFormat!, bookId);
            string canonicalPath = "/" + Uri.EscapeDataString(_r2Bucket!) + "/" + EncodePathKeepingSlashes(objectKey);
            var request = new HttpRequestMessage(HttpMethod.Get, new Uri("https://" + _r2Host + canonicalPath));

            DateTime now = DateTime.UtcNow;
            string amzDate = now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            string dateStamp = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

            const string signedHeaders = "host;x-amz-content-sha256;x-amz-date";
            string canonicalRequest =
                "GET\n" + canonicalPath + "\n\n" +
                "host:" + _r2Host + "\n" +
                "x-amz-content-sha256:" + EmptyBodySha256 + "\n" +
                "x-amz-date:" + amzDate + "\n\n" +
                signedHeaders + "\n" + EmptyBodySha256;

            string scope = dateStamp + "/" + _r2Region + "/s3/aws4_request";
            string stringToSign = "AWS4-HMAC-SHA256\n" + amzDate + "\n" + scope + "\n" +
                ToHex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)));

            byte[] key = Encoding.UTF8.GetBytes("AWS4" + _r2SecretKey);
            foreach (string part in new[] { dateStamp, _r2Region, "s3", "aws4_request" })
                key = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(part));
            string signature = ToHex(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(stringToSign)));

            request.Headers.TryAddWithoutValidation("x-amz-content-sha256", EmptyBodySha256);
            request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
            request.Headers.TryAddWithoutValidation("Authorization",
                "AWS4-HMAC-SHA256 Credential=" + _r2AccessKey + "/" + scope +
                ", SignedHeaders=" + signedHeaders + ", Signature=" + signature);
            return request;
        }

        private static string? Read(JsonElement root, string name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()!.Trim()
                : null;

        private static string ToHex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

        /// <summary>SigV4 URI-encodes each path segment but keeps the '/' between them.</summary>
        private static string EncodePathKeepingSlashes(string key)
        {
            string[] segments = key.Split('/');
            for (int i = 0; i < segments.Length; i++) segments[i] = Uri.EscapeDataString(segments[i]);
            return string.Join("/", segments);
        }
    }
}
