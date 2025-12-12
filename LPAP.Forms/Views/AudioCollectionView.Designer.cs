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
            this.wAVToolStripMenuItem.Name = "wAVToolStripMenuItem";
            this.wAVToolStripMenuItem.Size = new Size(180, 22);
            this.wAVToolStripMenuItem.Text = "WAV";
            // 
            // mP3ToolStripMenuItem
            // 
            this.mP3ToolStripMenuItem.Name = "mP3ToolStripMenuItem";
            this.mP3ToolStripMenuItem.Size = new Size(180, 22);
            this.mP3ToolStripMenuItem.Text = "MP3";
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
        private ToolStripMenuItem toolStripMenuItem1;
        private ToolStripMenuItem wAVToolStripMenuItem;
        private ToolStripMenuItem mP3ToolStripMenuItem;
    }
}