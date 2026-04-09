using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using WordToPdfLib;

namespace WordToPdfDemo
{
    public partial class Form1 : Form
    {
        private string _tempPdfPath;

        public Form1()
        {
            InitializeComponent();
            FormClosed += OnFormClosed;
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
                lblStatus.Text  = "Converting...";
                Refresh();

                try
                {
                    // Write PDF next to the docx as a temp file
                    _tempPdfPath = Path.Combine(
                        Path.GetTempPath(),
                        Path.GetFileNameWithoutExtension(docxPath) + "_preview.pdf");

                    new WordToPdfConverter().Convert(docxPath, _tempPdfPath);

                    lblStatus.Text = "Done. Opening PDF...";

                    // Open in default system viewer
                    Process.Start(new ProcessStartInfo
                    {
                        FileName        = _tempPdfPath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    lblStatus.Text = "Error: " + ex.Message;
                    MessageBox.Show(ex.Message, "Conversion failed",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OnFormClosed(object sender, FormClosedEventArgs e)
        {
            if (_tempPdfPath == null) return;
            try
            {
                if (File.Exists(_tempPdfPath))
                    File.Delete(_tempPdfPath);
            }
            catch { /* best effort */ }
        }
    }
}
