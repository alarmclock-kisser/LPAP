namespace LPAP.Forms.Views
{
    partial class OnnxControlView
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
			this.button_initialize = new Button();
			this.comboBox_models = new ComboBox();
			this.label_status = new Label();
			this.button_inference = new Button();
			this.progressBar_inferencing = new ProgressBar();
			this.label_steps = new Label();
			this.label_elapsed = new Label();
			this.listBox_log = new ListBox();
			this.SuspendLayout();
			// 
			// comboBox_devices
			// 
			this.comboBox_devices.FormattingEnabled = true;
			this.comboBox_devices.Location = new Point(12, 12);
			this.comboBox_devices.Name = "comboBox_devices";
			this.comboBox_devices.Size = new Size(299, 23);
			this.comboBox_devices.TabIndex = 0;
			this.comboBox_devices.Text = "Select ONNX Device...";
			// 
			// button_initialize
			// 
			this.button_initialize.Location = new Point(317, 11);
			this.button_initialize.Name = "button_initialize";
			this.button_initialize.Size = new Size(75, 23);
			this.button_initialize.TabIndex = 1;
			this.button_initialize.Text = "Initialize";
			this.button_initialize.UseVisualStyleBackColor = true;
			this.button_initialize.Click += this.button_initialize_Click;
			// 
			// comboBox_models
			// 
			this.comboBox_models.FormattingEnabled = true;
			this.comboBox_models.Location = new Point(12, 41);
			this.comboBox_models.Name = "comboBox_models";
			this.comboBox_models.Size = new Size(380, 23);
			this.comboBox_models.TabIndex = 2;
			this.comboBox_models.Text = "Select an Inferencing Model...";
			// 
			// label_status
			// 
			this.label_status.AutoSize = true;
			this.label_status.Location = new Point(12, 277);
			this.label_status.Name = "label_status";
			this.label_status.Size = new Size(96, 15);
			this.label_status.TabIndex = 3;
			this.label_status.Text = "ONNX <Offline>";
			// 
			// button_inference
			// 
			this.button_inference.BackColor = SystemColors.Info;
			this.button_inference.Location = new Point(317, 251);
			this.button_inference.Name = "button_inference";
			this.button_inference.Size = new Size(75, 23);
			this.button_inference.TabIndex = 4;
			this.button_inference.Text = "Inference";
			this.button_inference.UseVisualStyleBackColor = false;
			this.button_inference.Click += this.button_inference_Click;
			// 
			// progressBar_inferencing
			// 
			this.progressBar_inferencing.Location = new Point(12, 251);
			this.progressBar_inferencing.Name = "progressBar_inferencing";
			this.progressBar_inferencing.Size = new Size(299, 23);
			this.progressBar_inferencing.TabIndex = 5;
			// 
			// label_steps
			// 
			this.label_steps.AutoSize = true;
			this.label_steps.Location = new Point(272, 233);
			this.label_steps.Name = "label_steps";
			this.label_steps.Size = new Size(30, 15);
			this.label_steps.TabIndex = 6;
			this.label_steps.Text = "0 / 0";
			// 
			// label_elapsed
			// 
			this.label_elapsed.AutoSize = true;
			this.label_elapsed.Location = new Point(12, 233);
			this.label_elapsed.Name = "label_elapsed";
			this.label_elapsed.Size = new Size(90, 15);
			this.label_elapsed.TabIndex = 7;
			this.label_elapsed.Text = "Elapsed: - / ~- s";
			// 
			// listBox_log
			// 
			this.listBox_log.FormattingEnabled = true;
			this.listBox_log.HorizontalScrollbar = true;
			this.listBox_log.Location = new Point(12, 91);
			this.listBox_log.Name = "listBox_log";
			this.listBox_log.Size = new Size(380, 139);
			this.listBox_log.TabIndex = 8;
			this.listBox_log.MouseDoubleClick += this.listBox_log_MouseDoubleClick;
			// 
			// OnnxControlView
			// 
			this.AutoScaleDimensions = new SizeF(7F, 15F);
			this.AutoScaleMode = AutoScaleMode.Font;
			this.ClientSize = new Size(404, 301);
			this.Controls.Add(this.listBox_log);
			this.Controls.Add(this.label_elapsed);
			this.Controls.Add(this.label_steps);
			this.Controls.Add(this.progressBar_inferencing);
			this.Controls.Add(this.button_inference);
			this.Controls.Add(this.label_status);
			this.Controls.Add(this.comboBox_models);
			this.Controls.Add(this.button_initialize);
			this.Controls.Add(this.comboBox_devices);
			this.Name = "OnnxControlView";
			this.Text = "Onnx-Control";
			this.ResumeLayout(false);
			this.PerformLayout();
		}

		#endregion

		private ComboBox comboBox_devices;
        private Button button_initialize;
        private ComboBox comboBox_models;
        private Label label_status;
        private Button button_inference;
        private ProgressBar progressBar_inferencing;
		private Label label_steps;
		private Label label_elapsed;
		private ListBox listBox_log;
	}
}