using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nakdan.Core
{
    public static class RunWriter
    {
        public static void WriteTokensToRuns(
            List<Token> tokens,
            List<RunInfo> runs)
        {
            var runLookup =
                runs.ToDictionary(r => r.Index);

            foreach (var group in tokens.GroupBy(t => t.RunIndex))
            {
                if (!runLookup.TryGetValue(
                    group.Key,
                    out RunInfo run))
                {
                    continue;
                }

                var sb = new StringBuilder();

                foreach (Token tok in group.OrderBy(t => t.PosInRun))
                {
                    sb.Append(tok.Base);
                    sb.Append(tok.VowelsAfter);
                }

                string newText = sb.ToString();

                if (newText == run.OrigText)
                    continue;

                run.TextEl.Value = newText;

                OoxmlHelper.PreserveSpacesIfNeeded(
                    run.TextEl,
                    newText);
            }
        }
    }
}
