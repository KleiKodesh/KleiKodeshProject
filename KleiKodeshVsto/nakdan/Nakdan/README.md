# Nakdan

.NET class library for Hebrew diacritical mark (Nikkud) processing using the Dicta API.

## Architecture

The library is organized into four functional areas:

### Core/ — Engine

| File | Description |
|------|-------------|
| `NakdanEngine.cs` | Main orchestration — builds token stream, strips existing nikkud, chunks text, calls Dicta API in parallel, writes vowelized text back to OOXML |
| `NakdanWrapper.cs` | High-level API wrapper for VSTO integration (vowelize document, selection, footnotes; genre selection; ignored styles) |
| `DictaApiClient.cs` | HTTP client for the Dicta vowelization API |
| `Token.cs` | Token model — represents a single base character with its position and run index |
| `TokenChunker.cs` | Splits token stream at word boundaries, respecting Dicta's character limit |
| `TokenTextConverter.cs` | Converts between token stream and plain text (with/without nikkud) |
| `OoxmlHelper.cs` | OOXML (WordOpenXML) parsing utilities |
| `RunInfo.cs` | Model for a Word `<w:r>` run with formatting context |
| `RunWriter.cs` | Writes vowelized text back into OOXML, preserving `<w:rPr>` formatting |
| `HebrewTextExtensions.cs` | Hebrew character classification and nikkud manipulation extensions |
| `NakdanCofiguration.cs` | Configuration model (genre, ignored styles, API URL) |

### Helpers/ — Utilities

| File | Description |
|------|-------------|
| `SettingsManager.cs` | Persists user settings (genre, ignored styles) across sessions |
| `VstoHelper.cs` | Word interop helpers (selection, document, footnotes) |

### UI/ — WPF Controls

| File | Description |
|------|-------------|
| `NakdanView.xaml` / `.cs` | Main WPF UserControl for the vowelization task pane |
| `NakdanViewModel.cs` | ViewModel for the vowelization UI |
| `NakdanDictionary.xaml` | Resource dictionary with UI styles and templates |
| `Converters.cs` | XAML value converters |

### WdStyles/ — Word Style Management

| File | Description |
|------|-------------|
| `DocumentStyle.cs` | Model for a Word paragraph style |
| `DocumentStyleProvider.cs` | Provides the list of styles used in a document |
| `StyleExtractor.cs` | Extracts style information from Word documents |
| `StyleItem.cs` | ViewModel item for a selectable style |
| `StyleNameResolver.cs` | Resolves style names (Hebrew/English, case-insensitive) |

## How It Works

1. Reads OOXML via `WordOpenXML` — one call, no per-run interop.
2. Builds a **token stream**: one token per base character, tagged with run index and position.
3. Strips existing nikkud before sending to Dicta (clean input).
4. Chunks text at word boundaries up to Dicta's 5000-char limit.
5. Calls all chunks **in parallel** via `Task.WhenAll`.
6. Walks the vowelized response, attaching nikkud codepoints to preceding base-letter tokens.
7. Writes back only the `<w:t>` text — all formatting (`<w:rPr>`) is untouched.

## Dependencies

- **System.Text.Json**
- **System.Net.Http**
- **Microsoft.Web.WebView2** (for UI)
