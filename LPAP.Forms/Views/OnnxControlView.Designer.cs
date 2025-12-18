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
            this.SuspendLayout();
            // 
            // comboBox_devices
            // 
            this.comboBox_devices.FormattingEnabled = true;
            this.comboBox_devices.Location = new Point(12, 12);
            this.comboBox_devices.Name = "comboBox_devices";
            this.comboBox_devices.Size = new Size(200, 23);
            this.comboBox_devices.TabIndex = 0;
            this.comboBox_devices.Text = "Select ONNX Device...";
            // 
            // button_initialize
            // 
            this.button_initialize.Location = new Point(218, 12);
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
            this.comboBox_models.Size = new Size(281, 23);
            this.comboBox_models.TabIndex = 2;
            this.comboBox_models.Text = "Select an Inferencing Model...";
            // 
            // label_status
            // 
            this.label_status.AutoSize = true;
            this.label_status.Location = new Point(12, 417);
            this.label_status.Name = "label_status";
            this.label_status.Size = new Size(96, 15);
            this.label_status.TabIndex = 3;
            this.label_status.Text = "ONNX <Offline>";
            // 
            // button_inference
            // 
            this.button_inference.BackColor = SystemColors.Info;
            this.button_inference.Location = new Point(218, 142);
            this.button_inference.Name = "button_inference";
            this.button_inference.Size = new Size(75, 23);
            this.button_inference.TabIndex = 4;
            this.button_inference.Text = "Inference";
            this.button_inference.UseVisualStyleBackColor = false;
            this.button_inference.Click += this.button_inference_Click;
            // 
            // progressBar_inferencing
            // 
            this.progressBar_inferencing.Location = new Point(12, 142);
            this.progressBar_inferencing.Name = "progressBar_inferencing";
            this.progressBar_inferencing.Size = new Size(200, 23);
            this.progressBar_inferencing.TabIndex = 5;
            // 
            // OnnxControlView
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(704, 441);
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
    }
}