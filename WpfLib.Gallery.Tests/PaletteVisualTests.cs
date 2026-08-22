using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using VerifyXunit;
using Xunit;

namespace WpfLib.Gallery.Tests
{
    /// <summary>
    /// One baseline image per section per theme.
    ///
    /// The images come from the gallery's own "--render" mode, which draws the
    /// visual tree with RenderTargetBitmap. They are NOT screenshots.
    ///
    /// That distinction is the whole reason these tests are trustworthy. An
    /// earlier version photographed the screen through FlaUI's Capture.Element,
    /// and on a machine somebody is using it repeatedly photographed whatever
    /// window had stolen focus - the editor, once the Task View overlay. Six of
    /// thirty-six failed on a re-run against baselines captured minutes before.
    /// Drawing the visual tree does not care what is in front, so the output is
    /// reproducible and a run does not take over the desktop.
    ///
    /// The renderer applies a theme and then a section, repeatedly, on one
    /// window. So these also cover switching theme while a section is already
    /// realised, which is what a person does and is a different code path from
    /// rendering a section fresh.
    ///
    /// These are the defects this suite exists to catch, all of which shipped
    /// during the migration and were only found by a human looking: a checkbox
    /// tick that never rendered, a drop-down that painted black, menu headers
    /// that were white on white.
    ///
    /// Baselines are machine specific - font rendering and DPI differ - so a
    /// first run elsewhere needs its own baselines accepted.
    /// </summary>
    [Collection(nameof(GalleryCollection))]
    public class PaletteVisualTests
    {
        private readonly RenderedSnapshots _snapshots;

        public PaletteVisualTests(RenderedSnapshots snapshots) => _snapshots = snapshots;

        public static TheoryData<string, string> SectionsAndThemes()
        {
            var data = new TheoryData<string, string>();
            // "Indicators" is absent on purpose: it holds an indeterminate
            // ProgressBar, which sweeps forever, so no two renders of it can be
            // identical. Pixel comparison is the wrong instrument for animation.
            // ComboBoxBehaviourTests checks that section structurally instead.
            foreach (var section in new[]
                     {
                         "Buttons", "Selection", "Text input", "Type",
                         "Lists & trees", "Containers", "Menus", "Colour tokens",
                     })
            foreach (var theme in new[]
                     {
                         "Office White", "Office Light Gray", "Office Dark Gray", "Office Black",
                     })
                data.Add(section, theme);

            return data;
        }

        [Theory]
        [MemberData(nameof(SectionsAndThemes))]
        public Task Section_looks_the_same(string section, string theme)
        {
            var png = _snapshots.PathFor(section, theme);

            return Verifier.VerifyFile(png)
                           .UseDirectory("Baselines")
                           .UseFileName($"{Slug(section)}.{Slug(theme)}");
        }

        internal static string Slug(string s) =>
            s.Replace(" & ", "-").Replace(' ', '-').ToLowerInvariant();
    }

    /// <summary>
    /// Runs the gallery once in "--render" mode and keeps the output directory
    /// for the whole test run. One process launch produces all 36 images in
    /// about two seconds, against roughly a minute of driving the live UI.
    /// </summary>
    public sealed class RenderedSnapshots : IDisposable
    {
        private readonly string _directory;

        public RenderedSnapshots()
        {
            _directory = Path.Combine(Path.GetTempPath(), "wpflib-gallery-snapshots-" + Guid.NewGuid().ToString("N"));

            var exe = GalleryDriver.GalleryExePath();
            var process = Process.Start(new ProcessStartInfo(exe, $"--render \"{_directory}\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
            }) ?? throw new InvalidOperationException("Could not start " + exe);

            process.WaitForExit(120_000);

            if (!Directory.Exists(_directory))
                throw new InvalidOperationException("The gallery produced no snapshots in " + _directory);
        }

        public string PathFor(string section, string theme)
        {
            var path = Path.Combine(_directory,
                $"{PaletteVisualTests.Slug(section)}.{PaletteVisualTests.Slug(theme)}.png");

            if (!File.Exists(path))
                throw new FileNotFoundException($"No snapshot rendered for '{section}' / '{theme}'.", path);

            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(_directory, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// One rendered set and one live gallery, shared by every test in the run.
    /// </summary>
    [CollectionDefinition(nameof(GalleryCollection))]
    public class GalleryCollection : ICollectionFixture<RenderedSnapshots>, ICollectionFixture<GalleryFixture> { }

    /// <summary>
    /// The live window, for the tests that have to interact rather than look.
    /// </summary>
    public sealed class GalleryFixture : IDisposable
    {
        public GalleryDriver Driver { get; } = new GalleryDriver();
        public void Dispose() => Driver.Dispose();
    }
}
