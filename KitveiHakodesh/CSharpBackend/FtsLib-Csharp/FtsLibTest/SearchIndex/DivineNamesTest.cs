using FtsLib.Search;
using FtsLib.Snippets;
using System;
using System.Collections.Generic;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Placeholder spellings of the divine names (<see cref="DivineNames"/>): each spelling
    /// gives the others, up to two prefixes, excluded prefix pairs, the Elohim family, and the
    /// locator matches a placeholder in the text for a query of the full spelling. In-memory
    /// only; same cases as the Rust engine's divine_names tests.
    ///
    /// Usage:
    ///   FtsLibTest.exe divinenames
    /// </summary>
    internal static class DivineNamesTest
    {
        const string Full = "יהוה";
        const string Qof = "יקוק";
        const string Dalet = "ידוד";
        const string YY = "יי";
        const string Elohim = "אלהים";
        const string Elokim = "אלקים";
        const string Ab = "אב";

        static int _passed, _failed;

        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            _passed = _failed = 0;

            Expect("qof gives full, dalet, yy", Join(DivineNames.Alternatives(Qof)) == Full + "," + Dalet + "," + YY);
            Expect("dalet gives full, qof, yy", Join(DivineNames.Alternatives(Dalet)) == Full + "," + Qof + "," + YY);
            Expect("full gives qof, dalet, yy", Join(DivineNames.Alternatives(Full)) == Qof + "," + Dalet + "," + YY);
            Expect("yy gives full, qof, dalet", Join(DivineNames.Alternatives(YY)) == Full + "," + Qof + "," + Dalet);
            Expect("one prefix kept", DivineNames.Alternatives("ו" + Qof)[0] == "ו" + Full);
            Expect("two prefixes kept", DivineNames.Alternatives("ול" + Dalet)[0] == "ול" + Full);
            Expect("three prefixes: no name", DivineNames.Alternatives("וול" + Qof).Count == 0);
            Expect("mixed letters: no name", DivineNames.Alternatives("יקוד").Count == 0);
            Expect("other letter: no name", DivineNames.Alternatives("יכוכ").Count == 0);
            Expect("prefix outside the set: no name", DivineNames.Alternatives("א" + Qof).Count == 0);
            Expect("short word: no name", DivineNames.Alternatives("יקו").Count == 0);
            Expect("one letter: no name", DivineNames.Alternatives("י").Count == 0);

            // Excluded prefix pairs, both ways.
            Expect("shin + dalet: excluded", DivineNames.Alternatives("ש" + Dalet).Count == 0);
            Expect("vav shin + dalet: excluded", DivineNames.Alternatives("וש" + Dalet).Count == 0);
            Expect("shin + full: no dalet", Join(DivineNames.Alternatives("ש" + Full)) == "ש" + Qof + ",ש" + YY);
            Expect("he + yy: excluded", DivineNames.Alternatives("ה" + YY).Count == 0);
            Expect("kaf + yy: excluded", DivineNames.Alternatives("כ" + YY).Count == 0);
            Expect("he + qof: no yy", DivineNames.Alternatives("ה" + Qof).Count == 2);
            Expect("lamed + yy", DivineNames.Alternatives("ל" + YY)[0] == "ל" + Full);

            // Elohim family.
            Expect("elohim gives elokim", Join(DivineNames.Alternatives(Elohim)) == Elokim);
            Expect("elokim gives elohim", Join(DivineNames.Alternatives(Elokim)) == Elohim);
            Expect("vav, nun-vav suffix, he prefix",
                Join(DivineNames.Alternatives("האלוקינו")) == "האלוהינו");
            Expect("no suffix", Join(DivineNames.Alternatives("אלקי")) == "אלהי");
            Expect("other suffix: no name", DivineNames.Alternatives("אלקית").Count == 0);
            Expect("other third letter: no name", DivineNames.Alternatives("אלדים").Count == 0);
            Expect("missing yod: no name", DivineNames.Alternatives("אלקם").Count == 0);

            // The group the engine builds for the full spelling finds a placeholder line.
            var terms = new List<string> { Full };
            terms.AddRange(DivineNames.Alternatives(Full));
            var groups = PreparedQueryGroups.FromGroups(new List<IReadOnlyCollection<string>> { terms, new[] { Ab } });
            var locator = new SnippetWindowLocator();
            Expect("phrase with a placeholder", locator.ContainsPhrase(Qof + " " + Ab, groups));
            Expect("phrase with the double yod", locator.ContainsPhrase(YY + " " + Ab, groups));

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
