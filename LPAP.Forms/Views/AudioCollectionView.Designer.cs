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
            this.listBox_audios = new ListBox();
            this.SuspendLayout();
            // 
            // listBox_audios
            // 
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
            // AudioCollectionView
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(224, 121);
            this.Controls.Add(this.listBox_audios);
            this.MaximizeBox = false;
            this.Name = "AudioCollectionView";
            this.Text = "AudioCollectionView";
            this.ResumeLayout(false);
        }

        #endregion

        private ListBox listBox_audios;
    }
}