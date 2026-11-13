using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace FFmpegForge
{
    /// <summary>
    /// Saves WinForms geometry in the existing settings.ini without writing
    /// a second config file. Maximized windows retain their NORMAL bounds.
    /// Restoring handles disconnected displays, changed work areas and DPI.
    /// No window is moved while the user is dragging or resizing it.
    /// </summary>
    internal static class WindowPlacement
    {
        internal static void Save(Form form, IDictionary<string, string> settings, string key)
        {
            if (form == null || form.IsDisposed || settings == null) return;
            Rectangle bounds = form.WindowState == FormWindowState.Normal ? form.Bounds : form.RestoreBounds;
            if (bounds.Width < 100 || bounds.Height < 100) return;
            settings[key + "_bounds"] =
                bounds.X.ToString(CultureInfo.InvariantCulture) + "," +
                bounds.Y.ToString(CultureInfo.InvariantCulture) + "," +
                bounds.Width.ToString(CultureInfo.InvariantCulture) + "," +
                bounds.Height.ToString(CultureInfo.InvariantCulture);
            // Minimized windows reopen in their last usable state, never minimized.
            if (form.WindowState != FormWindowState.Minimized)
                settings[key + "_max"] = form.WindowState == FormWindowState.Maximized ? "1" : "0";
        }

        internal static bool Restore(Form form, IDictionary<string, string> settings, string key)
        {
            if (form == null || settings == null) return false;
            string raw;
            if (!settings.TryGetValue(key + "_bounds", out raw)) return false;
            string[] values = raw.Split(',');
            if (values.Length != 4) return false;
            int x, y, width, height;
            if (!int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out x) ||
                !int.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out y) ||
                !int.TryParse(values[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out width) ||
                !int.TryParse(values[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out height))
                return false;
            if (width < 100 || height < 100 || width > 30000 || height > 30000) return false;
            Rectangle saved = new Rectangle(x, y, width, height);

            Screen[] screens = Screen.AllScreens;
            Screen chosen = null;
            long bestArea = 0;
            foreach (Screen screen in screens)
            {
                Rectangle visible = Rectangle.Intersect(saved, screen.WorkingArea);
                long area = (long)Math.Max(0, visible.Width) * Math.Max(0, visible.Height);
                if (area > bestArea) { bestArea = area; chosen = screen; }
            }
            // A removed monitor or a severely clipped window must not make
            // the application impossible to reach. Center on primary.
            if (chosen == null || bestArea < (long)Theme.S(90) * Theme.S(50))
                chosen = Screen.PrimaryScreen ?? screens.FirstOrDefault();
            if (chosen == null) return false;
            Rectangle work = chosen.WorkingArea;

            int minWidth = Math.Max(100, form.MinimumSize.Width);
            int minHeight = Math.Max(100, form.MinimumSize.Height);
            int w = Math.Max(minWidth, Math.Min(width, work.Width));
            int h = Math.Max(minHeight, Math.Min(height, work.Height));
            bool wasOffscreen = bestArea < (long)Theme.S(90) * Theme.S(50);
            int left = wasOffscreen ? work.Left + (work.Width - w) / 2 :
                Math.Max(work.Left, Math.Min(x, work.Right - w));
            int top = wasOffscreen ? work.Top + (work.Height - h) / 2 :
                Math.Max(work.Top, Math.Min(y, work.Bottom - h));

            form.StartPosition = FormStartPosition.Manual;
            form.Bounds = new Rectangle(left, top, w, h);
            if (settings.TryGetValue(key + "_max", out raw) && raw == "1")
                form.WindowState = FormWindowState.Maximized;
            return true;
        }
    }
}
