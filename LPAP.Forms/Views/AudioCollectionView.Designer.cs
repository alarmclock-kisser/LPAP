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
			this.listBox_audios = new AudioListBox();
			this.contextMenuStrip_listBox = new ContextMenuStrip(this.components);
			this.openAsTrackToolStripMenuItem = new ToolStripMenuItem();
			this.renameToolStripMenuItem = new ToolStripMenuItem();
			this.editTagsToolStripMenuItem = new ToolStripMenuItem();
			this.visualizerToolStripMenuItem = new ToolStripMenuItem();
			this.stemsToolStripMenuItem = new ToolStripMenuItem();
			this.onnxToolStripMenuItem = new ToolStripMenuItem();
			this.openVinoToolStripMenuItem = new ToolStripMenuItem();
			this.torchToolStripMenuItem = new ToolStripMenuItem();
			this.addNumberingToolStripMenuItem = new ToolStripMenuItem();
			this.toolStripTextBox_format = new ToolStripTextBox();
			this.resampleToolStripMenuItem = new ToolStripMenuItem();
			this.rechannelToolStripMenuItem = new ToolStripMenuItem();
			this.deleteToolStripMenuItem = new ToolStripMenuItem();
			this.contextMenuStrip_listBox.SuspendLayout();
			this.SuspendLayout();
			// 
			// listBox_audios
			// 
			this.listBox_audios.ContextMenuStrip = this.contextMenuStrip_listBox;
			this.listBox_audios.Dock = DockStyle.Fill;
			this.listBox_audios.DrawMode = DrawMode.OwnerDrawFixed;
			this.listBox_audios.FormattingEnabled = true;
			this.listBox_audios.IntegralHeight = false;
			this.listBox_audios.Location = new Point(0, 0);
			this.listBox_audios.Name = "listBox_audios";
			this.listBox_audios.SelectionMode = SelectionMode.MultiExtended;
			this.listBox_audios.Size = new Size(224, 121);
			this.listBox_audios.TabIndex = 0;
			// 
			// contextMenuStrip_listBox
			// 
			this.contextMenuStrip_listBox.Items.AddRange(new ToolStripItem[] { this.openAsTrackToolStripMenuItem, this.renameToolStripMenuItem, this.editTagsToolStripMenuItem, this.visualizerToolStripMenuItem, this.stemsToolStripMenuItem, this.addNumberingToolStripMenuItem, this.resampleToolStripMenuItem, this.rechannelToolStripMenuItem, this.deleteToolStripMenuItem });
			this.contextMenuStrip_listBox.Name = "contextMenuStrip_listBox";
			this.contextMenuStrip_listBox.Size = new Size(179, 202);
			// 
			// openAsTrackToolStripMenuItem
			// 
			this.openAsTrackToolStripMenuItem.Name = "openAsTrackToolStripMenuItem";
			this.openAsTrackToolStripMenuItem.Size = new Size(178, 22);
			this.openAsTrackToolStripMenuItem.Text = "Open as Track-View";
			this.openAsTrackToolStripMenuItem.Click += this.openAsTrackToolStripMenuItem_Click;
			// 
			// renameToolStripMenuItem
			// 
			this.renameToolStripMenuItem.Name = "renameToolStripMenuItem";
			this.renameToolStripMenuItem.Size = new Size(178, 22);
			this.renameToolStripMenuItem.Text = "Rename ...";
			this.renameToolStripMenuItem.Click += this.renameToolStripMenuItem_Click;
			// 
			// editTagsToolStripMenuItem
			// 
			this.editTagsToolStripMenuItem.Name = "editTagsToolStripMenuItem";
			this.editTagsToolStripMenuItem.Size = new Size(178, 22);
			this.editTagsToolStripMenuItem.Text = "Edit Tags ...";
			this.editTagsToolStripMenuItem.Click += this.editTagsToolStripMenuItem_Click;
			// 
			// visualizerToolStripMenuItem
			// 
			this.visualizerToolStripMenuItem.Name = "visualizerToolStripMenuItem";
			this.visualizerToolStripMenuItem.Size = new Size(178, 22);
			this.visualizerToolStripMenuItem.Text = "Visualizer...";
			this.visualizerToolStripMenuItem.Click += this.visualizerToolStripMenuItem_Click;
			// 
			// stemsToolStripMenuItem
			// 
			this.stemsToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { this.onnxToolStripMenuItem, this.openVinoToolStripMenuItem, this.torchToolStripMenuItem });
			this.stemsToolStripMenuItem.Name = "stemsToolStripMenuItem";
			this.stemsToolStripMenuItem.Size = new Size(178, 22);
			this.stemsToolStripMenuItem.Text = "Stem Separation ...";
			this.stemsToolStripMenuItem.Click += this.stemsToolStripMenuItem_Click;
			// 
			// onnxToolStripMenuItem
			// 
			this.onnxToolStripMenuItem.Name = "onnxToolStripMenuItem";
			this.onnxToolStripMenuItem.Size = new Size(180, 22);
			this.onnxToolStripMenuItem.Text = "ONNX";
			this.onnxToolStripMenuItem.Click += this.onnxToolStripMenuItem_Click;
			// 
			// openVinoToolStripMenuItem
			// 
			this.openVinoToolStripMenuItem.Name = "openVinoToolStripMenuItem";
			this.openVinoToolStripMenuItem.Size = new Size(180, 22);
			this.openVinoToolStripMenuItem.Text = "OpenVino";
			this.openVinoToolStripMenuItem.Click += this.openVinoToolStripMenuItem_Click;
			// 
			// torchToolStripMenuItem
			// 
			this.torchToolStripMenuItem.Name = "torchToolStripMenuItem";
			this.torchToolStripMenuItem.Size = new Size(180, 22);
			this.torchToolStripMenuItem.Text = "Torch";
			this.torchToolStripMenuItem.Click += this.torchToolStripMenuItem_Click;
			// 
			// addNumberingToolStripMenuItem
			// 
			this.addNumberingToolStripMenuItem.CheckOnClick = true;
			this.addNumberingToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { this.toolStripTextBox_format });
			this.addNumberingToolStripMenuItem.Name = "addNumberingToolStripMenuItem";
			this.addNumberingToolStripMenuItem.Size = new Size(178, 22);
			this.addNumberingToolStripMenuItem.Text = "Add Numbering";
			this.addNumberingToolStripMenuItem.Click += this.addNumberingToolStripMenuItem_Click;
			// 
			// toolStripTextBox_format
			// 
			this.toolStripTextBox_format.Name = "toolStripTextBox_format";
			this.toolStripTextBox_format.Size = new Size(100, 23);
			this.toolStripTextBox_format.Text = "default format";
			// 
			// resampleToolStripMenuItem
			// 
			this.resampleToolStripMenuItem.Name = "resampleToolStripMenuItem";
			this.resampleToolStripMenuItem.Size = new Size(178, 22);
			this.resampleToolStripMenuItem.Text = "Resample ...";
			this.resampleToolStripMenuItem.Click += this.resampleToolStripMenuItem_Click;
			// 
			// rechannelToolStripMenuItem
			// 
			this.rechannelToolStripMenuItem.Name = "rechannelToolStripMenuItem";
			this.rechannelToolStripMenuItem.Size = new Size(178, 22);
			this.rechannelToolStripMenuItem.Text = "Re-Channel ...";
			this.rechannelToolStripMenuItem.Click += this.rechannelToolStripMenuItem_Click;
			// 
			// deleteToolStripMenuItem
			// 
			this.deleteToolStripMenuItem.ForeColor = Color.FromArgb(  192,   0,   0);
			this.deleteToolStripMenuItem.Name = "deleteToolStripMenuItem";
			this.deleteToolStripMenuItem.Size = new Size(178, 22);
			this.deleteToolStripMenuItem.Text = "Delete";
			this.deleteToolStripMenuItem.Click += this.deleteToolStripMenuItem_Click;
			// 
			// AudioCollectionView
			// 
			this.AutoScaleDimensions = new SizeF(7F, 15F);
			this.AutoScaleMode = AutoScaleMode.Font;
			this.ClientSize = new Size(224, 121);
			this.Controls.Add(this.listBox_audios);
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
        private ToolStripMenuItem visualizerToolStripMenuItem;
		private ToolStripMenuItem resampleToolStripMenuItem;
		private ToolStripMenuItem rechannelToolStripMenuItem;
		private ToolStripMenuItem stemsToolStripMenuItem;
		private ToolStripMenuItem onnxToolStripMenuItem;
		private ToolStripMenuItem openVinoToolStripMenuItem;
		private ToolStripMenuItem torchToolStripMenuItem;
	}
}