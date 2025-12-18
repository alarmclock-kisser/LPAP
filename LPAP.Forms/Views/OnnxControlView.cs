using LPAP.Audio;
using LPAP.Cuda;
using LPAP.Onnx.Demucs;
using LPAP.Onnx.Runtime;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Management;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LPAP.Forms.Views
{
    public partial class OnnxControlView : Form
    {
        private const string DefaultModelPath = @"D:\Models";
        private DemucsModel? _model;
        private DemucsService? _service;

        internal readonly AudioObj Audio;

        public OnnxControlView(AudioObj audio)
        {
            this.InitializeComponent();
            this.Audio = audio.Clone();

            this.ComboBox_FillDevices();
            this.ComboBox_FillModels();
        }




        private void ComboBox_FillDevices()
        {
            this.comboBox_devices.SuspendLayout();
            this.comboBox_devices.Items.Clear();

            using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController");
            foreach (ManagementObject obj in searcher.Get())
            {
                string name = obj["Name"]?.ToString() ?? "Unknown Device";
                var ramObj = obj["AdapterRAM"];
                ulong ramBytes = ramObj is null or DBNull ? 0UL : Convert.ToUInt64(ramObj, CultureInfo.InvariantCulture);
                double ramGB = ramBytes / (1024.0 * 1024.0 * 1024.0);
                string displayText = $"[{this.comboBox_devices.Items.Count}] {name} - {ramGB:F2} GB RAM";
                this.comboBox_devices.Items.Add(displayText);
            }

            if (this.comboBox_devices.Items.Count > 0)
            {
                this.comboBox_devices.SelectedIndex = 0;
            }

            this.comboBox_devices.ResumeLayout();
        }

        private void ComboBox_FillModels()
        {
            this.comboBox_models.SuspendLayout();
            this.comboBox_models.Items.Clear();

            var basePath = Environment.GetEnvironmentVariable("LPAP_DEMUCS_MODEL_DIR") ?? DefaultModelPath;

            var models = OnnxModelEnumerator.ListModels(basePath);
            if (models.Count == 0)
            {
                this.comboBox_models.Items.Add("(No .onnx models found)");
                this.comboBox_models.Enabled = false;
                this.comboBox_models.SelectedIndex = 0;
                return;
            }

            this.comboBox_models.Items.AddRange(models.ToArray());
            if (this.comboBox_models.Items.Count > 0)
            {
                this.comboBox_models.SelectedIndex = 0;
            }

            this.comboBox_models.ResumeLayout();
        }




        private void button_initialize_Click(object sender, EventArgs e)
        {
            this.button_initialize.Enabled = false;
            this.label_status.Text = "Initializing ONNX Demucs model '" + this.comboBox_models.SelectedItem?.ToString() + "'...";

            try
            {
                int deviceIndex = this.comboBox_devices.SelectedIndex;
                if (this.comboBox_models.SelectedItem is not OnnxModelItem modelItem)
                {
                    throw new InvalidOperationException("No valid ONNX model selected.");
                }

                var demucsOpts = new DemucsOptions
                {
                    ModelPath = modelItem.FullPath,
                    ExpectedSampleRate = this.Audio.SampleRate,
                    ExpectedChannels = this.Audio.Channels
                };
                var onnxOpts = new OnnxOptions
                {
                    PreferCuda = true,
                    DeviceId = deviceIndex,
                    WorkerCount = 1,
                    QueueCapacity = 4
                };

                this._model = new DemucsModel(demucsOpts, onnxOpts);
                this._service = new DemucsService(this._model);
                this.label_status.Text = "ONNX Demucs model initialized on [" + deviceIndex + "] successfully.";


                MessageBox.Show("ONNX Demucs model initialized successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                var dlgResult = MessageBox.Show($"Failed to initialize ONNX Demucs model:\n{ex.Message}\n\n - Copy to Clipboard?", "Error", MessageBoxButtons.YesNo, MessageBoxIcon.Error);
                this.button_initialize.Enabled = true;
                if (dlgResult == DialogResult.Yes)
                {
                    Clipboard.SetText(ex.ToString());
                }
            }
        }

        private async void button_inference_Click(object sender, EventArgs e)
        {
            if (this._service is null)
            {
                MessageBox.Show("Please initialize the ONNX model first.", "ONNX", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            this.button_inference.Enabled = false;
            this.button_initialize.Enabled = false;
            this.progressBar_inferencing.Minimum = 0;
            this.progressBar_inferencing.Maximum = 100;
            this.progressBar_inferencing.Value = 0;

            this.label_status.Text = "Inferencing…";

            try
            {
                // Progress marshals automatically to UI thread when created on UI thread.
                var progress = new Progress<double>(p =>
                {
                    p = Math.Clamp(p, 0.0, 1.0);
                    var v = (int) Math.Round(p * 100.0);
                    v = Math.Clamp(v, 0, 100);
                    this.progressBar_inferencing.Value = v;
                });

                // IMPORTANT: This assumes your Audio.Data is already loaded and matches Expected SR/Channels you initialized with.
                var stems = await this._service.SeparateInterleavedAsync(
                    inputInterleaved: this.Audio.Data,
                    sampleRate: this.Audio.SampleRate,
                    channels: this.Audio.Channels,
                    progress: progress,
                    ct: CancellationToken.None);

                this.label_status.Text = "Inference done.";
                this.progressBar_inferencing.Value = 100;

                // Du wolltest die AudioObjs bauen und in eine neue AudioCollectionView packen -> machst du.
                // Hier nur Beispiel, wie du aus stems AudioObjs machen kannst:
                async Task<AudioObj> MakeStem(string suffix, float[] data)
                {
                    var ao = AudioCollection.CreateNewEmpty(this.Audio.SampleRate, this.Audio.Channels, 32, (double)data.Length / (this.Audio.SampleRate * this.Audio.Channels));
                    ao.Name = this.Audio.Name + " - " + suffix;
                    ao.Data = data;
                    return ao;
                }

                var drums = await MakeStem("Drums", stems.Drums);
                var bass = await MakeStem("Bass", stems.Bass);
                var other = await MakeStem("Other", stems.Other);
                var vocals = await MakeStem("Vocals", stems.Vocals);

                var acv = new AudioCollectionView([drums, bass, other, vocals]);

                CudaLog.Info("ONNX Demucs inference completed successfully.", null, "ONNX");
            }
            catch (Exception ex)
            {
                this.label_status.Text = "Inference failed.";
                var dlgResult = MessageBox.Show(ex.ToString() + "\n\n - Copy to Clipboard?", "Inference Error", MessageBoxButtons.YesNo, MessageBoxIcon.Error);
                if (dlgResult == DialogResult.Yes)
                {
                    Clipboard.SetText(ex.ToString());
                }
            }
            finally
            {
                this.button_inference.Enabled = true;
                this.button_initialize.Enabled = true;
            }
        }

    }
}
