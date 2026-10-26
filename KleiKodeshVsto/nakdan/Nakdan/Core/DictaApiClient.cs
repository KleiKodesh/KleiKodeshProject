using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Nakdan.Core
{
    public class DictaApiClient
    {
        private static readonly string[] DictaEndpoints =
        {
            "https://nakdan-u2-1a.loadbalancer.dicta.org.il/api",
            "https://nakdan-u1-0.loadbalancer.dicta.org.il/api",
            "https://nakdan-5-1.loadbalancer.dicta.org.il/api"
        };

        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        public async Task<string> NakdanAsync(
            string text,
            string genre = "modern",
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var payload = BuildPayload(text, genre);

            JsonArray data = await PostNakdanAsync(payload, cancellationToken);
            return ExtractNiqqud(data);
        }

        /// <summary>
        /// Returns the alternative vocalizations Dicta offers for the token that
        /// covers <paramref name="offset"/> in <paramref name="context"/>, in
        /// Dicta's order, de-duplicated (first wins), capped at 10.
        /// </summary>
        public async Task<List<string>> GetWordOptionsAsync(
            string context,
            int offset,
            string genre = "modern",
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(context) || offset < 0 || offset >= context.Length)
                return result;

            JsonArray data = await PostNakdanAsync(BuildPayload(context, genre), cancellationToken);

            int pos = 0;
            foreach (JsonNode item in data)
            {
                if (!(item is JsonObject itemObj))
                    continue;

                string str = itemObj["str"]?.GetValue<string>() ?? string.Empty;
                int start = pos;
                pos += str.Length;
                if (offset < start || offset >= pos)
                    continue;

                JsonArray optionsArr = (itemObj["nakdan"] as JsonObject)?["options"] as JsonArray;
                if (optionsArr == null)
                    return result;

                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonNode opt in optionsArr)
                {
                    string w = (opt as JsonObject)?["w"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(w))
                        continue;
                    w = w.Replace("|", "").Replace("ֽ", "");
                    if (w.Length == 0 || !seen.Add(w))
                        continue;
                    result.Add(w);
                    if (result.Count >= 10)
                        break;
                }
                return result;
            }

            return result;
        }

        private static object BuildPayload(string text, string genre)
        {
            return new
            {
                task = "nakdan",
                data = text,
                genre = genre,
                addmorph = true,
                keepmetagim = true,
                useTokenization = true,
                keepqq = false,
                nodageshdefmem = false,
                patachma = false,
                matchpartial = true,
                userData = "gave permission"
            };
        }

        /// <summary>
        /// POSTs the payload to each Dicta endpoint in turn; returns the response's
        /// "data" array from the first that succeeds.
        /// </summary>
        private async Task<JsonArray> PostNakdanAsync(
            object payload,
            CancellationToken cancellationToken)
        {
            string body = JsonSerializer.Serialize(payload);
            Exception lastError = null;
            bool isNetworkError = false;

            foreach (string url in DictaEndpoints)
            {
                try
                {
                    var content = new StringContent(
                        body,
                        Encoding.UTF8,
                        "application/json");

                    HttpResponseMessage response =
                        await _http.PostAsync(
                            url,
                            content,
                            cancellationToken);

                    response.EnsureSuccessStatusCode();

                    string json =
                        await response.Content.ReadAsStringAsync();

                    JsonNode root = JsonNode.Parse(json);

                    if (!(root is JsonObject rootObj))
                        throw new InvalidOperationException(
                            $"Expected JSON object, got {root?.GetType().Name ?? "null"}");

                    JsonNode dataNode = rootObj["data"];
                    if (!(dataNode is JsonArray dataArr))
                        throw new InvalidOperationException(
                            $"Expected 'data' field to be an array, got {dataNode?.GetType().Name ?? "null"}");

                    return dataArr;
                }
                catch (HttpRequestException ex)
                {
                    // Network-level errors: DNS failure, connection refused, etc.
                    lastError = ex;
                    isNetworkError = true;
                }
                catch (TaskCanceledException ex)
                {
                    // Timeout or cancellation
                    lastError = ex;
                    isNetworkError = true;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            // Provide user-friendly error message
            string errorMessage = isNetworkError
                ? Nakdan.Properties.Strings.Err_NoInternet
                : "Dicta API unavailable: " + lastError?.Message;

            throw new Exception(errorMessage, lastError);
        }

        private string ExtractNiqqud(JsonArray data)
        {
            if (data == null)
                return string.Empty;

            var sb = new StringBuilder();

            foreach (JsonNode item in data)
            {
                if (!(item is JsonObject itemObj))
                    continue;

                JsonObject nakdanObj = itemObj["nakdan"] as JsonObject;
                if (nakdanObj == null)
                    continue;

                JsonArray optionsArr = nakdanObj["options"] as JsonArray;
                if (optionsArr != null && optionsArr.Count > 0 && optionsArr[0] is JsonObject firstOption)
                {
                    string w = firstOption["w"]?.GetValue<string>() ?? string.Empty;
                    w = w.Replace("|", "").Replace("ֽ", "");
                    sb.Append(w);
                }
                else
                {
                    // Fallback: use nakdan.word or str if options is empty
                    string fallback = nakdanObj["word"]?.GetValue<string>()
                                   ?? itemObj["str"]?.GetValue<string>()
                                   ?? string.Empty;
                    fallback = fallback.Replace("|", "");
                    sb.Append(fallback);
                }
            }

            return sb.ToString();
        }
    }
}