using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using LPAP.Audio;

namespace LPAP.Forms.Views
{
    public partial class AudioCollectionView : Form
    {
        internal readonly AudioCollection AudioC = new();

        private readonly DateTime CreatedAt = DateTime.Now;

        // --- Auto-Height + User-Delta ---
        private readonly int _baseHeightNoItems;
        private readonly int _itemHeight;
        private int _userHeightDelta;

        // --- Drag & Drop ---
        private Point _dragStartPoint;
        private bool _isDragging;
        private int _insertionIndex = -1;

        private sealed class DragPayload
        {
            public AudioCollectionView SourceView { get; }
            public List<AudioObj> Items { get; }

            public DragPayload(AudioCollectionView sourceView, List<AudioObj> items)
            {
                this.SourceView = sourceView;
                this.Items = items;
            }
        }

        public AudioCollectionView(IEnumerable<AudioObj>? audios = null)
        {
            this.InitializeComponent();

            WindowMain.OpenAudioCollectionViews.Add(this);

            foreach (var audio in audios ?? [])
            {
                this.AudioC.Add(audio);
            }

            // Set text
            this.Text = audios?.Count() > 0 ? Path.GetDirectoryName(audios.First().FilePath)?.Split($"\\").LastOrDefault() ?? "Imported" : "Audio Collection";

            // ListBox-Binding
            this.listBox_audios.DataSource = this.AudioC.Items;
            this.listBox_audios.DisplayMember = nameof(AudioObj.Name);

            // OwnerDraw + MultiSelect
            this.listBox_audios.DrawMode = DrawMode.OwnerDrawFixed;
            this.listBox_audios.SelectionMode = SelectionMode.MultiExtended;
            this.listBox_audios.IntegralHeight = false;
            this.listBox_audios.Dock = DockStyle.Fill;

            this.listBox_audios.DrawItem += this.ListBox_Audios_DrawItem;
            this.listBox_audios.MouseDown += this.ListBox_Audios_MouseDown;
            this.listBox_audios.MouseMove += this.ListBox_Audios_MouseMove;
            this.listBox_audios.MouseUp += this.ListBox_Audios_MouseUp;

            this.listBox_audios.AllowDrop = true;
            this.listBox_audios.DragEnter += this.ListBox_Audios_DragEnter;
            this.listBox_audios.DragOver += this.ListBox_Audios_DragOver;
            this.listBox_audios.DragDrop += this.ListBox_Audios_DragDrop;
            this.listBox_audios.DragLeave += (_, _) =>
            {
                this._insertionIndex = -1;
                this.listBox_audios.Invalidate();
            };

            // Auto-Height-Grundwerte merken
            this._itemHeight = this.listBox_audios.ItemHeight;
            this._baseHeightNoItems = this.Height;
            this._userHeightDelta = 0;

            // Größe bei Item-Änderungen automatisch anpassen
            this.AudioC.Items.ListChanged += this.Items_ListChanged;
            this.AdjustHeightForItems();

            // User-Delta bei manuellem Resize merken
            ResizeEnd += this.AudioCollectionView_ResizeEnd;
            this.listBox_audios.Resize += (_, __) => this.listBox_audios.Invalidate();
            ConfigureListBoxDoubleBuffered(this.listBox_audios);

            FormClosing += (s, e) =>
            {
                WindowMain.OpenAudioCollectionViews.Remove(this);
                this.AudioC.Dispose();
            };

            if (!this.DesignMode)
            {
                this.PositionAndShowSelf();
            }
        }

        // --------- OwnerDraw: Name links, Dauer rechts, Insert-Marker ---------

        public static void ConfigureListBoxDoubleBuffered(ListBox listBox)
        {
            try
            {
                typeof(ListBox).InvokeMember(
                    "DoubleBuffered",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.SetProperty,
                    null,
                    listBox,
                    [true]);
            }
            catch
            {
                // wenn das mal schief geht, ignorieren wir es einfach
            }
        }

        private void ListBox_Audios_DrawItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= this.listBox_audios.Items.Count)
            {
                return;
            }

            var audio = (AudioObj) this.listBox_audios.Items[e.Index];

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color backColor = selected ? SystemColors.Highlight : this.listBox_audios.BackColor;
            Color foreColor = selected ? SystemColors.HighlightText : this.listBox_audios.ForeColor;

            using (var backBrush = new SolidBrush(backColor))
            {
                e.Graphics.FillRectangle(backBrush, e.Bounds);
            }

            string durationText = FormatDuration(audio.Duration);
            int padding = 4;

            // Breite für Dauer rechts
            Size durationSize = TextRenderer.MeasureText(
                e.Graphics, durationText, e.Font,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            var rightRect = new Rectangle(
                e.Bounds.Right - durationSize.Width - padding,
                e.Bounds.Top,
                durationSize.Width + padding,
                e.Bounds.Height);

            var leftRect = new Rectangle(
                e.Bounds.Left + padding,
                e.Bounds.Top,
                rightRect.Left - e.Bounds.Left - 2 * padding,
                e.Bounds.Height);

            // Name links, mit Ellipsis
            TextRenderer.DrawText(
                e.Graphics,
                audio.Name ?? string.Empty,
                e.Font,
                leftRect,
                foreColor,
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.SingleLine |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix);

            // Dauer rechtsbündig
            TextRenderer.DrawText(
                e.Graphics,
                durationText,
                e.Font,
                rightRect,
                foreColor,
                TextFormatFlags.Right |
                TextFormatFlags.SingleLine |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix);

            // Fokusrahmen
            e.DrawFocusRectangle();

            // Insert-Marker zeichnen, falls aktiv
            if (this._insertionIndex >= 0)
            {
                using var pen = new Pen(Color.Black, 2);

                // Linie OBEN vor Item i
                if (e.Index == this._insertionIndex)
                {
                    int y = e.Bounds.Top;
                    e.Graphics.DrawLine(pen, e.Bounds.Left, y, e.Bounds.Right, y);
                }
                // Linie UNTEN nach letztem Item
                else if (e.Index == this.listBox_audios.Items.Count - 1 &&
                         this._insertionIndex == this.listBox_audios.Items.Count)
                {
                    int y = e.Bounds.Bottom - 1;
                    e.Graphics.DrawLine(pen, e.Bounds.Left, y, e.Bounds.Right, y);
                }
            }
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalHours >= 1)
            {
                return duration.ToString(@"h\:mm\:ss");
            }
            if (duration.TotalSeconds < 10)
            {
                return duration.TotalMilliseconds + " ms";
            }

            return duration.ToString(@"m\:ss");
        }

        // --------- Auto-Height-Handling ---------

        private void Items_ListChanged(object? sender, ListChangedEventArgs e)
        {
            this.AdjustHeightForItems();
        }

        private void AudioCollectionView_ResizeEnd(object? sender, EventArgs e)
        {
            // Wenn User die Form selbst resizet, merken wir das Delta zur Auto-Höhe
            int autoHeight = this.GetAutoHeightForItemCount(this.AudioC.Items.Count);
            this._userHeightDelta = this.Height - autoHeight;
        }

        private void AdjustHeightForItems()
        {
            int autoHeight = this.GetAutoHeightForItemCount(this.AudioC.Items.Count);
            int targetHeight = autoHeight + this._userHeightDelta;

            // Minimalhöhe nicht unterschreiten
            int minHeight = 120;
            if (targetHeight < minHeight)
            {
                targetHeight = minHeight;
            }

            if (this.Height != targetHeight)
            {
                this.Height = targetHeight;
            }
        }

        private int GetAutoHeightForItemCount(int count)
        {
            // Basis: Designer-Höhe für "keine Items"
            // plus n * ItemHeight
            int n = Math.Max(1, count);
            int add = n * this._itemHeight;

            // _baseHeightNoItems war die Höhe beim Start; wir tun so,
            // als wäre dort bereits 1 "Slot" eingeplant gewesen.
            // Darum ziehen wir einmal ItemHeight ab, damit es bei 1 Item
            // ungefähr so hoch bleibt wie am Anfang.
            int baseWithoutFirstRow = this._baseHeightNoItems - this._itemHeight;
            if (baseWithoutFirstRow < 50)
            {
                baseWithoutFirstRow = 50;
            }

            return baseWithoutFirstRow + add;
        }

        // --------- Maus / Drag-Start ---------

        private void ListBox_Audios_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                this._dragStartPoint = e.Location;
                this._isDragging = false;
            }
        }

        private void ListBox_Audios_MouseMove(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            if (this._isDragging)
            {
                return;
            }

            var dx = Math.Abs(e.X - this._dragStartPoint.X);
            var dy = Math.Abs(e.Y - this._dragStartPoint.Y);

            // kleine Schwelle, um zufällige Drags zu vermeiden
            if (dx + dy < SystemInformation.DragSize.Width / 2)
            {
                return;
            }

            if (this.listBox_audios.SelectedItems.Count == 0)
            {
                return;
            }

            this._isDragging = true;

            var selected = this.listBox_audios.SelectedItems.Cast<AudioObj>().ToList();
            if (selected.Count == 0)
            {
                return;
            }

            var payload = new DragPayload(this, selected);
            var data = new DataObject(payload);

            this.DoDragDrop(data, DragDropEffects.Move);

            this._isDragging = false;
            this._insertionIndex = -1;
            this.listBox_audios.Invalidate();
        }

        private void ListBox_Audios_MouseUp(object? sender, MouseEventArgs e)
        {
            this._isDragging = false;
        }

        // --------- Drag & Drop Ziel ---------

        private void ListBox_Audios_DragEnter(object? sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(typeof(DragPayload)))
            {
                e.Effect = DragDropEffects.Move;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        private void ListBox_Audios_DragOver(object? sender, DragEventArgs e)
        {
            if (e.Data?.GetDataPresent(typeof(DragPayload)) == false)
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            e.Effect = DragDropEffects.Move;

            var clientPoint = this.listBox_audios.PointToClient(new Point(e.X, e.Y));
            int index = this.listBox_audios.IndexFromPoint(clientPoint);

            if (index == ListBox.NoMatches)
            {
                this._insertionIndex = this.listBox_audios.Items.Count;
            }
            else
            {
                var itemRect = this.listBox_audios.GetItemRectangle(index);
                bool before = clientPoint.Y < itemRect.Top + itemRect.Height / 2;
                this._insertionIndex = before ? index : index + 1;
            }

            this.listBox_audios.Invalidate();
        }

        private void ListBox_Audios_DragDrop(object? sender, DragEventArgs e)
        {
            if (e.Data == null || e.Data?.GetDataPresent(typeof(DragPayload)) == false)
            {
                return;
            }

            var payloadObj = e.Data?.GetData(typeof(DragPayload));
            if (payloadObj is not DragPayload payload)
            {
                return;
            }

            var srcView = payload.SourceView;
            var movedItems = payload.Items;

            if (movedItems.Count == 0)
            {
                return;
            }

            int insertIndex = this._insertionIndex;
            if (insertIndex < 0 || insertIndex > this.AudioC.Items.Count)
            {
                insertIndex = this.AudioC.Items.Count;
            }

            // --- gleicher View: nur umsortieren ---
            if (ReferenceEquals(srcView, this))
            {
                this.ReorderWithinThisView(movedItems, insertIndex);
            }
            else
            {
                this.MoveBetweenViews(srcView, movedItems, insertIndex);
            }

            this._insertionIndex = -1;
            this.listBox_audios.Invalidate();
        }

        // Umsortieren innerhalb dieser AudioCollection
        private void ReorderWithinThisView(List<AudioObj> movedItems, int insertIndex)
        {
            var list = this.AudioC.Items;

            // aktuelle Indizes der selektierten Items
            var selectedIndices = movedItems
                .Select(item => list.IndexOf(item))
                .Where(i => i >= 0)
                .OrderByDescending(i => i)
                .ToList();

            // InsertIndex anpassen, wenn vor dem InsertIndex Elemente entfernt werden
            foreach (int idx in selectedIndices)
            {
                if (idx < insertIndex)
                {
                    insertIndex--;
                }

                list.RemoveAt(idx);
            }

            // In ursprünglicher Reihenfolge wieder einfügen
            movedItems.ForEach(item =>
            {
                list.Insert(insertIndex, item);
                insertIndex++;
            });
        }

        // Verschieben zwischen zwei AudioCollectionViews (ohne Dispose!)
        private void MoveBetweenViews(AudioCollectionView srcView, List<AudioObj> movedItems, int insertIndex)
        {
            var srcList = srcView.AudioC;
            var dstList = this.AudioC;

            // 1) aus Quell-Collection entfernen (ohne Dispose)
            foreach (var item in movedItems)
            {
                srcList.RemoveWithoutDispose(item); // siehe Zusatz-Methode unten
            }

            // 2) in Ziel-Collection an gegebener Stelle einfügen
            var dstItems = dstList.Items;
            foreach (var item in movedItems)
            {
                dstItems.Insert(insertIndex, item);
                insertIndex++;
            }
        }


        private void PositionAndShowSelf()
        {
            // Referenz auf WindowMain holen
            var main = Application.OpenForms.OfType<WindowMain>().FirstOrDefault();
            if (main == null)
            {
                // Fallback: einfach normal zeigen
                this.StartPosition = FormStartPosition.CenterScreen;
                this.Show();
                return;
            }

            // Alle bereits existierenden ACVs außer dieser
            var others = WindowMain.OpenAudioCollectionViews
                .Where(v => v != this)
                .OrderBy(v => v.CreatedAt)
                .ToList();

            this.StartPosition = FormStartPosition.Manual;

            if (others.Count == 0)
            {
                // ERSTE ACV:
                // unten + linksbündig an WindowMain "schmiegen"
                var mb = main.Bounds;

                int margin = 4; // kleiner Abstand

                int x = mb.Left + margin;
                int y = mb.Bottom + margin; // direkt unter dem Main-Fenster

                this.Location = new Point(x, y);
            }
            else
            {
                // Weitere ACVs:
                // an letzter ACV ausrichten und leicht nach unten/rechts versetzen
                var last = others.Last();
                int offsetX = 20;
                int offsetY = 20;

                int x = last.Left + offsetX;
                int y = last.Top + offsetY;

                this.Location = new Point(x, y);
            }

            this.Show(main);
        }

    }
}
