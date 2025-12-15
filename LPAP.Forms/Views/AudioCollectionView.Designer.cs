namespace LPAP.Forms.Views
{
    partial class AudioCollectionView
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
            this.contextMenuStrip_listBox = new ContextMenuStrip(this.components);
            this.openAsTrackToolStripMenuItem = new ToolStripMenuItem();
            this.renameToolStripMenuItem = new ToolStripMenuItem();
            this.editTagsToolStripMenuItem = new ToolStripMenuItem();
            this.addNumberingToolStripMenuItem = new ToolStripMenuItem();
            this.toolStripTextBox_format = new ToolStripTextBox();
            this.deleteToolStripMenuItem = new ToolStripMenuItem();
            this.toolStripMenuItem1 = new ToolStripMenuItem();
            this.wAVToolStripMenuItem = new ToolStripMenuItem();
            this.mP3ToolStripMenuItem = new ToolStripMenuItem();
            this.wav32ToolStripMenuItem = new ToolStripMenuItem();
            this.wav24ToolStripMenuItem = new ToolStripMenuItem();
            this.wav16ToolStripMenuItem = new ToolStripMenuItem();
            this.wav8ToolStripMenuItem = new ToolStripMenuItem();
            this.mp3320ToolStripMenuItem = new ToolStripMenuItem();
            this.mp3256ToolStripMenuItem = new ToolStripMenuItem();
            this.mp3224ToolStripMenuItem = new ToolStripMenuItem();
            this.mp3192ToolStripMenuItem = new ToolStripMenuItem();
            this.mp3160ToolStripMenuItem = new ToolStripMenuItem();
            this.mp3128ToolStripMenuItem = new ToolStripMenuItem();
            this.mp396ToolStripMenuItem = new ToolStripMenuItem();
            this.mp364ToolStripMenuItem = new ToolStripMenuItem();
            this.mp332ToolStripMenuItem = new ToolStripMenuItem();
            this.contextMenuStrip_listBox.SuspendLayout();
            this.SuspendLayout();
            // 
            // contextMenuStrip_listBox
            // 
            this.contextMenuStrip_listBox.Items.AddRange(new ToolStripItem[] { this.openAsTrackToolStripMenuItem, this.renameToolStripMenuItem, this.editTagsToolStripMenuItem, this.addNumberingToolStripMenuItem, this.toolStripMenuItem1, this.deleteToolStripMenuItem });
            this.contextMenuStrip_listBox.Name = "contextMenuStrip_listBox";
            this.contextMenuStrip_listBox.Size = new Size(181, 158);
            // 
            // openAsTrackToolStripMenuItem
            // 
            this.openAsTrackToolStripMenuItem.Name = "openAsTrackToolStripMenuItem";
            this.openAsTrackToolStripMenuItem.Size = new Size(180, 22);
            this.openAsTrackToolStripMenuItem.Text = "Open as Track-View";
            this.openAsTrackToolStripMenuItem.Click += this.openAsTrackToolStripMenuItem_Click;
            // 
            // renameToolStripMenuItem
            // 
            this.renameToolStripMenuItem.Name = "renameToolStripMenuItem";
            this.renameToolStripMenuItem.Size = new Size(180, 22);
            this.renameToolStripMenuItem.Text = "Rename ...";
            this.renameToolStripMenuItem.Click += this.renameToolStripMenuItem_Click;
            // 
            // editTagsToolStripMenuItem
            // 
            this.editTagsToolStripMenuItem.Name = "editTagsToolStripMenuItem";
            this.editTagsToolStripMenuItem.Size = new Size(180, 22);
            this.editTagsToolStripMenuItem.Text = "Edit Tags ...";
            this.editTagsToolStripMenuItem.Click += this.editTagsToolStripMenuItem_Click;
            // 
            // addNumberingToolStripMenuItem
            // 
            this.addNumberingToolStripMenuItem.CheckOnClick = true;
            this.addNumberingToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { this.toolStripTextBox_format });
            this.addNumberingToolStripMenuItem.Name = "addNumberingToolStripMenuItem";
            this.addNumberingToolStripMenuItem.Size = new Size(180, 22);
            this.addNumberingToolStripMenuItem.Text = "Add Numbering";
            this.addNumberingToolStripMenuItem.Click += this.addNumberingToolStripMenuItem_Click;
            // 
            // toolStripTextBox_format
            // 
            this.toolStripTextBox_format.Name = "toolStripTextBox_format";
            this.toolStripTextBox_format.Size = new Size(100, 23);
            this.toolStripTextBox_format.Text = "default format";
            // 
            // deleteToolStripMenuItem
            // 
            this.deleteToolStripMenuItem.ForeColor = Color.FromArgb(  192,   0,   0);
            this.deleteToolStripMenuItem.Name = "deleteToolStripMenuItem";
            this.deleteToolStripMenuItem.Size = new Size(180, 22);
            this.deleteToolStripMenuItem.Text = "Delete";
            this.deleteToolStripMenuItem.Click += this.deleteToolStripMenuItem_Click;
            // 
            // toolStripMenuItem1
            // 
            this.toolStripMenuItem1.DropDownItems.AddRange(new ToolStripItem[] { this.wAVToolStripMenuItem, this.mP3ToolStripMenuItem });
            this.toolStripMenuItem1.Name = "toolStripMenuItem1";
            this.toolStripMenuItem1.Size = new Size(180, 22);
            this.toolStripMenuItem1.Text = "Export";
            // 
            // wAVToolStripMenuItem
            // 
            this.wAVToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { this.wav32ToolStripMenuItem, this.wav24ToolStripMenuItem, this.wav16ToolStripMenuItem, this.wav8ToolStripMenuItem });
            this.wAVToolStripMenuItem.Name = "wAVToolStripMenuItem";
            this.wAVToolStripMenuItem.Size = new Size(180, 22);
            this.wAVToolStripMenuItem.Text = "WAV";
            // 
            // mP3ToolStripMenuItem
            // 
            this.mP3ToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { this.mp3320ToolStripMenuItem, this.mp3256ToolStripMenuItem, this.mp3224ToolStripMenuItem, this.mp3192ToolStripMenuItem, this.mp3160ToolStripMenuItem, this.mp3128ToolStripMenuItem, this.mp396ToolStripMenuItem, this.mp364ToolStripMenuItem, this.mp332ToolStripMenuItem });
            this.mP3ToolStripMenuItem.Name = "mP3ToolStripMenuItem";
            this.mP3ToolStripMenuItem.Size = new Size(180, 22);
            this.mP3ToolStripMenuItem.Text = "MP3";
            // 
            // wav32ToolStripMenuItem
            // 
            this.wav32ToolStripMenuItem.Name = "wav_32";
            this.wav32ToolStripMenuItem.Size = new Size(180, 22);
            this.wav32ToolStripMenuItem.Tag = "wav";
            this.wav32ToolStripMenuItem.Text = "32 bit";
            this.wav32ToolStripMenuItem.Click += this.exportToolStripMenuItem_Click;
            // 
            // wav24ToolStripMenuItem
            // 
            this.wav24ToolStripMenuItem.Name = "wav_24";
            this.wav24ToolStripMenuItem.Size = new Size(180, 22);
            this.wav24ToolStripMenuItem.Tag = "wav";
            this.wav24ToolStripMenuItem.Text = "24 bit";
            this.wav24ToolStripMenuItem.Click += this.exportToolStripMenuItem_Click;
            // 
            // wav16ToolStripMenuItem
            // 
            this.wav16ToolStripMenuItem.Name = "wav_16";
            this.wav16ToolStripMenuItem.Size = new Size(180, 22);
            this.wav16ToolStripMenuItem.Tag = "wav";
            this.wav16ToolStripMenuItem.Text = "16 bit";
            this.wav16ToolStripMenuItem.Click += this.exportToolStripMenuItem_Click;
            // 
            // wav8ToolStripMenuItem
            // 
            this.wav8ToolStripMenuItem.Name = "wav_8";
            this.wav8ToolStripMenuItem.Size = new Size(180, 22);
            this.wav8ToolStripMenuItem.Tag = "wav";
            this.wav8ToolStripMenuItem.Text = "8 bit";
            this.wav8ToolStripMenuItem.Click += this.exportToolStripMenuItem_Click;
            // 
            // mp3320ToolStripMenuItem
            // 
            this.mp3320ToolStripMenuItem.Name = "mp3_320";
            this.mp3320ToolStripMenuItem.Size = new Size(180, 22);
            this.mp3320ToolStripMenuItem.Tag = "mp3";
            this.mp3320ToolStripMenuItem.Text = "320 kbps";
            this.mp3320ToolStripMenuItem.Click += this.exportToolStripMenuItem_Click;
            // 
            // mp3256ToolStripMenuItem
            // 
            this.mp3256ToolStripMenuItem.Name = "mp3_256";
            this.mp3256ToolStripMenuItem.Size = new Size(180, 22);
            this.mp3256ToolStripMenuItem.Tag = "mp3";
            this.mp3256ToolStripMenuItem.Text = "256 kbps";
            this.mp3256ToolStripMenuItem.Click += this.exportToolStripMenuItem_Click;
            // 
            // mp3224ToolStripMenuItem
            // 
            this.mp3224ToolStripMenuItem.Name = "mp3_224";
            this.mp3224ToolStripMenuItem.Size = new Size(180, 22);
            this.mp3224ToolStripMenuItem.Tag = "mp3";
            this.mp3224ToolStripMenuItem.Text = "224 kbps";
            this.mp3224ToolStripMenuItem.Click += this.exportToolStripMenuItem_Click;
            // 
            // mp3192ToolStripMenuItem
            // 
            this.mp3192ToolStripMenuItem.Name = "mp3_192";
            this.mp3192ToolStripMenuItem.Size = new Size(180, 22);
            this.mp3192ToolStripMenuItem.Tag = "mp3";
            this.mp3192ToolStripMenuItem.Text = "192 kbps";
            this.mp3192ToolStripMenuItem.Click += this.exportToolStripMenuItem_Click;
            // 
            // mp3160ToolStripMenuItem
            // 
            this.mp3160ToolStripMenuItem.Name = "mp3_160";
            this.mp3160ToolStripMenuItem.Size = new Size(180, 22);
            this.mp3160ToolStripMenuItem.Tag = "mp3";
            this.mp3160ToolStripMenuItem.Text = "160 kbps";
            this.mp3160ToolStripMenuItem.Click += this.exportToolStripMenuItem_Click;
            // 
            // mp3128ToolStripMenuItem
            // 
            this.mp3128ToolStripMenuItem.Name = "mp3_128";
            this.mp3128ToolStripMenuItem.Size = new Size(180, 22);
            this.mp3128ToolStripMenuItem.Tag = "mp3";
            this.mp3128ToolStripMenuItem.Text = "128 kbps";
            this.mp3128ToolStripMenuItem.Click += this.exportToolStripMenuItem_Click;
            // 
            // mp396ToolStripMenuItem
            // 
            this.mp396ToolStripMenuItem.Name = "mp3_96";
            this.mp396ToolStripMenuItem.Size = new Size(180, 22);
            this.mp396ToolStripMenuItem.Tag = "mp3";
            this.mp396ToolStripMenuItem.Text = "96 kbps";
            this.mp396ToolStripMenuItem.Click += this.exportToolStripMenuItem_Click;
            // 
            // mp364ToolStripMenuItem
            // 
            this.mp364ToolStripMenuItem.Name = "mp3_64";
            this.mp364ToolStripMenuItem.Size = new Size(180, 22);
            this.mp364ToolStripMenuItem.Tag = "mp3";
            this.mp364ToolStripMenuItem.Text = "64 kbps";
            this.mp364ToolStripMenuItem.Click += this.exportToolStripMenuItem_Click;
            // 
            // mp332ToolStripMenuItem
            // 
            this.mp332ToolStripMenuItem.Name = "mp3_32";
            this.mp332ToolStripMenuItem.Size = new Size(180, 22);
            this.mp332ToolStripMenuItem.Tag = "mp3";
            this.mp332ToolStripMenuItem.Text = "32 kbps";
            this.mp332ToolStripMenuItem.Click += this.exportToolStripMenuItem_Click;
            // 
            // AudioCollectionView
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(224, 121);
            this.MaximizeBox = false;
            this.Name = "AudioCollectionView";
            this.Text = "AudioCollectionView";
            this.contextMenuStrip_listBox.ResumeLayout(false);
            this.ResumeLayout(false);
        }

		#endregion

		private LPAP.Forms.Views.AudioListBox listBox_audios;
        private ContextMenuStrip contextMenuStrip_listBox;
        private ToolStripMenuItem openAsTrackToolStripMenuItem;
        private ToolStripMenuItem renameToolStripMenuItem;
        private ToolStripMenuItem editTagsToolStripMenuItem;
        private ToolStripMenuItem addNumberingToolStripMenuItem;
        private ToolStripTextBox toolStripTextBox_format;
        private ToolStripMenuItem deleteToolStripMenuItem;
    }
}