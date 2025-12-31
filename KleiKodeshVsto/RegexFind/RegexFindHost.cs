using KleiKodesh.Common;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace KleiKodesh.RegexFind
{
    public partial class RegexFindHost : UserControl
    {
        private readonly RegexFind _regexFind = new RegexFind();
        KleiKodeshWebView _regexFindWebView;

        public RegexFindHost()
        {
            this.Dock = DockStyle.Fill;
            InitializeComponent();

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string htmlPath = Path.Combine(baseDir, "RegexFind", "index.html");

            _regexFindWebView = new KleiKodeshWebView(this, htmlPath);

            this.Controls.Add(_regexFindWebView);
        }

        private void InitializeComponent()
        {

            this.Controls.Add(_regexFindWebView);
        }

        // Command handlers called from HTML app - direct RegexFind deserialization
        public void Search(JsonElement options)
        {
            try
            {
                // Debug: Check what we received
                var json = JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = true });
                System.Diagnostics.Debug.WriteLine($"Search called with JSON: {json}");

                // Copy properties from received JsonElement to our instance
                CopyRegexFindProperties(options, _regexFind);

                // Debug: Check what was copied
                System.Diagnostics.Debug.WriteLine($"After copy, _regexFind.Text: '{_regexFind.Text}'");

                _regexFind.Search();

                // Debug: Check if search found results
                System.Diagnostics.Debug.WriteLine($"Search completed. Found {_regexFind.Results?.Length ?? 0} results");

                var results = _regexFind.Results?.Select((r, index) => new SearchResultDto
                {
                    Index = index,
                    Start = r.Start,
                    End = r.End,
                    Text = CreateContextSnippet(r.Range),
                    Before = "",
                    After = "",
                    HighlightStart = GetHighlightStart(r.Range),
                    HighlightEnd = GetHighlightEnd(r.Range),
                    Page = GetPageNumber(r.Range),
                    Line = GetLineNumber(r.Range)
                }).ToArray() ?? new SearchResultDto[0]; // C# 7.3 compatible

                System.Diagnostics.Debug.WriteLine($"Mapped {results.Length} results to DTOs");

                SendSearchResultsToHtml(results);

                // Set focus back to WebView after search
                FocusWebView();
            }
            catch (Exception ex)
            {
                SendErrorToHtml($"Search failed: {ex.Message}");
            }
        }

        public void Replace(JsonElement options)
        {
            try
            {
                CopyRegexFindProperties(options, _regexFind);

                _regexFind.ReplaceCurrent();
                SendSuccessToHtml("Replace completed successfully");

                // Set focus back to WebView after replace
                FocusWebView();
            }
            catch (Exception ex)
            {
                SendErrorToHtml($"Replace failed: {ex.Message}");
            }
        }

        public void ReplaceAll(JsonElement options)
        {
            try
            {
                CopyRegexFindProperties(options, _regexFind);
                _regexFind.Search();

                int replacedCount = _regexFind.Results?.Length ?? 0;
                if (replacedCount > 0)
                {
                    _regexFind.ReplaceAll();
                    SendReplaceCompleteToHtml(replacedCount);

                    // Set focus back to WebView after replace all
                    FocusWebView();
                }
                else
                {
                    SendErrorToHtml("No matches found to replace");
                }
            }
            catch (Exception ex)
            {
                SendErrorToHtml($"Replace all failed: {ex.Message}");
            }
        }

        private void CopyRegexFindProperties(JsonElement element, RegexFind target)
        {
            try
            {
                // Handle both camelCase (from JS) and PascalCase properties
                target.Text = GetStringProperty(element, "searchText", "SearchText", "text", "Text") ?? "";
                target.Bold = GetBoolProperty(element, "bold", "Bold");
                target.Italic = GetBoolProperty(element, "italic", "Italic");
                target.Underline = GetBoolProperty(element, "underline", "Underline");
                target.Superscript = GetBoolProperty(element, "superscript", "Superscript");
                target.Subscript = GetBoolProperty(element, "subscript", "Subscript");
                target.Style = GetStringProperty(element, "style", "Style") ?? "";
                target.Font = GetStringProperty(element, "font", "Font") ?? "";
                target.FontSize = GetFloatProperty(element, "fontSize", "FontSize");
                target.TextColor = GetIntProperty(element, "textColor", "TextColor", "color", "Color");

                // Debug: Log the mapped formatting properties
                System.Diagnostics.Debug.WriteLine($"Mapped formatting - Bold: {target.Bold}, Italic: {target.Italic}, Underline: {target.Underline}, TextColor: {target.TextColor}");

                // Handle search mode
                var modeStr = GetStringProperty(element, "searchMode", "SearchMode", "mode", "Mode") ?? "All";
                if (Enum.TryParse<SearchMode>(modeStr, true, out var mode))
                    target.Mode = mode;

                target.Slop = (short)GetIntProperty(element, "slop", "Slop");
                target.UseWildcards = GetBoolPropertyNonNullable(element, "useRegex", "UseRegex", "useWildcards", "UseWildcards");

                // Copy replace properties with better error handling
                var replaceElement = GetObjectProperty(element, "replaceOptions", "ReplaceOptions", "replace", "Replace");
                if (replaceElement.HasValue)
                {
                    var replace = replaceElement.Value;
                    target.Replace.Text = GetStringProperty(replace, "replaceText", "ReplaceText", "text", "Text") ?? "";
                    target.Replace.Bold = GetBoolProperty(replace, "bold", "Bold");
                    target.Replace.Italic = GetBoolProperty(replace, "italic", "Italic");
                    target.Replace.Underline = GetBoolProperty(replace, "underline", "Underline");
                    target.Replace.Superscript = GetBoolProperty(replace, "superscript", "Superscript");
                    target.Replace.Subscript = GetBoolProperty(replace, "subscript", "Subscript");
                    target.Replace.Style = GetStringProperty(replace, "style", "Style") ?? "";
                    target.Replace.Font = GetStringProperty(replace, "font", "Font") ?? "";
                    target.Replace.FontSize = GetFloatProperty(replace, "fontSize", "FontSize");
                    target.Replace.TextColor = GetIntProperty(replace, "textColor", "TextColor", "color", "Color");
                    target.Replace.TextColor = GetNullableIntProperty(replace, "textColor", "TextColor", "color", "Color");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in CopyRegexFindProperties: {ex.Message}");
                // Continue with default values
            }
        }

        private string GetStringProperty(JsonElement element, params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
                    return prop.GetString();
            }
            return null;
        }

        private bool? GetBoolProperty(JsonElement element, params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                if (element.TryGetProperty(name, out var prop))
                {
                    if (prop.ValueKind == JsonValueKind.Null)
                        return null;
                    if (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False)
                        return prop.GetBoolean();
                }
            }
            return null; // Return null when property is not specified
        }

        private bool GetBoolPropertyNonNullable(JsonElement element, params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                if (element.TryGetProperty(name, out var prop) &&
                    (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False))
                    return prop.GetBoolean();
            }
            return false; // Return false as default for non-nullable booleans
        }

        private int? GetIntProperty(JsonElement element, params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                if (element.TryGetProperty(name, out var prop))
                {
                    if (prop.ValueKind == JsonValueKind.Null)
                        return null;
                    if (prop.TryGetInt32(out var value))
                        return value;
                }
            }
            return null;
        }

        private int? GetNullableIntProperty(JsonElement element, params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                if (element.TryGetProperty(name, out var prop))
                {
                    if (prop.ValueKind == JsonValueKind.Null)
                        return null;
                    if (prop.TryGetInt32(out var value))
                        return value;
                }
            }
            return null; // Return null when property is not specified
        }

        private float? GetFloatProperty(JsonElement element, params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                if (element.TryGetProperty(name, out var prop))
                {
                    // Handle null values
                    if (prop.ValueKind == JsonValueKind.Null)
                        return null;

                    if (prop.TryGetSingle(out var value))
                        return value;

                    // Try to parse as int if single fails
                    if (prop.TryGetInt32(out var intValue))
                        return intValue;
                }
            }
            return null; // Return null when property is not specified
        }

        private JsonElement? GetObjectProperty(JsonElement element, params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Object)
                    return prop;
            }
            return null;
        }

        public void SelectResult(SelectResultDto data)
        {
            try
            {
                if (_regexFind.Results != null && data.Index >= 0 && data.Index < _regexFind.Results.Length)
                {
                    _regexFind.Select(data.Index);
                    SendSuccessToHtml("Result selected in document");

                    // Set focus back to WebView after selecting result
                    FocusWebView();
                }
                else
                {
                    SendErrorToHtml("Invalid result index");
                }
            }
            catch (Exception ex)
            {
                SendErrorToHtml($"Select result failed: {ex.Message}");
            }
        }

        public void PrevResult()
        {
            try
            {
                _regexFind.SelectPrevious();
                SendSuccessToHtml("Selected previous result");
            }
            catch (Exception ex)
            {
                SendErrorToHtml($"Previous result failed: {ex.Message}");
            }
        }

        public void NextResult()
        {
            try
            {
                _regexFind.SelectNext();
                SendSuccessToHtml("Selected next result");
            }
            catch (Exception ex)
            {
                SendErrorToHtml($"Next result failed: {ex.Message}");
            }
        }

        public void SelectInDoc()
        {
            try
            {
                // This would select the current result in the document
                // The selection is already handled by SelectResult
                SendSuccessToHtml("Text selected in document");
            }
            catch (Exception ex)
            {
                SendErrorToHtml($"Select in document failed: {ex.Message}");
            }
        }

        public void GetFontList()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("GetFontList method called");
                var fonts = GetAvailableFonts();
                System.Diagnostics.Debug.WriteLine($"Retrieved {fonts.Length} fonts");
                SendFontListToHtml(fonts);
                System.Diagnostics.Debug.WriteLine("Font list sent to HTML");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetFontList error: {ex.Message}");
                SendErrorToHtml($"Get font list failed: {ex.Message}");
            }
        }

        public void GetStyleList()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("GetStyleList method called");
                var styles = GetAvailableStyles();
                System.Diagnostics.Debug.WriteLine($"Retrieved {styles.Length} styles");
                SendStyleListToHtml(styles);
                System.Diagnostics.Debug.WriteLine("Style list sent to HTML");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetStyleList error: {ex.Message}");
                SendErrorToHtml($"Get style list failed: {ex.Message}");
            }
        }

        public void CopyFormatting(CopyFormattingDto data)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"CopyFormatting called for target: {data.Target}");
                
                var formatting = _regexFind.GetSelectionFormatting();
                
                System.Diagnostics.Debug.WriteLine($"Retrieved formatting - Bold: {formatting.Bold}, Italic: {formatting.Italic}, TextColor: {formatting.TextColor}");
                
                SendFormattingToHtml(formatting, data.Target);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CopyFormatting error: {ex.Message}");
                SendErrorToHtml($"Copy formatting failed: {ex.Message}");
            }
        }

        public void ThemeToggle(ThemeDto data)
        {
            try
            {
                // Handle theme toggle - could save preference or notify other components
                SendSuccessToHtml($"Theme changed to {data.Theme}");
            }
            catch (Exception ex)
            {
                SendErrorToHtml($"Theme toggle failed: {ex.Message}");
            }
        }



        private int GetPageNumber(Microsoft.Office.Interop.Word.Range range)
        {
            try
            {
                return range.Information[Microsoft.Office.Interop.Word.WdInformation.wdActiveEndPageNumber];
            }
            catch
            {
                return 1;
            }
        }

        private int GetLineNumber(Microsoft.Office.Interop.Word.Range range)
        {
            try
            {
                return range.Information[Microsoft.Office.Interop.Word.WdInformation.wdFirstCharacterLineNumber];
            }
            catch
            {
                return 1;
            }
        }

        private string CreateContextSnippet(Microsoft.Office.Interop.Word.Range matchRange)
        {
            try
            {
                const int contextLength = 50; // Characters before and after the match
                var doc = matchRange.Document;

                // Get the full paragraph or a larger context around the match
                var start = Math.Max(0, matchRange.Start - contextLength);
                var end = Math.Min(doc.Characters.Count, matchRange.End + contextLength);

                // Create a range for the context
                var contextRange = doc.Range(start, end);
                var contextText = contextRange.Text;

                // Clean up the text (remove extra whitespace, line breaks)
                contextText = System.Text.RegularExpressions.Regex.Replace(contextText, @"\s+", " ").Trim();

                return contextText;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating context snippet: {ex.Message}");
                // Fallback to just the match text
                return matchRange.Text;
            }
        }

        private int GetHighlightStart(Microsoft.Office.Interop.Word.Range matchRange)
        {
            try
            {
                const int contextLength = 50;
                var actualStart = Math.Max(0, matchRange.Start - contextLength);
                return matchRange.Start - actualStart;
            }
            catch
            {
                return 0;
            }
        }

        private int GetHighlightEnd(Microsoft.Office.Interop.Word.Range matchRange)
        {
            try
            {
                const int contextLength = 50;
                var actualStart = Math.Max(0, matchRange.Start - contextLength);
                var highlightStart = matchRange.Start - actualStart;
                return highlightStart + matchRange.Text.Length;
            }
            catch
            {
                return matchRange.Text.Length;
            }
        }

        private string[] GetAvailableFonts()
        {
            try
            {
                var fonts = new List<string>();
                foreach (string fontName in Globals.ThisAddIn.Application.FontNames)
                {
                    fonts.Add(fontName);
                }
                return fonts.ToArray(); // dont limit!
            }
            catch
            {
                return null;
            }
        }

        private string[] GetAvailableStyles()
        {
            try
            {
                var styles = new List<string>();
                var doc = Globals.ThisAddIn.Application.ActiveDocument;

                foreach (Microsoft.Office.Interop.Word.Style style in doc.Styles)
                {
                    try
                    {
                        if (style.InUse)
                            styles.Add(style.NameLocal);
                    }
                    catch
                    {
                        // Skip styles that can't be accessed
                    }
                }

                return styles.ToArray(); // dont limit!
            }
            catch
            {
                return null;
            }
        }

        private void SendSearchResultsToHtml(SearchResultDto[] results)
        {
            var response = new
            {
                command = "searchResults",
                data = new
                {
                    matches = results,
                    totalCount = results.Length,
                    currentIndex = results.Length > 0 ? 0 : -1
                }
            };
            var json = JsonSerializer.Serialize(response);

            // Debug output
            System.Diagnostics.Debug.WriteLine($"Sending {results.Length} search results to HTML");
            System.Diagnostics.Debug.WriteLine($"WebView2 initialized: {_regexFindWebView.CoreWebView2 != null}");
            System.Diagnostics.Debug.WriteLine($"Response JSON: {json}");

            // Use PostWebMessageAsString instead of PostWebMessageAsJson to avoid double-encoding
            _regexFindWebView.CoreWebView2?.PostWebMessageAsString(json);
        }

        private void SendReplaceCompleteToHtml(int replacedCount)
        {
            var response = new
            {
                command = "replaceComplete",
                data = new { replacedCount }
            };
            var json = JsonSerializer.Serialize(response);
            _regexFindWebView.CoreWebView2?.PostWebMessageAsString(json);
        }

        private void SendFontListToHtml(string[] fonts)
        {
            var response = new
            {
                command = "fontList",
                data = fonts
            };
            var json = JsonSerializer.Serialize(response);
            System.Diagnostics.Debug.WriteLine($"Sending font list with {fonts.Length} fonts: {json}");
            _regexFindWebView.CoreWebView2?.PostWebMessageAsString(json);
        }

        private void SendStyleListToHtml(string[] styles)
        {
            var response = new
            {
                command = "styleList",
                data = styles
            };
            var json = JsonSerializer.Serialize(response);
            System.Diagnostics.Debug.WriteLine($"Sending style list with {styles.Length} styles: {json}");
            _regexFindWebView.CoreWebView2?.PostWebMessageAsString(json);
        }

        private void SendFormattingToHtml(RegexFindBase formatting, string target)
        {
            var response = new
            {
                command = "formattingCopied",
                data = new
                {
                    target = target,
                    formatting = new
                    {
                        bold = formatting.Bold,
                        italic = formatting.Italic,
                        underline = formatting.Underline,
                        superscript = formatting.Superscript,
                        subscript = formatting.Subscript,
                        textColor = formatting.TextColor,
                        fontSize = formatting.FontSize,
                        font = formatting.Font,
                        style = formatting.Style
                    }
                }
            };
            var json = JsonSerializer.Serialize(response);
            System.Diagnostics.Debug.WriteLine($"Sending formatting to HTML: {json}");
            _regexFindWebView.CoreWebView2?.PostWebMessageAsString(json);
        }

        private void SendSuccessToHtml(string message)
        {
            var response = new
            {
                command = "success",
                data = new { message }
            };
            var json = JsonSerializer.Serialize(response);
            _regexFindWebView.CoreWebView2?.PostWebMessageAsString(json);
        }

        private void SendErrorToHtml(string message)
        {
            var response = new
            {
                command = "error",
                data = new { message }
            };
            var json = JsonSerializer.Serialize(response);
            _regexFindWebView.CoreWebView2?.PostWebMessageAsString(json);
        }

        private void FocusWebView()
        {
            try
            {
                // Set focus to the WebView control after search/replace operations
                if (_regexFindWebView != null)
                {
                    _regexFindWebView.Focus();

                    // Also try to focus the WebView2 core if available
                    if (_regexFindWebView.CoreWebView2 != null)
                    {
                        // Use BeginInvoke to ensure focus happens after current operation completes
                        this.BeginInvoke(new Action(() =>
                        {
                            _regexFindWebView.Focus();
                        }));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting WebView focus: {ex.Message}");
                // Don't throw - focus is not critical to functionality
            }
        }

        private void MapBasicOptionsToRegexFind()
        {
            // Use current search settings for selection operations
            _regexFind.Mode = SearchMode.All;
        }
    }

    // DTOs for communication with HTML app - only keeping essential DTOs

    public class SearchResultDto
    {
        public int Index { get; set; }
        public int Start { get; set; }
        public int End { get; set; }
        public string Text { get; set; } = "";
        public string Before { get; set; } = "";
        public string After { get; set; } = "";
        public int HighlightStart { get; set; }
        public int HighlightEnd { get; set; }
        public int Page { get; set; }
        public int Line { get; set; }
    }

    public class SelectResultDto
    {
        public int Index { get; set; }
    }

    public class CopyFormattingDto
    {
        public string Target { get; set; } = "find"; // "find" or "replace"
    }

    public class ThemeDto
    {
        public string Theme { get; set; } = "light";
    }
}