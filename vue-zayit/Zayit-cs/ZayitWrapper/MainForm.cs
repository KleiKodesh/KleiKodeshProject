using System;
using System.Windows.Forms;
using Zayit.Viewer;

namespace ZayitWrapper
{
    public partial class MainForm : Form
    {
        private ZayitViewerHost _zayitViewerHost;

        public MainForm()
        {
            InitializeComponent();
            
            // Set AutoScaleMode for better DPI handling
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            
            InitializeZayitViewer();
        }

        private void InitializeZayitViewer()
        {
            try
            {
                // Create the ZayitViewerHost which contains the WebView
                _zayitViewerHost = new ZayitViewerHost();
                _zayitViewerHost.Dock = DockStyle.Fill;
                
                // Add to the form
                this.Controls.Add(_zayitViewerHost);
                
                Console.WriteLine("[ZayitWrapper] ZayitViewerHost initialized successfully");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize Zayit Viewer: {ex.Message}", 
                    "Initialization Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Console.WriteLine($"[ZayitWrapper] Error initializing ZayitViewerHost: {ex}");
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Clean up resources if needed
            _zayitViewerHost?.Dispose();
        }
    }
}