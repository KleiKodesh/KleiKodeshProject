using MessagePack;

namespace KitveiHakodesh.Core
{
    /// <summary>
    /// MessagePack formatters for every <c>[MessagePackObject]</c> type in Core.
    ///
    /// Core needs its own because MessagePack's generator is a SOURCE generator: it emits
    /// formatters for the types in the compilation it runs in and cannot see across an
    /// assembly boundary. Without this, the Service's resolver would find no formatter for a
    /// Core model and MessagePack would fall back to reflection — which native AOT cannot do.
    ///
    /// Each host composes this with its own: the Service does
    /// <c>CompositeResolver.Create(KhsMsgPackResolver.Instance, CoreMessagePackResolver.Instance,
    /// StandardResolver.Instance)</c>. StandardResolver supplies the primitive and collection
    /// formatters; it is AOT-safe.
    /// </summary>
    [GeneratedMessagePackResolver]
    public partial class CoreMessagePackResolver
    {
    }
}
