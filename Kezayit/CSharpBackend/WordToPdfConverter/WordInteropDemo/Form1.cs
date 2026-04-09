using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WordInteropDemo
{
    public partial class Form1 : Form
    {
        private readonly WordConverter _converter = new WordConverter();
        private string _tempPdfPath;

        public Form1()
        {
            InitializeComponent();
            FormClosed += OnFormClosed;

            // Disable button until Word is warmed up
            btnBrowse.Enabled = false;
            lblStatus.Text    = "Warming up Word...";

            Task.Run(() =>
            {
                _converter.Initialize();
                Invoke(new Action(() =>
                {
                    btnBrowse.Enabled = true;
                    lblStatus.Text    = "Ready. Pick a .docx file.";
                }));
            });
        }

        private void btnBrowse_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog
            {
                Title  = "Select a Word document",
                Filter = "Word Documents (*.docx)|*.docx"
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;

                string docxPath = dlg.FileName;
                btnBrowse.Enabled = false;
                lblStatus.Text    = "Converting...";

                // Delete previous temp file if any
                TryDeleteTemp();

                _tempPdfPath = Path.Combine(
                    Path.GetTempPath(),
                    Path.GetFileNameWithoutExtension(docxPath) + "_preview.pdf");

                var sw = Stopwatch.StartNew();

                Task.Run(() =>
                {
                    try
                    {
                        _converter.Convert(docxPath, _tempPdfPath);
                        sw.Stop();

                        Invoke(new Action(() =>
                        {
                            lblStatus.Text    = $"Done in {sw.ElapsedMilliseconds}ms. Opening...";
                            btnBrowse.Enabled = true;
                            Process.Start(new ProcessStartInfo
                            {
                                FileName        = _tempPdfPath,
                                UseShellExecute = true
                            });
                        }));
                    }
                    catch (Exception ex)
                    {
                        Invoke(new Action(() =>
                        {
                            btnBrowse.Enabled = true;
                            lblStatus.Text    = "Error: " + ex.Message;
                            MessageBox.Show(ex.Message, "Conversion failed",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }));
                    }
                });
            }
        }

        private void OnFormClosed(object sender, FormClosedEventArgs e)
        {
            TryDeleteTemp();
            _converter.Dispose();
        }

        private void TryDeleteTemp()
        {
            try { if (_tempPdfPath != null && File.Exists(_tempPdfPath)) File.Delete(_tempPdfPath); }
            catch { }
        }
    }
}
