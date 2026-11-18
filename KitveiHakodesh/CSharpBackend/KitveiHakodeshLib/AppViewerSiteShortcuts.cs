using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Reflection;
using System.Windows.Forms;

namespace KitveiHakodeshLib
{
    // App keyboard shortcuts while a Torah website (web-view tab) has focus.
    //
    // A key pressed inside a site's iframe goes to the site's document, which the app cannot see
    // (different origin). The host sees every Ctrl / Alt / F-key press first
    // (CoreWebView2Controller.AcceleratorKeyPressed, raised whichever frame has focus), so while
    // the page reports that a site frame has focus ("siteFrameFocus"), an APP shortcut key is
    // taken here - Handled, so neither the site nor the browser acts on it - and pushed to the page
    // as a "siteFrameShortcut" event, which replays it as a keydown on the app's window.
    //
    // Text-editing keys (Ctrl+A/C/V/X/Z/Y, arrows, Backspace...), Ctrl+P, plain Ctrl+F and AltGr
    // (Ctrl+Alt) stay with the site.
    // The same key rule lives in vue-frontend/src/features/websites/siteFrameShortcutKeys.ts
    // (the dev browser's version of this); keep the two in step.
    public partial class AppViewer
    {
        private bool _siteFrameHasFocus;

        /// <summary>Set by the page's "siteFrameFocus" message.</summary>
        internal void SetSiteFrameFocus(bool focused) => _siteFrameHasFocus = focused;

        private void AttachSiteFrameShortcuts()
        {
            // The WinForms control keeps its controller private; AcceleratorKeyPressed lives there.
            var field = typeof(WebView2).GetField("_coreWebView2Controller", BindingFlags.Instance | BindingFlags.NonPublic);
            if (!(field?.GetValue(_webView) is CoreWebView2Controller controller))
            {
                Log("site shortcuts: no CoreWebView2Controller - shortcuts inside websites stay with the site");
                return;
            }
            controller.AcceleratorKeyPressed += OnAcceleratorKeyWhileSiteFocused;
        }

        private void OnAcceleratorKeyWhileSiteFocused(object sender, CoreWebView2AcceleratorKeyPressedEventArgs e)
        {
            if (!_siteFrameHasFocus) return;
            if (e.KeyEventKind != CoreWebView2KeyEventKind.KeyDown && e.KeyEventKind != CoreWebView2KeyEventKind.SystemKeyDown)
                return;

            var key = (Keys)e.VirtualKey;
            Keys modifiers = Control.ModifierKeys;
            bool ctrl = (modifiers & Keys.Control) != 0;
            bool shift = (modifiers & Keys.Shift) != 0;
            bool alt = (modifiers & Keys.Alt) != 0;

            string code = KeyboardEventCode(key);
            if (code == null || !IsAppShortcut(code, ctrl, alt, shift)) return;

            e.Handled = true;
            _bridge?.PushEvent(new
            {
                @event = "siteFrameShortcut", code, key = KeyboardEventKey(key, shift),
                repeat = e.PhysicalKeyStatus.WasKeyDown != 0, ctrlKey = ctrl, shiftKey = shift, altKey = alt,
            });
        }

        /// <summary>The app's shortcut keys - see siteFrameShortcutKeys.ts.</summary>
        private static bool IsAppShortcut(string code, bool ctrl, bool alt, bool shift)
        {
            if (code.Length >= 2 && code[0] == 'F' && char.IsDigit(code[1])) return true; // F1-F12
            if (ctrl && alt) return false; // AltGr: typing a character (shekel sign, maqaf...)
            if (alt) return code == "ArrowLeft" || code == "ArrowRight" || code == "KeyW" || code == "KeyX"; // Back/Forward, history
            if (!ctrl) return false;
            if (code == "KeyF") return shift; // Ctrl+Shift+F = app fullscreen; Ctrl+F = the site's find
            switch (code)
            {
                // Editing and print stay with the site.
                case "KeyA": case "KeyC": case "KeyV": case "KeyX": case "KeyZ": case "KeyY": case "KeyP":
                    return false;
            }
            return code.StartsWith("Key", StringComparison.Ordinal) || code.StartsWith("Digit", StringComparison.Ordinal)
                || code == "Tab" || code == "PageUp" || code == "PageDown" || code == "Equal" || code == "Minus" || code == "Backslash";
        }

        /// <summary>KeyboardEvent.code for the keys IsAppShortcut can take; null for any other.</summary>
        private static string KeyboardEventCode(Keys key)
        {
            if (key >= Keys.A && key <= Keys.Z) return "Key" + (char)('A' + (key - Keys.A));
            if (key >= Keys.D0 && key <= Keys.D9) return "Digit" + (char)('0' + (key - Keys.D0));
            if (key >= Keys.F1 && key <= Keys.F12) return "F" + (1 + (key - Keys.F1));
            switch (key)
            {
                case Keys.Left: return "ArrowLeft";
                case Keys.Right: return "ArrowRight";
                case Keys.Tab: return "Tab";
                case Keys.PageUp: return "PageUp";
                case Keys.PageDown: return "PageDown";
                case Keys.Oemplus: return "Equal";
                case Keys.OemMinus: return "Minus";
                case Keys.Oem5: return "Backslash";
                default: return null;
            }
        }

        private static string KeyboardEventKey(Keys key, bool shift)
        {
            if (key >= Keys.A && key <= Keys.Z)
            {
                char c = (char)('a' + (key - Keys.A));
                return (shift ? char.ToUpperInvariant(c) : c).ToString();
            }
            if (key >= Keys.D0 && key <= Keys.D9) return ((char)('0' + (key - Keys.D0))).ToString();
            return KeyboardEventCode(key) ?? "";
        }
    }
}
