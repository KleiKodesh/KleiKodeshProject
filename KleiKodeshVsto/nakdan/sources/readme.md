is the culmus nakdad open sorce and can you create a port of it in c# for generic usage

Kiro
Let me look up Culmus Nakdad first.


Web search: Culmus Nakdad Hebrew vowelization software open source license
10 results

Web search: Culmus Nakdad Hebrew nikud diacritics tool source code
10 results

Web search: Culmus Nakdad Hebrew vowelization program what is it
10 results
Web search failed
Error: Tool returned no results (code: -32602)
Est. Credits Used: 0.3(?)
Elapsed time: 18s


Checkpoint
Restore
culmus nakdan

Kiro
Let me search specifically for Culmus Nakdan.


Web search: Culmus Nakdan Hebrew font diacritics tool sourceforge
10 results

Web search: "culmus nakdan" site:sourceforge.net OR site:github.com
3 results
Fetched: https://sourceforge.net/projects/culmus/files/language_tools/
38 B • 713ms
Fetched: https://culmus.sourceforge.io/
38 B • 351ms

Web search: CulmusOOoNakdan OpenOffice extension Hebrew nikud what does it do algorithm
10 results
Fetched: https://extensions.libreoffice.org/en/extensions/show/nakdan-by-culmus
38 B • 562ms

Web search: Culmus Nakdan LibreOffice extension source code GPL Wiktionary Hebrew diacritics
10 results
Now I have a clear picture. Here's what I found:

Culmus Nakdan is a semi-automatic Hebrew diacritics (nikud/vowelization) extension for LibreOffice/OpenOffice, developed under the Culmus project. Key facts:

Yes, it is open source — the Culmus project is licensed under GPL v2, and the Nakdan extension follows the same license. It's hosted on SourceForge.
It works by using hspell (a Hebrew spell-checker engine) as its linguistic backend and a Wiktionary-based word database to look up correct nikud for words.
It's a dictionary lookup approach — not ML-based. It looks up words and returns their known diacritized forms.
It's quite limited: the early versions only handled nouns with preposition prefixes (אותיות שימוש), not pronouns or verb conjugations.
Porting it to C# for generic usage is very feasible. Here's what such a port would look like:

The core logic is:

Tokenize Hebrew text into words
Strip common prefixes (ב, כ, ל, מ, ו, ש, ה)
Look up the base word in a nikud dictionary (originally from Wiktionary/hspell)
Reattach prefixes with their voweled forms
Return the diacritized text
I can build a clean C# library that implements this. The dictionary data itself is the key asset — it comes from the open-source Wiktionary Hebrew data. Do you want me to:

Option A — Build a full C# library with the dictionary lookup engine + a bundled sample dictionary (from open Wiktionary data), ready to use as a NuGet-style class library?

Option B — Build just the engine/framework in C# where you plug in your own dictionary source (file, DB, API)?

Option C — Both: a clean engine with an interface, plus a default file-based dictionary implementation?

Also — do you want this to be a simple synchronous library, or async-friendly (e.g., for use with web APIs)?

Est. Credits Used: 0.65
Elapsed time: 40s


Checkpoint
Restore
whats the oxt file is taht useful?

Kiro
An .oxt file is an OpenOffice/LibreOffice extension package — it's basically a ZIP file with a different extension. It contains the actual source code, data files, and metadata of the extension.

For our purposes, it's very useful because we can unzip it and see exactly:

The nikud dictionary data (the word → voweled form mappings)
The actual logic/algorithm used
Any linguistic rules for prefix handling
Let me grab it and inspect it.


Web search: CulmusOOoNakdan.20141029.oxt download direct link culmus sourceforge
10 results
Command

curl -L "https://sourceforge.net/projects/culmus/files/language_tools/nakdan/20141029/CulmusOOoNakdan.20141029.oxt/download" -o CulmusOOoNakdan.oxt --max-time 30 -w "%{http_code}"
Est. Credits Used: 0.2(?)
Elapsed time: 25s


Checkpoint
Restore
it in the project folder already

Kiro
Let me find it.

Searched workspace
Find the oxt file in the workspace
Got it. Since .oxt is just a ZIP, let me unzip and inspect the contents.

Command
c:\Users\sh…\New folder

Expand-Archive -Path "CulmusOOoNakdan.20141029.oxt" -DestinationPath "nakdan_extracted" -Force
PS C:\Users\shemesh\Downloads\New folder> Expand-Archive -Path "CulmusOOoNakdan.
20141029.oxt" -DestinationPath "nakdan_extracted" -Force

 Expand-Archive
    The archive file 'nakdan_extracted' expansion is in progress...
    [                                                                    ]      




Expand-Archive : .oxt is not a supported archive file format. .zip is the only  
supported archive file format.
At line:1 char:1
+ Expand-Archive -Path "CulmusOOoNakdan.20141029.oxt" -DestinationPath  ...     
+ ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
    + CategoryInfo          : InvalidArgument: (.oxt:String) [Expand-Archive],  
    IOException
    + FullyQualifiedErrorId : NotSupportedArchiveFileExtension,Expand-Archive   
 
PS C:\Users\shemesh\Downloads\New folder
Command
c:\Users\sh…\New folder

Copy-Item "CulmusOOoNakdan.20141029.oxt" "CulmusOOoNakdan.20141029.zip"; Expand-Archive -Path "CulmusOOoNakdan.20141029.zip" -DestinationPath "nakdan_extracted" -Force
C
PS C:\Users\shemesh\Downloads\New folder> Copy-Ite
m "CulmusOOoNakdan.20141029.oxt" "CulmusOOoNakdan.
20141029.zip"; Expand-Archive -Path "CulmusOOoNakd
an.20141029.zip" -DestinationPath "nakdan_extracte
PS C:\Users\shemesh\Downloads\New folder> Copy-Ite
m "CulmusOOoNakdan.20141029.oxt" "CulmusOOoNakdan.
20141029.zip"; Expand-Archive -Path "CulmusOOoNakd
an.20141029.zip" -DestinationPath "nakdan_extracte
d" -Force

 Expand-Archive
    The archive file 'nakdan_extracted' expansion 
    is in progress...
    [                                      ]      


PS C:\Users\shemesh\Downloads\New folder> Copy-Ite
m "CulmusOOoNakdan.20141029.oxt" "CulmusOOoNakdan.
20141029.zip"; Expand-Archive -Path "CulmusOOoNakd
an.20141029.zip" -DestinationPath "nakdan_extracte
d" -Force

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\CulmusOOoNakdan.20141029.zip' expans
    ion is in progress...
    [oooooooooooooooo                      ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\CulmusOOoNakdan.20141029.zip' expans
    ion is in progress...
    [ooooooooooooooooooooo                 ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\CulmusOOoNakdan.20141029.zip' expans
    ion is in progress...
    [oooooooooooooooooooooooooooooooo      ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\CulmusOOoNakdan.20141029.zip' expans
    ion is in progress...
    [oooooooooooooooooooooooooooooooooooooo]      

PS C:\Users\shemesh\Downloads\New folder> Copy-Ite
m "CulmusOOoNakdan.20141029.oxt" "CulmusOOoNakdan.
20141029.zip"; Expand-Archive -Path "CulmusOOoNakd
an.20141029.zip" -DestinationPath "nakdan_extracte
d" -Force
PS C:\Users\shemesh\Downloads\New folder
Searched workspace
See what was extracted from the oxt file
The main logic is in the .jar file. Let me inspect it — it's also a ZIP (Java archive).

Command
c:\Users\sh…\New folder

Copy-Item "nakdan_extracted\CulmusOOoNakdan.uno.jar" "nakdan_extracted\CulmusOOoNakdan.uno.jar.zip"; Expand-Archive -Path "nakdan_extracted\CulmusOOoNakdan.uno.jar.zip" -DestinationPath "nakdan_jar_extracted" -Force
CCopyCopy-
PS C:\Users\shemesh\Downloads\New folder> Copy-Ite
m "
PS C:\Users\shemesh\Downloads\New folder> Copy-Ite
m "CulmusOOoNakdan.20141029.oxt" "CulmusOOoNakdan.
20141029.zip"; Expand-Archive -Path "CulmusOOoNakd
an.20141029.zip" -DestinationPath "nakdan_extracte
d" -Force
PS C:\Users\shemesh\Downloads\New folder> Copy-Ite
m "nakdan_extracted\CulmusOOoNakdan.uno.jar" "nakd
an_extracted\CulmusOOoNakdan.uno.jar.zip"; Expand-
Archive -Path "nakdan_extracted\CulmusOOoNakdan.un
o.jar.zip" -DestinationPath "nakdan_jar_extracted"
 -Force

 Expand-Archive
    The archive file 'nakdan_jar_extracted' expans
    ion is in progress...
    [                                      ]      
m "CulmusOOoNakdan.20141029.oxt" "CulmusOOoNakdan.
20141029.zip"; Expand-Archive -Path "CulmusOOoNakd
an.20141029.zip" -DestinationPath "nakdan_extracte
d" -Force
PS C:\Users\shemesh\Downloads\New folder> Copy-Ite
m "nakdan_extracted\CulmusOOoNakdan.uno.jar" "nakd
 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [oo                                    ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [ooo                                   ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [ooooo                                 ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [ooooooo                               ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [oooooooo                              ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [ooooooooo                             ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [oooooooooo                            ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [ooooooooooo                           ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [ooooooooooo                           ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [oooooooooooo                          ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [ooooooooooooo                         ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [oooooooooooooo                        ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [ooooooooooooooo                       ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [ooooooooooooooo                       ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [oooooooooooooooo                      ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [ooooooooooooooooo                     ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [oooooooooooooooooo                    ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [ooooooooooooooooooo                   ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [ooooooooooooooooooo                   ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [oooooooooooooooooooo                  ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [oooooooooooooooooooooo                ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [oooooooooooooooooooooo                ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [ooooooooooooooooooooooo               ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [ooooooooooooooooooooooooo             ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [oooooooooooooooooooooooooo            ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [ooooooooooooooooooooooooooo           ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [oooooooooooooooooooooooooooo          ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [ooooooooooooooooooooooooooooo         ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [oooooooooooooooooooooooooooooo        ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [oooooooooooooooooooooooooooooo        ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [oooooooooooooooooooooooooooooooo      ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [ooooooooooooooooooooooooooooooooo     ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [oooooooooooooooooooooooooooooooooo    ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [ooooooooooooooooooooooooooooooooooo   ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [oooooooooooooooooooooooooooooooooooo  ]      

 Expand-Archive
    The archive file 'C:\Users\shemesh\Downloads\N
    ew folder\nakdan_extracted\CulmusOOoNakdan.uno
    .jar.zip' expansion is in progress...
    [ooooooooooooooooooooooooooooooooooooo ]      
m "CulmusOOoNakdan.20141029.oxt" "CulmusOOoNakdan.
20141029.zip"; Expand-Archive -Path "CulmusOOoNakd
an.20141029.zip" -DestinationPath "nakdan_extracte
d" -Force
PS C:\Users\shemesh\Downloads\New folder> Copy-Ite
m "nakdan_extracted\CulmusOOoNakdan.uno.jar" "nakd
an_extracted\CulmusOOoNakdan.uno.jar.zip"; Expand-
Archive -Path "nakdan_extracted\CulmusOOoNakdan.un
o.jar.zip" -DestinationPath "nakdan_jar_extracted"
 -Force
PS C:\Users\shemesh\Downloads\New folder
Searched workspace
See what was extracted from the JAR file
Excellent — this is very revealing. Now let me read the dictionary data and decompile the key classes to understand the logic.

Read file(s)

nakdan.txt 1 - 51
Now I have everything I need. Let me also check a few more lines to understand the full format, then look at the class names to understand the logic.

Read file(s)

nakdan.txt 1 - 6
Command
c:\Users\sh…\New folder

Get-Content "nakdan_jar_extracted\org\culmus\nakdan\nakdan.txt" | Measure-Object -Line
GGet-Cont
ent "nakdan_jar_extracted\org\culmus\nakdan\nakdan
PS C:\Users\shemesh\Downloads\New folder> Get-Cont
ent "nakdan_jar_extracted\org\culmus\nakdan\nakdan
.txt" | Measure-Object -L20141029.zip"; Expand-Archive -Path "CulmusOOoNakd
an.20141029.zip" -DestinationPath "nakdan_extracte
d" -Force
PS C:\Users\shemesh\Downloads\New folder> Copy-Ite
m "nakdan_extracted\CulmusOOoNakdan.uno.jar" "nakd
an_extracted\CulmusOOoNakdan.uno.jar.zip"; Expand-
Archive -Path "nakdan_extracted\CulmusOOoNakdan.un
o.jar.zip" -DestinationPath "nakdan_jar_extracted"
 -Force
PS C:\Users\shemesh\Downloads\New folder> Get-Cont
ent "nakdan_jar_extracted\org\culmus\nakdan\nakdan
.txt" | Measure-Object -Line

Lines Words Characters Property
----- ----- ---------- --------
 8555


PS C:\Users\shemesh\Downloads\New folder
Now I have a complete picture. Here's what's inside the .oxt:

Structure of the dictionary (nakdan.txt) — 8,555 entries:

unvoweled|type|definition|singular|plural|construct-singular|construct-plural
Example:

מלון|nm0|מקום המשמש ללינה...|מָלוֹן|מְלוֹנוֹת|מְלוֹן|מְלוֹנוֹת
The Java classes reveal the full engine:

CulmusNakdan — main engine, lookup logic
DictionaryDataReader — parses nakdan.txt
LexicalItem — represents a word with gender, number, category, stress
NikudRule / NikudRuleFactory — rules for applying nikud
BakalHaShimush — handles ב/כ/ל prefix letters (prepositions)
MemHaShimush — handles מ prefix (from)
VavHaHibur — handles ו prefix (and)
HeHaYedia — handles ה prefix (the definite article)
ShinHaZika — handles ש prefix (that/which)
HSplit / HSpellRunner — hspell integration for morphological splitting
So the .oxt is extremely useful — it gives us:

The full 8,555-word nikud dictionary (ready to use as-is)
The exact prefix-handling rules (ב, כ, ל, מ, ו, ה, ש)
The data model (gender, number, construct state)
Want me to now build the C# port using this real data and logic?

Est. Credits Used: 1.01
Elapsed time: 1m 48s


Checkpoint
Restore
yes

Kiro
Let me first read more of the dictionary and the CHANGES file to understand all the data patterns before building.

Read file(s)

CHANGES
Command
c:\Users\sh…\New folder

Get-Content "nakdan_jar_extracted\org\culmus\nakdan\nakdan.txt" | Select-Object -First 100
GGeGet-Cont
ent "nakdan_jar_extracted\org\culmus\nakdan\nakdan
PS C:\Users\shemesh\Downloads\New folder> Get-Cont
ent "nakdan_jar_extracted\org\culmus\nakdan\nakdan
.txt" | Select-Object -FirstPS C:\Users\shemesh\Downloads\New folder> Get-Cont
ent "nakdan_jar_extracted\org\culmus\nakdan\nakdan
.txt" | Measure-Object -Line

Lines Words Characters Property
----- ----- ---------- --------
 8555


PS C:\Users\shemesh\Downloads\New folder> Get-Cont
ent "nakdan_jar_extracted\org\culmus\nakdan\nakdan
.txt" | Select-Object -First 100
׳׳׳•׳|nm0|׳׳§׳•׳ ׳”׳׳©׳׳© ׳׳׳™׳ ׳” ׳–׳׳ 
׳™׳×, ׳׳׳•׳¨׳—׳™׳ ׳•׳׳ ׳•׳₪׳©׳™׳|׳ײ¸׳׳•ײ¹׳
|׳ײ°׳׳•ײ¹׳ ׳•ײ¹׳×|׳ײ°׳׳•ײ¹׳|׳ײ°׳׳•ײ¹׳ ׳•ײ¹׳
×
׳׳׳•׳|nm0|׳₪׳¨׳™ ׳ײ´׳§׳©׳” ׳׳×׳•׳§ ׳•׳¢׳¡׳™׳¡
׳™|׳ײµ׳׳•ײ¹׳|׳ײµ׳׳•ײ¹׳ ײ´׳™׳||
׳׳™׳׳•׳|nm0|׳׳•׳¡׳£ ׳׳™׳׳™׳ ׳׳‘׳•׳׳¨׳•׳× 
׳‘׳©׳₪׳” ׳׳¡׳•׳™׳׳×, ׳׳¨׳•׳‘ ׳¢׳¨׳•ײ¼׳ ׳›׳¡׳₪׳
¨|׳ײ´׳ײ¼׳•ײ¹׳|׳ײ´׳ײ¼׳•ײ¹׳ ײ´׳™׳|׳ײ´׳ײ¼׳•ײ¹
׳|׳ײ´׳ײ¼׳•ײ¹׳ ײµ׳™
׳›׳“׳•׳¨׳’׳|nm0|׳׳©׳—׳§ ׳›׳“׳•׳¨ ׳”׳׳©׳•׳—׳§ - 
׳‘׳׳׳¦׳¢׳•׳× ׳‘׳¢׳™׳˜׳•׳× ׳‘׳›׳“׳•׳¨ - ׳¢׳ײ¾׳™׳
“׳™ ׳©׳×׳™ ׳§׳‘׳•׳¦׳•׳× ׳”׳׳×׳—׳¨׳•׳× ׳–׳• ׳‘׳–׳•
|׳›ײ·ײ¼׳“ײ¼׳•ײ¼׳¨ײ¶׳’ײ¶׳|||
׳‘׳¨׳§|nm0|׳×׳•׳₪׳¢׳× ׳˜׳‘׳¢, ׳‘׳¦׳•׳¨׳× ׳׳•׳¨ ׳—
׳©׳׳׳™ ׳”׳—׳•׳׳£ ׳‘׳©׳׳™׳, ׳”׳ ׳•׳¦׳¨׳× ׳›׳×׳
•׳¦׳׳” ׳׳”׳×׳ ׳’׳©׳•׳× ׳©׳ ׳¢׳ ׳ ׳™׳ ׳‘׳¢׳׳™ 
׳׳˜׳¢׳ ׳—׳©׳׳׳™|׳‘ײ¸ײ¼׳¨ײ¸׳§|׳‘ײ°ײ¼׳¨ײ¸׳§ײ´׳™׳
|׳‘ײ°ײ¼׳¨ײ·׳§|׳‘ײ´ײ¼׳¨ײ°׳§ײµ׳™
׳׳˜׳•׳¡|nm0|׳›׳׳™ ׳×׳—׳‘׳•׳¨׳” ׳׳•׳•׳™׳¨׳™|׳ײ¸
׳˜׳•ײ¹׳¡|׳ײ°׳˜׳•ײ¹׳¡ײ´׳™׳|׳ײ°׳˜׳•ײ¹׳¡|׳ײ°׳˜׳•ײ
¹׳¡ײµ׳™
׳׳•׳׳™׳’׳¨׳|nm0|׳׳—׳“ ׳׳›׳׳” ׳©׳׳™׳˜׳™׳ ׳×
׳§׳™׳₪׳™׳ ׳‘׳׳©׳˜׳¨ ׳׳•׳׳™׳’׳¨׳›׳™|׳׳•ײ¹׳ײ´׳
™׳’ײ·׳¨ײ°׳|׳׳•ײ¹׳ײ´׳™׳’ײ·׳¨ײ°׳›ײ´׳™׳||        
׳׳™׳¨׳™׳˜׳•׳₪׳™|nm0|׳™׳¦׳•׳¨ ׳©׳×׳₪׳•׳¦׳×׳• ׳”׳’׳
™׳׳•׳’׳¨׳₪׳™׳× ׳”׳™׳ ׳ ׳¨׳—׳‘׳×|׳ײµ׳™׳¨ײ´׳™׳˜׳•
ײ¹׳₪ײ´ײ¼׳™|||
׳׳¨׳©׳×׳×|nf0|׳¨׳©׳× ׳׳—׳©׳‘׳™׳ ׳’׳׳•׳‘׳׳™׳×,
 ׳”׳׳—׳‘׳¨׳× ׳¨׳©׳×׳•׳× ׳׳—׳©׳‘׳™׳ ׳©׳•׳ ׳•׳×, 
׳•׳׳§׳©׳¨׳× ׳‘׳™׳ ׳׳™׳׳™׳•׳ ׳™ ׳׳—׳©׳‘׳™׳ ׳‘
׳›׳ ׳”׳¢׳•׳׳|׳ײ´׳¨ײ°׳©ײ¶׳׳×ײ¶׳×|||
׳׳•׳˜׳•׳˜׳¨׳•׳£|nm0|[׳‘׳™׳•׳׳•׳’׳™׳”|׳׳§׳•׳׳•׳
’׳™׳”] ׳™׳¦׳¨׳|׳׳•ײ¹׳˜׳•ײ¹׳˜ײ°׳¨׳•ײ¹׳£|׳׳•ײ¹׳˜׳
•ײ¹׳˜ײ°׳¨׳•ײ¹׳₪ײ´׳™׳||
׳׳•׳˜׳׳§׳•׳׳•׳’׳™׳”|n-0|׳×׳—׳•׳ ׳‘׳׳§׳•׳׳•׳’
׳™׳” ׳”׳—׳•׳§׳¨ ׳׳× ׳™׳—׳¡׳™ײ¾׳”׳’׳•׳׳׳™׳ ׳‘׳™
׳ ׳׳•׳¨׳’׳ ׳™׳–׳ ׳׳• ׳׳™׳ ׳׳—׳“ ׳‘׳׳‘׳“ (׳
©׳ ׳‘׳¢׳ ׳—׳™׳™׳ ׳׳• ׳©׳ ׳¦׳׳—) ׳׳¡׳‘׳™׳‘׳×
׳•|׳׳•׳˜׳׳§׳•׳׳•׳’׳™׳”|||
׳׳•׳˜׳•׳₪׳™׳˜|nm0|׳¦׳׳— ׳”׳׳¡׳•׳’׳ ׳׳”׳¨׳›׳™׳
‘ ׳‘׳¢׳¦׳׳• ׳׳× ׳׳–׳•׳ ׳• ׳׳—׳•׳׳¨׳™׳ ׳׳•׳¨
׳’׳ ׳™׳™׳|׳׳•ײ¹׳˜׳•ײ¹׳₪ײ´׳™׳˜|׳׳•ײ¹׳˜׳•ײ¹׳₪ײ´׳™
׳˜ײ´׳™׳||
׳׳•׳¨׳ ׳™׳×׳•׳׳•׳’׳™׳”|nf0|׳×׳—׳•׳ ׳‘׳–׳•׳׳•׳
׳•׳’׳™׳” ׳”׳¢׳•׳¡׳§ ׳‘׳—׳§׳¨ ׳”׳¢׳•׳₪׳•׳×|׳׳•ײ¹׳¨
ײ°׳ ײ´׳™׳×׳•ײ¹׳׳•ײ¹׳’ײ°׳™ײ¸׳”|||
׳׳™׳›׳×׳™׳•׳׳•׳’׳™׳”|nf0|׳¢׳ ׳£ ׳‘׳–׳•׳׳•׳׳•׳’
׳™׳” ׳”׳¢׳•׳¡׳§ ׳‘׳—׳§׳¨ ׳”׳“׳’׳™׳|׳ײ´׳™׳›ײ°׳×ײ°
ײ¼׳™׳•ײ¹׳׳•ײ¹׳’ײ°׳™ײ¸׳”|||
׳׳™׳׳•׳ ׳•׳׳•׳’׳™׳”|nf0|׳—׳§׳¨ ׳׳¢׳¨׳›׳× ׳”׳—׳
™׳¡׳•׳ ׳¢׳ ׳”׳™׳‘׳˜׳™׳” ׳”׳©׳•׳ ׳™׳|׳ײ´׳™׳׳•ײ
¼׳ ׳•ײ¹׳׳•ײ¹׳’ײ°׳™ײ¸׳”|||
׳׳ ׳“׳•׳˜׳¨׳•׳₪׳™|nm0|׳—׳׳§ ׳׳₪׳˜׳¨׳™׳” ׳˜׳₪׳™׳
׳™׳× ׳”׳ ׳™׳–׳•׳ ׳” ׳׳×׳•׳ ׳׳•׳¨׳’׳ ׳™׳–׳ ׳׳
—׳¨|׳ײ¶׳ ײ°׳“ײ¼׳•ײ¹׳˜ײ°׳¨׳•ײ¹׳₪ײ´׳™|||
׳׳ ׳“׳•׳§׳¨׳™׳ ׳•׳׳•׳’׳™׳”|nf0|׳”׳׳“׳¢ ׳”׳¢׳•׳¡
׳§ ׳‘׳—׳§׳¨ ׳”׳׳‘׳ ׳” ׳•׳”׳₪׳¢׳•׳׳” ׳©׳ ׳׳¢׳¨׳
›׳× ׳”׳‘׳׳•׳˜׳•׳× ׳”׳”׳•׳¨׳׳•׳ ׳׳™׳•׳× ׳‘׳’׳•׳£
 ׳”׳׳“׳ ׳•׳”׳”׳•׳¨׳׳•׳ ׳™׳ ׳”׳׳•׳₪׳¨׳©׳™׳ ׳¢
׳ ׳™׳“׳”|׳ײ¶׳ ײ°׳“ײ¼׳•ײ¹׳§ײ°׳¨ײ´׳™׳ ׳•ײ¹׳׳•ײ¹׳’
ײ°׳™ײ¸׳”|||
׳׳ ׳˜׳•׳׳•׳׳•׳’׳™׳”|nf0|׳×׳—׳•׳ ׳‘׳–׳׳•׳׳•׳’
׳™׳” ׳׳©׳¨ ׳¢׳•׳¡׳§ ׳‘׳—׳§׳¨ ׳”׳—׳¨׳§׳™׳|׳ײ¶׳ ײ
°׳˜׳•ײ¹׳׳•ײ¹׳׳•ײ¹׳’ײ°׳™ײ¸׳”|||
׳׳ ׳˜׳•׳׳™׳”|nf0|׳׳׳ ׳•׳× ׳”׳—׳™׳×׳•׳ ׳•׳”׳₪׳
¨׳“׳× ׳׳™׳‘׳¨׳™׳”׳ ׳•׳¨׳§׳׳•׳×׳™׳”׳ ׳©׳ ׳™׳¦׳
•׳¨׳™׳ ׳—׳™׳™׳|׳ײ¸׳ ײ¸׳˜׳•ײ¹׳ײ°׳™ײ¸׳”|||      
׳׳ ׳˜׳™׳‘׳™׳•׳–׳”|nf0|׳׳ ׳˜׳’׳•׳ ׳™׳–׳ ׳‘׳™׳ ׳
©׳ ׳™ ׳™׳¦׳•׳¨׳™׳ ׳׳• ׳™׳•׳×׳¨, ׳©׳׳™׳ ׳• ׳׳׳
₪׳©׳¨ ׳׳”׳ ׳׳”׳×׳§׳™׳™׳ ׳›׳©׳”׳ ׳¡׳׳•׳›׳™׳ 
׳–׳” ׳׳–׳”|׳ײ·׳ ײ°׳˜ײ´׳™׳‘ײ´ײ¼׳™ײ¼׳•ײ¹׳–ײ¸׳”||| 
׳׳₪׳™׳“׳׳™׳•׳׳•׳’׳™׳”|nf0|׳×׳—׳•׳ ׳‘׳¨׳₪׳•׳׳”
 ׳”׳¢׳•׳¡׳§ ׳‘׳—׳§׳¨ ׳”׳׳—׳׳•׳×, ׳×׳₪׳•׳¦׳×׳ ׳•
׳”׳“׳¨׳›׳™׳ ׳׳׳ ׳™׳¢׳×׳ ׳‘׳§׳¨׳‘ ׳׳•׳›׳׳•׳¡׳
™׳•׳×|׳ײ¶׳₪ײ´ײ¼׳™׳“ײ¶׳ײ°׳™׳•ײ¹׳׳•ײ¹׳’ײ°׳™ײ¸׳”||
|
׳׳§׳•׳˜׳•׳|nm0|׳׳–׳•׳¨ ׳”׳’׳‘׳•׳ ׳•׳”׳׳¢׳‘׳¨ 
׳‘׳™׳ ׳©׳×׳™ ׳—׳‘׳¨׳•׳× ׳׳§׳•׳׳•׳’׳™׳•׳× ׳¡׳׳•
׳›׳•׳× ׳׳• ׳‘׳™׳ ׳©׳ ׳™ ׳”׳‘׳™׳˜׳˜׳™׳ ׳¡׳׳•׳›׳
™׳|׳ײ¶׳§׳•ײ¹׳˜׳•ײ¹׳|||
׳׳§׳•׳˜׳•׳₪|nm0|׳׳—׳“ ׳׳¡׳•׳’׳™ ׳”׳”׳‘׳™׳˜׳˜ ׳‘
׳׳–׳•׳¨ ׳›׳׳©׳”׳•|׳ײ¶׳§׳•ײ¹׳˜׳•ײ¹׳₪|||
׳׳§׳•׳˜׳™׳₪|nm0|׳‘׳™׳•׳˜׳™׳₪ ׳©׳”׳×׳₪׳×׳— ׳›׳×׳•׳
¦׳׳” ׳׳”׳×׳׳™׳™׳ ׳•׳× ׳‘׳”׳‘׳™׳˜׳˜ ׳׳¡׳•׳™׳, 
׳׳ ׳‘׳”׳©׳₪׳¢׳× ׳×׳ ׳׳™ ׳”׳§׳¨׳§׳¢ ׳•׳׳ ׳‘׳”׳
©׳₪׳¢׳× ׳”׳׳§׳׳™׳|׳ײµ׳§׳•ײ¹׳˜ײ´׳™׳₪|||        
׳׳§׳•׳׳•׳’׳™׳”|nf0|׳¢׳ ׳£ ׳‘׳‘׳™׳•׳׳•׳’׳™׳” ׳”׳
¢׳•׳¡׳§ ׳‘׳—׳§׳¨ ׳”׳™׳—׳¡׳™׳ ׳©׳‘׳™׳ ׳׳•׳¨׳’׳ ׳
™׳–׳׳™׳ ׳©׳•׳ ׳™׳, ׳•׳©׳‘׳™׳ ׳׳•׳¨׳’׳ ׳™׳–׳׳
™׳ ׳׳‘׳™׳ ׳¡׳‘׳™׳‘׳×׳ ׳”׳₪׳™׳¡׳™׳×|׳ײµ׳§׳•ײ¹׳
׳•ײ¹׳’ײ°׳™ײ¸׳”|||
׳׳§׳•׳₪׳ ׳•׳˜׳™׳₪|nm0|׳₪׳ ׳•׳˜׳™׳₪ ׳”׳׳×׳”׳•׳•׳”
 ׳›׳×׳•׳¦׳׳” ׳׳×׳ ׳׳™ ׳”׳¡׳‘׳™׳‘׳” ׳•׳׳ ׳‘׳’׳
׳ ׳”׳×׳•׳¨׳©׳”|׳ײµ׳§׳•ײ¹׳₪ײµ׳ ׳•ײ¹׳˜ײ´׳™׳₪|||  
׳׳§׳•׳§׳׳™׳ ׳”|nf0|׳”׳©׳×׳ ׳•׳× ׳¨׳¦׳•׳₪׳” ׳©׳ 
׳׳§׳•׳˜׳™׳₪׳™׳ ׳‘׳׳§׳‘׳™׳ ׳׳”׳©׳×׳ ׳•׳× ׳”׳×׳
 ׳׳™׳ ׳”׳¡׳‘׳™׳‘׳×׳™׳™׳|׳ײµ׳§׳•ײ¹׳§ײ°׳ײ´׳™׳ ײ
¸׳”|||
׳׳§׳˜׳•׳–׳™׳˜|nm0|׳˜׳₪׳™׳ ׳”׳׳×׳§׳™׳™׳ ׳¢׳ ׳¦
׳“׳• ׳”׳—׳™׳¦׳•׳ ׳™ ׳©׳ ׳™׳¦׳•׳¨ ׳׳—׳¨|׳ײ¶׳§ײ°׳
˜׳•ײ¹׳–ײ´׳™׳˜|||
׳׳§׳˜׳•׳˜׳¨׳•׳₪׳™|nm0|׳׳™׳§׳•׳¨׳™׳–׳” ׳©׳‘׳” ׳¢׳
•׳˜׳£ ׳”׳×׳₪׳˜׳™׳¨ ׳׳× ׳×׳׳™ ׳”׳©׳•׳¨׳© ׳©׳ ׳”׳
¦׳׳— ׳׳‘׳—׳•׳¥|׳ײ¶׳§ײ°׳˜׳•ײ¹׳˜ײ°׳¨׳•ײ¹׳₪ײ´׳™|||
׳׳§׳˜׳™׳ ׳•׳‘׳™׳•׳׳•׳’׳™׳”|nf0|׳—׳§׳¨ ׳”׳©׳₪׳¢׳×
 ׳”׳§׳¨׳™׳ ׳” ׳¢׳ ׳™׳¦׳•׳¨׳™׳ ׳—׳™׳™׳|׳ײ·׳§ײ°׳
˜ײ´׳™׳ ׳•ײ¹׳‘ײ´ײ¼׳™ײ¼׳•ײ¹׳׳•ײ¹׳’ײ°׳™ײ¸׳”|||      
׳׳§׳¡׳•׳‘׳™׳•׳׳•׳’׳™׳”|nf0|׳—׳§׳¨ ׳×׳•׳₪׳¢׳•׳× ׳
”׳—׳™׳™׳ ׳׳—׳•׳¥ ׳׳›׳“׳•׳¨ײ¾׳”׳׳¨׳¥|׳ײ¶׳§ײ°׳¡
׳•ײ¹׳‘ײ´ײ¼׳™ײ¼׳•ײ¹׳׳•ײ¹׳’ײ°׳™ײ¸׳”|||
׳׳¨׳›׳ ׳•׳׳•׳’׳™׳”|nf0|׳׳“׳¢ ׳”׳¢׳•׳¡׳§ ׳‘׳—׳§׳
¨ ׳׳©׳₪׳—׳× ׳”׳¢׳›׳‘׳™׳©׳™׳|׳ײ¸׳¨ײ·׳›ײ°׳ ׳•ײ¹׳
׳•ײ¹׳’ײ°׳™ײ¸׳”|||
׳׳×׳•׳׳•׳’׳™׳”|nf0|׳׳“׳¢ ׳”׳¢׳•׳¡׳§ ׳‘׳—׳§׳¨ ׳”
׳×׳ ׳”׳’׳•׳×׳ ׳©׳ ׳‘׳¢׳׳™ײ¾׳—׳™׳™׳ ׳‘׳¡׳‘׳™׳‘׳
×׳ ׳”׳˜׳‘׳¢׳™׳×|׳ײµ׳×׳•ײ¹׳׳•ײ¹׳’ײ°׳™ײ¸׳”|||    
׳‘׳™׳•׳’׳ ׳–׳™׳”|nf0|׳×׳”׳׳™׳ ׳”׳×׳₪׳×׳—׳•׳× ׳©׳
 ׳™׳¦׳•׳¨ ׳׳™׳¦׳•׳¨ ׳׳—׳¨, ׳›׳’׳•׳ ׳”׳˜׳׳× ׳‘
׳™׳¦׳™׳ ׳׳• ׳”׳׳׳˜׳”|׳‘ײ´ײ¼׳™׳•ײ¹׳’ײ¶׳ ײ¶׳–ײ¸׳
”|||
׳‘׳™׳•׳|nm1|׳—׳‘׳¨׳” ׳׳§׳•׳׳•׳’׳™׳× ׳׳–׳•׳¨׳™׳
× ׳׳§׳™׳₪׳” ׳©׳ ׳™׳¦׳•׳¨׳™׳ ׳”׳׳©׳×׳¨׳¢׳× ׳¢׳
 ׳₪׳ ׳™ ׳׳–׳•׳¨ ׳˜׳‘׳¢׳™ ׳¨׳—׳‘ ׳׳™׳׳“׳™׳ ׳›׳’
׳•׳ ׳™׳¢׳¨ ׳׳• ׳׳“׳‘׳¨|׳‘ײ´ײ¼׳™ײ¼׳•ײ¹׳|||     
׳‘׳™׳•׳׳¡׳”|nf0|׳”׳׳¡׳” ׳”׳›׳•׳׳׳× ׳©׳ ׳™׳¦׳•
׳¨׳™׳ ׳—׳™׳™׳ ׳•׳”׳—׳•׳׳¨ ׳”׳׳•׳¨׳’׳ ׳™ ׳©׳”׳
 ׳׳™׳™׳¦׳¨׳™׳ ׳‘׳™׳—׳™׳“׳× ׳©׳˜׳— ׳׳¡׳•׳™׳׳×|׳
‘ײ´ײ¼׳™ײ¼׳•ײ¹׳ײ¸׳¡ײ¸׳”|||
׳‘׳™׳•׳₪׳™׳–׳™׳§׳”|nf0|׳”׳׳“׳¢ ׳”׳¢׳•׳¡׳§ ׳‘׳—׳§׳
¨ ׳׳¢׳¨׳›׳•׳× ׳—׳™׳™׳ ׳‘׳”׳¡׳×׳׳›׳•׳×׳• ׳¢׳ ׳—
׳•׳§׳™׳ ׳₪׳™׳–׳™׳§׳׳׳™׳™׳ ׳‘׳׳‘׳“|׳‘ײ´ײ¼׳™ײ¼׳
•ײ¹׳₪ײ´׳™׳–ײ´׳™׳§ײ¸׳”|||
׳”׳‘׳™׳˜׳˜|nm1|׳¡׳‘׳™׳‘׳× ׳”׳׳—׳™׳” ׳”׳˜׳‘׳¢׳™׳× 
׳©׳ ׳™׳¦׳•׳¨ ׳—׳™|׳”ײ·׳‘ײ´ײ¼׳™׳˜ײ¸׳˜|||
׳”׳˜׳¨׳•׳˜׳¨׳•׳£|nm0|׳™׳¦׳•׳¨ ׳”׳—׳™׳™׳‘ ׳׳¦׳¨׳•׳
 ׳×׳¨׳›׳•׳‘׳•׳× ׳׳•׳¨׳’׳ ׳™׳•׳× ׳׳ ׳”׳¡׳‘׳™׳‘׳
” ׳›׳“׳™ ׳׳”׳×׳§׳™׳™׳|׳”׳˜׳¨׳•׳˜׳¨׳•׳£|||       
׳”׳›׳—׳“׳”|nf0|׳”׳™׳¢׳׳׳•׳×׳• ׳›׳׳™׳ ׳©׳ ׳׳™
׳ ׳׳ ׳”׳¢׳•׳׳ ׳¢׳“ ׳›׳™ ׳׳ ׳ ׳•׳×׳¨ ׳₪׳¨׳˜ 
׳׳—׳“ ׳׳׳•׳›׳׳•׳¡׳™׳™׳×׳•|׳”ײ·׳›ײ°׳—ײ¸׳“ײ¸׳”|׳
”ײ·׳›ײ°׳—ײ¸׳“׳•ײ¹׳×||
׳”׳׳•׳‘׳™׳•׳–|n-0|׳׳›׳׳•׳ ׳”׳™׳¦׳•׳¨׳™׳ ׳”׳—׳
™׳™׳ ׳‘׳™׳|׳”׳׳•׳‘׳™׳•׳–|||
׳•׳™׳¨׳•׳׳•׳’׳™׳”|nf0|׳”׳׳“׳¢ ׳”׳¢׳•׳¡׳§ ׳‘׳—׳§׳
¨ ׳”׳ ׳’׳™׳₪׳™׳|׳•ײ´׳™׳¨׳•ײ¹׳׳•ײ¹׳’ײ°׳™ײ¸׳”|||  
׳˜׳•׳¡׳™׳§׳•׳׳•׳’׳™׳”|nf0|׳×׳•׳¨׳× ׳”׳¨׳¢׳׳™׳, 
׳”׳׳“׳¢ ׳”׳¢׳•׳¡׳§ ׳‘׳¨׳¢׳׳™׳ ׳•׳‘׳׳׳₪׳™׳™׳ ׳
™׳”׳|׳˜׳•ײ¹׳§ײ°׳¡ײ´׳™׳§׳•ײ¹׳׳•ײ¹׳’ײ°׳™ײ¸׳”|||   
׳׳™׳׳ ׳•׳׳•׳’׳™׳”|nf0|׳”׳׳“׳¢ ׳”׳¢׳•׳¡׳§ ׳‘׳—׳
§׳¨ ׳׳§׳•׳•׳™ ׳׳™׳ ׳׳×׳•׳§׳™׳ ׳›׳ ׳”׳¨׳•׳× ׳•
׳›׳׳’׳׳™׳, ׳›׳•׳׳ ׳”׳™׳¦׳•׳¨׳™׳ ׳”׳׳×׳§׳™׳™
׳׳™׳ ׳‘׳”׳|׳ײ´׳™׳׳ ׳•ײ¹׳׳•ײ¹׳’ײ°׳™ײ¸׳”|||   
׳׳™׳×׳•׳₪׳™׳˜|nm0|׳¦׳׳— ׳”׳’׳“׳ ׳‘׳¡׳‘׳™׳‘׳” ׳¡
׳׳¢׳™׳×|׳׳™׳×׳•׳₪׳™׳˜|||
׳׳•׳ ׳•׳˜׳¨׳•׳₪׳™|am0|׳™׳¦׳•׳¨ ׳”׳ ׳™׳–׳•׳ ׳׳™׳
¦׳•׳¨ ׳׳—׳“ ׳‘׳׳‘׳“|׳׳•ײ¹׳ ׳•ײ¹׳˜ײ°׳¨׳•ײ¹׳₪ײ´׳™
|||
׳׳•׳¨׳₪׳•׳׳•׳’׳™׳”|nf0|׳”׳׳“׳¢ ׳”׳¢׳•׳¡׳§ ׳‘׳—׳
§׳¨ ׳”׳¦׳•׳¨׳” ׳•׳”׳׳‘׳ ׳” ׳©׳ ׳™׳¦׳•׳¨׳™׳|׳׳•
ײ¹׳¨ײ°׳₪׳•ײ¹׳׳•ײ¹׳’ײ°׳™ײ¸׳”|||
׳׳˜׳׳•׳¨׳•׳׳•׳’׳™׳”|nf0|׳—׳§׳¨ ׳”׳׳˜׳׳•׳¡׳₪׳¨
׳” ׳×׳•׳ ׳”׳×׳׳§׳“׳•׳× ׳‘׳—׳§׳¨ ׳•׳‘׳—׳™׳–׳•׳™ ׳
׳–׳’ײ¾׳”׳׳•׳•׳™׳¨|׳ײµ׳˜ײµ׳׳•ײ¹׳¨׳•ײ¹׳׳•ײ¹׳’ײ°
׳™ײ¸׳”|||
׳׳™׳§׳•׳˜׳¨׳•׳₪׳™|am0|׳¦׳׳— ׳”׳—׳™ ׳‘׳׳•׳˜׳•׳׳
׳™׳–׳ (׳”׳“׳“׳™׳•׳×) ׳¢׳ ׳׳™׳§׳•׳¨׳™׳–׳”|׳ײ´׳
™׳§׳•ײ¹׳˜ײ°׳¨׳•ײ¹׳₪ײ´׳™|׳ײ´׳™׳§׳•ײ¹׳˜ײ°׳¨׳•ײ¹׳₪ײ´
׳™ײ´ײ¼׳™׳||
׳׳™׳§׳¨׳•׳–׳”|nf0|׳™׳—׳¡׳™ ׳’׳•׳׳׳™׳ ׳‘׳™׳ ׳×
׳₪׳˜׳™׳¨ ׳©׳ ׳₪׳˜׳¨׳™׳” ׳•׳‘׳™׳ ׳©׳•׳¨׳©׳™ ׳”׳¦׳
׳—, ׳©׳‘׳׳”׳׳›׳ ׳”׳₪׳˜׳¨׳™׳™׳” ׳ ׳”׳ ׳™׳× ׳׳¡
׳₪׳™׳’׳× ׳”׳₪׳—׳׳™׳׳•׳× ׳׳”׳¦׳׳— ׳”׳₪׳•׳ ׳“׳§׳
׳™ ׳‘׳¢׳•׳“ ׳”׳¦׳׳— ׳¡׳•׳₪׳’ ׳׳™׳ ׳¨׳׳™׳ ׳׳×
׳₪׳˜׳™׳¨ ׳”׳₪׳˜׳¨׳™׳™׳”|׳ײ´׳™׳§׳•ײ¹׳¨ײ´׳™׳–ײ¸׳”|׳
ײ´׳™׳§׳•ײ¹׳¨ײ´׳™׳–׳•ײ¹׳×|׳ײ´׳™׳§׳•ײ¹׳¨ײ´׳™׳–ײ·׳×
|
׳‘׳•׳˜׳ ׳™׳§׳”|nf0|׳׳“׳¢ ׳”׳¢׳•׳¡׳§ ׳‘׳—׳§׳¨ ׳׳
׳׳›׳× ׳”׳¦׳•׳׳—|׳‘ײ¼׳•ײ¹׳˜ײ¸׳ ײ´׳™׳§ײ¸׳”|||     
׳ ׳•׳׳™׳¡׳׳׳˜׳™׳§׳”|nf0|׳—׳§׳¨ ׳”׳׳˜׳‘׳¢׳•׳× ׳
•׳”׳¨׳§׳¢ ׳”׳”׳™׳¡׳˜׳•׳¨׳™ ׳”׳¨׳׳•׳•׳ ׳˜׳™ ׳׳׳™
׳”׳|׳ ׳•ײ¼׳ײ´׳™׳¡ײ°׳ײ¸׳˜ײ´׳™׳§ײ¸׳”|||
׳¡׳˜׳ ׳•׳˜׳•׳₪׳™|nm0|׳™׳¦׳•׳¨ ׳©׳×׳₪׳•׳¦׳×׳• ׳”׳’׳
™׳׳•׳’׳¨׳₪׳™׳× ׳׳¦׳•׳׳¦׳׳×|׳¡׳˜׳ ׳•׳˜׳•׳₪׳™|||
׳¡׳₪׳¨׳•׳₪׳’|nm0|׳‘׳¢׳ײ¾׳—׳™׳™׳ ׳”׳ ׳™׳–׳•׳ ׳׳
—׳•׳׳¨ ׳¦׳׳—׳™ ׳¨׳§׳•׳‘|׳¡ײ·׳₪ײ°ײ¼׳¨׳•ײ¹׳₪ײ¸׳’||
|
׳¡׳₪׳¨׳׳˜׳•׳₪׳™׳˜|nm0|׳¦׳׳— ׳׳©׳¨ ׳¦׳•׳¨׳× ׳¨׳‘
׳™׳™׳×׳• ׳”׳™׳ ׳‘׳¢׳–׳¨׳× ׳–׳¨׳¢׳™׳ ׳•׳׳ ׳¢"׳™
 ׳ ׳‘׳’׳™׳|׳¡ײ°׳₪ײµײ¼׳¨ײ°׳ײ·׳˜׳•ײ¹׳₪ײ´׳™׳˜|||   
׳¡׳₪׳¨׳׳•׳₪׳™׳˜|nm0|׳¦׳׳— ׳‘׳¢׳ ׳–׳¨׳¢׳™׳|׳¡ײ°
׳₪ײµײ¼׳¨ײ°׳׳•ײ¹׳₪ײ´׳™׳˜|||
׳₪׳•׳ ׳“׳§׳׳™|nm0|׳‘׳¢׳ ׳₪׳•׳ ׳“׳§, ׳‘׳¢׳ ׳׳›׳
¡׳ ׳™׳”, ׳׳׳¨׳—|׳₪ײ»ײ¼׳ ײ°׳“ײ°ײ¼׳§ײ·׳׳™|׳₪ײ»ײ¼׳
 ײ°׳“ײ°ײ¼׳§ײ¸׳ײ´׳™׳||
׳₪׳׳׳•׳ ׳˜׳•׳׳•׳’׳™׳”|nf0|׳׳“׳¢ ׳”׳¢׳•׳¡׳§ ׳‘׳
—׳§׳¨ ׳”׳—׳™׳™׳ ׳”׳₪׳¨׳”ײ¾׳”׳™׳¡׳˜׳•׳¨׳™׳™׳ ׳¢׳
 ׳₪׳ ׳™ ׳›׳“׳•׳¨ ׳”׳׳¨׳¥ ׳‘׳׳׳¦׳¢׳•׳× ׳”׳×׳‘׳•׳
 ׳ ׳•׳× ׳‘׳׳׳•׳‘׳ ׳™׳|׳₪ײ¸ײ¼׳ײµ׳׳•ײ¹׳ ײ°׳˜׳•ײ
¹׳׳•ײ¹׳’ײ°׳™ײ¸׳”|||
׳₪׳¡׳׳•׳₪׳™׳˜|n-0|׳¦׳׳— ׳”׳’׳“׳ ׳‘׳¡׳‘׳™׳‘׳” ׳—
׳•׳׳™׳×|׳₪ײ°ײ¼׳¡ײ¸׳׳•ײ¹׳₪ײ´׳™׳˜|||
׳₪׳¨׳•׳˜׳•׳–׳•׳׳•׳׳•׳’׳™׳”|nm0|׳¢׳ ׳£ ׳‘׳–׳•׳׳•
׳׳•׳’׳™׳” ׳”׳¢׳•׳¡׳§ ׳‘׳—׳§׳¨ ׳—׳“ײ¾׳×׳׳™׳™׳ ׳
׳§׳‘׳•׳¦׳× ׳”׳₪׳¨׳•׳˜׳•׳–׳•׳׳”|׳₪ײ°ײ¼׳¨׳•ײ¹׳˜׳•ײ¹
׳–׳•ײ¹׳׳•ײ¹׳׳•ײ¹׳’ײ°׳™ײ¸׳”|||
׳₪׳¨׳–׳™׳˜׳•׳׳•׳’׳™׳”|n-0|׳—׳§׳¨ ׳”׳™׳¦׳•׳¨׳™׳ ׳
”׳˜׳₪׳™׳׳™׳|׳₪ײ¸ײ¼׳¨ײ¸׳–ײ´׳™׳˜׳•ײ¹׳׳•ײ¹׳’ײ°׳™ײ¸
׳”|||
׳§׳׳™׳׳˜׳•׳׳•׳’׳™׳”|nf0|׳×׳•׳¨׳× ׳”׳׳§׳׳™׳|׳
§ײ°׳ײ´׳™׳ײ¸׳˜׳•ײ¹׳׳•ײ¹׳’ײ°׳™ײ¸׳”|||
׳§׳¨׳™׳‘׳™׳•׳׳•׳’׳™׳”|nf0|׳—׳§׳¨ ׳”׳§׳₪׳׳× ׳×׳׳
™׳, ׳¨׳§׳׳•׳×, ׳׳™׳‘׳¨׳™׳ ׳•׳™׳¦׳•׳¨׳™׳ ׳©׳׳
׳™׳ ׳×׳•׳ ׳›׳“׳™ ׳©׳׳™׳¨׳× ׳—׳™׳•׳ ׳™׳•׳×׳|׳§
ײ°׳¨ײ´׳™׳‘ײ´ײ¼׳™ײ¼׳•ײ¹׳׳•ײ¹׳’ײ°׳™ײ¸׳”|||
׳§׳¨׳™׳•׳₪׳™׳|nm0|׳׳•׳¨׳’׳ ׳™׳–׳ ׳”׳’׳“׳ ׳‘׳×׳
 ׳׳™ ׳§׳•׳¨|׳§ײ°׳¨ײ´׳™ײ¼׳•ײ¹׳₪ײ´׳™׳|׳§ײ°׳¨ײ´׳™ײ¼
׳•ײ¹׳₪ײ´׳™׳ײ´׳™׳||
׳§׳¨׳™׳₪׳˜׳•׳‘׳™׳•׳–׳”|nm0|[׳₪׳™׳¨׳•׳© ׳׳§׳•׳™] ׳
׳¦׳‘ ׳©׳ ׳”׳×׳¢׳•׳¨׳¨׳•׳× ׳׳—׳™׳™׳ ׳׳¦׳ ׳™׳¦
׳•׳¨׳™׳ ׳‘׳×׳ ׳׳™ ׳¨׳˜׳™׳‘׳•׳×, ׳׳׳—׳¨ ׳׳¦׳‘ 
׳§׳¨׳•׳‘ ׳׳—׳•׳¡׳¨ ׳—׳™׳™׳ ׳›׳×׳•׳¦׳׳” ׳׳™׳™׳‘
׳•׳© ׳׳•׳—׳׳˜ ׳©׳ ׳’׳•׳₪׳|׳§ײ°׳¨ײ´׳™׳₪ײ°ײ¼׳˜׳•
ײ¹׳‘ײ´ײ¼׳™ײ¼׳•ײ¹׳–ײ¸׳”|||
׳×׳¨׳•׳₪׳™׳˜|nm0|׳¦׳׳— ׳—׳“ײ¾׳©׳ ׳×׳™, ׳¦׳׳— ׳”׳
׳×׳—׳“׳© ׳׳–׳¨׳¢׳™׳ ׳‘׳׳‘׳“|׳×ײµײ¼׳¨׳•ײ¹׳₪ײ´׳™
׳˜|||
׳¡׳˜׳•׳™׳›׳™׳•׳׳˜׳¨׳™׳”|nf0|׳×׳—׳•׳ ׳‘׳›׳™׳׳™׳”
 ׳”׳¢׳•׳¡׳§ ׳‘׳›׳׳•׳™׳•׳× ׳•׳‘׳¨׳™׳›׳•׳–׳™׳ ׳©׳
 ׳”׳—׳•׳׳¨׳™׳ ׳”׳׳©׳×׳×׳₪׳™׳ ׳‘׳×׳’׳•׳‘׳•׳× ׳›
׳™׳׳™׳•׳×|׳¡׳˜׳•׳™׳›׳™׳•׳׳˜׳¨׳™׳”|||
׳›׳™׳׳™׳”|nf1|׳׳“׳¢ ׳”׳¢׳•׳¡׳§ ׳‘׳—׳•׳׳¨ ׳•׳‘׳×
׳›׳•׳ ׳•׳×׳™׳•|׳›ײ´ײ¼׳™׳ײ°׳™ײ¸׳”|||
׳׳•׳–׳•׳|nms|׳׳•׳׳§׳•׳׳” ׳”׳׳•׳¨׳›׳‘׳× ׳׳©׳
׳•׳©׳” ׳׳˜׳•׳׳™׳ ׳©׳ ׳—׳׳¦׳ (׳‘׳׳§׳•׳ ׳©׳
 ׳™׳™׳ ׳‘׳׳‘׳ ׳” ׳”׳ ׳₪׳•׳¥)|׳׳•ײ¹׳–׳•ײ¹׳|||  
׳׳•׳׳™׳’׳•׳§׳|nm0|׳×׳§׳•׳₪׳” ׳‘׳׳•׳— ׳”׳–׳׳ ׳
™׳ ׳”׳’׳׳•׳׳•׳’׳™|׳׳•ײ¹׳ײ´׳™׳’׳•ײ¹׳§ײµ׳|||  
׳׳™׳§׳¨׳™׳•׳˜׳™|am0|׳™׳¦׳•׳¨ ׳‘׳¢׳ ׳×׳ ׳¢׳ ׳’׳
¨׳¢׳™׳|׳ײµ׳™׳§ײ·׳¨ײ°׳™׳•ײ¹׳˜ײ´׳™|׳ײµ׳™׳§ײ·׳¨ײ°׳
™׳•ײ¹׳˜ײ´׳™ײ´ײ¼׳™׳||
׳׳•׳¨׳“׳•׳‘׳™׳§|nm0|[׳’׳׳•׳׳•׳’׳™׳”] ׳”׳×׳•׳¨ ׳
”׳©׳ ׳™ ׳‘׳¢׳™׳“׳ ׳”׳₪׳׳׳•׳–׳•׳׳™׳§׳•׳, ׳‘׳™׳
 ׳”׳§׳׳‘׳¨׳™׳•׳ ׳׳¡׳™׳׳•׳¨|׳׳•ײ¹׳¨ײ°׳“ײ¼׳•ײ¹
׳‘ײ´׳™׳§|||
׳׳™׳׳•׳§׳|nm0|׳×׳§׳•׳₪׳” ׳‘׳׳•׳— ׳”׳–׳׳ ׳™׳ 
׳”׳’׳׳•׳׳•׳’׳™|׳ײµ׳™׳׳•ײ¹׳§ײµ׳|||
׳׳™׳‘׳¨|nm0|׳§׳‘׳•׳¦׳” ׳©׳ ׳¨׳§׳׳•׳× ׳”׳₪׳•׳¢׳
׳•׳× ׳‘׳׳©׳•׳×׳£ ׳׳‘׳™׳¦׳•׳¢ ׳×׳₪׳§׳™׳“ ׳׳¡׳•׳™
׳|׳ײµ׳™׳‘ײ¸׳¨|׳ײµ׳™׳‘ײ¸׳¨ײ´׳™׳||
׳׳ײ¾׳ ׳™׳ ׳™׳•|nf0|׳×׳•׳₪׳¢׳” ׳׳§׳׳™׳׳™׳× ׳”׳
׳×׳¨׳—׳©׳× ׳‘׳“׳¨׳•׳ײ¾׳׳׳¨׳™׳§׳” ׳׳—׳× ׳׳›׳
׳” ׳©׳ ׳™׳ ׳•׳׳©׳¨ ׳‘׳׳¡׳’׳¨׳×׳” ׳—׳•׳¨׳’׳× ׳”׳
˜׳׳₪׳¨׳˜׳•׳¨׳” ׳‘׳₪׳ ׳™ײ¾׳”׳©׳˜׳— ׳©׳ ׳”׳׳•׳§׳™
׳™׳ ׳•׳¡ ׳‘׳—׳¦׳™ ׳׳¢׳׳× ׳¦׳׳–׳™׳•׳¡|׳ײ¶׳ ׳ ײ
´׳™׳ ײ°׳™׳•|||
׳׳ ׳’׳׳™׳×|nf0|׳©׳₪׳” ׳’׳¨׳׳׳ ׳™׳× ׳×׳—׳×׳™׳× 
׳”׳׳“׳•׳‘׳¨׳× ׳‘׳׳ ׳’׳׳™׳”, ׳‘׳׳¨׳”"׳‘ ׳•׳‘׳׳
“׳™׳ ׳•׳× ׳ ׳•׳¡׳₪׳•׳× ׳•׳׳©׳׳©׳× ׳›׳׳™׳ ׳’׳•׳
׳” ׳₪׳¨׳ ׳§׳” ׳‘׳¨׳•׳‘ ׳”׳¢׳•׳׳ ׳‘׳™׳׳™׳ ׳•|׳ײ
·׳ ײ°׳’ײ°ײ¼׳ײ´׳™׳×|||
׳׳ ׳¦׳™׳§׳׳•׳₪׳“׳™׳”|nf0|׳׳׳’׳¨ ׳›׳×׳•׳‘ ׳©׳ 
׳”׳™׳“׳¢ ׳”׳׳ ׳•׳©׳™ ׳”׳§׳™׳™׳, ׳©׳ ׳׳¡׳£ ׳›׳“׳
™ ׳׳”׳ ׳—׳™׳ ׳׳•׳×׳• ׳׳׳—׳¨׳™׳ ׳•׳׳“׳•׳¨׳•׳
× ׳”׳‘׳׳™׳|׳ײ¶׳ ײ°׳¦ײ´׳™׳§ײ°׳׳•ײ¹׳₪ײ¶ײ¼׳“ײ°׳™ײ
¸׳”|׳ײ¶׳ ײ°׳¦ײ´׳™׳§ײ°׳׳•ײ¹׳₪ײ¶ײ¼׳“ײ°׳™׳•ײ¹׳×||  
׳׳₪׳™׳₪׳™׳˜|nm0|׳¦׳׳— ׳”׳’׳“׳ ׳¢׳ ׳¦׳׳— ׳׳—׳
¨ ׳©׳׳™׳ ׳• ׳׳§׳™׳™׳ ׳׳™׳×׳• ׳©׳•׳ ׳™׳—׳¡׳™׳
, ׳׳׳ ׳¨׳§ ׳׳©׳×׳׳© ׳‘׳• ׳›׳׳§׳•׳ ׳׳׳—׳™׳
–׳”|׳ײ¶׳₪ײ´ײ¼׳™׳₪ײ´׳™׳˜|||
׳׳¦׳”|nf0|׳¦׳׳— ׳”׳’׳“׳ ׳׳¨׳•׳‘ ׳‘׳׳™׳ ׳•׳ ׳
‘׳“׳ ׳׳¦׳׳—׳™׳ ׳¨׳’׳™׳׳™׳ ׳‘׳׳ ׳’׳ ׳•׳ ׳”׳
¨׳‘׳™׳™׳”, ׳׳ ׳“׳•׳׳” ׳׳”׳ ׳‘׳™׳›׳•׳׳×׳• ׳׳
‘׳¦׳¢ ׳₪׳•׳˜׳•׳¡׳™׳ ׳×׳–׳” (׳‘׳׳׳¦׳¢׳•׳× ׳›׳׳•׳
¨׳•׳₪׳™׳ ׳”׳׳¦׳•׳™ ׳‘׳•)|׳ײ·׳¦ײ¸ײ¼׳”|׳ײ·׳¦ײ¼׳•
ײ¹׳×|׳ײ·׳¦ײ·ײ¼׳×|׳ײ·׳¦ײ¼׳•ײ¹׳×
׳׳§׳•׳•׳¨׳™׳•׳|nm0|׳׳›׳ (׳׳¨׳•׳‘ ׳¢׳©׳•׳™ ׳–׳
›׳•׳›׳™׳×) ׳”׳׳™׳•׳¢׳“ ׳׳’׳™׳“׳•׳ ׳‘׳¢׳׳™ײ¾׳—׳
™׳™׳ ׳”׳—׳™׳™׳ ׳‘׳׳™׳|׳ײ·׳§ײ°׳•ײ·׳¨ײ°׳™׳•ײ¼׳
|׳ײ·׳§ײ°׳•ײ·׳¨ײ°׳™׳•ײ¼׳ײ´׳™׳||
׳‘׳׳˜|nm1|׳׳” ׳©׳‘׳•׳׳˜, ׳’׳‘׳•׳” ׳™׳•׳×׳¨ ׳׳₪
׳ ׳™ ׳”׳©׳˜׳—; ׳‘׳“׳¨׳ ׳›׳׳ ׳‘׳¦׳¨׳•׳£ ׳“׳₪׳•׳¡
 ׳‘׳׳˜|׳‘ײ¶ײ¼׳ײ¶׳˜|׳‘ײ°ײ¼׳ײ¸׳˜ײ´׳™׳|׳‘ײ¶ײ¼׳ײ¶
׳˜|׳‘ײ´ײ¼׳ײ°׳˜ײµ׳™
׳‘׳•׳׳˜|nm1|[׳™׳׳׳•׳×] ׳”׳׳¨׳—׳§ ׳”׳׳ ׳›׳™ ׳
׳§׳• ׳”׳˜׳¢׳™׳ ׳” ׳”׳§׳™׳¦׳™ ׳©׳ ׳׳ ׳™׳™׳” ׳¢׳“ 
׳”׳¡׳™׳₪׳•׳ ׳”׳’׳׳•׳™ ׳”׳¢׳׳™׳•׳ ׳•׳”׳¨׳¦׳•׳£ 
׳©׳׳”|׳‘ײ¹ײ¼׳ײ¶׳˜|||
׳‘׳׳˜|nm0|׳¦׳•׳¨׳” ׳©׳ ׳׳—׳•׳ ׳©׳”׳×׳₪׳×׳—׳” ׳
‘׳¦׳¨׳₪׳× ׳‘׳—׳¦׳¨׳• ׳©׳ ׳”׳׳׳ ׳׳•׳׳™ ׳”ײ¾14
|׳‘ײ¸ײ¼׳ײµ׳˜|||
׳‘׳“׳™׳•׳|nm0|׳“׳‘׳¨ ׳‘׳“׳•׳™, ׳“׳׳™׳•׳ ׳™, ׳©׳
׳™׳ ׳• ׳§׳™׳™׳ ׳‘׳׳¦׳™׳׳•׳×|׳‘ײ´ײ¼׳“ײ°׳™׳•ײ¹׳|
׳‘ײ´ײ¼׳“ײ°׳™׳•ײ¹׳ ׳•ײ¹׳×|׳‘ײ´ײ¼׳“ײ°׳™׳•ײ¹׳|׳‘ײ´ײ¼
׳“ײ°׳™׳•ײ¹׳ ׳•ײ¹׳×
׳‘׳•׳ ׳¡׳׳™|nm1|׳¢׳¥ ׳ ׳ ׳¡׳™ ׳©׳׳™׳׳“׳™׳• ׳’׳•
׳׳“׳• ׳¢׳ ׳™׳“׳™ ׳’׳™׳–׳•׳ ׳§׳₪׳“׳ ׳™ ׳‘׳©׳•׳¨׳
©׳™׳• ׳•׳¢׳ ׳₪׳™׳•|׳‘ײ¼׳•ײ¹׳ ײ°׳¡ײ·׳׳™|||        
׳‘׳׳“׳¨|nm0|׳©׳׳™׳— ׳׳”׳™׳¨ ׳”׳ ׳•׳©׳ ׳׳¡׳׳›
׳™׳ ׳׳• ׳”׳•׳“׳¢׳•׳×|׳‘ײ·ײ¼׳ײ°׳“ײ¸ײ¼׳¨|׳‘ײ·ײ¼׳
ײ°׳“ײ¸ײ¼׳¨ײ´׳™׳|׳‘ײ·ײ¼׳ײ°׳“ײ·ײ¼׳¨|׳‘ײ·ײ¼׳ײ°׳“ײ°
ײ¼׳¨ײµ׳™
׳‘׳¨׳–׳™׳|nf0|׳”׳׳“׳™׳ ׳” ׳”׳’׳“׳•׳׳” ׳‘׳™׳•׳×׳
¨ ׳‘׳׳׳¨׳™׳§׳” ׳”׳“׳¨׳•׳׳™׳×|׳‘ײ°ײ¼׳¨ײ¸׳–ײ´׳™׳
|||
׳’׳•׳׳™׳‘׳¨|nm1|[׳¡׳׳ ׳’] ׳׳“׳ ׳׳’׳•׳“׳|׳’ײ¼
׳•ײ¼׳ײ´׳™׳‘ײ¶׳¨|||
׳’׳•׳׳|nm1|׳’׳•׳©; ׳‘׳׳™׳•׳—׳“ ׳©׳ ׳׳ ׳× ׳—׳•
׳׳¨ ׳”׳׳™׳•׳¢׳“׳× ׳׳™׳¦׳¨ ׳׳׳ ׳” ׳׳©׳”׳•|׳’ײ
¹ײ¼׳ײ¶׳|׳’ײ°ײ¼׳ײ¸׳ײ´׳™׳|׳’ײ¹ײ¼׳ײ¶׳|׳’ײ¸ײ¼׳
ײ°׳ײµ׳™
׳’׳׳|nm1|׳—׳•׳׳¨ ׳׳ ׳׳¢׳•׳‘׳“, ׳—׳•׳׳¨ ׳׳•
׳¦׳ ׳©׳׳׳ ׳• ׳™׳™׳•׳¦׳¨׳• ׳׳•׳¦׳¨׳™׳ ׳׳•׳’׳
׳¨׳™׳|׳’ײ¶ײ¼׳ײ¶׳||׳’ײ¶ײ¼׳ײ¶׳|
׳“׳‘׳•׳|nm0|[׳’׳׳•׳׳•׳’׳™׳”] ׳”׳×׳•׳¨ ׳”׳¨׳‘׳™׳
¢׳™ ׳‘׳¢׳™׳“׳ ׳”׳₪׳׳׳•׳–׳•׳׳™׳§׳•׳|׳“ײµײ¼׳‘׳•
ײ¹׳|||
׳“׳‘׳¨|nm1|[׳׳§׳¨׳] ׳׳’׳₪׳” ׳”׳׳׳™׳×׳” ׳¨׳‘׳™
׳|׳“ײ¶ײ¼׳‘ײ¶׳¨|||
׳“׳™׳‘׳¨|nm0|׳׳—׳“ ׳׳¢׳©׳¨׳× ׳”׳“׳™׳‘׳¨׳•׳×|׳“ײ´
ײ¼׳‘ײµײ¼׳¨|׳“ײ´ײ¼׳‘ײ°ײ¼׳¨׳•ײ¹׳×||
׳“׳‘׳¨|nm0|[׳׳§׳¨׳] ׳“׳™׳‘׳•׳¨, ׳׳” ׳©׳ ׳׳׳¨|
׳“ײ¸ײ¼׳‘ײ¸׳¨|׳“ײ°ײ¼׳‘ײ¸׳¨ײ´׳™׳|׳“ײ°ײ¼׳‘ײ·׳¨|׳“ײ´ײ
¼׳‘ײ°׳¨ײµ׳™
׳“׳•׳‘׳¨|nm1|[׳׳§׳¨׳] ׳׳¨׳¢׳”|׳“ײ¹ײ¼׳‘ײ¶׳¨|׳“ײ°
ײ¼׳‘ײ¸׳¨ײ´׳™׳||
׳“׳‘׳¨|nm0|[׳׳©׳•׳ ׳—׳–"׳] ׳׳ ׳”׳™׳’|׳“ײ·ײ¼׳‘ײ
¸ײ¼׳¨|||
׳“׳•׳’׳¨|nm1|׳×׳§׳•׳₪׳” ׳‘׳×׳•׳¨ ׳”׳™׳•׳¨׳” ׳׳©׳¨
 ׳‘׳¢׳™׳“׳ ׳”׳׳–׳•׳–׳•׳׳™׳§׳•׳|׳“׳•ײ¹׳’ײ¶׳¨|||
׳”׳׳“׳|nm0|׳”׳×׳•׳¨ ׳”׳¨׳—׳•׳§ ׳‘׳™׳•׳×׳¨ ׳׳–׳
׳ ׳ ׳• ׳‘׳¢׳™׳“׳ ׳”׳׳¨׳›׳׳•׳|׳”ײ²׳ײ¶׳“ײ¶׳|||
׳”׳“׳‘׳¨׳”|nf0|׳”׳¨׳—׳§׳× ׳׳–׳™׳§׳™׳ ׳׳• ׳”׳¨׳™
׳’׳×׳|׳”ײ·׳“ײ°׳‘ײ¸ײ¼׳¨ײ¸׳”|׳”ײ·׳“ײ°׳‘ײ¸ײ¼׳¨׳•ײ¹׳×
|׳”ײ·׳“ײ°׳‘ײ¸ײ¼׳¨ײ·׳×|
׳”׳“׳•׳“ ׳¡׳|nm0|׳›׳™׳ ׳•׳™ ׳•׳¡׳׳ ׳׳׳¨׳¦׳•׳×
 ׳”׳‘׳¨׳™׳×|׳”ײ·׳“ײ¼׳•ײ¹׳“ ׳¡ײµ׳|||
PS C:\Users\shemesh\Downloads\New folder
Adding a command to the trust list allows me to auto run this command in the future without asking for your approval every time. Manage the command trust list in the Trusted Commands setting. Which variation would you like to add?


Full command
Get-Content "nakdan_jar_extracted\org\culmus\nakdan\nakdan.txt" | Select-Object -First 100

Base
Get-Content *
Now I have everything I need. Let me also check the type codes used in the dictionary:

Command
c:\Users\sh…\New folder

Get-Content "nakdan_jar_extracted\org\culmus\nakdan\nakdan.txt" -Encoding UTF8 | ForEach-Object { ($_ -split '\|')[1] } | Sort-Object -Unique
GeGet-Cont
ent "nakdan_jar_extracted\org\culmus\nakdan\nakdan
.txt" -Encoding UTF8 | ForEach-Object { ($_ -split
PS C:\Users\shemesh\Downloads\New folder> Get-Cont
ent "nakdan_jar_extracted\org\culmus\nakdan\nakdan
.txt" -Encoding UTF8 | ForEach-Object { ($_ -split
 '\|')[1] } | Sort-Object -Un ׳‘׳¢׳™׳“׳ ׳”׳׳–׳•׳–׳•׳׳™׳§׳•׳|׳“׳•ײ¹׳’ײ¶׳¨|||
׳”׳׳“׳|nm0|׳”׳×׳•׳¨ ׳”׳¨׳—׳•׳§ ׳‘׳™׳•׳×׳¨ ׳׳–׳
׳ ׳ ׳• ׳‘׳¢׳™׳“׳ ׳”׳׳¨׳›׳׳•׳|׳”ײ²׳ײ¶׳“ײ¶׳|||
׳”׳“׳‘׳¨׳”|nf0|׳”׳¨׳—׳§׳× ׳׳–׳™׳§׳™׳ ׳׳• ׳”׳¨׳™
׳’׳×׳|׳”ײ·׳“ײ°׳‘ײ¸ײ¼׳¨ײ¸׳”|׳”ײ·׳“ײ°׳‘ײ¸ײ¼׳¨׳•ײ¹׳×
|׳”ײ·׳“ײ°׳‘ײ¸ײ¼׳¨ײ·׳×|
׳”׳“׳•׳“ ׳¡׳|nm0|׳›׳™׳ ׳•׳™ ׳•׳¡׳׳ ׳׳׳¨׳¦׳•׳×
 ׳”׳‘׳¨׳™׳×|׳”ײ·׳“ײ¼׳•ײ¹׳“ ׳¡ײµ׳|||
PS C:\Users\shemesh\Downloads\New folder> Get-Cont
ent "nakdan_jar_extracted\org\culmus\nakdan\nakdan
.txt" -Encoding UTF8 | ForEach-Object { ($_ -split
 '\|')[1] } | Sort-Object -Unique
A---
a-0
a-1
Af--
af0
af1
Am--
am0
am1
ams
a-s
N---
n-0
n-1
N--A
N--B
N--C
N--D
N--E
Nf--
nf0
nf1
Nf-A
Nf-c
Nf-E
Nf-i
nfs
Nm--
nm0
nm1
Nm-A
Nm-B
Nm-c
Nm-D
Nm-i
nms
n-s
PS C:\Users\shemesh\Downloads\New folder>nfs
Nm--
nm0
nm1
Nm-A
Nm-B
Nm-c
Nm-D
Nm-i
nms
n-s
PS C:\Users\shemesh\Downloads\
New folder>nfs
Nm--
nm0
nm1
Nm-A
Nm-B
Nm-c
Nm-D
Nm-i
nms
n-s
PS C:\Users\shemesh\Downloads
\New folder>nfs
Nm--
nm0
nm1
Nm-A
Nm-B
Nm-c
Nm-D
Nm-i
nms
n-s
PS C:\Users\shemesh\Downloads\New
 folder
Too many requests, please wait before trying again.