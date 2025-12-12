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
			this.pictureBox_cores = new PictureBox();
			this.progressBar_memory = new ProgressBar();
			this.label_memory = new Label();
			((System.ComponentModel.ISupportInitialize) this.pictureBox_cores).BeginInit();
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
			// pictureBox_cores
			// 
			this.pictureBox_cores.BackColor = SystemColors.ControlLight;
			this.pictureBox_cores.Location = new Point(442, 12);
			this.pictureBox_cores.Name = "pictureBox_cores";
			this.pictureBox_cores.Size = new Size(150, 100);
			this.pictureBox_cores.TabIndex = 2;
			this.pictureBox_cores.TabStop = false;
			// 
			// progressBar_memory
			// 
			this.progressBar_memory.Location = new Point(442, 133);
			this.progressBar_memory.Name = "progressBar_memory";
			this.progressBar_memory.Size = new Size(150, 15);
			this.progressBar_memory.TabIndex = 3;
			// 
			// label_memory
			// 
			this.label_memory.AutoSize = true;
			this.label_memory.Location = new Point(442, 115);
			this.label_memory.Name = "label_memory";
			this.label_memory.Size = new Size(118, 15);
			this.label_memory.TabIndex = 4;
			this.label_memory.Text = "RAM: 0.00 MB / - MB";
			// 
			// WindowMain
			// 
			this.AutoScaleDimensions = new SizeF(7F, 15F);
			this.AutoScaleMode = AutoScaleMode.Font;
			this.ClientSize = new Size(604, 441);
			this.Controls.Add(this.label_memory);
			this.Controls.Add(this.progressBar_memory);
			this.Controls.Add(this.pictureBox_cores);
			this.Controls.Add(this.button_reflow);
			this.Controls.Add(this.button_import);
			this.MaximizeBox = false;
			this.MaximumSize = new Size(620, 480);
			this.MinimumSize = new Size(620, 480);
			this.Name = "WindowMain";
			this.Text = "LPAP (Forms) Main-Control";
			((System.ComponentModel.ISupportInitialize) this.pictureBox_cores).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();
		}

		#endregion

		private Button button_import;
        private Button button_reflow;
		private PictureBox pictureBox_cores;
		private ProgressBar progressBar_memory;
		private Label label_memory;
	}
}
