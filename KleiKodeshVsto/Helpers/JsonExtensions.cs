using System.Text.Json;

namespace KleiKodesh.Helpers
{
    public static class JsonExtensions
    {
        public static string GetStringProperty(this JsonElement element, params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
                    return prop.GetString();
            }
            return null;
        }

        public static bool? GetBoolProperty(this JsonElement element, params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                if (element.TryGetProperty(name, out var prop))
                {
                    if (prop.ValueKind == JsonValueKind.Null)
                        return null;
                    if (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False)
                        return prop.GetBoolean();
                }
            }
            return null; // Return null when property is not specified
        }

        public static bool GetBoolPropertyNonNullable(this JsonElement element, params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                if (element.TryGetProperty(name, out var prop) &&
                    (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False))
                    return prop.GetBoolean();
            }
            return false; // Return false as default for non-nullable booleans
        }

        public static int? GetIntProperty(this JsonElement element, params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                if (element.TryGetProperty(name, out var prop))
                {
                    if (prop.ValueKind == JsonValueKind.Null)
                        return null;
                    if (prop.TryGetInt32(out var value))
                        return value;
                }
            }
            return null;
        }

        public static int? GetNullableIntProperty(this JsonElement element, params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                if (element.TryGetProperty(name, out var prop))
                {
                    if (prop.ValueKind == JsonValueKind.Null)
                        return null;
                    if (prop.TryGetInt32(out var value))
                        return value;
                }
            }
            return null; // Return null when property is not specified
        }

        public static float? GetFloatProperty(this JsonElement element, params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                if (element.TryGetProperty(name, out var prop))
                {
                    // Handle null values
                    if (prop.ValueKind == JsonValueKind.Null)
                        return null;

                    if (prop.TryGetSingle(out var value))
                        return value;

                    // Try to parse as int if single fails
                    if (prop.TryGetInt32(out var intValue))
                        return intValue;
                }
            }
            return null; // Return null when property is not specified
        }

        public static JsonElement? GetObjectProperty(this JsonElement element, params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Object)
                    return prop;
            }
            return null;
        }
    }
}
