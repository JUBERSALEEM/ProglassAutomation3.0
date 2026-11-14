// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Export/ScreenshotService.cs
// PROJECT: ProGlassAutomation
// MODULE: Export — high-clarity window / UI capture
//
// Faithful implementation of the reference screenshot pipeline, with two
// capture paths:
//
//   NORMAL  — renders the LIVE window at the reference scale+DPI formula.
//   FULL    — scrolls the active module's main ScrollViewer top-to-bottom,
//             captures each viewport at the chosen quality, and stitches the
//             sections into ONE continuous long portrait (via SkiaSharp, so
//             the result is not limited by WPF's ~16k texture cap and can be
//             a large, sharp, 10 MB+ PNG).
//
// SAVE:
//   The user picks the location/format via a SaveFileDialog. The default
//   filename embeds the module name, the quality label and a date/time stamp:
//     <Module>_<Quality>_yyyyMMdd_HHmmss.png
//
// PRIVACY: everything stays local. No uploads, no AI upscaling, no
// telemetry. Screenshots are treated as sensitive business data.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SkiaSharp;
using ProGlassAutomation.Services.Dialogs;

namespace ProGlassAutomation.Services.Export
{
    /// <summary>Quality presets, exactly as in the reference.</summary>
    public enum ScreenshotQuality
    {
        HD,        // 2560 × 1440   (label "HD 720p")
        FullHD,    // 3840 × 2160   (label "Full HD 1080p")
        QHD,       // 5120 × 2880   (label "2K QHD")
        UltraHD,   // 7680 × 4320   (label "4K UHD")
        UHD8K      // 10240 × 5760  (label "8K UHD")
    }

    public static class ScreenshotService
    {
        /// <summary>
        /// Returns the quality preset's target output dimensions, exactly as
        /// in the reference switch.
        /// </summary>
        public static (int Width, int Height) GetTargetSize(ScreenshotQuality quality) => quality switch
        {
            ScreenshotQuality.HD      => (2560, 1440),
            ScreenshotQuality.QHD     => (5120, 2880),
            ScreenshotQuality.UltraHD => (7680, 4320),
            ScreenshotQuality.UHD8K   => (10240, 5760),
            _                         => (3840, 2160)   // FullHD / default
        };

        /// <summary>Friendly label per quality preset (reference wording).</summary>
        public static string GetLabel(ScreenshotQuality quality) => quality switch
        {
            ScreenshotQuality.HD      => "HD 720p",
            ScreenshotQuality.FullHD  => "Full HD 1080p",
            ScreenshotQuality.QHD     => "2K QHD",
            ScreenshotQuality.UltraHD => "4K UHD",
            ScreenshotQuality.UHD8K   => "8K UHD",
            _                         => "Full HD 1080p"
        };

        /// <summary>Resolution string shown under each quality button.</summary>
        public static string GetResolutionText(ScreenshotQuality quality)
        {
            var (w, h) = GetTargetSize(quality);
            return $"{w} × {h}";
        }

        /// <summary>
        /// Renders the main window using the reference's scale + DPI formula
        /// and returns the frozen bitmap (or null on failure). This is the
        /// NORMAL capture path and is unchanged.
        /// </summary>
        public static BitmapSource? CaptureMainWindow(ScreenshotQuality quality)
        {
            var window = Application.Current?.MainWindow;
            if (window == null) return null;

            try
            {
                // Reference: force layout + render-priority pump before capture.
                window.UpdateLayout();
                var frame = new DispatcherFrame();
                window.Dispatcher.BeginInvoke(
                    DispatcherPriority.Render,
                    new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);

                var (targetWidth, targetHeight) = GetTargetSize(quality);

                double actualW = Math.Max(1, window.ActualWidth);
                double actualH = Math.Max(1, window.ActualHeight);

                // Reference formula.
                double scaleX = targetWidth / actualW;
                double scaleY = targetHeight / actualH;
                double scale = Math.Max(scaleX, scaleY);
                if (scale < 4.0) scale = 4.0;

                int width = (int)(actualW * scale);
                int height = (int)(actualH * scale);
                int dpi = (int)(96 * scale);

                // Render the LIVE window directly (GPU-accelerated path).
                var rtb = new RenderTargetBitmap(
                    width, height, dpi, dpi, PixelFormats.Pbgra32);
                rtb.Render(window);
                rtb.Freeze();
                return rtb;
            }
            catch (Exception ex)
            {
                Services.Logging.AppLogService.Error(
                    "Screenshot render failed.", ex,
                    new Services.Logging.LogContext
                    {
                        Module = "Export",
                        Class = nameof(ScreenshotService),
                        Operation = "Capture"
                    });
                return null;
            }
        }

        /// <summary>
        /// Captures the FULL scrollable module as one continuous long portrait
        /// by scrolling the main ScrollViewer top-to-bottom, capturing each
        /// viewport at the chosen quality, and stitching the sections together
        /// in order (no duplication, no skips, no seams).
        ///
        /// WHY STITCH (NOT "EXPAND + RENDER ONCE"): a ScrollViewer's height is
        /// clamped by the shell's fixed content card, and a virtualized list
        /// (ListBox/DataGrid) only REALIZES the rows currently in view — so a
        /// single render cannot capture rows below the fold. Scrolling through
        /// the page lets a virtualized list realize every row as it is passed,
        /// and stitching preserves exact order. Each section after the first
        /// strips a small overlap so adjacent sections never drop or repeat
        /// content (they touch edge-to-edge exactly).
        ///
        /// Returns a SkiaSharp SKBitmap so the result is NOT limited by WPF's
        /// ~16k texture cap — a long portrait can be arbitrarily tall and still
        /// save as a large (10 MB+) PNG. Returns null when there is nothing to
        /// scroll (caller falls back to a normal window capture).
        /// </summary>
        private static SKBitmap? StitchFullPage(ScreenshotQuality quality)
        {
            var window = Application.Current?.MainWindow;
            if (window == null) return null;

            System.Windows.Controls.ScrollViewer? sv = null;
            if (window.Content is System.Windows.DependencyObject root)
                sv = FindMainVerticalScrollViewer(root);

            if (sv == null || sv.ScrollableHeight <= 0)
                return null;   // nothing scrolls — caller falls back

            var savedScrollBar = sv.VerticalScrollBarVisibility;
            double savedOffset = sv.VerticalOffset;
            bool savedCanScroll = sv.CanContentScroll;
            bool forcedPixel = false;
            try
            {
                // Hide the vertical scrollbar so it never appears in the
                // portrait and the viewport is identical across every section.
                sv.VerticalScrollBarVisibility =
                    System.Windows.Controls.ScrollBarVisibility.Hidden;

                // Some lists scroll by ITEM (ScrollUnit=Item, e.g. Job Order /
                // Sheet Store): their ExtentHeight/ViewportHeight are reported
                // in ITEMS, not pixels, which would corrupt the stitch geometry.
                // Force PIXEL scrolling for the duration of the capture so the
                // math is exact. (This realises the list's content while the
                // capture runs, which is inherent to capturing the full module;
                // it is restored in finally.)
                if (savedCanScroll)
                {
                    forcedPixel = true;
                    sv.CanContentScroll = false;
                    for (System.Windows.DependencyObject a = sv; a != null;
                         a = System.Windows.Media.VisualTreeHelper.GetParent(a) as System.Windows.DependencyObject)
                    {
                        if (a is System.Windows.Controls.ItemsControl ic)
                            System.Windows.Controls.ScrollViewer.SetCanContentScroll(ic, false);
                    }
                }
                window.UpdateLayout();

                double viewportW = Math.Max(1, sv.ViewportWidth);
                double viewportH = Math.Max(1, sv.ViewportHeight);
                double contentW = Math.Max(viewportW, Math.Max(1, sv.ExtentWidth));
                double contentH = Math.Max(1, sv.ExtentHeight);   // full scrollable height
                double scrollMax = Math.Max(0, sv.ScrollableHeight);

                var (targetWidth, _) = GetTargetSize(quality);

                // Render the page width at roughly the quality preset's width.
                double scale = targetWidth / viewportW;
                scale = Math.Max(1.0, Math.Min(4.0, scale));
                double finalW = contentW * scale;
                double finalH = contentH * scale;

                // Memory guard: cap total pixels (~150 MP). Normal modules are
                // unaffected and can still save 10 MB+ PNGs; only an extreme
                // page gets a proportionally smaller (still tall) render rather
                // than exhausting RAM.
                const long maxPixels = 150_000_000L;
                long pixels = (long)(finalW * finalH);
                if (pixels > maxPixels)
                {
                    double r = Math.Sqrt((double)maxPixels / pixels);
                    scale *= r;
                    finalW = contentW * scale;
                    finalH = contentH * scale;
                }

                int W = Math.Max(1, (int)Math.Round(finalW));
                int H = Math.Max(1, (int)Math.Round(finalH));
                int dpi = Math.Max(96, (int)(96 * scale));

                // Small overlap prevents hairline seams; each section after the
                // first strips this overlap when placed, so no content is lost
                // or duplicated.
                double overlap = Math.Min(48, viewportH * 0.05);
                double stepH = Math.Max(1, viewportH - overlap);
                int sections = Math.Max(1, (int)Math.Ceiling(contentH / stepH));

                var info = new SKImageInfo(W, H, SKColorType.Bgra8888, SKAlphaType.Premul);
                var result = new SKBitmap(info);
                using (var canvas = new SKCanvas(result))
                {
                    canvas.Clear(SKColors.White);
                    for (int i = 0; i < sections; i++)
                    {
                        double yTop = Math.Min(i * stepH, scrollMax);
                        sv.ScrollToVerticalOffset(yTop);
                        WaitForRender(window);

                        // BitmapSource is a Freezable, not IDisposable — no
                        // `using` (it is collected by the GC once out of scope).
                        var section = CaptureViewport(sv, viewportW, viewportH, scale, dpi);
                        if (section == null) continue;

                        int secW = section.PixelWidth;
                        int secH = section.PixelHeight;
                        int yTopPx = (int)Math.Round(yTop * scale);
                        int srcY0Px = (i > 0) ? (int)Math.Round(overlap * scale) : 0;
                        int dstY = yTopPx + srcY0Px;
                        if (dstY >= H) break;
                        int srcHpx = Math.Min(secH - srcY0Px, H - dstY);
                        if (srcHpx <= 0) break;

                        using var skSection = ToSkBitmap(section);
                        // 1:1 blit (src and dst are both at `scale`) so edges
                        // stay crisp — no stretch, no blur.
                        canvas.DrawBitmap(
                            skSection,
                            new SKRect(0, srcY0Px, secW, srcY0Px + srcHpx),
                            new SKRect(0, dstY, secW, dstY + srcHpx));
                    }
                }
                return result;
            }
            catch (Exception ex)
            {
                Services.Logging.AppLogService.Error(
                    "Full-page stitch failed.", ex,
                    new Services.Logging.LogContext
                    {
                        Module = "Export",
                        Class = nameof(ScreenshotService),
                        Operation = "CaptureFullPage"
                    });
                return null;
            }
            finally
            {
                if (sv != null)
                {
                    try
                    {
                        if (forcedPixel)
                        {
                            for (System.Windows.DependencyObject a = sv; a != null;
                                 a = System.Windows.Media.VisualTreeHelper.GetParent(a) as System.Windows.DependencyObject)
                            {
                                if (a is System.Windows.Controls.ItemsControl ic)
                                    System.Windows.Controls.ScrollViewer.SetCanContentScroll(ic, savedCanScroll);
                            }
                            sv.CanContentScroll = savedCanScroll;
                        }
                        sv.VerticalScrollBarVisibility = savedScrollBar;
                        sv.InvalidateMeasure();
                        sv.InvalidateArrange();
                        window.UpdateLayout();
                        sv.ScrollToVerticalOffset(savedOffset);
                    }
                    catch (System.Exception ex) { ProGlassAutomation.Services.Logging.AppLogService.Debug("[ScreenshotService] StitchFullPage: restore is best-effort: " + ex.Message); }
                }
            }
        }

        /// <summary>Renders the ScrollViewer's current viewport at `scale`.</summary>
        private static BitmapSource? CaptureViewport(
            System.Windows.Controls.ScrollViewer sv,
            double viewportW, double viewportH, double scale, int dpi)
        {
            int w = Math.Max(1, (int)Math.Round(viewportW * scale));
            int h = Math.Max(1, (int)Math.Round(viewportH * scale));
            var rtb = new RenderTargetBitmap(w, h, dpi, dpi, PixelFormats.Pbgra32);
            rtb.Render(sv);
            rtb.Freeze();
            return rtb;
        }

        /// <summary>
        /// Forces layout then pumps the dispatcher to Render priority so the
        /// scroll is realized and drawn before the viewport is captured.
        /// </summary>
        private static void WaitForRender(System.Windows.Window window)
        {
            window.UpdateLayout();
            var frame = new DispatcherFrame();
            window.Dispatcher.BeginInvoke(
                DispatcherPriority.Render,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        /// <summary>Converts a Pbgra32 BitmapSource into a SkiaSharp bitmap.</summary>
        private static SKBitmap ToSkBitmap(BitmapSource source)
        {
            int w = source.PixelWidth;
            int h = source.PixelHeight;
            var bmp = new SKBitmap(
                new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
            source.CopyPixels(
                new Int32Rect(0, 0, w, h), bmp.GetPixels(), w * 4 * h, w * 4);
            return bmp;
        }

        /// <summary>
        /// Depth-first search for the module's main vertical scroll region —
        /// the ScrollViewer that can actually scroll vertically and has the
        /// largest ExtentHeight (so a virtualized ListBox/DataGrid list wins
        /// over a short wrapper).
        /// </summary>
        private static System.Windows.Controls.ScrollViewer? FindMainVerticalScrollViewer(
            System.Windows.DependencyObject parent)
        {
            System.Windows.Controls.ScrollViewer? best = null;
            double bestExtent = 0;

            void Walk(System.Windows.DependencyObject node)
            {
                if (node is System.Windows.Controls.ScrollViewer sv
                    && sv.ScrollableHeight > 0
                    && sv.ExtentHeight > bestExtent)
                {
                    best = sv;
                    bestExtent = sv.ExtentHeight;
                }
                // Keep walking into it — there may be a deeper scrolling list.
                // Skip ColumnDefinition / RowDefinition (not Visuals).
                if (node is not System.Windows.Media.Visual
                    and not System.Windows.Media.Media3D.Visual3D)
                    return;
                int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);
                for (int i = 0; i < count; i++)
                    Walk(System.Windows.Media.VisualTreeHelper.GetChild(node, i));
            }

            Walk(parent);
            return best;
        }

        /// <summary>
        /// Runs the reference's capture + SaveFileDialog flow. Returns true
        /// when a file was saved.
        /// </summary>
        public static bool CaptureAndSave(ScreenshotQuality quality)
            => SaveBitmap(CaptureMainWindow(quality), GetLabel(quality));

        /// <summary>
        /// Captures the full scrollable page (long lists) via scroll + stitch,
        /// then prompts for a save location. Returns true when a file was
        /// saved. Falls back to a normal window capture if the module does not
        /// scroll.
        /// </summary>
        public static bool CaptureFullPageAndSave(ScreenshotQuality quality)
        {
            var stitched = StitchFullPage(quality);
            if (stitched == null)
                return SaveBitmap(CaptureMainWindow(quality), GetLabel(quality) + "_FullPage");

            try
            {
                return SaveSkBitmap(stitched, GetLabel(quality) + "_FullPage");
            }
            finally
            {
                stitched.Dispose();
            }
        }

        private static bool SaveBitmap(BitmapSource? bitmap, string label)
        {
            if (bitmap == null)
            {
                Prompts.Show("The screenshot could not be rendered. See the log for details.",
                    "Screenshot Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }

            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PNG Image|*.png|BMP Image|*.bmp|JPEG Image|*.jpg",
                FileName = $"{SafeModuleName()}_{label}_{DateTime.Now:yyyyMMdd_HHmmss}.png",
                Title = "Save Screenshot",
                DefaultExt = ".png",
                AddExtension = true
            };

            if (sfd.ShowDialog() != true) return false;

            try
            {
                string filePath = sfd.FileName;
                string extension = Path.GetExtension(filePath).ToLowerInvariant();
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                BitmapEncoder encoder = extension switch
                {
                    ".bmp" => new BmpBitmapEncoder(),
                    ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 100 },
                    _ => new PngBitmapEncoder()   // default lossless PNG
                };
                encoder.Frames.Add(BitmapFrame.Create(bitmap));

                using var fs = File.Create(filePath);
                encoder.Save(fs);

                var info = new FileInfo(filePath);
                string size = info.Length >= 1024 * 1024
                    ? $"{info.Length / (1024.0 * 1024.0):F2} MB"
                    : $"{info.Length / 1024.0:F1} KB";

                Prompts.Show($"Screenshot saved!\n\nFile: {filePath}\n" +
                    $"Resolution: {bitmap.PixelWidth} × {bitmap.PixelHeight}\n" +
                    $"Size: {size}",
                    "Screenshot",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return true;
            }
            catch (Exception ex)
            {
                Services.Logging.AppLogService.Error(
                    "Screenshot save failed.", ex,
                    new Services.Logging.LogContext
                    {
                        Module = "Export",
                        Class = nameof(ScreenshotService),
                        Operation = "Save"
                    });
                Prompts.Show($"Error saving screenshot:\n\n{ex.Message}",
                    "Screenshot Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// Saves a stitched SkiaSharp bitmap (the full-page portrait) via
        /// SkiaSharp, which handles very large images that exceed WPF's
        /// texture cap. The caller disposes <paramref name="bitmap"/>.
        /// </summary>
        private static bool SaveSkBitmap(SKBitmap bitmap, string label)
        {
            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PNG Image|*.png|JPEG Image|*.jpg",
                FileName = $"{SafeModuleName()}_{label}_{DateTime.Now:yyyyMMdd_HHmmss}.png",
                Title = "Save Screenshot",
                DefaultExt = ".png",
                AddExtension = true
            };

            if (sfd.ShowDialog() != true) return false;

            try
            {
                string filePath = sfd.FileName;
                string extension = Path.GetExtension(filePath).ToLowerInvariant();
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                using var image = SKImage.FromBitmap(bitmap);
                var format = extension switch
                {
                    ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
                    _ => SKEncodedImageFormat.Png   // default lossless PNG
                };
                using var data = image.Encode(format, 100);
                using (var fs = File.Create(filePath))
                    data.SaveTo(fs);

                var info = new FileInfo(filePath);
                string size = info.Length >= 1024 * 1024
                    ? $"{info.Length / (1024.0 * 1024.0):F2} MB"
                    : $"{info.Length / 1024.0:F1} KB";

                Prompts.Show($"Screenshot saved!\n\nFile: {filePath}\n" +
                    $"Resolution: {bitmap.Width} × {bitmap.Height}\n" +
                    $"Size: {size}",
                    "Screenshot",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return true;
            }
            catch (Exception ex)
            {
                Services.Logging.AppLogService.Error(
                    "Full-page screenshot save failed.", ex,
                    new Services.Logging.LogContext
                    {
                        Module = "Export",
                        Class = nameof(ScreenshotService),
                        Operation = "SaveFullPage"
                    });
                Prompts.Show($"Error saving screenshot:\n\n{ex.Message}",
                    "Screenshot Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>Module name for the default filename, sanitised.</summary>
        private static string SafeModuleName()
        {
            string name = Application.Current?.MainWindow?.DataContext is
                ViewModels.MainViewModel vm
                ? vm.CurrentModuleTitle
                : "MODULE";

            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');

            return string.IsNullOrWhiteSpace(name) ? "MODULE" : name.Trim();
        }
    }
}
