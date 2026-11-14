// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/ProformaInvoice/Step8Attachments/
//       ProformaAttachmentRowViewModel.cs
// PROJECT: ProGlassAutomation
// MODULE: Proforma Invoice — Step 8 Attachments
//
// One row in the Step 8 attachments list. Mirrors the proven
// DnAttachmentRowViewModel (lazy thumbnail caching included) so both
// sections render identically and stay easy to maintain side by side.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.ProformaInvoice.Attachments;
using ProGlassAutomation.Services.ProformaInvoice.Attachments;
using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step8Attachments
{
    /// <summary>
    /// Display wrapper around one ProformaAttachmentRecord.
    /// Preview thumbnails load lazily ONCE and are cached — the list
    /// can re-render freely without re-reading files from disk.
    /// </summary>
    public class ProformaAttachmentRowViewModel : ViewModelBase
    {
        public string Id { get; }
        public string ProformaId { get; }
        public string AttachmentType { get; }
        public string FileName { get; private set; }
        public string FilePath { get; private set; }
        public long FileSize { get; }
        public DateTime UploadedAt { get; }

        // ── Display helpers ───────────────────────────────────────────

        public string FileSizeDisplay { get; }
        public string UploadedAtDisplay { get; }
        public string TypeIcon { get; }
        public bool IsImage { get; }
        public bool IsPdf { get; }

        private ImageSource? _preview;
        private bool _previewLoaded;

        /// <summary>
        /// Thumbnail for images — loaded on first access, then cached.
        /// Small decode width keeps memory low even for huge photos.
        /// </summary>
        public ImageSource? Preview
        {
            get
            {
                if (!IsImage) return null;

                if (!_previewLoaded)
                {
                    _preview = LoadPreview();
                    _previewLoaded = true;
                }

                return _preview;
            }
        }

        private string _description = "";
        public string Description
        {
            get => _description;
            set
            {
                if (_description == value) return;
                _description = value;
                OnPropertyChanged();
            }
        }

        public ProformaAttachmentRowViewModel(ProformaAttachmentRecord r)
        {
            Id = r.Id;
            ProformaId = r.ProformaId;
            AttachmentType = r.AttachmentType;
            FileName = r.FileName;
            FilePath = r.FilePath;
            FileSize = r.FileSize;
            UploadedAt = r.UploadedAt;
            _description = r.Description ?? "";

            // Fully qualified to avoid clash with the AttachmentType
            // string property above (same pattern as DN row VM).
            IsImage = string.Equals(
                AttachmentType,
                global::ProGlassAutomation.Services.DeliveryNote.Attachments
                    .AttachmentType.Image,
                StringComparison.OrdinalIgnoreCase);

            IsPdf = string.Equals(
                AttachmentType,
                global::ProGlassAutomation.Services.DeliveryNote.Attachments
                    .AttachmentType.Pdf,
                StringComparison.OrdinalIgnoreCase);

            TypeIcon = IsImage ? "🖼️" : "📄";
            FileSizeDisplay =
                ProformaAttachmentService.FormatFileSize(FileSize);
            UploadedAtDisplay = UploadedAt.ToString("dd MMM yyyy HH:mm");
        }

        /// <summary>Called after a successful rename keeps the row fresh.</summary>
        public void ApplyRename(string newFileName, string newFilePath)
        {
            FileName = newFileName;
            FilePath = newFilePath;
            OnPropertyChanged(nameof(FileName));
            OnPropertyChanged(nameof(FilePath));
        }

        private ImageSource? LoadPreview()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;

                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.DecodePixelWidth = 80;
                img.UriSource = new Uri(FilePath, UriKind.Absolute);
                img.EndInit();
                img.Freeze();          // frozen → safe + cheap to render
                return img;
            }
            catch
            {
                return null;
            }
        }
    }
}
