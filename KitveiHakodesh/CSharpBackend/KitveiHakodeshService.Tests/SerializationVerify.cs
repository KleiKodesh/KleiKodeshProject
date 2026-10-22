// Verification that every wire type is serialized by SOURCE-GENERATED formatters only.
//
// Run: dotnet run -c Release -- --serialization
//
// Why this exists: the Service is published native AOT, where MessagePack's reflection / IL-emit
// fallback does not work. A type the generated resolvers do not cover still serializes fine on
// the ordinary runtime these tests run on, so a plain round trip through MsgPack.Options proves
// nothing - the very bug this guards (catalogTocSearch answering "Failed to serialize
// CatalogTocSearchResult value" in the installed service, while every test passed) was invisible
// to all of them. So these checks run through MsgPack.Generated - the exact generated resolvers
// the service composes - plus MessagePack's built-in primitive formatters and NOTHING else: no
// StandardResolver, hence no dynamic object formatter, no reflection generic formatter and no
// assembly-attribute discovery to hide a missing registration.
//
//   S1. Every [MessagePackObject] type in Core and in the Service has a generated formatter.
//   S2. CatalogTocSearchResult WITH hits serializes and round-trips, with the [IgnoreMember]
//       properties left off the wire.
//   S3. An instance of every wire type (all members at their defaults) serializes and
//       deserializes - this is what exposes a member type, such as List<Hit>, that no
//       generated resolver registered.
using System.Reflection;
using KitveiHakodesh.Core.SeforimDbCatalog;
using KitveiHakodeshService.Ipc;
using MessagePack;
using MessagePack.Resolvers;

namespace KitveiHakodeshService.Tests;

internal static class SerializationVerify
{
    private static int _failures;

    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what);
        if (!ok) _failures++;
    }

    /// <summary>The generated resolvers the service composes, with only primitives behind them.</summary>
    private static readonly MessagePackSerializerOptions Strict = MessagePackSerializerOptions.Standard.WithResolver(
        CompositeResolver.Create(MsgPack.Generated, BuiltinResolver.Instance));

    public static int Run()
    {
        _failures = 0;

        var wireTypes = new[] { typeof(CatalogTocHit).Assembly, typeof(MsgPack).Assembly }
            .SelectMany(a => a.GetTypes())
            .Where(t => t.GetCustomAttribute<MessagePackObjectAttribute>() is not null && !t.IsGenericTypeDefinition)
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();

        // ── S1 ───────────────────────────────────────────────────────────────────
        var getFormatter = typeof(IFormatterResolver).GetMethod(nameof(IFormatterResolver.GetFormatter))!;
        var missing = wireTypes.Where(t => getFormatter.MakeGenericMethod(t).Invoke(MsgPack.Generated, null) is null).ToList();
        Console.WriteLine($"      {wireTypes.Count} [MessagePackObject] types in Core and Service");
        foreach (var t in missing) Console.WriteLine("      no generated formatter: " + t.FullName);
        Check(missing.Count == 0, "S1  every wire type has a generated formatter");

        // ── S2 ───────────────────────────────────────────────────────────────────
        var result = new CatalogTocSearchResult
        {
            Ready = true,
            Results =
            {
                new CatalogTocHit { BookId = 7, LineIndex = 12, FullTocPath = "בבא מציעא / פרק א", Level = 1, TreeOrder = 99L, IsLiteral = true, StructureId = 3, WordsFound = 2 },
                new CatalogTocHit { BookId = 8, LineIndex = -1, FullTocPath = "בבא מציעא", Level = 0, TreeOrder = 100L },
            },
        };
        byte[]? bytes = null;
        try { bytes = MessagePackSerializer.Serialize(result, Strict); }
        catch (Exception e) { Console.WriteLine("      " + e.GetType().Name + ": " + e.Message); }
        Check(bytes is not null, "S2a CatalogTocSearchResult with hits serializes");
        if (bytes is not null)
        {
            var back = MessagePackSerializer.Deserialize<CatalogTocSearchResult>(bytes, Strict);
            Check(back.Ready && back.Results.Count == 2 && back.Results[0].BookId == 7 && back.Results[0].LineIndex == 12
                  && back.Results[0].FullTocPath == "בבא מציעא / פרק א" && back.Results[0].TreeOrder == 99L && back.Results[1].LineIndex == -1,
                "S2b round trip keeps Ready / Results / hit fields");
            Check(!back.Results[0].IsLiteral && back.Results[0].StructureId == 0 && back.Results[0].WordsFound == 0,
                "S2c [IgnoreMember] properties stay off the wire");
        }

        // ── S3 ───────────────────────────────────────────────────────────────────
        var failed = new List<string>();
        int tried = 0;
        foreach (var t in wireTypes)
        {
            if (t.GetConstructor(Type.EmptyTypes) is null || t.IsAbstract) continue;
            tried++;
            try
            {
                object instance = Activator.CreateInstance(t)!;
                byte[] b = MessagePackSerializer.Serialize(t, instance, Strict);
                MessagePackSerializer.Deserialize(t, b, Strict);
            }
            catch (Exception e) { failed.Add($"{t.FullName}: {e.GetType().Name}: {e.Message}"); }
        }
        foreach (var f in failed) Console.WriteLine("      " + f);
        Check(failed.Count == 0, $"S3  a default instance of each of {tried} wire types round-trips");

        Console.WriteLine(_failures == 0 ? "\nALL PASS" : $"\n{_failures} FAILED");
        return _failures;
    }
}
