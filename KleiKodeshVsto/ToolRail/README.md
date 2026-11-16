# ToolRail — one side pane for all Klei Kodesh tools

Every tool opens in one task pane per Word window, titled "כלי קודש". With one tool open, the
pane is just that tool. From the second tool onwards, a 50px icon rail appears on the pane's
outer edge (away from the document) and lists the tools that are open, in the order they were
opened, to switch between them in one click. The user can bring back the old behaviour, one pane
per tool, with the Settings checkbox "כל כלי בחלונית צד נפרדת" (`ToolRail/SeparatePanes`).

Design, rejected alternatives and the in-or-out-of-Klei-Kodesh question: `DESIGN.he.md`.

## Files

- `RailPane.cs`: the entry point for the rest of the add-in. `Show(id)` finds or creates this
  window's pane and shows the tool, or runs a program or macro item without creating a pane.
  `UsableControls<T>(id)` finds live tools for features that read from a pane, such as compare.
  `Enabled` reads the settings checkbox.
- `ToolRailHost.cs`: the pane's control. It holds the open tools (one slot each), the rail,
  the pane width, per-tool pop-out, closing a tool, duplicate and close-time cleanup.
- `ToolCatalog.cs`: every tool the pane can open: the built-in ones (ids equal to the ribbon
  control ids) and the user's external items. The rail never shows this whole list.
- `RailButton.cs`: one owner-drawn rail icon.
- `ExternalTools.cs`: reads `tool-rail.json`, creates it for editing, and starts or snaps a
  program or runs a macro.
- `RailWebView.cs`: a `url` item's page.

## Behaviour

- **Only open tools, only from two.** The rail lists the tools opened in this pane and is hidden
  while there is just one. Settings is an ordinary tool: it is on the rail once opened. External
  items are opened from the ribbon menu "כלים חיצוניים" and then behave like any open tool. A
  program or macro item runs and never takes a place on the rail. Right-clicking an icon offers
  "close"; with one tool left the rail goes, and closing the last one hides the pane.
- **Kept alive.** A tool is built the first time it is opened and then only hidden and shown,
  inside one suspended layout per switch. It is never rebuilt on a switch. `AppViewer` and
  `KiwixWebview` cache their WebView2 environment in a static, and a rebuilt tool would lose its
  tabs and state. Measured outside Word with the real `AppViewer` (2026-11-16): hiding and
  showing it cost 6-14 ms for the show and 13-36 ms to its first script answer, and its WebView2
  did not report itself suspended. A switch itself is cheap.
- **One width.** The pane has a single width for all tools (`ToolRail/PaneWidth`): the first
  tool's default until the user drags the pane's edge. Each tool used to apply its own width,
  which moved the document's edge on every switch. The dock position is `ToolRail/DockPosition`.
- **No collapse.** Clicking the open tool's icon does nothing. The fold-to-rail mode was dropped:
  it was another width jump, and Word's minimum pane width was never measured.
- **Look.** Each icon sits on its own light tile at 26px, pre-scaled once in high quality, on
  a rail strip slightly darker than the pane. The tools' artwork is thin line drawing made for a
  white background (18-53% of the pixels drawn); at 20px straight on the theme colour it was hard
  to see, and in dark Office themes it vanished. The open tool gets a blue border and a blue bar
  on the rail's inner edge. A tool in a floating window gets a dot.
- **Height.** Icons are laid out from the top. What does not fit in the height that is actually
  on screen (the control's height cut down by every ancestor window, `VisibleHeight`) goes behind
  a "more" button. Nothing is anchored to the bottom edge: a Settings icon placed there never
  appeared, because Word's pane container can be shorter than the control it holds.
- **Pop-out.** Only the current tool goes into a floating window (`TaskPanePopOut` in slot mode).
  The content area then shows a "return to the pane" button, and the tool's icon gets a dot.
  Clicking the icon brings the window forward. Closing the window puts the tool back.
- **Duplicate side pane.** Opens the current tool in a separate wide pane titled `@<name>`, the
  same kind of pane the separate-pane mode uses. This is the way to see two tools at once.
- **Context-menu search, open link, compare.** They reach the `AppViewer` in the pane
  (`AddinCommands.ShowKitveiHakodesh`). Compare also reads viewers in duplicated or separate panes.
- **Focus toggle macro** (`KleiKodesh_TogglePaneFocus`). It moves the keyboard into the open
  tool, or that tool's floating window, and back to the document.
- **Document close.** Kitvei Hakodesh writes out its pending state, every floating window
  closes, and the pane and all its tools are disposed.

## External items: `%APPDATA%\KleiKodesh\tool-rail.json`

The ribbon's "כלים חיצוניים" menu lists the items and ends with "ערוך את רשימת הכלים
החיצוניים", which opens the file in Notepad. If the file does not exist yet, that action first
creates it with commented-out examples. The menu reads the file every time it opens. Comments
and trailing commas are allowed. Environment variables (`%USERPROFILE%`) work in paths. An item
with an unknown type or a missing field is skipped. A file that cannot be parsed is reported
once per version of the file.

```json
{
  "tools": [
    { "id": "tamlil", "title": "תמליל", "type": "program",
      "path": "%LOCALAPPDATA%\\Programs\\Tamlil\\Tamlil.exe", "arguments": "", "snap": true },
    { "id": "sefaria", "title": "ספריא", "type": "url",
      "url": "https://www.sefaria.org.il", "icon": "C:\\icons\\sefaria.png" },
    { "id": "mymacro", "title": "המאקרו שלי", "type": "macro", "macro": "MyMacro",
      "tip": "מריץ את המאקרו שלי" }
  ]
}
```

| Field | Types | Meaning |
|---|---|---|
| `id` | all | Unique name. It is stored internally as `ext:<id>`, so it can never clash with a built-in tool. Defaults to `title`. |
| `title` | all | Menu and tooltip text, and the letter shown when there is no icon. |
| `tip` | all | Second tooltip line. |
| `icon` | all | `.png` / `.ico` path. A program without one uses the icon of its `.exe`. |
| `url`, `width` | url | `http`, `https` or `file` page. `width` is the pane width when this is the first tool opened. |
| `path`, `arguments`, `snap` | program | The program to start. `snap` (default `true`) places it at the screen edge on the pane's side, with Word taking the rest of the screen. A program that is already running is brought forward instead of started again. |
| `macro` | macro | The name `Application.Run` takes. |

What stays impossible: the pane cannot hold another add-in's task pane, and it does not embed
another program's window. Embedding would join the program's input queue to Word's, so a hang
there would freeze Word. See `DESIGN.he.md`.

Open question: the repo's rule "files live beside the app" (root `CLAUDE.md`) is written for the
portable Kitvei Hakodesh app. `tool-rail.json` (in `%APPDATA%`) and the `url` pages' browser data
(`%LOCALAPPDATA%\KleiKodesh\rail-web`) belong to the installed add-in, but whether the rule covers
them too has not been decided.
