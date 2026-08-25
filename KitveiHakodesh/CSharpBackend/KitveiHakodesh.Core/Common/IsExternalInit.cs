// C# records and `init` accessors need System.Runtime.CompilerServices.IsExternalInit at
// COMPILE time only — the compiler emits a modreq referencing it and never looks for it
// again. .NET 5+ ships it; net48 does not, so records would be a modern-leg-only feature
// without this. Declaring it ourselves is the documented workaround, and it is compiled
// into the net48 leg alone so the modern leg keeps the framework's own type (two copies of
// the same type name in one assembly is a compile error).
//
// This is the ONLY thing in Core that exists to bridge the two legs. Nothing calls it.

#if NETFRAMEWORK
namespace System.Runtime.CompilerServices
{
    using System.ComponentModel;

    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit
    {
    }
}
#endif
