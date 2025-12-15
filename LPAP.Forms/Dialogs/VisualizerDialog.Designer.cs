namespace LPAP.Forms.Dialogs
{
	partial class VisualizerDialog
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

		#region Windows Form Designer generated code

		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.button_render = new Button();
			this.progressBar_rendering = new ProgressBar();
			this.comboBox_resolution = new ComboBox();
			this.numericUpDown_frameRate = new NumericUpDown();
			this.label_info_frameRate = new Label();
			this.numericUpDown_startSeconds = new NumericUpDown();
			this.label_info_startSeconds = new Label();
			this.label_info_endSeconds = new Label();
			this.numericUpDown_endSeconds = new NumericUpDown();
			this.label_sizeApprox = new Label();
			this.checkBox_offload = new CheckBox();
			this.numericUpDown_threads = new NumericUpDown();
			this.label_info_threads = new Label();
			this.label_cuda = new Label();
			((System.ComponentModel.ISupportInitialize) this.numericUpDown_frameRate).BeginInit();
			((System.ComponentModel.ISupportInitialize) this.numericUpDown_startSeconds).BeginInit();
			((System.ComponentModel.ISupportInitialize) this.numericUpDown_endSeconds).BeginInit();
			((System.ComponentModel.ISupportInitialize) this.numericUpDown_threads).BeginInit();
			this.SuspendLayout();
			// 
			// button_render
			// 
			this.button_render.Location = new Point(377, 406);
			this.button_render.Name = "button_render";
			this.button_render.Size = new Size(75, 23);
			this.button_render.TabIndex = 0;
			this.button_render.Text = "Render";
			this.button_render.UseVisualStyleBackColor = true;
			this.button_render.Click += this.button_render_Click;
			// 
			// progressBar_rendering
			// 
			this.progressBar_rendering.Location = new Point(12, 406);
			this.progressBar_rendering.Maximum = 1000;
			this.progressBar_rendering.Name = "progressBar_rendering";
			this.progressBar_rendering.Size = new Size(359, 23);
			this.progressBar_rendering.TabIndex = 1;
			// 
			// comboBox_resolution
			// 
			this.comboBox_resolution.FormattingEnabled = true;
			this.comboBox_resolution.Location = new Point(12, 159);
			this.comboBox_resolution.Name = "comboBox_resolution";
			this.comboBox_resolution.Size = new Size(200, 23);
			this.comboBox_resolution.TabIndex = 2;
			this.comboBox_resolution.Text = "Select Resolution...";
			// 
			// numericUpDown_frameRate
			// 
			this.numericUpDown_frameRate.DecimalPlaces = 2;
			this.numericUpDown_frameRate.Location = new Point(12, 265);
			this.numericUpDown_frameRate.Maximum = new decimal(new int[] { 144, 0, 0, 0 });
			this.numericUpDown_frameRate.Minimum = new decimal(new int[] { 5, 0, 0, 65536 });
			this.numericUpDown_frameRate.Name = "numericUpDown_frameRate";
			this.numericUpDown_frameRate.Size = new Size(60, 23);
			this.numericUpDown_frameRate.TabIndex = 3;
			this.numericUpDown_frameRate.Value = new decimal(new int[] { 24, 0, 0, 0 });
			this.numericUpDown_frameRate.ValueChanged += this.numericUpDown_frameRate_ValueChanged;
			// 
			// label_info_frameRate
			// 
			this.label_info_frameRate.AutoSize = true;
			this.label_info_frameRate.Location = new Point(12, 247);
			this.label_info_frameRate.Name = "label_info_frameRate";
			this.label_info_frameRate.Size = new Size(26, 15);
			this.label_info_frameRate.TabIndex = 4;
			this.label_info_frameRate.Text = "FPS";
			// 
			// numericUpDown_startSeconds
			// 
			this.numericUpDown_startSeconds.DecimalPlaces = 2;
			this.numericUpDown_startSeconds.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
			this.numericUpDown_startSeconds.Location = new Point(12, 309);
			this.numericUpDown_startSeconds.Maximum = new decimal(new int[] { 999999, 0, 0, 0 });
			this.numericUpDown_startSeconds.Name = "numericUpDown_startSeconds";
			this.numericUpDown_startSeconds.Size = new Size(70, 23);
			this.numericUpDown_startSeconds.TabIndex = 5;
			this.numericUpDown_startSeconds.ValueChanged += this.numericUpDown_startSeconds_ValueChanged;
			// 
			// label_info_startSeconds
			// 
			this.label_info_startSeconds.AutoSize = true;
			this.label_info_startSeconds.Location = new Point(12, 291);
			this.label_info_startSeconds.Name = "label_info_startSeconds";
			this.label_info_startSeconds.Size = new Size(54, 15);
			this.label_info_startSeconds.TabIndex = 6;
			this.label_info_startSeconds.Text = "Start sec.";
			// 
			// label_info_endSeconds
			// 
			this.label_info_endSeconds.AutoSize = true;
			this.label_info_endSeconds.Location = new Point(88, 291);
			this.label_info_endSeconds.Name = "label_info_endSeconds";
			this.label_info_endSeconds.Size = new Size(50, 15);
			this.label_info_endSeconds.TabIndex = 8;
			this.label_info_endSeconds.Text = "End sec.";
			// 
			// numericUpDown_endSeconds
			// 
			this.numericUpDown_endSeconds.DecimalPlaces = 2;
			this.numericUpDown_endSeconds.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
			this.numericUpDown_endSeconds.Location = new Point(88, 309);
			this.numericUpDown_endSeconds.Maximum = new decimal(new int[] { 999999, 0, 0, 0 });
			this.numericUpDown_endSeconds.Name = "numericUpDown_endSeconds";
			this.numericUpDown_endSeconds.Size = new Size(70, 23);
			this.numericUpDown_endSeconds.TabIndex = 7;
			this.numericUpDown_endSeconds.ValueChanged += this.numericUpDown_endSeconds_ValueChanged;
			// 
			// label_sizeApprox
			// 
			this.label_sizeApprox.AutoSize = true;
			this.label_sizeApprox.Location = new Point(12, 388);
			this.label_sizeApprox.Name = "label_sizeApprox";
			this.label_sizeApprox.Size = new Size(106, 15);
			this.label_sizeApprox.TabIndex = 9;
			this.label_sizeApprox.Text = "ca. - frames (- MB)";
			// 
			// checkBox_offload
			// 
			this.checkBox_offload.AutoSize = true;
			this.checkBox_offload.Location = new Point(377, 381);
			this.checkBox_offload.Name = "checkBox_offload";
			this.checkBox_offload.Size = new Size(66, 19);
			this.checkBox_offload.TabIndex = 10;
			this.checkBox_offload.Text = "Offload";
			this.checkBox_offload.UseVisualStyleBackColor = true;
			// 
			// numericUpDown_threads
			// 
			this.numericUpDown_threads.Location = new Point(377, 352);
			this.numericUpDown_threads.Maximum = new decimal(new int[] { 1, 0, 0, 0 });
			this.numericUpDown_threads.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
			this.numericUpDown_threads.Name = "numericUpDown_threads";
			this.numericUpDown_threads.Size = new Size(75, 23);
			this.numericUpDown_threads.TabIndex = 11;
			this.numericUpDown_threads.Value = new decimal(new int[] { 1, 0, 0, 0 });
			// 
			// label_info_threads
			// 
			this.label_info_threads.AutoSize = true;
			this.label_info_threads.Location = new Point(377, 334);
			this.label_info_threads.Name = "label_info_threads";
			this.label_info_threads.Size = new Size(49, 15);
			this.label_info_threads.TabIndex = 12;
			this.label_info_threads.Text = "Threads";
			// 
			// label_cuda
			// 
			this.label_cuda.AutoSize = true;
			this.label_cuda.Location = new Point(12, 9);
			this.label_cuda.Name = "label_cuda";
			this.label_cuda.Size = new Size(97, 15);
			this.label_cuda.TabIndex = 13;
			this.label_cuda.Text = "CUDA: <Offline>";
			// 
			// VisualizerDialog
			// 
			this.AutoScaleDimensions = new SizeF(7F, 15F);
			this.AutoScaleMode = AutoScaleMode.Font;
			this.ClientSize = new Size(464, 441);
			this.Controls.Add(this.label_cuda);
			this.Controls.Add(this.label_info_threads);
			this.Controls.Add(this.numericUpDown_threads);
			this.Controls.Add(this.checkBox_offload);
			this.Controls.Add(this.label_sizeApprox);
			this.Controls.Add(this.label_info_endSeconds);
			this.Controls.Add(this.numericUpDown_endSeconds);
			this.Controls.Add(this.label_info_startSeconds);
			this.Controls.Add(this.numericUpDown_startSeconds);
			this.Controls.Add(this.label_info_frameRate);
			this.Controls.Add(this.numericUpDown_frameRate);
			this.Controls.Add(this.comboBox_resolution);
			this.Controls.Add(this.progressBar_rendering);
			this.Controls.Add(this.button_render);
			this.MaximumSize = new Size(480, 480);
			this.MinimumSize = new Size(480, 480);
			this.Name = "VisualizerDialog";
			this.Text = "VisualizerDialog";
			((System.ComponentModel.ISupportInitialize) this.numericUpDown_frameRate).EndInit();
			((System.ComponentModel.ISupportInitialize) this.numericUpDown_startSeconds).EndInit();
			((System.ComponentModel.ISupportInitialize) this.numericUpDown_endSeconds).EndInit();
			((System.ComponentModel.ISupportInitialize) this.numericUpDown_threads).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();
		}

		#endregion

		private Button button_render;
		private ProgressBar progressBar_rendering;
		private ComboBox comboBox_resolution;
		private NumericUpDown numericUpDown_frameRate;
		private Label label_info_frameRate;
		private NumericUpDown numericUpDown_startSeconds;
		private Label label_info_startSeconds;
		private Label label_info_endSeconds;
		private NumericUpDown numericUpDown_endSeconds;
		private Label label_sizeApprox;
		private CheckBox checkBox_offload;
		private NumericUpDown numericUpDown_threads;
		private Label label_info_threads;
		private Label label_cuda;
	}
}