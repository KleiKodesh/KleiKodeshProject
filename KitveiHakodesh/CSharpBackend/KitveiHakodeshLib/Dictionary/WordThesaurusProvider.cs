using Microsoft.Office.Interop.Word;
using System;
using System.Collections.Generic;

namespace KitveiHakodeshLib.Dictionary
{
    /// <summary>
    /// Thin wrapper around the Word Application thesaurus (SynonymInfo).
    /// Only active inside the VSTO environment — HostApplication must be set
    /// during ThisAddIn_Startup. When null (e.g. standalone / dev), all calls
    /// return empty results so the feature degrades gracefully.
    ///
    /// This class is intentionally self-contained 
    /// </summary>
    public static class WordThesaurusProvider
    {
        /// <summary>
        /// Set once in ThisAddIn_Startup. Null outside the VSTO host.
        /// </summary>
        public static Application HostApplication { get; set; }

        /// <summary>
        /// Returns synonym lists for <paramref name="word"/> grouped by meaning.
        /// Each element in the outer list is one meaning; the inner list is its synonyms.
        /// Returns an empty list when not running inside Word or when no synonyms are found.
        /// </summary>
        public static List<List<string>> GetSynonyms(string word)
        {
            var result = new List<List<string>>();

            if (HostApplication == null || string.IsNullOrWhiteSpace(word))
                return result;

            try
            {
                object langId = WdLanguageID.wdHebrew;
                SynonymInfo syn = HostApplication.get_SynonymInfo(word, ref langId);

                if (!syn.Found || syn.MeaningCount == 0)
                    return result;

                for (int i = 1; i <= syn.MeaningCount; i++)
                {
                    object index = i;
                    // SynonymList returns a Variant array — use Array to avoid dynamic dispatch
                    object raw = syn.get_SynonymList(ref index);
                    Array arr = raw as Array;
                    if (arr == null) continue;

                    var group = new List<string>();
                    foreach (object item in arr)
                    {
                        string s = item as string;
                        if (!string.IsNullOrWhiteSpace(s))
                            group.Add(s);
                    }

                    if (group.Count > 0)
                        result.Add(group);
                }
            }
            catch
            {
                // Thesaurus not available for this language / Word version — degrade silently.
            }

            return result;
        }
    }
}
