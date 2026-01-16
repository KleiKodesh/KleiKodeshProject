# About Document Template Implementation

## Overview
Changed from WPF FlowDocument window to native Word document template (.dotx) for the About functionality.

## Implementation

### Code Changes

**File**: `KleiKodeshVsto/Ribbon/KeliKodeshRibbon.cs`

Replaced `ShowAboutWindow()` with `OpenAboutDocument()`:
```csharp
private void OpenAboutDocument()
{
    try
    {
        string templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "About.dotx");
        
        if (!File.Exists(templatePath))
        {
            MessageBox.Show($"קובץ אודות לא נמצא:\n{templatePath}", "שגיאה", 
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // Open the template as a new document (not the template itself)
        var doc = Globals.ThisAddIn.Application.Documents.Add(templatePath);
        doc.Activate();
    }
    catch (Exception ex)
    {
        MessageBox.Show($"שגיאה בפתיחת מסמך אודות:\n{ex.Message}", "שגיאה",
            MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
```

### Deleted Files
- `KleiKodeshVsto/Ribbon/AboutWindow.xaml`
- `KleiKodeshVsto/Ribbon/AboutWindow.xaml.cs`

### Project File Updates
**File**: `KleiKodeshVsto/KleiKodeshVsto.csproj`

- Removed XAML Page and Compile entries
- Added About.dotx as Content with CopyToOutputDirectory

### Document Template

**File**: `KleiKodeshVsto/Resources/About.dotx` (to be created)

**Content** (in Hebrew, RTL):

1. **Title**: כלי קודש לוורד (28pt, bold, blue #2563EB)
2. **Subtitle**: ארגז כלים לעורך התורני (20pt, bold)
3. **Description**: Brief explanation of the add-in
4. **Features List**:
   - כזית - ספרייה תורנית
   - חיפוש רגקס בוורד
   - עיצוב תורני
   - דרך האתרים
5. **License**: Free and open source
6. **Links**: GitHub, website, Google Drive
7. **Contact**: kleikodeshproject@gmail.com

**Formatting**:
- Font: Segoe UI or Arial, 14pt
- Line spacing: 1.5
- Alignment: Right (RTL)
- Margins: Normal (2.54 cm)
- Blue accent color: #2563EB

## How It Works

1. User clicks "אודות" button in ribbon
2. Code loads `About.dotx` from Resources folder
3. Word opens template as new document (not the template itself)
4. User can read, edit, save, or close the document
5. Template remains unchanged for future use

## Advantages Over WPF Window

- Native Word experience
- User can edit, save, or print the content
- No external window management
- Consistent with Word's UI paradigm
- Can include Word-specific formatting
- Easier to update content (just edit the .dotx file)

## Creating the Template

See `KleiKodeshVsto/Resources/About-Template-Instructions.md` for detailed instructions on creating the About.dotx file.

