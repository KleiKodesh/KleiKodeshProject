using KleiKodesh.Common;
using KleiKodesh.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;

namespace KleiKodesh.RegexSearch
{
    public partial class RegexFindHost : UserControl
    {
        readonly RegexSearch _regexFind = new RegexSearch();
        KleiKodeshWebView _regexFindWebView;

        public RegexFindHost()
        {
            this.Dock = DockStyle.Fill;

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string htmlPath = Path.Combine(baseDir, "RegexFind", "index.html");

            _regexFindWebView = new KleiKodeshWebView(this, htmlPath);

            this.Controls.Add(_regexFindWebView);
        }

        // Command handlers called from HTML app - direct RegexFind deserialization
        public void Search(JsonElement options)
        {
            try
            {
#if DEBUG
                var json = JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = true });
                Debug.WriteLine($"=== SEARCH DEBUG START ===");
                Debug.WriteLine($"Search method called with JSON: {json}");
#endif
                var data = DeserializeData(options);
#if DEBUG
                Debug.WriteLine($"Deserialized data - RegexFind: {data.regexFind != null}, RegexFindReplace: {data.regexFindReplace != null}");
                if (data.regexFind != null)
                {
                    Debug.WriteLine($"Search Text: '{data.regexFind.Text}'");
                    Debug.WriteLine($"Search Mode: {data.regexFind.Mode}");
                    Debug.WriteLine($"Use Regex: {data.regexFind.UseWildcards}");
                    Debug.WriteLine($"Slop: {data.regexFind.Slop}");
                    Debug.WriteLine($"Bold: {data.regexFind.Bold}, Italic: {data.regexFind.Italic}");
                    Debug.WriteLine($"Font: '{data.regexFind.Font}', Style: '{data.regexFind.Style}'");
                    Debug.WriteLine($"FontSize: {data.regexFind.FontSize}, TextColor: {data.regexFind.TextColor}");
                }
#endif
                _regexFind.Execute(data.regexFind);
                Debug.WriteLine($"Search completed. Found {_regexFind.Results?.Length ?? 0} results");
                
                // Send only the snippets (pre-highlighted by C#) to HTML
                var snippets = _regexFind.Results?.Select(r => r.Snippet).ToArray() ?? new string[0];
#if DEBUG
                Debug.WriteLine($"Sending {snippets.Length} snippets to HTML");
                for (int i = 0; i < Math.Min(snippets.Length, 3); i++)
                {
                    var snippet = snippets[i];
                    var preview = snippet != null ? snippet.Substring(0, Math.Min(50, snippet.Length)) : "null";
                    Debug.WriteLine($"Snippet {i}: {preview}...");
                }
                Debug.WriteLine($"=== SEARCH DEBUG END ===");
#endif
                SendDataToHtml("searchResults", snippets);
                FocusWebView();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Search failed: {ex.Message}");
#if DEBUG
                Debug.WriteLine($"Search exception stack trace: {ex.StackTrace}");
#endif
            }
        }

        public void Replace(JsonElement options)
        {
            try
            {
#if DEBUG
                Debug.WriteLine($"=== REPLACE DEBUG START ===");
                var json = JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = true });
                Debug.WriteLine($"Replace method called with JSON: {json}");
#endif
                var data = DeserializeData(options);
                _regexFind.Execute(data.regexFind, data.regexFindReplace, replace: true);
#if DEBUG
                Debug.WriteLine($"Replace completed");
                Debug.WriteLine($"=== REPLACE DEBUG END ===");
#endif
                FocusWebView();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Replace failed: {ex.Message}");
#if DEBUG
                Debug.WriteLine($"Replace exception stack trace: {ex.StackTrace}");
#endif
            }
        }

        public void ReplaceAll(JsonElement options)
        {
            try
            {
#if DEBUG
                Debug.WriteLine($"=== REPLACE ALL DEBUG START ===");
                var json = JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = true });
                Debug.WriteLine($"ReplaceAll method called with JSON: {json}");
#endif
                var data = DeserializeData(options);
                _regexFind.Execute(data.regexFind, data.regexFindReplace, replaceAll: true);
#if DEBUG
                Debug.WriteLine($"ReplaceAll completed");
                Debug.WriteLine($"=== REPLACE ALL DEBUG END ===");
#endif
                FocusWebView();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Replace all failed: {ex.Message}");
#if DEBUG
                Debug.WriteLine($"ReplaceAll exception stack trace: {ex.StackTrace}");
#endif
            }
        }

        private (RegexFind regexFind, RegexFindReplace regexFindReplace) DeserializeData(JsonElement element)
        {
            try
            {
#if DEBUG
                Debug.WriteLine($"=== DESERIALIZE DEBUG START ===");
                var rawJson = JsonSerializer.Serialize(element, new JsonSerializerOptions { WriteIndented = true });
                Debug.WriteLine($"Raw element JSON: {rawJson}");
#endif
                var regexFind = new RegexFind();
                
                // Handle main search properties
                regexFind.Text = element.GetStringProperty("searchText", "SearchText", "text", "Text") ?? "";
#if DEBUG
                Debug.WriteLine($"Extracted searchText: '{regexFind.Text}'");
#endif
                var modeStr = element.GetStringProperty("searchMode", "SearchMode", "mode", "Mode") ?? "All";
                if (Enum.TryParse<RegexSearchMode>(modeStr, true, out var mode))
                    regexFind.Mode = mode;
#if DEBUG
                Debug.WriteLine($"Extracted searchMode: '{modeStr}' -> {regexFind.Mode}");
#endif
                regexFind.Slop = (short)(element.GetIntProperty("slop", "Slop") ?? 0);
                regexFind.UseWildcards = element.GetBoolPropertyNonNullable("useRegex", "UseRegex", "useWildcards", "UseWildcards");
#if DEBUG
                Debug.WriteLine($"Extracted slop: {regexFind.Slop}, useRegex: {regexFind.UseWildcards}");
#endif

                // Handle find formatting options from nested findOptions object
                var findOptionsElement = element.GetObjectProperty("findOptions", "FindOptions");
#if DEBUG
                Debug.WriteLine($"FindOptions element found: {findOptionsElement.HasValue}");
#endif
                if (findOptionsElement.HasValue)
                {
                    var findOptions = findOptionsElement.Value;
                    regexFind.Bold = findOptions.GetBoolProperty("bold", "Bold");
                    regexFind.Italic = findOptions.GetBoolProperty("italic", "Italic");
                    regexFind.Underline = findOptions.GetBoolProperty("underline", "Underline");
                    regexFind.Superscript = findOptions.GetBoolProperty("superscript", "Superscript");
                    regexFind.Subscript = findOptions.GetBoolProperty("subscript", "Subscript");
                    regexFind.Style = findOptions.GetStringProperty("style", "Style") ?? "";
                    regexFind.Font = findOptions.GetStringProperty("font", "Font") ?? "";
                    regexFind.FontSize = findOptions.GetFloatProperty("fontSize", "FontSize");
                    regexFind.TextColor = findOptions.GetIntProperty("textColor", "TextColor", "color", "Color");
#if DEBUG
                    Debug.WriteLine($"FindOptions - Bold: {regexFind.Bold}, Italic: {regexFind.Italic}, FontSize: {regexFind.FontSize}");
#endif
                }

                // Handle replace options
                var replaceOptionsElement = element.GetObjectProperty("replaceOptions", "ReplaceOptions");
#if DEBUG
                Debug.WriteLine($"ReplaceOptions element found: {replaceOptionsElement.HasValue}");
#endif
                if (replaceOptionsElement.HasValue)
                {
                    var regexFindReplace = new RegexFindReplace();
                    var replaceOptions = replaceOptionsElement.Value;
                    
                    // Get replace text from main element, not nested
                    regexFindReplace.Text = element.GetStringProperty("replaceText", "ReplaceText", "text", "Text") ?? "";
#if DEBUG
                    Debug.WriteLine($"Extracted replaceText: '{regexFindReplace.Text}'");
#endif
                    
                    // Get formatting from replaceOptions
                    regexFindReplace.Bold = replaceOptions.GetBoolProperty("bold", "Bold");
                    regexFindReplace.Italic = replaceOptions.GetBoolProperty("italic", "Italic");
                    regexFindReplace.Underline = replaceOptions.GetBoolProperty("underline", "Underline");
                    regexFindReplace.Superscript = replaceOptions.GetBoolProperty("superscript", "Superscript");
                    regexFindReplace.Subscript = replaceOptions.GetBoolProperty("subscript", "Subscript");
                    regexFindReplace.Style = replaceOptions.GetStringProperty("style", "Style") ?? "";
                    regexFindReplace.Font = replaceOptions.GetStringProperty("font", "Font") ?? "";
                    regexFindReplace.FontSize = replaceOptions.GetFloatProperty("fontSize", "FontSize");
                    regexFindReplace.TextColor = replaceOptions.GetIntProperty("textColor", "TextColor", "color", "Color");
#if DEBUG
                    Debug.WriteLine($"ReplaceOptions - Bold: {regexFindReplace.Bold}, FontSize: {regexFindReplace.FontSize}");
                    Debug.WriteLine($"=== DESERIALIZE DEBUG END ===");
#endif
                    return (regexFind, regexFindReplace);
                }

#if DEBUG
                Debug.WriteLine($"=== DESERIALIZE DEBUG END (no replace options) ===");
#endif
                return (regexFind, null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in DeserializeData: {ex.Message}");
#if DEBUG
                Debug.WriteLine($"DeserializeData exception stack trace: {ex.StackTrace}");
#endif
                return (null, null);
            }
        }

        public void SelectResult(JsonElement data)
        {
            try
            {
                var index = data.GetIntProperty("Index", "index") ?? -1;
                if (_regexFind.Results != null && index >= 0 && index < _regexFind.Results.Length)
                {
                    _regexFind.SelectResultByIndex(index);
                    FocusWebView();
                }
                else
                {
                    Debug.WriteLine($"Invalid result index: {index}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Select result failed: {ex.Message}");
            }
        }

        public void GetFontList()
        {
            try
            {
                Debug.WriteLine("GetFontList method called");
                var fonts = new List<string>();

                using (var installedFonts = new InstalledFontCollection())
                    foreach (var font in installedFonts.Families)
                        fonts.Add(font.Name);

                Debug.WriteLine($"Retrieved {fonts.Count} fonts");
                SendDataToHtml("fontList", fonts);
                Debug.WriteLine("Font list sent to HTML");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetFontList error: {ex.Message}");
            }
        }

        public void GetStyleList()
        {
            try
            {
                Debug.WriteLine("GetStyleList method called");
                var styles = new List<string>();
                var doc = Globals.ThisAddIn.Application.ActiveDocument;

                foreach (Microsoft.Office.Interop.Word.Style style in doc.Styles)
                    try
                    {
                        if (style.InUse)
                            styles.Add(style.NameLocal);
                    }  catch {  /* Skip styles that can't be accessed */ }

                Debug.WriteLine($"Retrieved {styles.Count} styles");
                SendDataToHtml("styleList", styles);
                Debug.WriteLine("Style list sent to HTML");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetStyleList error: {ex.Message}");
            }
        }

        public void CopyFormatting(JsonElement data)
        {
            try
            {
                var target = data.GetStringProperty("target", "Target") ?? "find";
                Debug.WriteLine($"CopyFormatting called for target: {target}");              
                var formatting = _regexFind.GetSelectionFormatting();                
                Debug.WriteLine($"Retrieved formatting - Bold: {formatting.Bold}, Italic: {formatting.Italic}, TextColor: {formatting.TextColor}");                
                SendFormattingToHtml(formatting, target);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CopyFormatting error: {ex.Message}");
            }
        }

        private void SendFormattingToHtml(RegexFindBase formatting, string target)
        {
            var data = new
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
            };

            SendDataToHtml("formattingCopied", data);           
        }

        private void SendDataToHtml(string commandId, object dataToSend)
        {
            var message = new
            {
                command = commandId,
                data = dataToSend
            };

            string json = JsonSerializer.Serialize(message);
#if DEBUG
            Debug.WriteLine($"=== SEND TO HTML DEBUG ===");
            Debug.WriteLine($"Sending command '{commandId}' to HTML");
            Debug.WriteLine($"Data type: {dataToSend?.GetType().Name ?? "null"}");
            if (dataToSend is string[] array)
            {
                Debug.WriteLine($"Array length: {array.Length}");
            }
            Debug.WriteLine($"JSON length: {json.Length} characters");
            Debug.WriteLine($"JSON preview: {json.Substring(0, Math.Min(200, json.Length))}...");
#endif
            _regexFindWebView?.CoreWebView2?.PostWebMessageAsString(json);
#if DEBUG
            Debug.WriteLine($"Message posted to WebView2");
            Debug.WriteLine($"=== SEND TO HTML DEBUG END ===");
#endif
        }

        private void FocusWebView()
        {
            this.BeginInvoke(new Action(() => {
                _regexFindWebView.Focus();
            }));
        }
    }

   

    //public class CopyFormattingDto
    //{
    //    public string Target { get; set; } = "find"; // "find" or "replace"
    //}

    //public class ThemeDto
    //{
    //    public string Theme { get; set; } = "light";
    //}
}