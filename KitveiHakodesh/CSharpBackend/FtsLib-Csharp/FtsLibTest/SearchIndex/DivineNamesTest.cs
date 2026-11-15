using FtsLib.Search;
using FtsLib.Snippets;
using System;
using System.Collections.Generic;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Placeholder spellings of the divine name (<see cref="DivineNames"/>): each spelling
    /// gives the other two, up to two prefixes, and the locator marks a placeholder in the
    /// text for a query of the full spelling. In-memory only; same cases as the Rust
    /// engine's divine_names tests.
    ///
    /// Usage:
    ///   FtsLibTest.exe divinenames
    /// </summary>
    internal static class DivineNamesTest
    {
        const string Full = "יהוה";
        const string Qof = "יקוק";
        const string Dalet = "ידוד";
        const string Ab = "אב";

        static int _passed, _failed;

        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            _passed = _failed = 0;

            Expect("qof gives full, dalet", Join(DivineNames.Alternatives(Qof)) == Full + "," + Dalet);
            Expect("dalet gives full, qof", Join(DivineNames.Alternatives(Dalet)) == Full + "," + Qof);
            Expect("full gives qof, dalet", Join(DivineNames.Alternatives(Full)) == Qof + "," + Dalet);
            Expect("one prefix kept", Join(DivineNames.Alternatives("ו" + Qof)) == "ו" + Full + ",ו" + Dalet);
            Expect("two prefixes kept", DivineNames.Alternatives("ול" + Dalet)[0] == "ול" + Full);
            Expect("three prefixes: no name", DivineNames.Alternatives("וול" + Qof).Count == 0);
            Expect("mixed letters: no name", DivineNames.Alternatives("יקוד").Count == 0);
            Expect("other letter: no name", DivineNames.Alternatives("יכוכ").Count == 0);
            Expect("prefix outside the set: no name", DivineNames.Alternatives("א" + Qof).Count == 0);
            Expect("short word: no name", DivineNames.Alternatives("יקו").Count == 0);

            // The group the engine builds for the full spelling finds a placeholder line.
            var terms = new List<string> { Full };
            terms.AddRange(DivineNames.Alternatives(Full));
            var groups = PreparedQueryGroups.FromGroups(new List<IReadOnlyCollection<string>> { terms, new[] { Ab } });
            var locator = new SnippetWindowLocator();
            Expect("phrase with a placeholder", locator.ContainsPhrase(Qof + " " + Ab, groups));
            Expect("phrase with the other placeholder", locator.ContainsPhrase(Dalet + " " + Ab, groups));

            Console.WriteLine();
            Console.WriteLine($"Divine name tests: {_passed} passed, {_failed} failed");
            if (_failed > 0) Environment.ExitCode = 1;
        }

        static string Join(List<string> list) => string.Join(",", list);

        static void Expect(string name, bool ok)
        {
            if (ok) { _passed++; Console.WriteLine("PASS  " + name); }
            else { _failed++; Console.WriteLine("FAIL  " + name); }
        }
    }
}
