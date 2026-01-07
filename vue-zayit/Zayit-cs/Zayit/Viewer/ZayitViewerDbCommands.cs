using Microsoft.Web.WebView2.WinForms;
using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Zayit.Viewer
{
    internal class ZayitViewerDbCommands
    {
        readonly WebView2 _webView;
        readonly SeforimDb.DbQueries _db;

        public ZayitViewerDbCommands(WebView2 webView)
        {
            this._webView = webView;
            _db = new SeforimDb.DbQueries();
        }

        /// <summary>
        /// Get Category / Book Tree
        /// </summary>
        public async void GetTree()
        {
            try
            {
                Debug.WriteLine("GetTree called");

                var treeData = new {
                    categoriesFlat = _db.ExecuteQuery(SeforimDb.SqlQueries.GetAllCategories),
                    booksFlat = _db.ExecuteQuery(SeforimDb.SqlQueries.GetAllBooks)
                };

                string json = JsonSerializer.Serialize(treeData, new JsonSerializerOptions  {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                string js = $"window.receiveTreeData({json});";
                await _webView.ExecuteScriptAsync(js);

                Debug.WriteLine("Tree data sent successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetTree error: {ex}");
            }
        }

        /// <summary>
        /// Get table of contents for a book
        /// </summary>
        public async void GetToc(int bookId)
        {
            try
            {
                Debug.WriteLine($"GetToc called: bookId={bookId}");

                var tocEntries = _db.ExecuteQuery(SeforimDb.SqlQueries.GetToc(bookId));
                var tocData = new { tocEntriesFlat = tocEntries };

                string json = JsonSerializer.Serialize(tocData, new JsonSerializerOptions {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                string js = $"window.receiveTocData({bookId}, {json});";
                await _webView.ExecuteScriptAsync(js);

                Debug.WriteLine($"TOC data sent for bookId={bookId}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetToc error: {ex}");
            }
        }

        /// <summary>
        /// Get links/commentary for a line
        /// </summary>
        public async void GetLinks(int lineId, string tabId, int bookId)
        {
            try
            {
                Debug.WriteLine($"GetLinks called: lineId={lineId}, tabId={tabId}, bookId={bookId}");

                var links = _db.ExecuteQuery(SeforimDb.SqlQueries.GetLinks(lineId));

                string json = JsonSerializer.Serialize(links, new JsonSerializerOptions {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                string js = $"window.receiveLinks({JsonSerializer.Serialize(tabId)}, {bookId}, {json});";
                await _webView.ExecuteScriptAsync(js);

                Debug.WriteLine($"Links sent for lineId={lineId}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetLinks error: {ex}");
            }
        }

        /// <summary>
        /// Get total line count for a book
        /// </summary>
        public async void GetTotalLines(int bookId)
        {
            try
            {
                Debug.WriteLine($"GetTotalLines called: bookId={bookId}");

                var result = _db.ExecuteQuery(SeforimDb.SqlQueries.GetBookLineCount(bookId));

                Debug.WriteLine($"Query result type: {result?.GetType()}");

                var resultArray = result as Array;
                if (resultArray != null && resultArray.Length > 0)
                {
                    var firstItem = resultArray.GetValue(0);
                    Debug.WriteLine($"First item type: {firstItem?.GetType()}");

                    // For Dapper's dynamic rows, use direct property access
                    try
                    {
                        dynamic dynamicRow = firstItem;
                        var totalLines = (int)dynamicRow.totalLines;
                        string js = $"window.receiveTotalLines({bookId}, {totalLines});";
                        await _webView.ExecuteScriptAsync(js);
                        Debug.WriteLine($"Total lines sent for bookId={bookId}: {totalLines}");
                        return;
                    }
                    catch (Exception dynamicEx)
                    {
                        Debug.WriteLine($"Dynamic access failed: {dynamicEx.Message}");

                        // Fallback: try reflection on all properties
                        var properties = firstItem.GetType().GetProperties();
                        Debug.WriteLine($"Available properties: {string.Join(", ", properties.Select(p => p.Name))}");

                        foreach (var prop in properties)
                        {
                            var value = prop.GetValue(firstItem);
                            Debug.WriteLine($"Property {prop.Name}: {value}");

                            if (prop.Name.Equals("totalLines", StringComparison.OrdinalIgnoreCase) ||
                                prop.Name.Equals("TotalLines", StringComparison.OrdinalIgnoreCase))
                            {
                                var totalLines = Convert.ToInt32(value);
                                string js = $"window.receiveTotalLines({bookId}, {totalLines});";
                                await _webView.ExecuteScriptAsync(js);
                                Debug.WriteLine($"Total lines sent for bookId={bookId}: {totalLines}");
                                return;
                            }
                        }
                    }
                }

                Debug.WriteLine($"No result or no valid data returned for bookId={bookId}");
                string fallbackJs = $"window.receiveTotalLines({bookId}, 0);";
                await _webView.ExecuteScriptAsync(fallbackJs);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetTotalLines error: {ex}");
            }
        }

        /// <summary>
        /// Get single line content
        /// </summary>
        public async void GetLineContent(int bookId, int lineIndex)
        {
            try
            {
                Debug.WriteLine($"GetLineContent called: bookId={bookId}, lineIndex={lineIndex}");

                var result = _db.ExecuteQuery(SeforimDb.SqlQueries.GetLineContent(bookId, lineIndex));

                string content = null;
                var resultArray = result as Array;
                if (resultArray != null && resultArray.Length > 0)
                {
                    var firstItem = resultArray.GetValue(0);

                    // Use dynamic access for Dapper rows
                    try
                    {
                        dynamic dynamicRow = firstItem;
                        content = dynamicRow.content;
                    }
                    catch (Exception dynamicEx)
                    {
                        Debug.WriteLine($"Dynamic access failed for GetLineContent: {dynamicEx.Message}");

                        // Fallback to reflection
                        var contentProperty = firstItem?.GetType().GetProperty("content");
                        content = contentProperty?.GetValue(firstItem) as string;
                    }
                }

                string contentJson = JsonSerializer.Serialize(content);
                string js = $"window.receiveLineContent({bookId}, {lineIndex}, {contentJson});";
                await _webView.ExecuteScriptAsync(js);

                Debug.WriteLine($"Line content sent for bookId={bookId}, lineIndex={lineIndex}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetLineContent error: {ex}");
            }
        }

        /// <summary>
        /// Get line ID by bookId and lineIndex
        /// </summary>
        public async void GetLineId(int bookId, int lineIndex)
        {
            try
            {
                Debug.WriteLine($"GetLineId called: bookId={bookId}, lineIndex={lineIndex}");

                var result = _db.ExecuteQuery(SeforimDb.SqlQueries.GetLineId(bookId, lineIndex));

                int? lineId = null;
                var resultArray = result as Array;
                if (resultArray != null && resultArray.Length > 0)
                {
                    var firstItem = resultArray.GetValue(0);

                    // Use dynamic access for Dapper rows
                    try
                    {
                        dynamic dynamicRow = firstItem;
                        lineId = (int)dynamicRow.id;
                    }
                    catch (Exception dynamicEx)
                    {
                        Debug.WriteLine($"Dynamic access failed for GetLineId: {dynamicEx.Message}");

                        // Fallback to reflection
                        var idProperty = firstItem?.GetType().GetProperty("id");
                        var idValue = idProperty?.GetValue(firstItem);
                        if (idValue != null)
                        {
                            lineId = Convert.ToInt32(idValue);
                        }
                    }
                }

                string lineIdJson = lineId.HasValue ? lineId.Value.ToString() : "null";
                string js = $"window.receiveLineId({bookId}, {lineIndex}, {lineIdJson});";
                await _webView.ExecuteScriptAsync(js);

                Debug.WriteLine($"Line ID sent for bookId={bookId}, lineIndex={lineIndex}: {lineIdJson}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetLineId error: {ex}");
            }
        }

        /// <summary>
        /// Get range of lines
        /// </summary>
        public async void GetLineRange(int bookId, int start, int end)
        {
            try
            {
                Debug.WriteLine($"GetLineRange called: bookId={bookId}, start={start}, end={end}");

                var lines = _db.ExecuteQuery(SeforimDb.SqlQueries.GetLineRange(bookId, start, end));

                string json = JsonSerializer.Serialize(lines, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                string js = $"window.receiveLineRange({bookId}, {start}, {end}, {json});";
                await _webView.ExecuteScriptAsync(js);

                Debug.WriteLine($"Line range sent for bookId={bookId}, start={start}, end={end}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetLineRange error: {ex}");
            }
        }

        /// <summary>
        /// Search for lines containing a search term
        /// </summary>
        public async void SearchLines(int bookId, string searchTerm)
        {
            try
            {
                Debug.WriteLine($"SearchLines called: bookId={bookId}, searchTerm={searchTerm}");

                var lines = _db.ExecuteQuery(SeforimDb.SqlQueries.SearchLines(bookId, searchTerm));

                string json = JsonSerializer.Serialize(lines, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                string searchTermJson = JsonSerializer.Serialize(searchTerm);
                string js = $"window.receiveSearchResults({bookId}, {searchTermJson}, {json});";
                await _webView.ExecuteScriptAsync(js);

                Debug.WriteLine($"Search results sent for bookId={bookId}, searchTerm={searchTerm}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SearchLines error: {ex}");
            }
        }
    }
}