using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace KitveiHakodeshLib.Bridge
{
    /// <summary>
    /// Writes the host's small ad-hoc message payloads (anonymous objects, lists, strings,
    /// numbers, booleans) as JSON with <see cref="Utf8JsonWriter"/> directly.
    ///
    /// Not <see cref="JsonSerializer"/>: that builds reflection metadata and instantiates
    /// generic converters for every payload type the first time it sees it - the host has
    /// ~75 distinct anonymous shapes - and on .NET Framework all of that code is JIT-compiled
    /// too. Measured at startup it was 836 of the 1129 methods compiled, running on the UI
    /// thread while the page's first requests waited. Plain property enumeration is all these
    /// payloads need.
    /// </summary>
    internal static class JsonPayloadWriter
    {
        public static string ToJson(object payload)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream))
                    WriteValue(writer, payload);
                return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
            }
        }

        private static void WriteValue(Utf8JsonWriter w, object value)
        {
            switch (value)
            {
                case null: w.WriteNullValue(); return;
                case string s: w.WriteStringValue(s); return;
                case bool b: w.WriteBooleanValue(b); return;
                case int i: w.WriteNumberValue(i); return;
                case long l: w.WriteNumberValue(l); return;
                case double d: w.WriteNumberValue(d); return;
                case float f: w.WriteNumberValue(f); return;
                case decimal m: w.WriteNumberValue(m); return;
                case uint ui: w.WriteNumberValue(ui); return;
                case ulong ul: w.WriteNumberValue(ul); return;
                case short sh: w.WriteNumberValue(sh); return;
                case ushort ush: w.WriteNumberValue(ush); return;
                case byte by: w.WriteNumberValue(by); return;
                case sbyte sb: w.WriteNumberValue(sb); return;
                case byte[] bytes: w.WriteBase64StringValue(bytes); return; // as JsonSerializer does; before the IEnumerable case
                case char c: w.WriteStringValue(c.ToString()); return;
                case DateTime dt: w.WriteStringValue(dt); return;
                case DateTimeOffset dto: w.WriteStringValue(dto); return;
                case Guid g: w.WriteStringValue(g); return;
                case Enum e: w.WriteStringValue(e.ToString()); return;
                case JsonElement je: je.WriteTo(w); return;
                case IDictionary dict:
                    w.WriteStartObject();
                    foreach (DictionaryEntry entry in dict)
                    {
                        w.WritePropertyName(Convert.ToString(entry.Key));
                        WriteValue(w, entry.Value);
                    }
                    w.WriteEndObject();
                    return;
                case IEnumerable list:
                    w.WriteStartArray();
                    foreach (object item in list) WriteValue(w, item);
                    w.WriteEndArray();
                    return;
            }

            // Any other object: its public readable properties, names as declared (the
            // payloads are written camelCase at the call sites, as the page expects).
            w.WriteStartObject();
            foreach (PropertyInfo p in Properties(value.GetType()))
            {
                w.WritePropertyName(p.Name);
                WriteValue(w, p.GetValue(value));
            }
            w.WriteEndObject();
        }

        private static readonly Dictionary<Type, PropertyInfo[]> PropertyCache = new Dictionary<Type, PropertyInfo[]>();

        private static PropertyInfo[] Properties(Type type)
        {
            lock (PropertyCache)
            {
                if (!PropertyCache.TryGetValue(type, out PropertyInfo[] props))
                {
                    var list = new List<PropertyInfo>();
                    foreach (PropertyInfo p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                        if (p.CanRead && p.GetIndexParameters().Length == 0) list.Add(p);
                    PropertyCache[type] = props = list.ToArray();
                }
                return props;
            }
        }
    }
}
