namespace LPAP.Forms.Dialogs
{
	partial class TorchDialog
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
			this.button_load = new Button();
			this.SuspendLayout();
			// 
			// comboBox_devices
			// 
			this.comboBox_devices.FormattingEnabled = true;
			this.comboBox_devices.Location = new Point(12, 12);
			this.comboBox_devices.Name = "comboBox_devices";
			this.comboBox_devices.Size = new Size(240, 23);
			this.comboBox_devices.TabIndex = 0;
			this.comboBox_devices.Text = "Select Torch Device...";
			this.comboBox_devices.SelectedIndexChanged += this.comboBox_devices_SelectedIndexChanged;
			// 
			// button_initialize
			// 
			this.button_initialize.Location = new Point(258, 12);
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
			this.comboBox_models.Size = new Size(240, 23);
			this.comboBox_models.TabIndex = 2;
			this.comboBox_models.Text = "Select Torch Model...";
			this.comboBox_models.SelectedIndexChanged += this.comboBox_models_SelectedIndexChanged;
			// 
			// button_load
			// 
			this.button_load.Location = new Point(258, 41);
			this.button_load.Name = "button_load";
			this.button_load.Size = new Size(75, 23);
			this.button_load.TabIndex = 3;
			this.button_load.Text = "Load";
			this.button_load.UseVisualStyleBackColor = true;
			this.button_load.Click += this.button_load_Click;
			// 
			// TorchDialog
			// 
			this.AutoScaleDimensions = new SizeF(7F, 15F);
			this.AutoScaleMode = AutoScaleMode.Font;
			this.ClientSize = new Size(464, 321);
			this.Controls.Add(this.button_load);
			this.Controls.Add(this.comboBox_models);
			this.Controls.Add(this.button_initialize);
			this.Controls.Add(this.comboBox_devices);
			this.MaximumSize = new Size(480, 360);
			this.MinimumSize = new Size(480, 360);
			this.Name = "TorchDialog";
			this.Text = "TorchDialog";
			this.ResumeLayout(false);
		}

		#endregion

		private ComboBox comboBox_devices;
		private Button button_initialize;
		private ComboBox comboBox_models;
		private Button button_load;
	}
}