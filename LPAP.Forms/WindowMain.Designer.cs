namespace LPAP.Forms
{
    partial class WindowMain
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.button_import = new Button();
            this.button_reflow = new Button();
            this.SuspendLayout();
            // 
            // button_import
            // 
            this.button_import.BackColor = Color.FromArgb(  192,   255,   255);
            this.button_import.Location = new Point(12, 12);
            this.button_import.Name = "button_import";
            this.button_import.Size = new Size(75, 23);
            this.button_import.TabIndex = 0;
            this.button_import.Text = "Import";
            this.button_import.UseVisualStyleBackColor = false;
            this.button_import.Click += this.button_import_Click;
            // 
            // button_reflow
            // 
            this.button_reflow.BackColor = Color.FromArgb(  192,   192,   255);
            this.button_reflow.Location = new Point(517, 406);
            this.button_reflow.Name = "button_reflow";
            this.button_reflow.Size = new Size(75, 23);
            this.button_reflow.TabIndex = 1;
            this.button_reflow.Text = "Reflow";
            this.button_reflow.UseVisualStyleBackColor = false;
            this.button_reflow.Click += this.button_reflow_Click;
            // 
            // WindowMain
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(604, 441);
            this.Controls.Add(this.button_reflow);
            this.Controls.Add(this.button_import);
            this.MaximizeBox = false;
            this.MaximumSize = new Size(620, 480);
            this.MinimumSize = new Size(620, 480);
            this.Name = "WindowMain";
            this.Text = "LPAP (Forms) Main-Control";
            this.ResumeLayout(false);
        }

        #endregion

        private Button button_import;
        private Button button_reflow;
    }
}
