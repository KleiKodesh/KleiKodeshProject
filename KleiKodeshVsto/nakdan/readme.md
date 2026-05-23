# HebrewNakdan — VSTO Add-in Helper

Two files, drop both into your VSTO project.

---

## Files

| File | Purpose |
|------|---------|
| `HebrewNakdan.cs` | Core engine — Dicta API calls, token stream, OOXML parsing. Never touch this. |
| `NakdanApi.cs`    | Simple wrapper — all Word interop. Wire your UI to this. |

---

## Setup

### 1. Add to your VSTO project
Copy both `.cs` files into your project. Make sure the namespace matches or adjust to yours.

### 2. NuGet — no extra packages needed
- `System.Text.Json` — built into .NET 6+. For .NET Framework 4.x add the Microsoft `System.Text.Json` NuGet package.
- Everything else (`System.Xml.Linq`, `System.Net.Http`) is inbox.

### 3. Create the API instance (once, in ThisAddIn.cs)
```csharp
public NakdanApi Nakdan;

private void ThisAddIn_Startup(object sender, EventArgs e)
{
    Nakdan = new NakdanApi(this.Application);
}
```

---

## Wiring Buttons

```csharp
// Button: vowelize entire document
btnAll.Click += (s, e) => Globals.ThisAddIn.Nakdan.RunSafe(
    Globals.ThisAddIn.Nakdan.VowelizeDocumentAsync);

// Button: vowelize current selection
btnSelection.Click += (s, e) => Globals.ThisAddIn.Nakdan.RunSafe(
    Globals.ThisAddIn.Nakdan.VowelizeSelectionAsync);

// Button: vowelize all footnotes
btnFootnotes.Click += (s, e) => Globals.ThisAddIn.Nakdan.RunSafe(
    Globals.ThisAddIn.Nakdan.VowelizeFootnotesAsync);
```

`RunSafe` handles:
- Background thread (won't freeze Word)
- Busy cursor (hourglass while running)
- Error messages in Hebrew

---

## Genre Selection

```csharp
// From a ComboBox, radio button, etc.
Globals.ThisAddIn.Nakdan.SetGenre(DictaGenre.Modern);    // default
Globals.ThisAddIn.Nakdan.SetGenre(DictaGenre.Poetry);
Globals.ThisAddIn.Nakdan.SetGenre(DictaGenre.Bible);
Globals.ThisAddIn.Nakdan.SetGenre(DictaGenre.Rabbinic);
```

---

## Ignored Styles

Paragraphs whose Word style name is in the ignore list are skipped entirely.
Style names can be Hebrew or English, case-insensitive.

```csharp
// Set the full list at once (e.g. from a settings UI)
Globals.ThisAddIn.Nakdan.SetIgnoredStyles("כותרת 1", "כותרת 2", "Heading 1");

// Add / remove one at a time (e.g. from a checkbox list)
Globals.ThisAddIn.Nakdan.AddIgnoredStyle("הדגשה");
Globals.ThisAddIn.Nakdan.RemoveIgnoredStyle("כותרת 1");

// Clear all (vowelize everything)
Globals.ThisAddIn.Nakdan.ClearIgnoredStyles();
```

### Finding the internal style name
Word sometimes stores styles under their English internal ID even in Hebrew documents.
To find the exact string to use, run this once in the Immediate Window or a test button:

```csharp
foreach (Word.Style s in Globals.ThisAddIn.Application.ActiveDocument.Styles)
    System.Diagnostics.Debug.WriteLine(s.NameLocal + " => " + s.Name);
```

Use the `NameLocal` value (Hebrew) in `SetIgnoredStyles`.

---

## How It Works (brief)

1. Reads OOXML via `WordOpenXML` — one call, no per-run interop.
2. Builds a **token stream**: one token per base character, tagged with its run index and position.
3. Strips existing nikkud before sending to Dicta (clean input).
4. Chunks text at word boundaries up to Dicta's 5000-char limit.
5. Calls all chunks **in parallel** via `Task.WhenAll`.
6. Walks the vowelized response: nikkud codepoints attach to the preceding base-letter token.
7. Writes back only the `<w:t>` text — all formatting (`<w:rPr>`) untouched.
