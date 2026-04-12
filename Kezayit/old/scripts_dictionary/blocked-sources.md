# Blocked / Unavailable Data Sources

Sources that were investigated but could not be accessed due to NetFree filtering
or dead links. Worth retrying in the future from an unfiltered network.

---

## ראשי תיבות from יד מאיר

**URL:** `https://www.yadmeir.co.il/?CategoryID=342`  
**Status:** Site always redirects to homepage — CategoryID parameter is ignored.
The actual data was shared as a private DOCX file on the Mitmachim forum.

**Dead link:** `https://mitmachim.top/assets/uploads/files/1719990267812-92b106bf-f477-4ef6-a2d0-88d2ac4d27a3-ראשי-תיבות-מאתר-יד-מאיר.docx`  
Returns 404.

**What it contains:** A large collection of ראשי תיבות (abbreviations) from the
יד מאיר website. Would supplement the ~1,359 abbreviation entries we already have
from the ToratEmet FinalDictionary.txt.

---

## ראשי תיבות from daat.ac.il

**URL:** `https://www.daat.ac.il/daat/vl/tohen.asp?id=9`  
**Status:** HTTP 500 (server error). Mentioned in the Mitmachim thread as a
comprehensive abbreviations resource with links to other sources.

---

## Hebrew Wiktionary dump (dumps.wikimedia.org)

**URL:** `https://dumps.wikimedia.org/hewiktionary/latest/hewiktionary-latest-pages-articles.xml.bz2`  
**Status:** HTTP 418 — blocked by NetFree.  
**Size:** ~14.3 MB compressed.

**Workaround used:** The `Special:Export` endpoint (`he.wiktionary.org/wiki/מיוחד:ייצוא`)
was accessible and used instead. The full import completed successfully via
`import-wiktionary-export.cjs` (18,809 entries).

**If you want the raw dump** (for offline processing or re-import without API calls):
download from an unfiltered network and place at `scripts/dictionary/hewiktionary-dump.xml.bz2`.
Then write a parser using the `bz2` Node module to extract pages from the XML.

---

## archive.org Wiktionary mirror

**URL:** `https://archive.org/download/hewiktionary-20210220/hewiktionary-20210220-pages-articles.xml.bz2`  
**Status:** HTTP 418 — blocked by NetFree.

---

## Hebrew Wiktionary API (allpages)

**URL:** `https://he.wiktionary.org/w/api.php?action=query&list=allpages&...`  
**Status:** Intermittently blocked (HTTP 418). Works sometimes, blocked other times.
The `Special:Export` POST endpoint was more reliably accessible.

---

## Bar Ilan Responsa Project

Mentioned in the Mitmachim thread as a potential source for synonyms and word data.
Proprietary — not freely available. The ToratEmet developer explicitly said he
only wants free/open sources.

---

## Anki Hebrew Wiktionary plugin

**URL:** `https://ankiweb.net/shared/info/2087444887`  
**Status:** Not investigated. Contains a Python script that extracts data from
Hebrew Wiktionary. Could be useful as an alternative extraction method.

---

## Word (Microsoft) built-in thesaurus

Mentioned in the Mitmachim thread as a potential source for Hebrew synonyms.
Proprietary — not extractable without reverse engineering.
