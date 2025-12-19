using System.Windows.Forms;

namespace KleiKodesh.Settings
{
    partial class RibbonSettingsControl
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.flowLayoutPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.label1 = new System.Windows.Forms.Label();
            this.Kezayit_Visible = new System.Windows.Forms.CheckBox();
            this.HebrewBooks_Visible = new System.Windows.Forms.CheckBox();
            this.WebSites_Visible = new System.Windows.Forms.CheckBox();
            this.KleiKodesh_Visible = new System.Windows.Forms.CheckBox();
            this.label2 = new System.Windows.Forms.Label();
            this.Kezayit_Option = new System.Windows.Forms.RadioButton();
            this.HebrewBooks_Option = new System.Windows.Forms.RadioButton();
            this.WebSites_Option = new System.Windows.Forms.RadioButton();
            this.KleiKodesh_Option = new System.Windows.Forms.RadioButton();
            this.Settings_Option = new System.Windows.Forms.RadioButton();
            this.ResetButton = new System.Windows.Forms.Button();
            this.flowLayoutPanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // flowLayoutPanel
            // 
            this.flowLayoutPanel.AutoSize = true;
            this.flowLayoutPanel.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.flowLayoutPanel.Controls.Add(this.label1);
            this.flowLayoutPanel.Controls.Add(this.Kezayit_Visible);
            this.flowLayoutPanel.Controls.Add(this.HebrewBooks_Visible);
            this.flowLayoutPanel.Controls.Add(this.WebSites_Visible);
            this.flowLayoutPanel.Controls.Add(this.KleiKodesh_Visible);
            this.flowLayoutPanel.Controls.Add(this.label2);
            this.flowLayoutPanel.Controls.Add(this.Kezayit_Option);
            this.flowLayoutPanel.Controls.Add(this.HebrewBooks_Option);
            this.flowLayoutPanel.Controls.Add(this.WebSites_Option);
            this.flowLayoutPanel.Controls.Add(this.KleiKodesh_Option);
            this.flowLayoutPanel.Controls.Add(this.Settings_Option);
            this.flowLayoutPanel.Controls.Add(this.ResetButton);
            this.flowLayoutPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.flowLayoutPanel.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.flowLayoutPanel.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(177)));
            this.flowLayoutPanel.Location = new System.Drawing.Point(0, 0);
            this.flowLayoutPanel.Margin = new System.Windows.Forms.Padding(0);
            this.flowLayoutPanel.Name = "flowLayoutPanel";
            this.flowLayoutPanel.Size = new System.Drawing.Size(186, 512);
            this.flowLayoutPanel.TabIndex = 0;
            this.flowLayoutPanel.WrapContents = false;
            // 
            // label1
            // 
            this.label1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.label1.AutoSize = true;
            this.label1.BackColor = System.Drawing.Color.Transparent;
            this.label1.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label1.Location = new System.Drawing.Point(0, 0);
            this.label1.Margin = new System.Windows.Forms.Padding(0, 0, 0, 5);
            this.label1.Name = "label1";
            this.label1.Padding = new System.Windows.Forms.Padding(5, 5, 0, 5);
            this.label1.Size = new System.Drawing.Size(186, 42);
            this.label1.TabIndex = 4;
            this.label1.Text = "רכיבים זמינים";
            // 
            // Kezayit_Visible
            // 
            this.Kezayit_Visible.AutoSize = true;
            this.Kezayit_Visible.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(177)));
            this.Kezayit_Visible.Location = new System.Drawing.Point(103, 52);
            this.Kezayit_Visible.Margin = new System.Windows.Forms.Padding(10, 5, 10, 5);
            this.Kezayit_Visible.Name = "Kezayit_Visible";
            this.Kezayit_Visible.Size = new System.Drawing.Size(73, 28);
            this.Kezayit_Visible.TabIndex = 1;
            this.Kezayit_Visible.Text = "כזית";
            this.Kezayit_Visible.UseVisualStyleBackColor = true;
            // 
            // HebrewBooks_Visible
            // 
            this.HebrewBooks_Visible.AutoSize = true;
            this.HebrewBooks_Visible.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(177)));
            this.HebrewBooks_Visible.Location = new System.Drawing.Point(39, 90);
            this.HebrewBooks_Visible.Margin = new System.Windows.Forms.Padding(10, 5, 10, 5);
            this.HebrewBooks_Visible.Name = "HebrewBooks_Visible";
            this.HebrewBooks_Visible.Size = new System.Drawing.Size(137, 28);
            this.HebrewBooks_Visible.TabIndex = 2;
            this.HebrewBooks_Visible.Text = "היברו בוקס";
            this.HebrewBooks_Visible.UseVisualStyleBackColor = true;
            // 
            // WebSites_Visible
            // 
            this.WebSites_Visible.AutoSize = true;
            this.WebSites_Visible.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(177)));
            this.WebSites_Visible.Location = new System.Drawing.Point(28, 128);
            this.WebSites_Visible.Margin = new System.Windows.Forms.Padding(10, 5, 10, 5);
            this.WebSites_Visible.Name = "WebSites_Visible";
            this.WebSites_Visible.Size = new System.Drawing.Size(148, 28);
            this.WebSites_Visible.TabIndex = 3;
            this.WebSites_Visible.Text = "דרך האתרים";
            this.WebSites_Visible.UseVisualStyleBackColor = true;
            // 
            // KleiKodesh_Visible
            // 
            this.KleiKodesh_Visible.AutoSize = true;
            this.KleiKodesh_Visible.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(177)));
            this.KleiKodesh_Visible.Location = new System.Drawing.Point(43, 166);
            this.KleiKodesh_Visible.Margin = new System.Windows.Forms.Padding(10, 5, 10, 5);
            this.KleiKodesh_Visible.Name = "KleiKodesh_Visible";
            this.KleiKodesh_Visible.Size = new System.Drawing.Size(133, 28);
            this.KleiKodesh_Visible.TabIndex = 10;
            this.KleiKodesh_Visible.Text = "עיצוב תורני";
            this.KleiKodesh_Visible.UseVisualStyleBackColor = true;
            // 
            // label2
            // 
            this.label2.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.label2.AutoSize = true;
            this.label2.BackColor = System.Drawing.Color.Transparent;
            this.label2.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label2.Location = new System.Drawing.Point(0, 204);
            this.label2.Margin = new System.Windows.Forms.Padding(0, 5, 0, 5);
            this.label2.Name = "label2";
            this.label2.Padding = new System.Windows.Forms.Padding(5, 5, 0, 5);
            this.label2.Size = new System.Drawing.Size(186, 42);
            this.label2.TabIndex = 5;
            this.label2.Text = "לחצן ראשי";
            // 
            // Kezayit_Option
            // 
            this.Kezayit_Option.AutoSize = true;
            this.Kezayit_Option.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(177)));
            this.Kezayit_Option.Location = new System.Drawing.Point(104, 256);
            this.Kezayit_Option.Margin = new System.Windows.Forms.Padding(10, 5, 5, 5);
            this.Kezayit_Option.Name = "Kezayit_Option";
            this.Kezayit_Option.Size = new System.Drawing.Size(72, 28);
            this.Kezayit_Option.TabIndex = 7;
            this.Kezayit_Option.TabStop = true;
            this.Kezayit_Option.Text = "כזית";
            this.Kezayit_Option.UseVisualStyleBackColor = true;
            // 
            // HebrewBooks_Option
            // 
            this.HebrewBooks_Option.AutoSize = true;
            this.HebrewBooks_Option.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(177)));
            this.HebrewBooks_Option.Location = new System.Drawing.Point(40, 294);
            this.HebrewBooks_Option.Margin = new System.Windows.Forms.Padding(10, 5, 5, 5);
            this.HebrewBooks_Option.Name = "HebrewBooks_Option";
            this.HebrewBooks_Option.Size = new System.Drawing.Size(136, 28);
            this.HebrewBooks_Option.TabIndex = 8;
            this.HebrewBooks_Option.TabStop = true;
            this.HebrewBooks_Option.Text = "היברו בוקס";
            this.HebrewBooks_Option.UseVisualStyleBackColor = true;
            // 
            // WebSites_Option
            // 
            this.WebSites_Option.AutoSize = true;
            this.WebSites_Option.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(177)));
            this.WebSites_Option.Location = new System.Drawing.Point(29, 332);
            this.WebSites_Option.Margin = new System.Windows.Forms.Padding(10, 5, 5, 5);
            this.WebSites_Option.Name = "WebSites_Option";
            this.WebSites_Option.Size = new System.Drawing.Size(147, 28);
            this.WebSites_Option.TabIndex = 9;
            this.WebSites_Option.TabStop = true;
            this.WebSites_Option.Text = "דרך האתרים";
            this.WebSites_Option.UseVisualStyleBackColor = true;
            // 
            // KleiKodesh_Option
            // 
            this.KleiKodesh_Option.AutoSize = true;
            this.KleiKodesh_Option.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(177)));
            this.KleiKodesh_Option.Location = new System.Drawing.Point(44, 370);
            this.KleiKodesh_Option.Margin = new System.Windows.Forms.Padding(10, 5, 5, 5);
            this.KleiKodesh_Option.Name = "KleiKodesh_Option";
            this.KleiKodesh_Option.Size = new System.Drawing.Size(132, 28);
            this.KleiKodesh_Option.TabIndex = 11;
            this.KleiKodesh_Option.TabStop = true;
            this.KleiKodesh_Option.Text = "עיצוב תורני";
            this.KleiKodesh_Option.UseVisualStyleBackColor = true;
            // 
            // Settings_Option
            // 
            this.Settings_Option.AutoSize = true;
            this.Settings_Option.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(177)));
            this.Settings_Option.Location = new System.Drawing.Point(76, 408);
            this.Settings_Option.Margin = new System.Windows.Forms.Padding(10, 5, 5, 5);
            this.Settings_Option.Name = "Settings_Option";
            this.Settings_Option.Size = new System.Drawing.Size(100, 28);
            this.Settings_Option.TabIndex = 12;
            this.Settings_Option.TabStop = true;
            this.Settings_Option.Text = "הגדרות";
            this.Settings_Option.UseVisualStyleBackColor = true;
            // 
            // ResetButton
            // 
            this.ResetButton.BackColor = System.Drawing.Color.Transparent;
            this.ResetButton.FlatAppearance.BorderColor = System.Drawing.Color.LightGray;
            this.ResetButton.FlatAppearance.MouseDownBackColor = System.Drawing.Color.Transparent;
            this.ResetButton.FlatAppearance.MouseOverBackColor = System.Drawing.Color.Transparent;
            this.ResetButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.ResetButton.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(177)));
            this.ResetButton.Location = new System.Drawing.Point(10, 456);
            this.ResetButton.Margin = new System.Windows.Forms.Padding(10, 15, 10, 10);
            this.ResetButton.Name = "ResetButton";
            this.ResetButton.Size = new System.Drawing.Size(166, 46);
            this.ResetButton.TabIndex = 0;
            this.ResetButton.Text = "איפוס הגדרות";
            this.ResetButton.UseVisualStyleBackColor = false;
            this.ResetButton.Click += new System.EventHandler(this.ResetButton_Click);
            // 
            // RibbonSettingsControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(144F, 144F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.AutoSize = true;
            this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.Controls.Add(this.flowLayoutPanel);
            this.Name = "RibbonSettingsControl";
            this.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.Size = new System.Drawing.Size(186, 512);
            this.flowLayoutPanel.ResumeLayout(false);
            this.flowLayoutPanel.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.FlowLayoutPanel flowLayoutPanel;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.CheckBox Kezayit_Visible;       
        private System.Windows.Forms.CheckBox HebrewBooks_Visible;
        private System.Windows.Forms.CheckBox WebSites_Visible;
        private Label label2;
        private RadioButton Kezayit_Option;
        private CheckBox KleiKodesh_Visible;
        private RadioButton HebrewBooks_Option;
        private RadioButton WebSites_Option;
        private RadioButton KleiKodesh_Option;
        private RadioButton Settings_Option;
        private Button ResetButton;
    }
}
