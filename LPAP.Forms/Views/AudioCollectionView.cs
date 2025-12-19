using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.VisualBasic;
using LPAP.Audio;
using LPAP.Forms.Dialogs;
using LPAP.Cuda;

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
		private List<AudioObj>? _selectionBeforeClick;



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
			this.listBox_audios.DoubleClick += this.ListBox_Audios_DoubleClick;


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

			string nameText = audio.Name ?? string.Empty;
			if (audio.PlaybackState == PlaybackState.Playing)
			{
				nameText = "▶ " + nameText;
			}

			// Name links, mit Ellipsis
			TextRenderer.DrawText(
				e.Graphics,
				nameText,
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
				if (duration.TotalMilliseconds < 10)
				{
					return duration.TotalMilliseconds.ToString("F1") + " ms";
				}

				return Math.Round(duration.TotalMilliseconds, 0) + " ms";
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
			if (e.Button == MouseButtons.Right)
			{
				int index = this.listBox_audios.IndexFromPoint(e.Location);
				if (index >= 0)
				{
					// Rechtsklick: Explorer-Style – wenn nicht in Auswahl, dann nur dieses Item wählen
					if (!this.listBox_audios.SelectedIndices.Contains(index))
					{
						this.listBox_audios.SelectedIndex = index;
					}
				}

				// Snapshot hier nicht anfassen
				return;
			}

			if (e.Button == MouseButtons.Left)
			{
				bool ctrl = (ModifierKeys & Keys.Control) == Keys.Control;
				bool shift = (ModifierKeys & Keys.Shift) == Keys.Shift;

				int index = this.listBox_audios.IndexFromPoint(e.Location);

				// Ohne Ctrl/Shift: Auswahl in allen anderen ACVs löschen
				if (!ctrl && !shift)
				{
					foreach (var view in WindowMain.OpenAudioCollectionViews)
					{
						if (!ReferenceEquals(view, this))
						{
							view.listBox_audios.ClearSelected();
							view._selectionBeforeClick = null;
						}
					}

					// WICHTIGER TEIL:
					// Wenn wir eine Mehrfachauswahl aus Ctrl/Shift aufgebaut hatten
					// UND jetzt ohne Ctrl/Shift auf eines der selektierten Items klicken,
					// dann stellen wir NACH dem normalen ListBox-Verhalten die alte Auswahl
					// wieder her.
					if (index >= 0 &&
						this._selectionBeforeClick != null &&
						this._selectionBeforeClick.Count > 1 &&
						this.listBox_audios.Items[index] is AudioObj clicked &&
						this._selectionBeforeClick.Contains(clicked))
					{
						// Nach dem internen Selection-Update der ListBox ausführen
						this.BeginInvoke(new Action(() =>
						{
							this.listBox_audios.ClearSelected();

							foreach (var ao in this._selectionBeforeClick)
							{
								int idx = this.AudioC.Items.IndexOf(ao);
								if (idx >= 0 && idx < this.listBox_audios.Items.Count)
								{
									this.listBox_audios.SetSelected(idx, true);
								}
							}
						}));
					}
				}

				// Snapshot der Selection NUR aktualisieren, wenn der User bewusst
				// mit Ctrl/Shift an der Mehrfachauswahl arbeitet.
				if (ctrl || shift)
				{
					this._selectionBeforeClick = this.listBox_audios.SelectedItems
						.Cast<AudioObj>()
						.ToList();
				}

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

			if (dx + dy < SystemInformation.DragSize.Width / 2)
			{
				return;
			}

			// Quelle der Drag-Items:
			// Wenn der User mit Ctrl/Shift eine Mehrfachauswahl gebaut hat,
			// liegt sie in _selectionBeforeClick. Die benutzen wir bevorzugt.
			List<AudioObj> dragItems;

			if (this._selectionBeforeClick != null && this._selectionBeforeClick.Count > 0)
			{
				dragItems = this._selectionBeforeClick.ToList();
			}
			else
			{
				dragItems = this.listBox_audios.SelectedItems.Cast<AudioObj>().ToList();
			}

			if (dragItems.Count == 0)
			{
				return;
			}

			this._isDragging = true;

			var payload = new DragPayload(this, dragItems);
			var data = new DataObject(payload);

			this.DoDragDrop(data, DragDropEffects.Move);

			this._isDragging = false;
			this._insertionIndex = -1;
			this.listBox_audios.Invalidate();

			// Nach beendetem Drag Snapshot leeren,
			// damit der nächste Drag seinen eigenen Kontext hat
			this._selectionBeforeClick = null;
		}

		private void ListBox_Audios_MouseUp(object? sender, MouseEventArgs e)
		{
			// Wenn kein Drag & kein DoubleClick folgt, kann man hier bei Bedarf
			// _selectionBeforeClick zurücksetzen – ich lasse es stehen,
			// damit DoubleClick kurz danach noch Zugriff hat.
			// Optional könnte man mit einem kleinen Timer arbeiten, ist aber overkill.
			var selected = this.GetSelectedAudioItems();
			if (selected.Count != 1)
			{
				return;
			}

			WindowMain.UpdateTrackDependentUi(selected.First());
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

		private void ListBox_Audios_DoubleClick(object? sender, EventArgs e)
		{
			var items = this._selectionBeforeClick ?? this.GetSelectedAudioItems();
			this.OpenSelectedAsTrackView(items);

			this._selectionBeforeClick = null;
		}




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



		// --------- Private Helpers ---------
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

		private List<AudioObj> GetSelectedAudioItems()
		{
			return this.listBox_audios.SelectedItems.Cast<AudioObj>().ToList();
		}

		private void OpenSelectedAsTrackView(List<AudioObj>? items = null)
		{
			var selected = items ?? this.GetSelectedAudioItems();
			if (selected.Count == 0)
			{
				return;
			}

			foreach (var audio in selected)
			{
				_ = new TrackView(audio, this.AudioC);
			}
		}






		// --------- Kontextmenü-Events ---------

		private void openAsTrackToolStripMenuItem_Click(object sender, EventArgs e)
		{
			this.OpenSelectedAsTrackView();
		}

		private void renameToolStripMenuItem_Click(object sender, EventArgs e)
		{
			var selected = this.GetSelectedAudioItems();
			if (selected.Count == 0)
			{
				return;
			}

			// Basisname: Name des ersten selektierten
			string currentName = selected[0].Name ?? string.Empty;

			string input = Interaction.InputBox(
				"Enter new name (base name for multiple items):",
				"Rename",
				currentName);

			if (string.IsNullOrWhiteSpace(input))
			{
				return;
			}

			if (selected.Count == 1)
			{
				selected[0].Name = input;
				return;
			}

			// Mehrere: Nummerierung anhängen
			// 1–9 → 1…9 ; 10–99 → 01…99 ; 100+ → 001… usw.
			int count = selected.Count;
			int digits = count.ToString().Length; // 1..3...

			string numberFormat = new('0', digits); // "0", "00", "000", ...

			// Reihenfolge: nach Index in der Liste
			var ordered = selected
				.Select(a => new { Audio = a, Index = this.AudioC.Items.IndexOf(a) })
				.Where(x => x.Index >= 0)
				.OrderBy(x => x.Index)
				.ToList();

			int n = 1;
			foreach (var item in ordered)
			{
				string suffix = n.ToString(numberFormat);
				item.Audio.Name = $"{input} {suffix}";
				n++;
			}
		}

		private void editTagsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			var selected = this.GetSelectedAudioItems();
			if (selected.Count == 0)
			{
				return;
			}

			// Open one TagEditor for all selected items
			var tagEditor = new TagEditorDialog(selected);
		}

		private void visualizerToolStripMenuItem_Click(object sender, EventArgs e)
		{
			var audio = this.GetSelectedAudioItems().FirstOrDefault();
			if (audio == null)
			{
				MessageBox.Show("No audio selected.", "Visualizer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			var dlg = new VisualizerDialog(audio);
			dlg.ShowDialog(this);
		}

		private void stemsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			var selected = this.GetSelectedAudioItems();
			if (selected.Count != 1)
			{
				MessageBox.Show("Please select exactly one audio track for stem separation.", "Stems", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			var dlg = new OnnxControlView(selected.First());
			dlg.ShowDialog(this);
		}

		private void addNumberingToolStripMenuItem_Click(object sender, EventArgs e)
		{
			// Nur bei aktivem Häkchen ausführen
			if (!this.addNumberingToolStripMenuItem.Checked)
			{
				return;
			}

			var selected = this.GetSelectedAudioItems();
			if (selected.Count == 0)
			{
				return;
			}

			int count = selected.Count;
			if (count == 0)
			{
				return;
			}

			string fmt = this.toolStripTextBox_format.Text?.Trim() ?? string.Empty;
			if (fmt.Equals("default format", StringComparison.OrdinalIgnoreCase))
			{
				fmt = string.Empty;
			}

			// Reihenfolge: wie in der ListBox
			var ordered = selected
				.Select(a => new { Audio = a, Index = this.AudioC.Items.IndexOf(a) })
				.Where(x => x.Index >= 0)
				.OrderBy(x => x.Index)
				.ToList();

			int n = 1;
			foreach (var item in ordered)
			{
				int number = n;

				string numberString;
				try
				{
					numberString = string.IsNullOrEmpty(fmt)
						? number.ToString()
						: number.ToString(fmt);
				}
				catch
				{
					numberString = number.ToString();
				}

				// Numerierungs-Präfix vor den vorhandenen Namen stellen
				item.Audio.Name = $"{numberString} {item.Audio.Name}";
				n++;
			}
		}

		private async void resampleToolStripMenuItem_Click(object sender, EventArgs e)
		{
			// VBasic Dialog to enter new sample rate
			var selected = this.GetSelectedAudioItems();
			if (selected.Count == 0)
			{
				return;
			}

			int? sr = selected.Count == 1 ? selected[0].SampleRate : null;

			string input = Interaction.InputBox(
				"Enter new sample rate (Hz):",
				"Resample",
				sr?.ToString() ?? "44100");

			sr = int.TryParse(input, out var val) ? val : null;
			if (sr == null || sr <= 0)
			{
				return;
			}

			// Make cursor wait
			var cur = this.Cursor;
			this.Cursor = Cursors.WaitCursor;

			var tasks = selected.Where(a => a.SampleRate != sr.Value).Select(audio =>
			{
				CudaLog.Info($"Resampling '{audio.Name}' from {audio.SampleRate} Hz to {sr.Value} Hz...", null, "AudioObj");
				return audio.ResampleAsync(sr.Value);
			});

			await Task.WhenAll(tasks);

			this.Cursor = cur;
			CudaLog.Info("Resampling completed.", null, "AudioObj");
			WindowMain.UpdateTrackDependentUi();
		}

		private async void rechannelToolStripMenuItem_Click(object sender, EventArgs e)
		{
			var selected = this.GetSelectedAudioItems();
			if (selected.Count == 0)
			{
				return;
			}

			int? ch = selected.Count == 1 ? selected[0].Channels : null;
			string input = Interaction.InputBox(
				"Enter new number of channels:",
				"Rechannel",
				ch?.ToString() ?? "2");

			ch = int.TryParse(input, out var val) ? val : null;
			if (ch == null || ch <= 0)
			{
				return;
			}

			// Make cursor wait
			var cur = this.Cursor;
			this.Cursor = Cursors.WaitCursor;

			var tasks = selected.Where(a => a.Channels != ch.Value).Select(audio =>
			{
				CudaLog.Info($"Rechanneling '{audio.Name}' from {audio.Channels} to {ch.Value} channels...", null, "AudioObj");
				return audio.TransformChannelsAsync(ch.Value);
			});

			await Task.WhenAll(tasks);

			this.Cursor = cur;
			CudaLog.Info("Rechanneling completed.", null, "AudioObj");
			WindowMain.UpdateTrackDependentUi();
		}

		private void deleteToolStripMenuItem_Click(object sender, EventArgs e)
		{
			var selected = this.GetSelectedAudioItems();
			if (selected.Count == 0)
			{
				return;
			}

			// Optional: Nachfrage
			var result = MessageBox.Show(
				$"Delete {selected.Count} track(s)?",
				"Delete",
				MessageBoxButtons.OKCancel,
				MessageBoxIcon.Warning);

			if (result != DialogResult.OK)
			{
				return;
			}

			// Kopie, damit wir während des Löschens die Auswahl nicht verändern
			foreach (var audio in selected.ToList())
			{
				this.AudioC.Remove(audio); // Remove() disposet den Track
			}
		}


		// Public
		public void RefreshListBox()
		{
			this.listBox_audios.Invalidate();
		}

		public void Rename(string newName)
		{
			this.Text = newName;
		}

		
	}





	internal class AudioListBox : ListBox
	{
		protected override void OnMouseDown(MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				bool ctrl = (ModifierKeys & Keys.Control) == Keys.Control;
				bool shift = (ModifierKeys & Keys.Shift) == Keys.Shift;

				int index = this.IndexFromPoint(e.Location);

				// Fall: Mehrfachauswahl vorhanden, kein Ctrl/Shift,
				// und man klickt auf ein bereits selektiertes Item
				// => wir wollen NUR draggen, NICHT die Auswahl ändern.
				if (!ctrl && !shift &&
					index >= 0 &&
					this.SelectedIndices.Count > 1 &&
					this.SelectedIndices.Contains(index))
				{
					// Fokus setzen, aber KEIN base.OnMouseDown aufrufen
					// => Selection bleibt visuell erhalten.
					this.Focus();
					return;
				}
			}

			base.OnMouseDown(e);
		}
	}
}
