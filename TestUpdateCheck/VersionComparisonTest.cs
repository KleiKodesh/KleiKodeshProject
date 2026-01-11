using System;

namespace TestUpdateCheck
{
    public static class VersionComparisonTest
    {
        public static void RunTests()
        {
            Console.WriteLine("=== Version Comparison Tests ===");
            
            // Test cases: (github, registry, expected result)
            var testCases = new[]
            {
                ("v1.0.32", "v1.0.31", 1),   // Update available
                ("v1.0.31", "v1.0.31", 0),   // Same version
                ("v1.0.30", "v1.0.31", -1),  // Registry newer
                ("1.0.32", "v1.0.31", 1),    // Mixed v prefix
                ("v2.0.0", "v1.9.99", 1),    // Major version bump
                ("v1.1.0", "v1.0.99", 1),    // Minor version bump
            };

            foreach (var (github, registry, expected) in testCases)
            {
                var result = CompareVersions(github, registry);
                var status = result == expected ? "✓" : "✗";
                var description = expected switch
                {
                    1 => "Update available",
                    0 => "Same version",
                    -1 => "Registry newer",
                    _ => "Unknown"
                };
                
                Console.WriteLine($"{status} {github} vs {registry} = {result} ({description})");
            }
        }

        private static int CompareVersions(string githubVersion, string registryVersion)
        {
            // Normalize versions by removing 'v' prefix if present
            var normalizedGithub = githubVersion?.TrimStart('v') ?? "";
            var normalizedRegistry = registryVersion?.TrimStart('v') ?? "";

            // Try to parse as semantic versions (e.g., "1.0.31")
            if (Version.TryParse(normalizedGithub, out var githubVer) && 
                Version.TryParse(normalizedRegistry, out var registryVer))
            {
                return githubVer.CompareTo(registryVer);
            }

            // Fallback to string comparison if not valid semantic versions
            return string.Compare(normalizedGithub, normalizedRegistry, StringComparison.OrdinalIgnoreCase);
        }
    }
}