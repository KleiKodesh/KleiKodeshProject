using Microsoft.Office.Interop.Word;
using System.Collections.Generic;
using CoreCommon = KitveiHakodesh.Core.Common;

namespace KitveiHakodeshLib.Dictionary
{
    /// <summary>
    /// The hosted app's name for Word's thesaurus. Core's
    /// <see cref="CoreCommon.WordThesaurus"/> underneath.
    ///
    /// This used to be a second copy of the SynonymInfo walk — the same Hebrew language
    /// constant, the same meaning loop, the same deliberate avoidance of `dynamic` — differing
    /// only in where it got the Word application from. Core asks
    /// <see cref="CoreCommon.RunningWordFinder"/>, which is the better answer: it checks the
    /// host instance is still ALIVE before using it (a severed COM proxy otherwise throws on
    /// every call for the rest of the session) and can bind to a running Word when there is no
    /// host at all, so synonyms work outside the add-in too.
    ///
    /// The name and the signatures stay because the add-in and the message handler use them.
    /// </summary>
    public static class WordThesaurusProvider
    {
        /// <summary>
        /// Set once in ThisAddIn_Startup; null outside the VSTO host.
        ///
        /// Forwards to <see cref="CoreCommon.RunningWordFinder.HostApplication"/> — that is
        /// where the lookup reads it from, so keeping a private copy here would leave the
        /// add-in silently falling back to binding whatever Word happened to be running.
        /// </summary>
        public static Application HostApplication
        {
            get => CoreCommon.RunningWordFinder.HostApplication;
            set => CoreCommon.RunningWordFinder.HostApplication = value;
        }

        /// <summary>
        /// Synonym lists for <paramref name="word"/>, grouped by meaning: each element of the
        /// outer list is one meaning, the inner list its synonyms. Empty when Word cannot be
        /// reached or the word has no entry.
        /// </summary>
        public static List<List<string>> GetSynonyms(string word) =>
            CoreCommon.WordThesaurus.Synonyms(word);
    }
}
