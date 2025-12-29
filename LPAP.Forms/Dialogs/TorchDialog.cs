using LPAP.Audio;
using LPAP.Torch;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace LPAP.Forms.Dialogs
{
	public partial class TorchDialog : Form
	{
		private readonly AudioObj Audio;
		internal readonly TorchService Torch = new();


		public TorchDialog(AudioObj audio)
		{
			this.Audio = audio;
			this.InitializeComponent();

			this.ComboBox_FillDevices();
			this.ComboBox_FillModels();
		}



		private void ComboBox_FillDevices()
		{
			this.comboBox_devices.SuspendLayout();
			this.comboBox_devices.Items.Clear();
			this.comboBox_devices.DataSource = this.Torch.Devices;
			this.comboBox_devices.ResumeLayout();
			if (this.comboBox_devices.Items.Count > 0)
			{
				this.comboBox_devices.SelectedIndex = 0;
			}
		}

		private void ComboBox_FillModels()
		{
			this.comboBox_models.SuspendLayout();
			this.comboBox_models.Items.Clear();
			this.comboBox_models.DataSource = this.Torch.Models;
			this.comboBox_models.DisplayMember = "Name";
			this.comboBox_models.ResumeLayout();
			if (this.comboBox_models.Items.Count > 0)
			{
				this.comboBox_models.SelectedIndex = 0;
			}
		}

		private void comboBox_devices_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (this.comboBox_devices.SelectedIndex < 0)
			{
				this.button_initialize.Enabled = false;
			}
			else
			{
				this.button_initialize.Enabled = true;
			}
		}

		private void comboBox_models_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (this.comboBox_models.SelectedIndex < 0 || this.Torch.CurrentDevice == null)
			{
				this.button_load.Enabled = false;
			}
			else
			{
				this.button_load.Enabled = true;
			}
		}

		private void button_initialize_Click(object sender, EventArgs e)
		{
			if (this.Torch.CurrentDevice != null)
			{
				this.Torch.Dispose();
				if (this.Torch.CurrentDevice == null && this.Torch.CurrentDeviceInfo == null)
				{
					MessageBox.Show(this, $"Disposed current device.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
					this.button_initialize.Text = "Initialize";
				}
				else
				{
					MessageBox.Show(this, $"Could not dispose current device.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
			else if (this.comboBox_devices.SelectedIndex >= 0)
			{
				this.Torch.InitDeviceByIndex(this.comboBox_devices.SelectedIndex);
				if (this.Torch.CurrentDevice == null || this.Torch.CurrentDeviceInfo == null)
				{
					MessageBox.Show(this, $"Could not initialize selected device.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
				else
				{
					MessageBox.Show(this, $"Initialized device: {this.Torch.CurrentDeviceInfo.ToString()}", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
					this.button_initialize.Text = "Dispose";
				}
			}
		}

		private void button_load_Click(object sender, EventArgs e)
		{
			if (this.comboBox_models.SelectedIndex < 0 || this.comboBox_models.SelectedItem == null)
			{
				MessageBox.Show(this, $"No model selected.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			if (this.Torch.CurrentDevice == null)
			{
				MessageBox.Show(this, $"No device initialized.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			var modelName = this.comboBox_models.SelectedItem.ToString() ?? string.Empty;
			this.Torch.LoadModelByName(modelName);
			if (this.Torch.CurrentModel == null || this.Torch.CurrentModelInfo== null)
			{
				MessageBox.Show(this, $"Could not load model '{modelName}'.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			else
			{
				MessageBox.Show(this, $"Loaded model: {this.Torch.CurrentModelInfo.ToString()}", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
		}
	}
}
