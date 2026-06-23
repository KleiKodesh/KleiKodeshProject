# sources — CulmusOOoNakdan Reference

This folder contains the unpacked [CulmusOOoNakdan](https://sourceforge.net/projects/culmus/files/language_tools/) LibreOffice/OpenOffice extension, used as a reference when implementing the Nakdan library's vowelization logic.

## Contents

| Path | Description |
|------|-------------|
| `CulmusOOoNakdan.20141029.oxt` | Original extension package (ZIP format) |
| `CulmusOOoNakdan.20141029.zip` | Same archive renamed for extraction |
| `nakdan_extracted/` | Unpacked `.oxt` contents — `CulmusOOoNakdan.uno.jar`, `description.xml`, license files, `CHANGES` |
| `nakdan_jar_extracted/` | Unpacked `.jar` contents — Java class files and `nakdan.txt` dictionary (8,555 entries) |
| `com.niqqud-dicta.newplugin/` | Additional reference material |

## Key Reference Files

- **`CulmusOOoNakdan.uno.jar`** — Java implementation of the Culmus Nakdan engine (prefix handling rules, lexicographic lookup)
- **`nakdan.txt`** — Dictionary file mapping unvowelized word forms to their vowelized equivalents with grammatical metadata (gender, number, construct state)
- **`CHANGES`** — Version history of the Culmus Nakdan extension

The Culmus project is GPL v2 licensed.
