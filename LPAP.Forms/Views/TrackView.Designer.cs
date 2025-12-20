namespace LPAP.Forms.Views
{
    partial class TrackView
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
			this.components = new System.ComponentModel.Container();
			this.button_playback = new Button();
			this.button_pause = new Button();
			this.vScrollBar_volume = new VScrollBar();
			this.textBox_timestamp = new TextBox();
			this.checkBox_sync = new CheckBox();
			this.checkBox_mute = new CheckBox();
			this.pictureBox_waveform = new PictureBox();
			this.contextMenuStrip_waveform = new ContextMenuStrip(this.components);
			this.applyToolStripMenuItem = new ToolStripMenuItem();
			this.copyToolStripMenuItem = new ToolStripMenuItem();
			this.removeToolStripMenuItem = new ToolStripMenuItem();
			this.normalizeToolStripMenuItem = new ToolStripMenuItem();
			this.fadeInToolStripMenuItem = new ToolStripMenuItem();
			this.fadeOutToolStripMenuItem = new ToolStripMenuItem();
			this.trimSilenceToolStripMenuItem = new ToolStripMenuItem();
			this.drawBeatGridToolStripMenuItem = new ToolStripMenuItem();
			this.timeStretchToolStripMenuItem = new ToolStripMenuItem();
			this.hScrollBar_offset = new HScrollBar();
			this.label_volume = new Label();
			((System.ComponentModel.ISupportInitialize) this.pictureBox_waveform).BeginInit();
			this.contextMenuStrip_waveform.SuspendLayout();
			this.SuspendLayout();
			// 
			// button_playback
			// 
			this.button_playback.Location = new Point(12, 12);
			this.button_playback.Name = "button_playback";
			this.button_playback.Size = new Size(23, 23);
			this.button_playback.TabIndex = 0;
			this.button_playback.Tag = "■";
			this.button_playback.Text = "▶";
			this.button_playback.UseVisualStyleBackColor = true;
			this.button_playback.Click += this.button_playback_Click;
			// 
			// button_pause
			// 
			this.button_pause.Font = new Font("Bahnschrift", 9F, FontStyle.Regular, GraphicsUnit.Point,  0);
			this.button_pause.Location = new Point(41, 12);
			this.button_pause.Name = "button_pause";
			this.button_pause.Size = new Size(23, 23);
			this.button_pause.TabIndex = 1;
			this.button_pause.Text = "||";
			this.button_pause.UseVisualStyleBackColor = true;
			this.button_pause.Click += this.button_pause_Click;
			// 
			// vScrollBar_volume
			// 
			this.vScrollBar_volume.LargeChange = 1;
			this.vScrollBar_volume.Location = new Point(67, 9);
			this.vScrollBar_volume.Maximum = 1000;
			this.vScrollBar_volume.Name = "vScrollBar_volume";
			this.vScrollBar_volume.Size = new Size(17, 123);
			this.vScrollBar_volume.TabIndex = 2;
			this.vScrollBar_volume.Value = 150;
			this.vScrollBar_volume.Scroll += this.vScrollBar_volume_Scroll;
			// 
			// textBox_timestamp
			// 
			this.textBox_timestamp.Font = new Font("Bahnschrift SemiLight Condensed", 8.25F, FontStyle.Regular, GraphicsUnit.Point,  0);
			this.textBox_timestamp.Location = new Point(12, 106);
			this.textBox_timestamp.Name = "textBox_timestamp";
			this.textBox_timestamp.PlaceholderText = "0:00:00.000";
			this.textBox_timestamp.Size = new Size(52, 21);
			this.textBox_timestamp.TabIndex = 3;
			// 
			// checkBox_sync
			// 
			this.checkBox_sync.AutoSize = true;
			this.checkBox_sync.Font = new Font("Bahnschrift Condensed", 9F);
			this.checkBox_sync.Location = new Point(12, 41);
			this.checkBox_sync.Name = "checkBox_sync";
			this.checkBox_sync.Size = new Size(46, 18);
			this.checkBox_sync.TabIndex = 4;
			this.checkBox_sync.Text = "Sync";
			this.checkBox_sync.UseVisualStyleBackColor = true;
			this.checkBox_sync.CheckedChanged += this.checkBox_sync_CheckedChanged;
			// 
			// checkBox_mute
			// 
			this.checkBox_mute.AutoSize = true;
			this.checkBox_mute.Font = new Font("Bahnschrift Condensed", 9F);
			this.checkBox_mute.Location = new Point(12, 65);
			this.checkBox_mute.Name = "checkBox_mute";
			this.checkBox_mute.Size = new Size(46, 18);
			this.checkBox_mute.TabIndex = 5;
			this.checkBox_mute.Text = "Mute";
			this.checkBox_mute.UseVisualStyleBackColor = true;
			this.checkBox_mute.CheckedChanged += this.checkBox_mute_CheckedChanged;
			// 
			// pictureBox_waveform
			// 
			this.pictureBox_waveform.BackColor = Color.White;
			this.pictureBox_waveform.ContextMenuStrip = this.contextMenuStrip_waveform;
			this.pictureBox_waveform.Location = new Point(84, 9);
			this.pictureBox_waveform.Margin = new Padding(0);
			this.pictureBox_waveform.Name = "pictureBox_waveform";
			this.pictureBox_waveform.Size = new Size(491, 106);
			this.pictureBox_waveform.TabIndex = 6;
			this.pictureBox_waveform.TabStop = false;
			this.pictureBox_waveform.Click += this.pictureBox_waveform_Click;
			// 
			// contextMenuStrip_waveform
			// 
			this.contextMenuStrip_waveform.Items.AddRange(new ToolStripItem[] { this.applyToolStripMenuItem, this.copyToolStripMenuItem, this.removeToolStripMenuItem, this.normalizeToolStripMenuItem, this.fadeInToolStripMenuItem, this.fadeOutToolStripMenuItem, this.trimSilenceToolStripMenuItem, this.drawBeatGridToolStripMenuItem, this.timeStretchToolStripMenuItem });
			this.contextMenuStrip_waveform.Name = "contextMenuStrip_waveform";
			this.contextMenuStrip_waveform.Size = new Size(181, 224);
			this.contextMenuStrip_waveform.Text = "Actions";
			// 
			// applyToolStripMenuItem
			// 
			this.applyToolStripMenuItem.Name = "applyToolStripMenuItem";
			this.applyToolStripMenuItem.Size = new Size(180, 22);
			this.applyToolStripMenuItem.Text = "Apply";
			this.applyToolStripMenuItem.Click += this.applyToolStripMenuItem_Click;
			// 
			// copyToolStripMenuItem
			// 
			this.copyToolStripMenuItem.Name = "copyToolStripMenuItem";
			this.copyToolStripMenuItem.Size = new Size(180, 22);
			this.copyToolStripMenuItem.Text = "Copy Selection";
			this.copyToolStripMenuItem.Click += this.copyToolStripMenuItem_Click;
			// 
			// removeToolStripMenuItem
			// 
			this.removeToolStripMenuItem.Name = "removeToolStripMenuItem";
			this.removeToolStripMenuItem.Size = new Size(180, 22);
			this.removeToolStripMenuItem.Text = "Remove Selection";
			this.removeToolStripMenuItem.Click += this.removeToolStripMenuItem_Click;
			// 
			// normalizeToolStripMenuItem
			// 
			this.normalizeToolStripMenuItem.Name = "normalizeToolStripMenuItem";
			this.normalizeToolStripMenuItem.Size = new Size(180, 22);
			this.normalizeToolStripMenuItem.Text = "Normalize...";
			this.normalizeToolStripMenuItem.Click += this.normalizeToolStripMenuItem_Click;
			// 
			// fadeInToolStripMenuItem
			// 
			this.fadeInToolStripMenuItem.Name = "fadeInToolStripMenuItem";
			this.fadeInToolStripMenuItem.Size = new Size(180, 22);
			this.fadeInToolStripMenuItem.Text = "Fade In...";
			this.fadeInToolStripMenuItem.Click += this.fadeInToolStripMenuItem_Click;
			// 
			// fadeOutToolStripMenuItem
			// 
			this.fadeOutToolStripMenuItem.Name = "fadeOutToolStripMenuItem";
			this.fadeOutToolStripMenuItem.Size = new Size(180, 22);
			this.fadeOutToolStripMenuItem.Text = "Fade Out...";
			this.fadeOutToolStripMenuItem.Click += this.fadeOutToolStripMenuItem_Click;
			// 
			// trimSilenceToolStripMenuItem
			// 
			this.trimSilenceToolStripMenuItem.Name = "trimSilenceToolStripMenuItem";
			this.trimSilenceToolStripMenuItem.Size = new Size(180, 22);
			this.trimSilenceToolStripMenuItem.Text = "Trim Silence...";
			// 
			// drawBeatGridToolStripMenuItem
			// 
			this.drawBeatGridToolStripMenuItem.CheckOnClick = true;
			this.drawBeatGridToolStripMenuItem.Name = "drawBeatGridToolStripMenuItem";
			this.drawBeatGridToolStripMenuItem.Size = new Size(180, 22);
			this.drawBeatGridToolStripMenuItem.Text = "Draw Beat Grid";
			this.drawBeatGridToolStripMenuItem.CheckStateChanged += this.drawBeatGridToolStripMenuItem_CheckStateChanged;
			// 
			// timeStretchToolStripMenuItem
			// 
			this.timeStretchToolStripMenuItem.Name = "timeStretchToolStripMenuItem";
			this.timeStretchToolStripMenuItem.Size = new Size(180, 22);
			this.timeStretchToolStripMenuItem.Text = "Time Stretch...";
			this.timeStretchToolStripMenuItem.Click += this.timeStretchToolStripMenuItem_Click;
			// 
			// hScrollBar_offset
			// 
			this.hScrollBar_offset.Location = new Point(84, 115);
			this.hScrollBar_offset.Name = "hScrollBar_offset";
			this.hScrollBar_offset.Size = new Size(491, 17);
			this.hScrollBar_offset.TabIndex = 7;
			this.hScrollBar_offset.Scroll += this.hScrollBar_offset_Scroll;
			// 
			// label_volume
			// 
			this.label_volume.AutoSize = true;
			this.label_volume.Font = new Font("Bahnschrift SemiLight Condensed", 9F, FontStyle.Regular, GraphicsUnit.Point,  0);
			this.label_volume.Location = new Point(26, 88);
			this.label_volume.Name = "label_volume";
			this.label_volume.Size = new Size(41, 14);
			this.label_volume.TabIndex = 8;
			this.label_volume.Text = "Vol 100%";
			// 
			// TrackView
			// 
			this.AutoScaleDimensions = new SizeF(7F, 15F);
			this.AutoScaleMode = AutoScaleMode.Font;
			this.BackColor = SystemColors.ControlLight;
			this.ClientSize = new Size(584, 141);
			this.Controls.Add(this.label_volume);
			this.Controls.Add(this.hScrollBar_offset);
			this.Controls.Add(this.pictureBox_waveform);
			this.Controls.Add(this.checkBox_mute);
			this.Controls.Add(this.checkBox_sync);
			this.Controls.Add(this.textBox_timestamp);
			this.Controls.Add(this.vScrollBar_volume);
			this.Controls.Add(this.button_pause);
			this.Controls.Add(this.button_playback);
			this.MaximizeBox = false;
			this.MaximumSize = new Size(8192, 180);
			this.MinimumSize = new Size(200, 180);
			this.Name = "TrackView";
			this.Text = "TrackView";
			((System.ComponentModel.ISupportInitialize) this.pictureBox_waveform).EndInit();
			this.contextMenuStrip_waveform.ResumeLayout(false);
			this.ResumeLayout(false);
			this.PerformLayout();
		}

		#endregion

		private Button button_playback;
        private Button button_pause;
        private VScrollBar vScrollBar_volume;
        private TextBox textBox_timestamp;
        private CheckBox checkBox_sync;
        private CheckBox checkBox_mute;
        private PictureBox pictureBox_waveform;
        private HScrollBar hScrollBar_offset;
        private ContextMenuStrip contextMenuStrip_waveform;
        private ToolStripMenuItem copyToolStripMenuItem;
        private ToolStripMenuItem removeToolStripMenuItem;
        private ToolStripMenuItem normalizeToolStripMenuItem;
        private ToolStripMenuItem fadeInToolStripMenuItem;
        private ToolStripMenuItem fadeOutToolStripMenuItem;
        private ToolStripMenuItem trimSilenceToolStripMenuItem;
        private ToolStripMenuItem drawBeatGridToolStripMenuItem;
        private ToolStripMenuItem timeStretchToolStripMenuItem;
		private Label label_volume;
		private ToolStripMenuItem applyToolStripMenuItem;
	}
}