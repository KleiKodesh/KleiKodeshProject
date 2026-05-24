using System;
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

            var payload = new
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

            string body = JsonSerializer.Serialize(payload);
            Exception lastError = null;

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

                    return ExtractNiqqud(dataArr);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            throw new Exception(
                "Dicta API unavailable: " + lastError?.Message,
                lastError);
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
                    w = w.Replace("|", "");
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