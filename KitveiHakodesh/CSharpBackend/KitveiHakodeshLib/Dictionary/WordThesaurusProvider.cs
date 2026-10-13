using System;
using System.Collections.Generic;
using KitveiHakodeshLib.Common;
using Word = Microsoft.Office.Interop.Word;

namespace KitveiHakodeshLib.Dictionary
{
    /// <summary>
    /// Word's own thesaurus, for a word (the page's getWordSynonyms action).
    ///
    /// Finds Word through <see cref="RunningWordFinder"/>: the add-in's host instance when it is
    /// still alive, otherwise a Word that is already running. It never STARTS Word - nobody wants
    /// a copy of Word launching because they looked up a synonym, so no running instance means
    /// no synonyms.
    ///
    /// A different feature from the dictionary database, despite both returning related words:
    /// one is Microsoft's thesaurus for a language, the other is a curated lexicon.
    ///
    /// Hosted (net48) only - Office PIA, in the host's own process.
    /// </summary>
    public static class WordThesaurusProvider
    {
        /// <summary>
        /// The VSTO host's Word, set once in ThisAddIn_Startup; null outside the add-in.
        /// Stored on <see cref="RunningWordFinder.HostApplication"/>, which is where the lookup
        /// reads it from.
        /// </summary>
        public static Word.Application HostApplication
        {
            get => RunningWordFinder.HostApplication;
            set => RunningWordFinder.HostApplication = value;
        }

        /// <summary>
        /// Synonyms grouped by meaning: each inner list is one sense of the word.
        ///
        /// Empty when Word is not running, when the word has no entry, or when this Word has no
        /// Hebrew thesaurus installed - none of them is an error worth interrupting a lookup for.
        /// </summary>
        public static List<List<string>> GetSynonyms(string word)
        {
            var meanings = new List<List<string>>();
            if (string.IsNullOrWhiteSpace(word)) return meanings;

            Word.Application application = RunningWordFinder.FindRunning();
            if (application == null) return meanings;

            try
            {
                object languageId = Word.WdLanguageID.wdHebrew;
                Word.SynonymInfo info = application.get_SynonymInfo(word, ref languageId);

                if (!info.Found || info.MeaningCount == 0) return meanings;

                for (int meaning = 1; meaning <= info.MeaningCount; meaning++)
                {
                    object index = meaning;
                    // SynonymList hands back a Variant array; taking it as Array rather than
                    // dynamic keeps this off the late-bound dispatch path.
                    object raw = info.get_SynonymList(ref index);
                    if (!(raw is Array synonyms)) continue;

                    var group = new List<string>();
                    foreach (object item in synonyms)
                    {
                        if (item is string synonym && !string.IsNullOrWhiteSpace(synonym))
                            group.Add(synonym);
                    }

                    if (group.Count > 0) meanings.Add(group);
                }
            }
            catch (Exception)
            {
                // No thesaurus for this language, or this Word build does not expose one.
                // Returning what we have is the whole contract: an absent thesaurus is a state.
            }

            return meanings;
        }
    }
}
