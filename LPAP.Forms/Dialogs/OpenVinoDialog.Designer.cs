namespace LPAP.Forms.Dialogs
{
	partial class OpenVinoDialog
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
			this.comboBox_devices = new ComboBox();
			this.comboBox_models = new ComboBox();
			this.button_modelInfo = new Button();
			this.progressBar_inferencing = new ProgressBar();
			this.label_status = new Label();
			this.button_inference = new Button();
			this.label_elapsed = new Label();
			this.label_percentage = new Label();
			this.listBox_modelsBrowser = new ListBox();
			this.label_info_models = new Label();
			this.button_download = new Button();
			this.label_downloadSpeed = new Label();
			this.textBox_query = new TextBox();
			this.button_search = new Button();
			this.label_info_query = new Label();
			this.label_directory = new Label();
			this.button_browse = new Button();
			this.checkBox_stemsOnly = new CheckBox();
			this.numericUpDown_resultsLimit = new NumericUpDown();
			((System.ComponentModel.ISupportInitialize) this.numericUpDown_resultsLimit).BeginInit();
			this.SuspendLayout();
			// 
			// comboBox_devices
			// 
			this.comboBox_devices.FormattingEnabled = true;
			this.comboBox_devices.Location = new Point(12, 12);
			this.comboBox_devices.Name = "comboBox_devices";
			this.comboBox_devices.Size = new Size(411, 23);
			this.comboBox_devices.TabIndex = 0;
			this.comboBox_devices.Text = "Select OpenVino Device ...";
			// 
			// comboBox_models
			// 
			this.comboBox_models.FormattingEnabled = true;
			this.comboBox_models.Location = new Point(12, 41);
			this.comboBox_models.Name = "comboBox_models";
			this.comboBox_models.Size = new Size(411, 23);
			this.comboBox_models.TabIndex = 2;
			this.comboBox_models.Text = "Select an IR-Model ...";
			// 
			// button_modelInfo
			// 
			this.button_modelInfo.Font = new Font("Bahnschrift SemiBold Condensed", 9F, FontStyle.Bold, GraphicsUnit.Point,  0);
			this.button_modelInfo.Location = new Point(429, 41);
			this.button_modelInfo.Name = "button_modelInfo";
			this.button_modelInfo.Size = new Size(23, 23);
			this.button_modelInfo.TabIndex = 3;
			this.button_modelInfo.Text = "i";
			this.button_modelInfo.UseVisualStyleBackColor = true;
			this.button_modelInfo.Click += this.button_modelInfo_Click;
			// 
			// progressBar_inferencing
			// 
			this.progressBar_inferencing.Location = new Point(12, 391);
			this.progressBar_inferencing.Maximum = 1000;
			this.progressBar_inferencing.Name = "progressBar_inferencing";
			this.progressBar_inferencing.Size = new Size(359, 23);
			this.progressBar_inferencing.Step = 1;
			this.progressBar_inferencing.TabIndex = 4;
			// 
			// label_status
			// 
			this.label_status.AutoSize = true;
			this.label_status.Location = new Point(12, 417);
			this.label_status.Name = "label_status";
			this.label_status.Size = new Size(118, 15);
			this.label_status.TabIndex = 5;
			this.label_status.Text = "OpenVino: <Offline>";
			// 
			// button_inference
			// 
			this.button_inference.BackColor = SystemColors.Info;
			this.button_inference.Location = new Point(377, 391);
			this.button_inference.Name = "button_inference";
			this.button_inference.Size = new Size(75, 23);
			this.button_inference.TabIndex = 6;
			this.button_inference.Text = "Inference";
			this.button_inference.UseVisualStyleBackColor = false;
			this.button_inference.Click += this.button_inference_Click;
			// 
			// label_elapsed
			// 
			this.label_elapsed.AutoSize = true;
			this.label_elapsed.Location = new Point(12, 373);
			this.label_elapsed.Name = "label_elapsed";
			this.label_elapsed.Size = new Size(93, 15);
			this.label_elapsed.TabIndex = 7;
			this.label_elapsed.Text = "Elapsed: - / ~- s.";
			// 
			// label_percentage
			// 
			this.label_percentage.AutoSize = true;
			this.label_percentage.Location = new Point(325, 373);
			this.label_percentage.Name = "label_percentage";
			this.label_percentage.Size = new Size(25, 15);
			this.label_percentage.TabIndex = 8;
			this.label_percentage.Text = "- %";
			// 
			// listBox_modelsBrowser
			// 
			this.listBox_modelsBrowser.FormattingEnabled = true;
			this.listBox_modelsBrowser.HorizontalScrollbar = true;
			this.listBox_modelsBrowser.Location = new Point(12, 198);
			this.listBox_modelsBrowser.Name = "listBox_modelsBrowser";
			this.listBox_modelsBrowser.Size = new Size(440, 154);
			this.listBox_modelsBrowser.TabIndex = 9;
			this.listBox_modelsBrowser.DoubleClick += this.listBox_modelsBrowser_DoubleClick;
			// 
			// label_info_models
			// 
			this.label_info_models.AutoSize = true;
			this.label_info_models.Location = new Point(12, 180);
			this.label_info_models.Name = "label_info_models";
			this.label_info_models.Size = new Size(166, 15);
			this.label_info_models.TabIndex = 10;
			this.label_info_models.Text = "Model Browser (Huggingface)";
			// 
			// button_download
			// 
			this.button_download.Location = new Point(377, 168);
			this.button_download.Name = "button_download";
			this.button_download.Size = new Size(75, 23);
			this.button_download.TabIndex = 11;
			this.button_download.Text = "Download";
			this.button_download.UseVisualStyleBackColor = true;
			this.button_download.Click += this.button_download_Click;
			// 
			// label_downloadSpeed
			// 
			this.label_downloadSpeed.AutoSize = true;
			this.label_downloadSpeed.Location = new Point(377, 150);
			this.label_downloadSpeed.Name = "label_downloadSpeed";
			this.label_downloadSpeed.Size = new Size(59, 15);
			this.label_downloadSpeed.TabIndex = 12;
			this.label_downloadSpeed.Text = "0.00 MB/s";
			// 
			// textBox_query
			// 
			this.textBox_query.Location = new Point(12, 124);
			this.textBox_query.Name = "textBox_query";
			this.textBox_query.PlaceholderText = "demucs";
			this.textBox_query.Size = new Size(373, 23);
			this.textBox_query.TabIndex = 13;
			// 
			// button_search
			// 
			this.button_search.Location = new Point(429, 124);
			this.button_search.Name = "button_search";
			this.button_search.Size = new Size(23, 23);
			this.button_search.TabIndex = 14;
			this.button_search.Text = "🔍";
			this.button_search.UseVisualStyleBackColor = true;
			this.button_search.Click += this.button_search_Click;
			// 
			// label_info_query
			// 
			this.label_info_query.AutoSize = true;
			this.label_info_query.Location = new Point(12, 106);
			this.label_info_query.Name = "label_info_query";
			this.label_info_query.Size = new Size(39, 15);
			this.label_info_query.TabIndex = 15;
			this.label_info_query.Text = "Query";
			// 
			// label_directory
			// 
			this.label_directory.AutoSize = true;
			this.label_directory.Location = new Point(50, 74);
			this.label_directory.Name = "label_directory";
			this.label_directory.Size = new Size(31, 15);
			this.label_directory.TabIndex = 16;
			this.label_directory.Text = "DIR: ";
			// 
			// button_browse
			// 
			this.button_browse.Font = new Font("Bahnschrift", 9F, FontStyle.Bold, GraphicsUnit.Point,  0);
			this.button_browse.Location = new Point(12, 70);
			this.button_browse.Name = "button_browse";
			this.button_browse.Size = new Size(32, 23);
			this.button_browse.TabIndex = 21;
			this.button_browse.Text = "[...]";
			this.button_browse.UseVisualStyleBackColor = true;
			this.button_browse.Click += this.button_browse_Click;
			// 
			// checkBox_stemsOnly
			// 
			this.checkBox_stemsOnly.AutoSize = true;
			this.checkBox_stemsOnly.Location = new Point(368, 99);
			this.checkBox_stemsOnly.Name = "checkBox_stemsOnly";
			this.checkBox_stemsOnly.Size = new Size(84, 19);
			this.checkBox_stemsOnly.TabIndex = 22;
			this.checkBox_stemsOnly.Text = "Stems only";
			this.checkBox_stemsOnly.UseVisualStyleBackColor = true;
			this.checkBox_stemsOnly.Visible = false;
			// 
			// numericUpDown_resultsLimit
			// 
			this.numericUpDown_resultsLimit.Location = new Point(391, 124);
			this.numericUpDown_resultsLimit.Maximum = new decimal(new int[] { 99, 0, 0, 0 });
			this.numericUpDown_resultsLimit.Name = "numericUpDown_resultsLimit";
			this.numericUpDown_resultsLimit.Size = new Size(32, 23);
			this.numericUpDown_resultsLimit.TabIndex = 23;
			this.numericUpDown_resultsLimit.Value = new decimal(new int[] { 25, 0, 0, 0 });
			// 
			// OpenVinoDialog
			// 
			this.AutoScaleDimensions = new SizeF(7F, 15F);
			this.AutoScaleMode = AutoScaleMode.Font;
			this.ClientSize = new Size(464, 441);
			this.Controls.Add(this.numericUpDown_resultsLimit);
			this.Controls.Add(this.checkBox_stemsOnly);
			this.Controls.Add(this.button_browse);
			this.Controls.Add(this.label_directory);
			this.Controls.Add(this.label_info_query);
			this.Controls.Add(this.button_search);
			this.Controls.Add(this.textBox_query);
			this.Controls.Add(this.label_downloadSpeed);
			this.Controls.Add(this.button_download);
			this.Controls.Add(this.label_info_models);
			this.Controls.Add(this.listBox_modelsBrowser);
			this.Controls.Add(this.label_percentage);
			this.Controls.Add(this.label_elapsed);
			this.Controls.Add(this.button_inference);
			this.Controls.Add(this.label_status);
			this.Controls.Add(this.progressBar_inferencing);
			this.Controls.Add(this.button_modelInfo);
			this.Controls.Add(this.comboBox_models);
			this.Controls.Add(this.comboBox_devices);
			this.MaximumSize = new Size(480, 480);
			this.MinimumSize = new Size(480, 480);
			this.Name = "OpenVinoDialog";
			this.Text = "OpenVino-Inferencing";
			((System.ComponentModel.ISupportInitialize) this.numericUpDown_resultsLimit).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();
		}

		#endregion

		private ComboBox comboBox_devices;
		private ComboBox comboBox_models;
		private Button button_modelInfo;
		private ProgressBar progressBar_inferencing;
		private Label label_status;
		private Button button_inference;
		private Label label_elapsed;
		private Label label_percentage;
		private ListBox listBox_modelsBrowser;
		private Label label_info_models;
		private Button button_download;
		private Label label_downloadSpeed;
		private TextBox textBox_query;
		private Button button_search;
		private Label label_info_query;
		private Label label_directory;
		private Button button_browse;
		private CheckBox checkBox_stemsOnly;
		private NumericUpDown numericUpDown_resultsLimit;
	}
}