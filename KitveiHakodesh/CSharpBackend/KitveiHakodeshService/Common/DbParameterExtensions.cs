using System.Data.Common;

namespace KitveiHakodeshService.Common;

/// <summary>
/// AddWithValue for the ADO.NET base type.
///
/// The catalog index and the DB content stamp are SHARED SOURCE: the same files compile into
/// the net10 service (Microsoft.Data.Sqlite) and the net48 hosted app (System.Data.SQLite),
/// so their DB code is written against DbConnection/DbCommand rather than either provider's
/// concrete types. AddWithValue is the one convenience that lives only on the concrete
/// parameter collections, so it is supplied here for the base type instead.
///
/// Implemented via CreateParameter() rather than by naming a provider's parameter class, which
/// is what keeps this file itself provider-agnostic — the command hands back whichever
/// parameter type its own provider uses.
/// </summary>
internal static class DbParameterExtensions
{
    public static void AddWithValue(this DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
