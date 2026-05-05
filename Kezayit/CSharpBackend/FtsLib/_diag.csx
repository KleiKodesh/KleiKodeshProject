using FtsLib.Core;
using System;
using System.Text;
Console.OutputEncoding = Encoding.UTF8;

// 1. rawStart1 — "שלום עולם": how many chars is "שלום "?
string s = "שלום עולם";
Console.WriteLine("String bytes/chars:");
for(int i=0;i<s.Length;i++) Console.WriteLine($"  [{i}] U+{(int)s[i]:X4} '{s[i]}'");

// 2. both highlighted — "תורה ומצוה" with terms {תורה, מצוה}
var sb2 = new SnippetBuilder(snippetLength:9999);
var r2 = sb2.Build("תורה ומצוה", new[]{"תורה","מצוה"});
Console.WriteLine($"\nboth highlighted html: {r2.Html}");

// 3. long ellipsis — snippetLength=40, contextMargin=5
string longText = new string('א',100) + " תורה " + new string('ב',100);
var sb3 = new SnippetBuilder(snippetLength:40, contextMargin:5);
var r3 = sb3.Build(longText, new[]{"תורה"});
Console.WriteLine($"\nlong ellipsis html (first 60): {r3.Html.Substring(0,Math.Min(60,r3.Html.Length))}");
Console.WriteLine($"starts with ellipsis: {r3.Html.StartsWith("…")}");
Console.WriteLine($"score={r3.Score}");

// 4. window centered
string text4 = new string('ד',200) + " תורה";
var sb4 = new SnippetBuilder(snippetLength:40, contextMargin:5);
var r4 = sb4.Build(text4, new[]{"תורה"});
Console.WriteLine($"\nwindow centered html (first 60): {r4.Html.Substring(0,Math.Min(60,r4.Html.Length))}");
Console.WriteLine($"starts with ellipsis: {r4.Html.StartsWith("…")}");
