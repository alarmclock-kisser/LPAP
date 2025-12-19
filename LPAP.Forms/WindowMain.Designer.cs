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
			this.checkBox_enableMonitoring = new CheckBox();
			this.numericUpDown_statisticsUpdateDelay = new NumericUpDown();
			this.label_info_statisticsUpdateDelay = new Label();
			this.checkBox_autoApply = new CheckBox();
			this.button_looping = new Button();
			this.button_cudaInitialize = new Button();
			this.comboBox_cudaDevices = new ComboBox();
			this.listBox_cudaLog = new ListBox();
			this.label_vram = new Label();
			this.progressBar_vram = new ProgressBar();
			this.label_gpuLoad = new Label();
			this.panel_cudaKernelArguments = new Panel();
			this.comboBox_cudaKernels = new ComboBox();
			this.button_cudaExecute = new Button();
			this.button_cudaInfo = new Button();
			this.button_browse = new Button();
			this.label_exportDirectory = new Label();
			this.label_kernelType = new Label();
			this.label_fftRequired = new Label();
			this.textBox_meta = new TextBox();
			this.label_cudaWatts = new Label();
			((System.ComponentModel.ISupportInitialize) this.pictureBox_cores).BeginInit();
			((System.ComponentModel.ISupportInitialize) this.numericUpDown_statisticsUpdateDelay).BeginInit();
			this.SuspendLayout();
			// 
			// button_import
			// 
			this.button_import.BackColor = Color.FromArgb(  192,   255,   255);
			this.button_import.Location = new Point(517, 285);
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
			this.button_reflow.Location = new Point(517, 381);
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
			// checkBox_enableMonitoring
			// 
			this.checkBox_enableMonitoring.AutoSize = true;
			this.checkBox_enableMonitoring.Checked = true;
			this.checkBox_enableMonitoring.CheckState = CheckState.Checked;
			this.checkBox_enableMonitoring.Location = new Point(442, 220);
			this.checkBox_enableMonitoring.Name = "checkBox_enableMonitoring";
			this.checkBox_enableMonitoring.Size = new Size(68, 19);
			this.checkBox_enableMonitoring.TabIndex = 5;
			this.checkBox_enableMonitoring.Text = "Enabled";
			this.checkBox_enableMonitoring.UseVisualStyleBackColor = true;
			this.checkBox_enableMonitoring.CheckedChanged += this.checkBox_enableMonitoring_CheckedChanged;
			// 
			// numericUpDown_statisticsUpdateDelay
			// 
			this.numericUpDown_statisticsUpdateDelay.Increment = new decimal(new int[] { 125, 0, 0, 0 });
			this.numericUpDown_statisticsUpdateDelay.Location = new Point(542, 190);
			this.numericUpDown_statisticsUpdateDelay.Maximum = new decimal(new int[] { 2500, 0, 0, 0 });
			this.numericUpDown_statisticsUpdateDelay.Minimum = new decimal(new int[] { 125, 0, 0, 0 });
			this.numericUpDown_statisticsUpdateDelay.Name = "numericUpDown_statisticsUpdateDelay";
			this.numericUpDown_statisticsUpdateDelay.Size = new Size(50, 23);
			this.numericUpDown_statisticsUpdateDelay.TabIndex = 6;
			this.numericUpDown_statisticsUpdateDelay.Value = new decimal(new int[] { 500, 0, 0, 0 });
			this.numericUpDown_statisticsUpdateDelay.ValueChanged += this.numericUpDown_statisticsUpdateDelay_ValueChanged;
			// 
			// label_info_statisticsUpdateDelay
			// 
			this.label_info_statisticsUpdateDelay.AutoSize = true;
			this.label_info_statisticsUpdateDelay.Location = new Point(542, 216);
			this.label_info_statisticsUpdateDelay.Name = "label_info_statisticsUpdateDelay";
			this.label_info_statisticsUpdateDelay.Size = new Size(55, 15);
			this.label_info_statisticsUpdateDelay.TabIndex = 7;
			this.label_info_statisticsUpdateDelay.Text = "Delay ms";
			// 
			// checkBox_autoApply
			// 
			this.checkBox_autoApply.AutoSize = true;
			this.checkBox_autoApply.Location = new Point(486, 410);
			this.checkBox_autoApply.Name = "checkBox_autoApply";
			this.checkBox_autoApply.Size = new Size(106, 19);
			this.checkBox_autoApply.TabIndex = 8;
			this.checkBox_autoApply.Text = "Apply on Close";
			this.checkBox_autoApply.UseVisualStyleBackColor = true;
			this.checkBox_autoApply.CheckedChanged += this.checkBox_autoApply_CheckedChanged;
			// 
			// button_looping
			// 
			this.button_looping.Location = new Point(517, 314);
			this.button_looping.Name = "button_looping";
			this.button_looping.Size = new Size(75, 23);
			this.button_looping.TabIndex = 9;
			this.button_looping.Text = "Looping";
			this.button_looping.UseVisualStyleBackColor = true;
			this.button_looping.Click += this.button_looping_Click;
			// 
			// button_cudaInitialize
			// 
			this.button_cudaInitialize.Location = new Point(218, 12);
			this.button_cudaInitialize.Name = "button_cudaInitialize";
			this.button_cudaInitialize.Size = new Size(75, 23);
			this.button_cudaInitialize.TabIndex = 10;
			this.button_cudaInitialize.Text = "Initialize";
			this.button_cudaInitialize.UseVisualStyleBackColor = true;
			this.button_cudaInitialize.Click += this.button_cudaInitialize_Click;
			// 
			// comboBox_cudaDevices
			// 
			this.comboBox_cudaDevices.FormattingEnabled = true;
			this.comboBox_cudaDevices.Location = new Point(12, 12);
			this.comboBox_cudaDevices.Name = "comboBox_cudaDevices";
			this.comboBox_cudaDevices.Size = new Size(200, 23);
			this.comboBox_cudaDevices.TabIndex = 11;
			this.comboBox_cudaDevices.Text = "Select a CUDA Device...";
			this.comboBox_cudaDevices.SelectedIndexChanged += this.comboBox_cudaDevices_SelectedIndexChanged;
			// 
			// listBox_cudaLog
			// 
			this.listBox_cudaLog.Font = new Font("Bahnschrift Condensed", 9F, FontStyle.Regular, GraphicsUnit.Point,  0);
			this.listBox_cudaLog.FormattingEnabled = true;
			this.listBox_cudaLog.Location = new Point(12, 41);
			this.listBox_cudaLog.Name = "listBox_cudaLog";
			this.listBox_cudaLog.SelectionMode = SelectionMode.MultiExtended;
			this.listBox_cudaLog.Size = new Size(310, 144);
			this.listBox_cudaLog.TabIndex = 12;
			this.listBox_cudaLog.MouseClick += this.listBox_cudaLog_RightClick;
			this.listBox_cudaLog.MouseDoubleClick += this.listBox_cudaLog_MouseDoubleClick;
			// 
			// label_vram
			// 
			this.label_vram.AutoSize = true;
			this.label_vram.Location = new Point(442, 151);
			this.label_vram.Name = "label_vram";
			this.label_vram.Size = new Size(125, 15);
			this.label_vram.TabIndex = 13;
			this.label_vram.Text = "VRAM: 0.00 MB / - MB";
			// 
			// progressBar_vram
			// 
			this.progressBar_vram.Location = new Point(442, 169);
			this.progressBar_vram.Name = "progressBar_vram";
			this.progressBar_vram.Size = new Size(150, 15);
			this.progressBar_vram.TabIndex = 14;
			// 
			// label_gpuLoad
			// 
			this.label_gpuLoad.AutoSize = true;
			this.label_gpuLoad.Location = new Point(442, 187);
			this.label_gpuLoad.Name = "label_gpuLoad";
			this.label_gpuLoad.Size = new Size(54, 15);
			this.label_gpuLoad.TabIndex = 15;
			this.label_gpuLoad.Text = "Load: -%";
			// 
			// panel_cudaKernelArguments
			// 
			this.panel_cudaKernelArguments.Location = new Point(12, 220);
			this.panel_cudaKernelArguments.Name = "panel_cudaKernelArguments";
			this.panel_cudaKernelArguments.Size = new Size(310, 151);
			this.panel_cudaKernelArguments.TabIndex = 16;
			// 
			// comboBox_cudaKernels
			// 
			this.comboBox_cudaKernels.FormattingEnabled = true;
			this.comboBox_cudaKernels.Location = new Point(12, 191);
			this.comboBox_cudaKernels.Name = "comboBox_cudaKernels";
			this.comboBox_cudaKernels.Size = new Size(310, 23);
			this.comboBox_cudaKernels.TabIndex = 17;
			this.comboBox_cudaKernels.Text = "Select a CUDA Kernel...";
			this.comboBox_cudaKernels.SelectedIndexChanged += this.comboBox_cudaKernels_SelectedIndexChanged;
			// 
			// button_cudaExecute
			// 
			this.button_cudaExecute.Location = new Point(247, 377);
			this.button_cudaExecute.Name = "button_cudaExecute";
			this.button_cudaExecute.Size = new Size(75, 23);
			this.button_cudaExecute.TabIndex = 18;
			this.button_cudaExecute.Text = "Execute";
			this.button_cudaExecute.UseVisualStyleBackColor = true;
			this.button_cudaExecute.Click += this.button_cudaExecute_Click;
			// 
			// button_cudaInfo
			// 
			this.button_cudaInfo.Font = new Font("Bahnschrift", 9F, FontStyle.Bold, GraphicsUnit.Point,  0);
			this.button_cudaInfo.Location = new Point(299, 12);
			this.button_cudaInfo.Name = "button_cudaInfo";
			this.button_cudaInfo.Size = new Size(23, 23);
			this.button_cudaInfo.TabIndex = 19;
			this.button_cudaInfo.Text = "i";
			this.button_cudaInfo.UseVisualStyleBackColor = true;
			this.button_cudaInfo.Click += this.button_cudaInfo_Click;
			// 
			// button_browse
			// 
			this.button_browse.Font = new Font("Bahnschrift", 9F, FontStyle.Bold, GraphicsUnit.Point,  0);
			this.button_browse.Location = new Point(290, 406);
			this.button_browse.Name = "button_browse";
			this.button_browse.Size = new Size(32, 23);
			this.button_browse.TabIndex = 20;
			this.button_browse.Text = "[...]";
			this.button_browse.UseVisualStyleBackColor = true;
			this.button_browse.Click += this.button_browse_Click;
			// 
			// label_exportDirectory
			// 
			this.label_exportDirectory.AutoSize = true;
			this.label_exportDirectory.Location = new Point(12, 410);
			this.label_exportDirectory.Name = "label_exportDirectory";
			this.label_exportDirectory.Size = new Size(33, 15);
			this.label_exportDirectory.TabIndex = 21;
			this.label_exportDirectory.Text = "Dir: -";
			// 
			// label_kernelType
			// 
			this.label_kernelType.AutoSize = true;
			this.label_kernelType.Location = new Point(12, 374);
			this.label_kernelType.Name = "label_kernelType";
			this.label_kernelType.Size = new Size(100, 15);
			this.label_kernelType.TabIndex = 22;
			this.label_kernelType.Text = "No kernel loaded.";
			// 
			// label_fftRequired
			// 
			this.label_fftRequired.AutoSize = true;
			this.label_fftRequired.Location = new Point(12, 389);
			this.label_fftRequired.Name = "label_fftRequired";
			this.label_fftRequired.Size = new Size(95, 15);
			this.label_fftRequired.TabIndex = 23;
			this.label_fftRequired.Text = "No FFT required.";
			// 
			// textBox_meta
			// 
			this.textBox_meta.Font = new Font("Bahnschrift SemiLight Condensed", 9F, FontStyle.Regular, GraphicsUnit.Point,  0);
			this.textBox_meta.Location = new Point(328, 285);
			this.textBox_meta.Multiline = true;
			this.textBox_meta.Name = "textBox_meta";
			this.textBox_meta.PlaceholderText = "No track meta available ...";
			this.textBox_meta.ReadOnly = true;
			this.textBox_meta.Size = new Size(152, 144);
			this.textBox_meta.TabIndex = 24;
			// 
			// label_cudaWatts
			// 
			this.label_cudaWatts.AutoSize = true;
			this.label_cudaWatts.Location = new Point(442, 202);
			this.label_cudaWatts.Name = "label_cudaWatts";
			this.label_cudaWatts.Size = new Size(62, 15);
			this.label_cudaWatts.TabIndex = 25;
			this.label_cudaWatts.Text = "Power: -W";
			// 
			// WindowMain
			// 
			this.AutoScaleDimensions = new SizeF(7F, 15F);
			this.AutoScaleMode = AutoScaleMode.Font;
			this.ClientSize = new Size(604, 441);
			this.Controls.Add(this.label_cudaWatts);
			this.Controls.Add(this.textBox_meta);
			this.Controls.Add(this.label_fftRequired);
			this.Controls.Add(this.label_kernelType);
			this.Controls.Add(this.label_exportDirectory);
			this.Controls.Add(this.button_browse);
			this.Controls.Add(this.button_cudaInfo);
			this.Controls.Add(this.button_cudaExecute);
			this.Controls.Add(this.comboBox_cudaKernels);
			this.Controls.Add(this.panel_cudaKernelArguments);
			this.Controls.Add(this.label_gpuLoad);
			this.Controls.Add(this.progressBar_vram);
			this.Controls.Add(this.label_vram);
			this.Controls.Add(this.listBox_cudaLog);
			this.Controls.Add(this.comboBox_cudaDevices);
			this.Controls.Add(this.button_cudaInitialize);
			this.Controls.Add(this.button_looping);
			this.Controls.Add(this.checkBox_autoApply);
			this.Controls.Add(this.label_info_statisticsUpdateDelay);
			this.Controls.Add(this.numericUpDown_statisticsUpdateDelay);
			this.Controls.Add(this.checkBox_enableMonitoring);
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
			((System.ComponentModel.ISupportInitialize) this.numericUpDown_statisticsUpdateDelay).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();
		}

		#endregion

		private Button button_import;
        private Button button_reflow;
		private PictureBox pictureBox_cores;
		private ProgressBar progressBar_memory;
		private Label label_memory;
		private CheckBox checkBox_enableMonitoring;
		private NumericUpDown numericUpDown_statisticsUpdateDelay;
		private Label label_info_statisticsUpdateDelay;
		private CheckBox checkBox_autoApply;
		private Button button_looping;
		private Button button_cudaInitialize;
		private ComboBox comboBox_cudaDevices;
		private ListBox listBox_cudaLog;
		private Label label_vram;
		private ProgressBar progressBar_vram;
		private Label label_gpuLoad;
		private Panel panel_cudaKernelArguments;
		private ComboBox comboBox_cudaKernels;
		private Button button_cudaExecute;
        private Button button_cudaInfo;
        private Button button_browse;
        private Label label_exportDirectory;
		private Label label_kernelType;
		private Label label_fftRequired;
		private TextBox textBox_meta;
		private Label label_cudaWatts;
	}
}
