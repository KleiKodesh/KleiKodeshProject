namespace WordToPdfDemo
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.btnBrowse = new System.Windows.Forms.Button();
            this.lblStatus = new System.Windows.Forms.Label();
            this.SuspendLayout();

            // btnBrowse
            this.btnBrowse.Text     = "Select Word Document...";
            this.btnBrowse.Size     = new System.Drawing.Size(220, 40);
            this.btnBrowse.Location = new System.Drawing.Point(140, 160);
            this.btnBrowse.Font     = new System.Drawing.Font("Segoe UI", 10f);
            this.btnBrowse.Click   += new System.EventHandler(this.btnBrowse_Click);

            // lblStatus
            this.lblStatus.Text      = "Pick a .docx file to convert.";
            this.lblStatus.Size      = new System.Drawing.Size(400, 30);
            this.lblStatus.Location  = new System.Drawing.Point(100, 220);
            this.lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.lblStatus.Font      = new System.Drawing.Font("Segoe UI", 9f);
            this.lblStatus.ForeColor = System.Drawing.Color.DimGray;

            // Form1
            this.ClientSize  = new System.Drawing.Size(500, 400);
            this.Text        = "Word → PDF Converter";
            this.Font        = new System.Drawing.Font("Segoe UI", 9f);
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Controls.Add(this.btnBrowse);
            this.Controls.Add(this.lblStatus);
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.Button btnBrowse;
        private System.Windows.Forms.Label  lblStatus;
    }
}
