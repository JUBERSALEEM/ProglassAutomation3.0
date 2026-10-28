// ═══════════════════════════════════════════════════════════════════════
// FILE: step4.CompanyProfile/step4.c.2.CompanyLogoPreview.cs
// MODULE: step4 — turning a stored image into something a screen can show.
//
// Kept apart from step4.a.3 on purpose: that class owns files and knows nothing
// about WPF; this one owns bitmaps and knows nothing about saving. A test that
// has no display can exercise the storage rules without dragging WPF in.
//
// Two rules that matter:
//   • Decode at the size the preview needs (DecodePixelWidth), so a 6000 px
//     phone photo does not become a 96 MB bitmap in a 320 px panel.
//   • Never throw. A file that vanished, a corrupt JPEG or an unsupported
//     colour profile produces "no preview", not a crash on the settings page.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ProGlassAutomation.Services.Company
{
    /// <summary>Preview bitmap and intrinsic size for the logo panel.</summary>
    public sealed class CompanyLogoPreview
    {
        public bool Available { get; init; }

        /// <summary>Decoded bitmap at preview resolution, or null when unusable.</summary>
        public ImageSource? Image { get; init; }

        /// <summary>Width ÷ height of the source image; 1 when unknown.</summary>
        public double AspectRatio { get; init; } = 1;

        public int PixelWidth { get; init; }

        public int PixelHeight { get; init; }

        /// <summary>Operator-facing reason when there is no preview.</summary>
        public string Problem { get; init; } = "";

        public static readonly CompanyLogoPreview Unavailable = new() { Available = false };
    }

    /// <summary>Loads a logo image for the preview panel.</summary>
    public static class CompanyLogoPreviewLoader
    {
        /// <summary>The preview never decodes wider than this.</summary>
        public const int PreviewPixelWidth = 640;

        /// <summary>
        /// Loads <paramref name="path"/> at preview resolution.
        /// </summary>
        /// <remarks>
        /// BitmapImage with CacheOption.OnLoad reads the file once and closes it,
        /// so replacing or deleting the asset afterwards is not blocked by the
        /// preview holding a handle.
        /// </remarks>
        public static CompanyLogoPreview Load(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return new CompanyLogoPreview
                {
                    Available = false,
                    Problem = "The image file is not on disk. Choose the logo again."
                };

            try
            {
                // Intrinsic size first, so the preview can report it and the
                // aspect ratio can be computed even if the decode is scaled.
                int width = 0;
                int height = 0;
                using (var stream = File.OpenRead(path!))
                {
                    BitmapFrame frame = BitmapFrame.Create(
                        stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    width = frame.PixelWidth;
                    height = frame.PixelHeight;
                }

                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                if (width > PreviewPixelWidth) image.DecodePixelWidth = PreviewPixelWidth;
                image.UriSource = new Uri(Path.GetFullPath(path!), UriKind.Absolute);
                image.EndInit();
                image.Freeze();

                double ratio = height > 0 ? (double)width / height : 1;

                return new CompanyLogoPreview
                {
                    Available = true,
                    Image = image,
                    AspectRatio = ratio > 0 ? ratio : 1,
                    PixelWidth = width,
                    PixelHeight = height
                };
            }
            catch (Exception error)
            {
                return new CompanyLogoPreview
                {
                    Available = false,
                    Problem = "That image could not be read (" + error.GetType().Name
                              + "). Choose a PNG or JPEG file."
                };
            }
        }
    }
}
