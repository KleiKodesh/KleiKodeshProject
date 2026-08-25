using System;
using System.Collections.Generic;
using Word = Microsoft.Office.Interop.Word;

namespace KitveiHakodesh.Core.Common
{
    /// <summary>
    /// Word's own thesaurus, for a word.
    ///
    /// AUTONOMOUS. It finds Word through <see cref="RunningWordFinder"/> rather than being
    /// handed an instance, so it works wherever Word is open and not only inside the add-in.
    /// It never STARTS Word: nobody wants a copy of Word launching because they looked up a
    /// synonym, so no running instance means no synonyms.
    ///
    /// This is a different feature from the dictionary database, despite both returning related
    /// words — one is Microsoft's thesaurus for a language, the other is a curated lexicon. They
    /// are not interchangeable and must not be merged behind one name.
    ///
    /// net48 leg only — Office PIA.
    /// </summary>
    public static class WordThesaurus
    {
        /// <summary>
        /// Synonyms grouped by meaning: each inner list is one sense of the word.
        ///
        /// Empty when Word is not running, when the word has no entry, or when this Word has no
        /// Hebrew thesaurus installed — three different reasons for the same answer, and none of
        /// them is an error worth interrupting a lookup for.
        /// </summary>
        public static List<List<string>> Synonyms(string word)
        {
            var meanings = new List<List<string>>();
            if (string.IsNullOrWhiteSpace(word)) return meanings;

            Word.Application? application = RunningWordFinder.FindRunning();
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
