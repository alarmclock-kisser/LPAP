using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LPAP.Forms
{
	internal static class WindowsScreenHelper
	{
		internal static float? GetScreenRefreshRate(Form? window = null, int? screenIndex = null)
		{
			try
			{
				Screen? targetScreen = null;

				// Priority 1: screen containing the given form
				if (window != null)
				{
					try
					{
						targetScreen = Screen.FromHandle(window.Handle);
					}
					catch
					{
						// fallback to control-based lookup if handle fails
						try { targetScreen = Screen.FromControl(window); } catch { }
					}
				}

				// Priority 2: screen by index
				if (targetScreen == null && screenIndex.HasValue)
				{
					try
					{
						var screens = Screen.AllScreens;
						if (screenIndex.Value >= 0 && screenIndex.Value < screens.Length)
						{
							targetScreen = screens[screenIndex.Value];
						}
					}
					catch { }
				}

				// Priority 3: primary screen
				targetScreen ??= Screen.PrimaryScreen;

				if (targetScreen == null)
				{
					return null;
				}

				// Use Win32 EnumDisplaySettings to get dmDisplayFrequency
				var devMode = new DEVMODE();
				devMode.dmSize = (short) Marshal.SizeOf<DEVMODE>();
				if (EnumDisplaySettings(targetScreen.DeviceName, ENUM_CURRENT_SETTINGS, ref devMode))
				{
					// Some drivers report 0; treat 0 as unknown -> null
					if (devMode.dmDisplayFrequency > 0)
					{
						return devMode.dmDisplayFrequency;
					}
				}

				return null;
			}
			catch
			{
				return null;
			}
		}

		internal static Size? GetScreenSize(Form? window = null, int? screenIndex = null)
		{
			try
			{
				Screen? targetScreen = null;
				// Priority 1: screen containing the given form
				if (window != null)
				{
					try
					{
						targetScreen = Screen.FromHandle(window.Handle);
					}
					catch
					{
						// fallback to control-based lookup if handle fails
						try { targetScreen = Screen.FromControl(window); } catch { }
					}
				}
				// Priority 2: screen by index
				if (targetScreen == null && screenIndex.HasValue)
				{
					try
					{
						var screens = Screen.AllScreens;
						if (screenIndex.Value >= 0 && screenIndex.Value < screens.Length)
						{
							targetScreen = screens[screenIndex.Value];
						}
					}
					catch { }
				}
				// Priority 3: primary screen
				targetScreen ??= Screen.PrimaryScreen;
				if (targetScreen == null)
				{
					return null;
				}
				return targetScreen.Bounds.Size;
			}
			catch
			{
				return null;
			}
        }

        internal static Point GetWindowScreenPosition(Form window, IEnumerable<AnchorStyles>? anchors = null)
		{
			anchors ??= []; // Empty means centered

			var screen = Screen.FromControl(window);
			int x = screen.WorkingArea.X;
			int y = screen.WorkingArea.Y;
			int w = screen.WorkingArea.Width;
			int h = screen.WorkingArea.Height;
			int winW = window.Width;
			int winH = window.Height;
			if (anchors.Contains(AnchorStyles.Left))
			{
				// x stays at screen.X
			}
			else if (anchors.Contains(AnchorStyles.Right))
			{
				x += w - winW;
			}
			else
			{
				x += (w - winW) / 2;
			}
			if (anchors.Contains(AnchorStyles.Top))
			{
				// y stays at screen.Y
			}
			else if (anchors.Contains(AnchorStyles.Bottom))
			{
				y += h - winH;
			}
			else
			{
				y += (h - winH) / 2;
			}

			return new Point(x, y);
		}

		internal static void SetWindowScreenPosition(Form window, IEnumerable<AnchorStyles>? anchors = null, bool keepTopMost = false, bool autoShow = true)
		{
			var pos = GetWindowScreenPosition(window, anchors);
			window.StartPosition = FormStartPosition.Manual;
			window.Location = new Point(pos.X, pos.Y);
			window.TopMost = keepTopMost;
			if (autoShow)
			{
				window.Show();
			}
		}

		internal static Point GetCenterStartingPoint(Form? form = null, int? screenId = null)
		{
			if (form != null)
			{
				screenId = Array.IndexOf(Screen.AllScreens, Screen.FromControl(form));
			}

			screenId ??= Screen.PrimaryScreen != null
				? Array.IndexOf(Screen.AllScreens, Screen.PrimaryScreen)
				: 0;

			Screen screen;
			if (screenId.HasValue)
			{
				Screen[] allScreens = Screen.AllScreens;
				if (screenId.Value >= 0 && screenId.Value < allScreens.Length)
				{
					screen = allScreens[screenId.Value];
				}
				else
				{
					screen = Screen.PrimaryScreen ?? allScreens.FirstOrDefault() ?? throw new InvalidOperationException("Kein Bildschirm gefunden.");
				}
			}
			else
			{
				screen = Screen.PrimaryScreen ?? Screen.AllScreens.FirstOrDefault() ?? throw new InvalidOperationException("Kein Bildschirm gefunden.");
			}

			int x = screen.WorkingArea.X + (screen.WorkingArea.Width - (form?.Width ?? 0)) / 2;
			int y = screen.WorkingArea.Y + (screen.WorkingArea.Height - (form?.Height ?? 0)) / 2;
			return new Point(x, y);
		}

		internal static Point GetCornerPosition(Form? form = null, bool left = true, bool top = true, int? screenId = null)
		{
			// Calculate starting position based on screen working area and formif given
			if (form != null)
			{
				screenId = Array.IndexOf(Screen.AllScreens, Screen.FromControl(form));
			}

			screenId ??= Screen.PrimaryScreen != null
				? Array.IndexOf(Screen.AllScreens, Screen.PrimaryScreen)
				: 0;
			Screen screen;
			if (screenId.HasValue)
			{
				Screen[] allScreens = Screen.AllScreens;
				if (screenId.Value >= 0 && screenId.Value < allScreens.Length)
				{
					screen = allScreens[screenId.Value];
				}
				else
				{
					screen = Screen.PrimaryScreen ?? allScreens.FirstOrDefault() ?? throw new InvalidOperationException("Kein Bildschirm gefunden.");
				}
			}
			else
			{
				screen = Screen.PrimaryScreen ?? Screen.AllScreens.FirstOrDefault() ?? throw new InvalidOperationException("Kein Bildschirm gefunden.");
			}
			int x = left
				? screen.WorkingArea.X
				: screen.WorkingArea.X + screen.WorkingArea.Width - (form?.Width ?? 0);
			int y = top
				? screen.WorkingArea.Y
				: screen.WorkingArea.Y + screen.WorkingArea.Height - (form?.Height ?? 0);
			return new Point(x, y);
		}



		private const int ENUM_CURRENT_SETTINGS = -1;

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
		private struct DEVMODE
		{
			private const int CCHDEVICENAME = 32;
			private const int CCHFORMNAME = 32;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
			public string dmDeviceName;
			public short dmSpecVersion;
			public short dmDriverVersion;
			public short dmSize;
			public short dmDriverExtra;
			public int dmFields;

			public int dmPositionX;
			public int dmPositionY;
			public int dmDisplayOrientation;
			public int dmDisplayFixedOutput;

			public short dmColor;
			public short dmDuplex;
			public short dmYResolution;
			public short dmTTOption;
			public short dmCollate;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHFORMNAME)]
			public string dmFormName;
			public short dmLogPixels;
			public int dmBitsPerPel;
			public int dmPelsWidth;
			public int dmPelsHeight;
			public int dmDisplayFlags;
			public int dmDisplayFrequency;
			public int dmICMMethod;
			public int dmICMIntent;
			public int dmMediaType;
			public int dmDitherType;
			public int dmReserved1;
			public int dmReserved2;
			public int dmPanningWidth;
			public int dmPanningHeight;
		}

		[DllImport("user32.dll", CharSet = CharSet.Auto)]
		private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);
	}
}
