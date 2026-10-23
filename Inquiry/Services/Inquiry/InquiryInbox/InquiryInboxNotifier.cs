// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/Services/Inquiry/InquiryInbox/InquiryInboxNotifier.cs
// MODULE: Inquiry — desktop notification for new website inquiries.
//
// A NotifyIcon tray balloon ("like the usual apps") plus a system sound,
// fired the moment a sync sees a submission the local ledger has not
// seen before. Created and driven on the UI thread (the NotifyIcon owns
// a message loop); the icon is a small accent tile drawn in code so the
// app carries no extra binary assets.
// ═══════════════════════════════════════════════════════════════════════

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Media;
using System.Windows.Forms;

namespace ProGlassAutomation.Services.Inquiry.InquiryInbox
{
    public sealed class InquiryInboxNotifier : IDisposable
    {
        private NotifyIcon? _icon;
        private bool _disposed;
        private string _title = "New inquiry from the customer website";
        private bool _soundEnabled = true;

        /// <summary>Double-click on the tray icon (view can route it).</summary>
        public event EventHandler? DoubleClicked;

        /// <summary>Creates the tray icon. Call once on the UI thread.</summary>
        public void EnsureCreated()
        {
            if (_icon != null || _disposed) return;
            _icon = new NotifyIcon
            {
                Icon = CreateIcon(),
                Text = "ProGlass — Customer Inquiry Inbox",
                Visible = true
            };
            _icon.DoubleClick += (_, _) => DoubleClicked?.Invoke(this, EventArgs.Empty);
        }

        public void SetSoundEnabled(bool enabled) => _soundEnabled = enabled;

        /// <summary>
        /// Shows a balloon for one new delivery. <paramref name="company"/>
        /// and <paramref name="estimator"/> may be empty.
        /// </summary>
        public void NotifyNewInquiry(string reference, string company, string estimator)
        {
            if (_icon == null || _disposed) return;
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(reference)) parts.Add(reference);
            if (!string.IsNullOrWhiteSpace(company)) parts.Add(company);
            var detail = parts.Count > 0 ? string.Join(" — ", parts) : "New website submission";
            if (!string.IsNullOrWhiteSpace(estimator)) detail += " · " + estimator;
            try
            {
                _icon.ShowBalloonTip(5000, _title, detail, ToolTipIcon.Info);
                if (_soundEnabled) SystemSounds.Asterisk.Play();
            }
            catch { /* notification failure must never break a sync */ }
        }

        /// <summary>Generic status balloon (e.g. sync failure).</summary>
        public void NotifyStatus(string title, string message)
        {
            if (_icon == null || _disposed) return;
            try { _icon.ShowBalloonTip(5000, title, message, ToolTipIcon.Warning); }
            catch { }
        }

        /// <summary>
        /// 32×32 tile: rounded accent square with a white "i". Colours are
        /// icon artwork (not theme chrome), fixed on purpose.
        /// </summary>
        private static Icon CreateIcon()
        {
            using var bitmap = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using var fill = new SolidBrush(Color.FromArgb(0x25, 0x63, 0xEB));
                using var path = RoundedRect(2, 2, 28, 28, 8);
                g.FillPath(fill, path);
                using var textBrush = new SolidBrush(Color.White);
                using var font = new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel);
                var format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                g.DrawString("i", font, textBrush, new RectangleF(0, 1f, 32, 32), format);
            }
            var handle = bitmap.GetHicon();
            using var icon = Icon.FromHandle(handle);
            var clone = (Icon)icon.Clone();
            DestroyIconHandle(handle);
            return clone;
        }

        private static GraphicsPath RoundedRect(float x, float y, float w, float h, float radius)
        {
            var path = new GraphicsPath();
            path.AddArc(x, y, radius, radius, 180, 90);
            path.AddArc(x + w - radius, y, radius, radius, 270, 90);
            path.AddArc(x + w - radius, y + h - radius, radius, radius, 0, 90);
            path.AddArc(x, y + h - radius, radius, radius, 90, 90);
            path.CloseFigure();
            return path;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);
        private static void DestroyIconHandle(IntPtr handle) => DestroyIcon(handle);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_icon != null)
            {
                _icon.Visible = false;
                _icon.Dispose();
                _icon = null;
            }
        }
    }
}
