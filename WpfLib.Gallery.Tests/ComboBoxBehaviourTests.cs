using System.Linq;
using FlaUI.Core.Definitions;
using Xunit;
using Xunit.Abstractions;

namespace WpfLib.Gallery.Tests
{
    /// <summary>
    /// What the combo boxes actually do, as opposed to what anyone assumes.
    ///
    /// These report rather than merely pass. Two questions were asked of them:
    /// are they editable, and does the drop-down open when they take focus?
    /// Both are library-wide defaults, so the answer is worth measuring before
    /// anything is changed.
    /// </summary>
    [Collection(nameof(GalleryCollection))]
    public class ComboBoxBehaviourTests
    {
        private readonly GalleryFixture _fixture;
        private readonly ITestOutputHelper _out;

        public ComboBoxBehaviourTests(GalleryFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _out = output;
        }

        [Fact]
        public void Reports_which_combo_boxes_are_editable()
        {
            var gallery = _fixture.Driver;
            gallery.ShowSection("Text input");

            var combos = gallery.SectionComboBoxes();
            Assert.NotEmpty(combos);

            foreach (var (combo, i) in combos.Select((c, i) => (c, i)))
            {
                // FlaUI surfaces this directly from UIA, which is also what a
                // screen reader sees: an editable combo exposes a Value pattern
                // and a child Edit, a selection-only one does not.
                _out.WriteLine($"section combo #{i}: IsEditable = {combo.IsEditable}");
            }

            // The theme picker is deliberately selection-only.
            _out.WriteLine($"theme picker: IsEditable = {gallery.ThemePicker().IsEditable}");
        }

        [Fact]
        public void Reports_whether_focus_opens_the_drop_down()
        {
            var gallery = _fixture.Driver;
            gallery.ShowSection("Text input");

            var combo = gallery.SectionComboBoxes().First();
            Assert.Equal(ExpandCollapseState.Collapsed, combo.ExpandCollapseState);

            combo.Focus();
            gallery.WaitIdle();

            _out.WriteLine($"after Focus(): ExpandCollapseState = {combo.ExpandCollapseState}");

            // Leave it as we found it, so the visual baselines are unaffected.
            if (combo.ExpandCollapseState == ExpandCollapseState.Expanded) combo.Collapse();
        }

        /// <summary>
        /// The Indicators section is excluded from the pixel baselines because
        /// its progress bar animates. It still gets checked, so it is not
        /// simply unwatched.
        /// </summary>
        [Fact]
        public void Indicators_section_renders_its_controls()
        {
            var gallery = _fixture.Driver;
            gallery.ShowSection("Indicators");

            var window = gallery.Window;
            Assert.NotEmpty(window.FindAllDescendants(cf => cf.ByControlType(ControlType.ProgressBar)));
            Assert.NotNull(window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Slider)));
            Assert.NotNull(window.FindFirstDescendant(cf => cf.ByName("Hover me for a tooltip")));
        }

        [Fact]
        public void Drop_down_opens_and_lists_its_items()
        {
            var gallery = _fixture.Driver;
            gallery.ShowSection("Text input");

            var combo = gallery.SectionComboBoxes().First();
            combo.Expand();
            gallery.WaitIdle();

            Assert.Equal(ExpandCollapseState.Expanded, combo.ExpandCollapseState);
            var items = combo.Items;
            _out.WriteLine($"drop-down items: {string.Join(", ", items.Select(i => i.Text))}");
            Assert.Equal(3, items.Length);

            combo.Collapse();
            gallery.WaitIdle();
            Assert.Equal(ExpandCollapseState.Collapsed, combo.ExpandCollapseState);
        }
    }
}
