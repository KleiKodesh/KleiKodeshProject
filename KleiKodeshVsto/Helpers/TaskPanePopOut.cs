using Dark.Net;
using KitveiHakodeshLib.Settings;
using Microsoft.Office.Tools;
using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KleiKodesh.Helpers
{
    public sealed class TaskPanePopOut
    {
        readonly UserControl _host;
        readonly CustomTaskPane _pane;
        Control _content;
        Form _form;

        public TaskPanePopOut(UserControl host, CustomTaskPane pane)
        {
            _host = host;
            _pane = pane;

            Register(pane, this);
        }

        /// <summary>
        /// True while the content lives in a floating window instead of the task pane.
        /// This is the single authoritative answer to "is it popped out?" - it reads the
        /// form, which is the thing that actually holds the content. Callers used to
        /// infer this from Visible flags on the host and the pane, which are cross-driven
        /// by this class and so cannot distinguish "hidden because popped out" from
        /// "hidden because the user closed the pane".
        /// </summary>
        public bool IsPoppedOut => _form != null && !_form.IsDisposed;

        // ── Pop-out registry ──────────────────────────────────────────────────
        // TaskPaneManager.Show needs to know whether a pane it is about to reveal is
        // currently popped out, but the handler that knows lives here and was never
        // reachable from there. A ConditionalWeakTable keyed on the pane keeps the
        // lookup without extending any object's lifetime: when Word drops the pane,
        // the entry goes with it.
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CustomTaskPane, TaskPanePopOut>
            _byPane = new System.Runtime.CompilerServices.ConditionalWeakTable<CustomTaskPane, TaskPanePopOut>();

        static void Register(CustomTaskPane pane, TaskPanePopOut handler)
        {
            if (pane == null) return;

            // A pane must map to exactly one handler. Two handlers on one pane both
            // subscribe to its events and both try to reparent the same content, so the
            // second to run finds the content already moved. Detach any predecessor
            // rather than leaving it subscribed and invisible - its event registrations
            // would otherwise keep it alive and racing.
            TaskPanePopOut existing;
            if (_byPane.TryGetValue(pane, out existing) && existing != handler)
                // ClosePopOut, not Detach: once the table slot is taken, nothing
                // references the predecessor, so a floating window it still held could
                // never be closed or popped back in. The incoming handler cannot adopt
                // it either - it reads content from the host, which the predecessor
                // emptied.
                existing.ClosePopOut();

            _byPane.Remove(pane);
            _byPane.Add(pane, handler);
        }

        /// <summary>
        /// Closes the floating window and disposes its content, without trying to put
        /// anything back. For use when the pane's document is closing: the host is about
        /// to be disposed, so there is nothing to pop in to.
        /// </summary>
        public void ClosePopOut()
        {
            // Detach first and unconditionally: a handler that is not popped out still
            // holds a live pane subscription, and this is the point at which it stops
            // being the handler for that pane.
            Detach();
            if (!IsPoppedOut) return;

            try
            {
                // Save the window's geometry before tearing it down. Dispose does not
                // raise FormClosing, so the lambda that normally persists bounds never
                // runs on this path - the user would silently lose a pop-out window they
                // had sized and placed.
                SaveFormBounds();

                // Dispose rather than Close: Close is a no-op on a form whose handle was
                // never created, which would leave a live window with nothing referencing
                // it once _form is cleared below. Disposing the form disposes the content
                // with it, since the content is still parented to the form - Detach
                // removed the handler that would otherwise have reparented it.
                _form.Dispose();
            }
            catch (Exception ex) { Console.WriteLine("[TaskPanePopOut] " + ex.Message); }
            finally
            {
                _form = null;
                _content = null;
            }
        }

        void SaveFormBounds()
        {
            try
            {
                if (_form != null && !_form.IsDisposed)
                    FormSettingsHelper.SaveFormSettings(_form, "KleiKodesh", _content?.AccessibleName);
            }
            catch (Exception ex) { Console.WriteLine("[TaskPanePopOut] " + ex.Message); }
        }

        /// <summary>
        /// Stops this handler reacting to its pane and to its form's closing. The
        /// FormClosing lambda that saves window bounds is anonymous and stays subscribed;
        /// it only reads the form's own geometry, so letting it run is harmless.
        /// </summary>
        void Detach()
        {
            if (_pane != null)
                try { _pane.VisibleChanged -= OnPaneVisibilityChanged; } catch { }
            if (_form != null && !_form.IsDisposed)
                try { _form.FormClosing -= OnFormClosing; } catch { }
        }

        /// <summary>
        /// The pop-out handler owning <paramref name="pane"/>, or null if the pane was
        /// created without pop-out behaviour.
        /// </summary>
        public static TaskPanePopOut For(CustomTaskPane pane)
        {
            TaskPanePopOut handler;
            return pane != null && _byPane.TryGetValue(pane, out handler) ? handler : null;
        }

        /// <summary>
        /// Brings the floating window forward, restoring it if minimised. Called instead
        /// of showing the task pane when content is popped out - without it, driving a
        /// popped-out pane (a context-menu search, say) looks like nothing happened at
        /// all when the window is buried behind Word or minimised.
        /// </summary>
        public void FocusPopOutWindow()
        {
            if (!IsPoppedOut) return;
            try
            {
                if (_form.WindowState == FormWindowState.Minimized)
                    _form.WindowState = FormWindowState.Normal;
                _form.Activate();
            }
            catch { /* best-effort - focus is a courtesy, not a correctness requirement */ }
        }

        Control GetContent()
        {
            // If content was provided in constructor and is valid, use it
            if (_content != null && !_content.IsDisposed)
                return _content;

            // Otherwise, get the first child control from host (for ZayitViewerHost case)
            if (_host.Controls.Count > 0)
            {
                _content = _host.Controls[0];
                return _content;
            }

            return null;
        }

        public void Toggle(bool goFullScreen = false)
        {
            if (_form == null || _form.IsDisposed)
                PopOut(goFullScreen);
            else
                PopIn();
        }

        void PopOut(bool goFullScreen = false)
        {
            try
            {
                if (_form != null && !_form.IsDisposed)
                    return; // Already popped out

                var content = GetContent();
                if (content == null)
                {
                    Console.WriteLine("[TaskPanePopOut] No content to pop out");
                    return;
                }

                Console.WriteLine("[TaskPanePopOut] Popping out");

                // Remove content from host
                if (_host.Controls.Contains(content))
                    _host.Controls.Remove(content);

                // Create popout window
                _form = CreateForm();
                content.Dock = DockStyle.Fill;
                _form.Controls.Add(content);

                _form.Load += (_, __) => { FormSettingsHelper.LoadFormSettings(_form, "KleiKodesh", content.AccessibleName); };
                _form.FormClosing += (_, __) => { FormSettingsHelper.SaveFormSettings(_form, "KleiKodesh", content.AccessibleName); };

                // ── DarkNet title bar theming ─────────────────────────────────────
                // In the VSTO (Word) context, AppViewer's child WebView2 is moved
                // directly into this new Form — AppViewer itself stays in the task
                // pane host and never becomes a child of this Form.  This means
                // AppViewer's own OnParentChanged/OnHostFormHandleCreated hooks never
                // fire for this window, so we apply the theme here instead.
                //
                // We subscribe to HandleCreated BEFORE calling SetOwner, because
                // SetOwner accesses _form.Handle which forces immediate HWND creation
                // and fires HandleCreated synchronously on that line.  If we subscribed
                // after SetOwner, the event would already have fired and been missed.
                //
                // If the handle was already created before our subscription (can happen
                // on repeated popout), we call the handler directly as a fallback.
                _form.HandleCreated += OnPopoutFormHandleCreated;
                SetOwner(_form.Handle);
                if (_form.IsHandleCreated)
                    OnPopoutFormHandleCreated(_form, EventArgs.Empty);

                _form.FormClosing += OnFormClosing;
                _pane.VisibleChanged += OnPaneVisibilityChanged;

                _pane.Visible = false;
                _form.Show();

                // Hiding the pane hides the host control that stayed behind - and for
                // AppViewer that host suspends the WebView2 renderer on VisibleChanged,
                // hiding the very control we just moved into this form. Un-hide it after
                // the pane goes down. Previously a context-menu search happened to undo
                // this by setting pane.Visible = true (which showed an empty pane - the
                // bug); now that Reveal focuses this window instead, nothing else would.
                RestoreContentVisibility(content);

                // If requested, enter fullscreen mode immediately after showing
                if (goFullScreen)
                {
                    _form.FormBorderStyle = FormBorderStyle.None;
                    _form.WindowState = FormWindowState.Maximized;
                }
            }
            catch (Exception ex) { Console.WriteLine(ex.Message); }
        }

        /// <summary>
        /// Makes the popped-out content visible again after the task pane hid its former
        /// host. WebView2 auto-resumes a suspended renderer when its own control becomes
        /// visible, so restoring Visible is enough - no explicit resume call is needed.
        /// </summary>
        static void RestoreContentVisibility(Control content)
        {
            try
            {
                if (content != null && !content.IsDisposed && !content.Visible)
                    content.Visible = true;
            }
            catch (Exception ex) { Console.WriteLine("[TaskPanePopOut] " + ex.Message); }
        }

        void PopIn()
        {
            if (_form == null || _form.IsDisposed)
                return; // Already popped in

            var content = GetContent();
            if (content == null)
            {
                Console.WriteLine("[TaskPanePopOut] No content to pop in");
                return;
            }

            Console.WriteLine("[TaskPanePopOut] Popping in");

            _pane.VisibleChanged -= OnPaneVisibilityChanged;
            _form.FormClosing -= OnFormClosing;

            // Remove content from form
            if (_form.Controls.Contains(content))
                _form.Controls.Remove(content);

            // Add content back to host
            if (!_host.IsDisposed)
            {
                content.Dock = DockStyle.Fill;
                _host.Controls.Add(content);
            }
            else
            {
                // The host went away while we were popped out (its document closed).
                // There is nowhere to put the content back, and it is no longer parented
                // to the form either - dispose it rather than leaking a live WebView2.
                content.Dispose();
                _content = null;
            }

            if (!_form.IsDisposed)
                _form.Close();

            _form = null;

            // The host may have no HWND yet - it has been sitting empty while the
            // content lived in the form, and Word does not necessarily realise an empty
            // task pane control. BeginInvoke on a handle-less control throws, which used
            // to abort the pop-in silently and strand the pane hidden.
            OnHost(() => _pane.Visible = true);
        }

        void OnPopoutFormHandleCreated(object sender, EventArgs e)
        {
            var form = (Form)sender;
            form.HandleCreated -= OnPopoutFormHandleCreated;
            bool isDark = AppSettings.LoadDarkMode();
            try
            {
                // SetCurrentProcessTheme(Auto) must be called immediately before
                // SetWindowThemeForms in the Word process.  Without it, DarkNet
                // silently ignores SetWindowThemeForms(Light) when the OS is also in
                // light mode — the window stays unregistered and subsequent live-toggle
                // calls have no effect.  Calling Auto here resets DarkNet's process
                // state so the explicit per-window call always takes effect.
                DarkNet.Instance.SetCurrentProcessTheme(Theme.Auto);
                DarkNet.Instance.SetWindowThemeForms(form, isDark ? Theme.Dark : Theme.Light);
            }
            catch { /* best-effort — title bar theming is non-critical */ }

            // Register this form with AppViewer so live theme toggles from Vue
            // (HandleSetTheme) also update this popout window's title bar.
            var applyThemeMethod = _host.GetType().GetMethod("ApplyTitleBarThemeToForm");
            applyThemeMethod?.Invoke(_host, new object[] { form });
        }

        void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            // When the user closes the popout window, put the content back in the pane.
            if (!_host.IsDisposed)
                PopIn();
        }

        void OnPaneVisibilityChanged(object sender, EventArgs e)
        {
            // The user reopened the pane from the ribbon while the content was popped
            // out - an empty pane is not what they asked for, so pop the content back in.
            if (_pane.Visible && !_host.IsDisposed)
                OnHost(PopIn);
        }

        /// <summary>
        /// Defers <paramref name="action"/> to the next turn of the message loop.
        ///
        /// The asynchrony is the point, not the thread hop - every caller is already on
        /// the UI thread. These actions run from inside Word's own pane-visibility
        /// callbacks, and both re-entering PopIn and setting a CustomTaskPane's Visible
        /// from within its VisibleChanged notification are reentrant COM calls that Word
        /// can reject. Letting the current notification finish first avoids that.
        ///
        /// BeginInvoke needs a handle, and the host may not have one: it has been sitting
        /// empty while the content lived in the form, and Word does not necessarily
        /// realise an empty task pane control. Post to the WinForms synchronisation
        /// context in that case rather than running inline.
        /// </summary>
        void OnHost(Action action)
        {
            try
            {
                if (_host != null && !_host.IsDisposed && _host.IsHandleCreated)
                {
                    _host.BeginInvoke(action);
                    return;
                }

                var context = System.Threading.SynchronizationContext.Current;
                if (context != null)
                    context.Post(_ => Run(action), null);
                else
                    Run(action);
            }
            catch (Exception ex) { Console.WriteLine("[TaskPanePopOut] " + ex.Message); }
        }

        // Posted work runs outside the caller's try, so it carries its own.
        static void Run(Action action)
        {
            try { action(); }
            catch (Exception ex) { Console.WriteLine("[TaskPanePopOut] " + ex.Message); }
        }

        static Form CreateForm() => new Form
        {
            Width = 570,
            Height = 850,
            StartPosition = FormStartPosition.CenterParent,
            ShowInTaskbar = false,
            Icon = File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KleiKodesh_Main.ico"))
        ? new Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KleiKodesh_Main.ico"))
        : null
        };

        void SetOwner(IntPtr formHandle)
        {
            var word = new IntPtr(Globals.ThisAddIn.Application.ActiveWindow.Hwnd);
            SetWindowLong(formHandle, GWL_HWNDPARENT, word.ToInt32());
        }

        const int GWL_HWNDPARENT = -8;

        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
