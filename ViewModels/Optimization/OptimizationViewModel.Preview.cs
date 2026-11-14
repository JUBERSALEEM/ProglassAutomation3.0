// ═══════════════════════════════════════════════════════════════
// FILE: OptimizationViewModel.Preview.cs
// MODULE: Optimization — 2D VIEW — sheet canvas, zoom, filmstrip,
//        single sheet CAD cut sizes, offcuts, piece schedule,
//        and multi-sheet gallery (8/12 per page) (partial of OptimizationViewModel)
// ═══════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using ProGlassAutomation.Commands;
using ProGlassAutomation.Services.Optimization;

namespace ProGlassAutomation.ViewModels.Optimization
{
    public partial class OptimizationViewModel : ViewModels.ViewModelBase
    {

        // ── 2D VIEW tab ────────────────────────────────────────────────

        public sealed class PieceRectVm
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public string Label { get; set; } = string.Empty;
            public bool Rotated { get; set; }

            // Enhanced industrial cutting attributes for CAD visualization
            public string DimensionsText { get; set; } = string.Empty;
            public string ShortDimensionsText { get; set; } = string.Empty;
            public string PieceRef { get; set; } = string.Empty;
            public int CutNumber { get; set; }
            public string CutTag { get; set; } = string.Empty;
            public int WidthMm { get; set; }
            public int HeightMm { get; set; }
            public decimal AreaM2 { get; set; }
            public string AreaM2Text { get; set; } = string.Empty;
            public bool ShowDimensions { get; set; } = true;
            public bool ShowSequence { get; set; } = true;
            public bool ShowFullDetails { get; set; } = true;
        }

        public sealed class OffcutRectVm
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public string DimensionsText { get; set; } = string.Empty;
            public string Label { get; set; } = string.Empty;
            public bool IsReusable { get; set; } = true;
        }

        public sealed class PlacedPieceDetailVm : ViewModels.ViewModelBase
        {
            public int SequenceNumber { get; set; }
            public string CutTag { get; set; } = string.Empty;
            public string PieceRef { get; set; } = string.Empty;
            public string DimensionsText { get; set; } = string.Empty;
            public int WidthMm { get; set; }
            public int HeightMm { get; set; }
            public string RotatedText { get; set; } = string.Empty;
            public bool Rotated { get; set; }
            public string AreaM2Text { get; set; } = string.Empty;
            public string GlassSpec { get; set; } = string.Empty;
            public string Position { get; set; } = string.Empty;
        }

        public sealed class GallerySheetVm : ViewModels.ViewModelBase
        {
            private bool _isSelected;
            public int Index { get; set; }
            public int SheetNo { get; set; }
            public string SheetNoLabel { get; set; } = string.Empty;
            public string StockLabel { get; set; } = string.Empty;
            public string Composition { get; set; } = string.Empty;
            public string UtilizationLabel { get; set; } = string.Empty;
            public double UtilizationPct { get; set; }
            public string UtilizationTone { get; set; } = "Good";
            public string WasteLabel { get; set; } = string.Empty;
            public string PieceCountLabel { get; set; } = string.Empty;
            public int PieceCount { get; set; }
            public double CanvasWidth { get; set; } = 240.0;
            public double CanvasHeight { get; set; } = 160.0;
            public ObservableCollection<PieceRectVm> Pieces { get; } = new();

            public bool IsSelected
            {
                get => _isSelected;
                set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); }
            }
        }

        public ObservableCollection<PieceRectVm> SheetRects { get; } = new();
        public ObservableCollection<OffcutRectVm> SheetOffcuts { get; } = new();
        public ObservableCollection<PlacedPieceDetailVm> CurrentSheetPieces { get; } = new();

        // ── Multi-sheet gallery collections ─────────────────────────────
        public ObservableCollection<GallerySheetVm> AllGallerySheets { get; } = new();
        public ObservableCollection<GallerySheetVm> PagedGallerySheets { get; } = new();

        // ── Mini-map thumbnail: a fixed 120px-wide scaled-down
        //    render of the current sheet so the operator sees the whole
        //    layout at a glance, independent of the canvas zoom. ────────
        public ObservableCollection<PieceRectVm> MiniMapRects { get; } = new();

        private double _miniMapWidth;
        private double _miniMapHeight;
        public double MiniMapWidth { get => _miniMapWidth; private set { _miniMapWidth = value; OnPropertyChanged(); } }
        public double MiniMapHeight { get => _miniMapHeight; private set { _miniMapHeight = value; OnPropertyChanged(); } }

        private double _renderWidth;
        private double _renderHeight;
        private string _previewCaption = string.Empty;
        private int _selectedSheetIndex;
        private double _previewScale = 1.0;

        public double RenderWidth { get => _renderWidth; private set { _renderWidth = value; OnPropertyChanged(); } }
        public double RenderHeight { get => _renderHeight; private set { _renderHeight = value; OnPropertyChanged(); } }
        public string PreviewCaption { get => _previewCaption; private set { _previewCaption = value; OnPropertyChanged(); } }

        private string _previewSheetCounter = string.Empty;
        /// <summary>"Sheet 3 of 17" — the clean centre read-out of the 2D navigator.</summary>
        public string PreviewSheetCounter { get => _previewSheetCounter; private set { _previewSheetCounter = value; OnPropertyChanged(); } }

        private string _previewGroupLabel = string.Empty;
        /// <summary>Cutting-group (glass) label shown next to the sheet counter.</summary>
        public string PreviewGroupLabel { get => _previewGroupLabel; private set { _previewGroupLabel = value; OnPropertyChanged(); } }

        private string _sheetDimensionsLabel = string.Empty;
        public string SheetDimensionsLabel { get => _sheetDimensionsLabel; private set { _sheetDimensionsLabel = value; OnPropertyChanged(); } }

        private string _sheetDimensionsTop = string.Empty;
        public string SheetDimensionsTop { get => _sheetDimensionsTop; private set { _sheetDimensionsTop = value; OnPropertyChanged(); } }

        private string _sheetDimensionsLeft = string.Empty;
        public string SheetDimensionsLeft { get => _sheetDimensionsLeft; private set { _sheetDimensionsLeft = value; OnPropertyChanged(); } }

        /// <summary>Canvas zoom factor (0.4× .. 2.0×) for the 2D layout view.</summary>
        public double PreviewScale
        {
            get => _previewScale;
            private set
            {
                double clamped = System.Math.Clamp(value, 0.4, 2.0);
                if (_previewScale == clamped) return;
                _previewScale = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PreviewScaleLabel));
                RebuildPreview();
            }
        }

        public string PreviewScaleLabel => (_previewScale * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

        // ── 2D chrome: scaled margin zone (usable rectangle) ────────────
        private double _usableX;
        private double _usableY;
        private double _usableW;
        private double _usableH;
        public double UsableX { get => _usableX; private set { _usableX = value; OnPropertyChanged(); } }
        public double UsableY { get => _usableY; private set { _usableY = value; OnPropertyChanged(); } }
        public double UsableW { get => _usableW; private set { _usableW = value; OnPropertyChanged(); } }
        public double UsableH { get => _usableH; private set { _usableH = value; OnPropertyChanged(); } }

        // ── Minimum-breakout band (drawn in a DIFFERENT colour from the
        //    LM/RM/TM/BM margin zone, per the floor's two-tone rule) ──────
        private double _breakoutX;
        private double _breakoutY;
        private double _breakoutW;
        private double _breakoutH;
        public double BreakoutX { get => _breakoutX; private set { _breakoutX = value; OnPropertyChanged(); } }
        public double BreakoutY { get => _breakoutY; private set { _breakoutY = value; OnPropertyChanged(); } }
        public double BreakoutW { get => _breakoutW; private set { _breakoutW = value; OnPropertyChanged(); } }
        public double BreakoutH { get => _breakoutH; private set { _breakoutH = value; OnPropertyChanged(); } }

        private string _previewStockLabel = string.Empty;
        public string PreviewStockLabel { get => _previewStockLabel; private set { _previewStockLabel = value; OnPropertyChanged(); } }

        private string _previewUtilization = "—";
        public string PreviewUtilization { get => _previewUtilization; private set { _previewUtilization = value; OnPropertyChanged(); } }

        private string _previewPiecesCount = "—";
        public string PreviewPiecesCount { get => _previewPiecesCount; private set { _previewPiecesCount = value; OnPropertyChanged(); } }

        private string _marginZoneLabel = string.Empty;
        public string MarginZoneLabel { get => _marginZoneLabel; private set { _marginZoneLabel = value; OnPropertyChanged(); } }

        private bool _hasPreview;
        public bool HasPreview { get => _hasPreview; private set { _hasPreview = value; OnPropertyChanged(); } }

        public int SelectedSheetIndex
        {
            get => _selectedSheetIndex;
            set
            {
                if (_selectedSheetIndex == value) return;
                _selectedSheetIndex = value;
                OnPropertyChanged();
                RebuildPreview();
            }
        }

        // ── Mode switcher: Single Sheet Focus vs Multi-Sheet Gallery ────
        private bool _isGalleryMode;
        public bool IsGalleryMode
        {
            get => _isGalleryMode;
            set
            {
                if (_isGalleryMode == value) return;
                _isGalleryMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsSingleSheetMode));
            }
        }

        public bool IsSingleSheetMode => !_isGalleryMode;

        // Normal view fits the whole sheet into the stage. Zoom leaves that
        // mode so the operator can inspect a cut at actual canvas size.
        private bool _isFitView = true;
        public bool IsFitView
        {
            get => _isFitView;
            private set
            {
                if (_isFitView == value) return;
                _isFitView = value;
                OnPropertyChanged();
            }
        }

        public ICommand PreviewFitCommand { get; private set; } = null!;

        private int _galleryPageSize = 8;
        public int GalleryPageSize
        {
            get => _galleryPageSize;
            set
            {
                if (_galleryPageSize == value || value <= 0) return;
                _galleryPageSize = value;
                OnPropertyChanged();
                _galleryCurrentPage = 1;
                OnPropertyChanged(nameof(GalleryCurrentPage));
                UpdatePagedGallery();
            }
        }

        private int _galleryCurrentPage = 1;
        public int GalleryCurrentPage
        {
            get => _galleryCurrentPage;
            set
            {
                if (_galleryCurrentPage == value) return;
                _galleryCurrentPage = value;
                OnPropertyChanged();
                UpdatePagedGallery();
            }
        }

        private int _galleryTotalPages = 1;
        public int GalleryTotalPages
        {
            get => _galleryTotalPages;
            private set
            {
                if (_galleryTotalPages == value) return;
                _galleryTotalPages = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasMultipleGalleryPages));
            }
        }

        public bool HasMultipleGalleryPages => GalleryTotalPages > 1;

        private string _galleryPaginationSummary = string.Empty;
        public string GalleryPaginationSummary
        {
            get => _galleryPaginationSummary;
            private set { _galleryPaginationSummary = value; OnPropertyChanged(); }
        }

        private bool _canGalleryPrev;
        public bool CanGalleryPrev
        {
            get => _canGalleryPrev;
            private set { _canGalleryPrev = value; OnPropertyChanged(); }
        }

        private bool _canGalleryNext;
        public bool CanGalleryNext
        {
            get => _canGalleryNext;
            private set { _canGalleryNext = value; OnPropertyChanged(); }
        }

        public ICommand SelectPreviousSheetCommand { get; }
        public ICommand SelectNextSheetCommand { get; }
        public ICommand SelectSheetCommand { get; }
        public ICommand PreviewZoomInCommand { get; }
        public ICommand PreviewZoomOutCommand { get; }
        public ICommand PreviewZoomResetCommand { get; }

        public ICommand SwitchToSingleViewCommand { get; private set; } = null!;
        public ICommand SwitchToGalleryViewCommand { get; private set; } = null!;
        public ICommand GalleryPrevPageCommand { get; private set; } = null!;
        public ICommand GalleryNextPageCommand { get; private set; } = null!;
        public ICommand SetGalleryPageSizeCommand { get; private set; } = null!;
        public ICommand FocusGallerySheetCommand { get; private set; } = null!;

        public void InitPreviewGalleryCommands()
        {
            SwitchToSingleViewCommand = new RelayCommand(_ => IsGalleryMode = false);
            SwitchToGalleryViewCommand = new RelayCommand(_ =>
            {
                IsGalleryMode = true;
                UpdatePagedGallery();
            });
            GalleryPrevPageCommand = new RelayCommand(_ =>
            {
                if (GalleryCurrentPage > 1)
                {
                    GalleryCurrentPage--;
                }
            });
            GalleryNextPageCommand = new RelayCommand(_ =>
            {
                if (GalleryCurrentPage < GalleryTotalPages)
                {
                    GalleryCurrentPage++;
                }
            });
            SetGalleryPageSizeCommand = new RelayCommand(p =>
            {
                if (p is int size && size > 0)
                    GalleryPageSize = size;
                else if (p is string s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0)
                    GalleryPageSize = parsed;
            });
            FocusGallerySheetCommand = new RelayCommand(p =>
            {
                if (p is int idx && idx >= 0 && idx < _flatSheets.Count)
                {
                    SelectedSheetIndex = idx;
                    IsGalleryMode = false;
                }
            });
        }

        // ── 2D preview ─────────────────────────────────────────────────

        private const double PreviewRenderWidth = 860.0;
        private const double MiniMapRenderWidth = 120.0;

        /// <summary>
        /// Insets a placed rectangle by a visible saw channel. The packer
        /// already leaves kerf in millimetres, but at sheet scale a 3 mm
        /// kerf is under a pixel, so neighbouring pieces fuse into one block.
        /// </summary>
        private static void SeparateCut(
            double originX, double originY, double width, double height,
            double kerfPx, double minChannel,
            out double x, out double y, out double w, out double h)
        {
            double channel = System.Math.Max(minChannel, kerfPx);
            if (width <= channel + 2 || height <= channel + 2)
                channel = System.Math.Min(channel, System.Math.Min(width, height) * 0.18);
            w = System.Math.Max(2, width - channel);
            h = System.Math.Max(2, height - channel);
            x = originX + (width - w) / 2.0;
            y = originY + (height - h) / 2.0;
        }

        private void RebuildPreview()
        {
            SheetRects.Clear();
            SheetOffcuts.Clear();
            CurrentSheetPieces.Clear();
            MiniMapRects.Clear();
            RefreshSheetSelection();

            if (_flatSheets.Count == 0 ||
                SelectedSheetIndex < 0 || SelectedSheetIndex >= _flatSheets.Count)
            {
                RenderWidth = 0;
                RenderHeight = 0;
                MiniMapWidth = 0;
                MiniMapHeight = 0;
                PreviewCaption = string.Empty;
                PreviewSheetCounter = string.Empty;
                PreviewGroupLabel = string.Empty;
                PreviewStockLabel = string.Empty;
                SheetDimensionsLabel = string.Empty;
                SheetDimensionsTop = string.Empty;
                SheetDimensionsLeft = string.Empty;
                PreviewUtilization = "—";
                PreviewPiecesCount = "—";
                MarginZoneLabel = string.Empty;
                BreakoutX = BreakoutY = BreakoutW = BreakoutH = 0;
                HasPreview = false;
                RebuildGallerySheets();
                return;
            }

            HasPreview = true;
            var flat = _flatSheets[SelectedSheetIndex];
            var result = flat.Run.Result;
            var sheet = result.Sheets[flat.SheetIndex];
            var margins = flat.Run.Margins;

            double scale = PreviewRenderWidth * PreviewScale / sheet.Stock.WidthMm;
            RenderWidth = PreviewRenderWidth * PreviewScale;
            RenderHeight = sheet.Stock.HeightMm * scale;

            // Margin zone = the usable rectangle inside the edge trims.
            UsableX = sheet.UsableX * scale;
            UsableY = sheet.UsableY * scale;
            UsableW = sheet.UsableWidth * scale;
            UsableH = sheet.UsableHeight * scale;

            // Minimum-breakout band = usable rect inset by the breakout
            // allowance — the offcut strip the saw keeps inside the trims.
            double breakoutPx = margins.MinBreakoutMm * scale;
            BreakoutX = UsableX + breakoutPx;
            BreakoutY = UsableY + breakoutPx;
            BreakoutW = System.Math.Max(0, UsableW - 2 * breakoutPx);
            BreakoutH = System.Math.Max(0, UsableH - 2 * breakoutPx);

            MarginZoneLabel = string.Format(CultureInfo.InvariantCulture,
                "margins LM {0} · RM {1} · TM {2} · BM {3} · kerf {4} mm",
                margins.LeftMm, margins.RightMm,
                margins.TopMm, margins.BottomMm, margins.KerfMm);

            PreviewSheetCounter = string.Format(CultureInfo.InvariantCulture,
                "Sheet {0} of {1}", flat.SheetIndex + 1, result.Sheets.Count);
            PreviewGroupLabel = flat.Run.Composition;

            PreviewStockLabel = string.Format(CultureInfo.InvariantCulture,
                "{0} × {1} mm", sheet.Stock.WidthMm, sheet.Stock.HeightMm);
            SheetDimensionsLabel = string.Format(CultureInfo.InvariantCulture,
                "{0} × {1} mm", sheet.Stock.WidthMm, sheet.Stock.HeightMm);
            SheetDimensionsTop = string.Format(CultureInfo.InvariantCulture,
                "{0} mm", sheet.Stock.WidthMm);
            SheetDimensionsLeft = string.Format(CultureInfo.InvariantCulture,
                "{0} mm", sheet.Stock.HeightMm);

            PreviewUtilization = sheet.UtilizationLabel;
            PreviewPiecesCount = sheet.Pieces.Count.ToString(CultureInfo.InvariantCulture);

            int seq = 1;
            int maxX = 0;
            int maxY = 0;

            foreach (var p in sheet.Pieces)
            {
                string refLabel = string.IsNullOrWhiteSpace(p.Piece.PieceLabel)
                    ? p.Piece.GlassRef : p.Piece.PieceLabel;
                string positionTag = string.IsNullOrWhiteSpace(p.Piece.Position)
                    ? string.Empty : p.Piece.Position + " · ";

                double pieceW = System.Math.Max(1, p.WidthMm * scale);
                double pieceH = System.Math.Max(1, p.HeightMm * scale);
                decimal pieceAreaM2 = System.Math.Round((decimal)p.WidthMm * p.HeightMm / 1_000_000m, 3, MidpointRounding.AwayFromZero);

                if (p.X + p.WidthMm > maxX) maxX = p.X + p.WidthMm;
                if (p.Y + p.HeightMm > maxY) maxY = p.Y + p.HeightMm;

                SeparateCut(p.X * scale, p.Y * scale, pieceW, pieceH,
                    margins.KerfMm * scale, 2.6,
                    out double cutX, out double cutY, out double cutW, out double cutH);

                SheetRects.Add(new PieceRectVm
                {
                    X = cutX,
                    Y = cutY,
                    Width = cutW,
                    Height = cutH,
                    Rotated = p.Rotated,
                    Label = string.Format(CultureInfo.InvariantCulture, "{0}{1} · {2}×{3}{4}",
                        positionTag, refLabel,
                        p.Piece.WidthMm, p.Piece.HeightMm, p.Rotated ? " ⟳" : string.Empty),
                    DimensionsText = string.Format(CultureInfo.InvariantCulture, "{0} × {1} mm", p.WidthMm, p.HeightMm),
                    ShortDimensionsText = string.Format(CultureInfo.InvariantCulture, "{0}×{1}", p.WidthMm, p.HeightMm),
                    PieceRef = refLabel,
                    CutNumber = seq,
                    CutTag = string.Format(CultureInfo.InvariantCulture, "Cut {0}", seq),
                    WidthMm = p.WidthMm,
                    HeightMm = p.HeightMm,
                    AreaM2 = pieceAreaM2,
                    AreaM2Text = pieceAreaM2.ToString("0.000", CultureInfo.InvariantCulture) + " m²",
                    ShowDimensions = pieceW >= 42 && pieceH >= 22,
                    ShowSequence = pieceW >= 60 && pieceH >= 34,
                    ShowFullDetails = pieceW >= 80 && pieceH >= 44
                });

                CurrentSheetPieces.Add(new PlacedPieceDetailVm
                {
                    SequenceNumber = seq,
                    CutTag = string.Format(CultureInfo.InvariantCulture, "Cut #{0}", seq),
                    PieceRef = refLabel,
                    DimensionsText = string.Format(CultureInfo.InvariantCulture, "{0} × {1} mm", p.WidthMm, p.HeightMm),
                    WidthMm = p.WidthMm,
                    HeightMm = p.HeightMm,
                    Rotated = p.Rotated,
                    RotatedText = p.Rotated ? "Yes (90° ⟳)" : "No",
                    AreaM2Text = pieceAreaM2.ToString("0.000", CultureInfo.InvariantCulture) + " m²",
                    GlassSpec = flat.Run.Composition,
                    Position = string.IsNullOrWhiteSpace(p.Piece.Position) ? "—" : p.Piece.Position
                });

                seq++;
            }

            // ── Calculate reusable remnant offcut on the right/bottom ────
            int usableRight = sheet.UsableX + sheet.UsableWidth;
            int offcutWidthMm = usableRight - maxX;
            int offcutHeightMm = sheet.UsableHeight;

            if (offcutWidthMm >= 200 && offcutHeightMm >= 200)
            {
                SheetOffcuts.Add(new OffcutRectVm
                {
                    X = maxX * scale,
                    Y = sheet.UsableY * scale,
                    Width = System.Math.Max(1, offcutWidthMm * scale),
                    Height = System.Math.Max(1, offcutHeightMm * scale),
                    DimensionsText = string.Format(CultureInfo.InvariantCulture, "Offcut: {0} × {1} mm", offcutWidthMm, offcutHeightMm),
                    Label = "Reusable Offcut",
                    IsReusable = true
                });
            }

            PreviewCaption = string.Format(CultureInfo.InvariantCulture,
                "{0} — sheet {1} of {2} — stock {3} × {4} mm — {5} pieces",
                flat.Run.Composition, flat.SheetIndex + 1, result.Sheets.Count,
                sheet.Stock.WidthMm, sheet.Stock.HeightMm, sheet.Pieces.Count);

            // Mini-map thumbnail: a fixed 120px-wide, zoom-independent
            // scaled-down render of the whole sheet + its placed pieces.
            double miniScale = MiniMapRenderWidth / sheet.Stock.WidthMm;
            MiniMapWidth = MiniMapRenderWidth;
            MiniMapHeight = sheet.Stock.HeightMm * miniScale;
            foreach (var p in sheet.Pieces)
            {
                double miniW = System.Math.Max(1, p.WidthMm * miniScale);
                double miniH = System.Math.Max(1, p.HeightMm * miniScale);
                SeparateCut(p.X * miniScale, p.Y * miniScale, miniW, miniH,
                    margins.KerfMm * miniScale, 1.1,
                    out double miniX, out double miniY, out double miniCutW, out double miniCutH);
                MiniMapRects.Add(new PieceRectVm
                {
                    X = miniX,
                    Y = miniY,
                    Width = miniCutW,
                    Height = miniCutH,
                    Rotated = p.Rotated,
                    Label = string.Empty // the thumbnail shows placement, not labels
                });
            }

            RebuildGallerySheets();
        }

        private void RebuildGallerySheets()
        {
            AllGallerySheets.Clear();
            const double GallerySheetWidth = 240.0;

            for (int i = 0; i < _flatSheets.Count; i++)
            {
                var flat = _flatSheets[i];
                var sheet = flat.Run.Result.Sheets[flat.SheetIndex];
                double gScale = GallerySheetWidth / sheet.Stock.WidthMm;
                double gHeight = sheet.Stock.HeightMm * gScale;

                var gallerySheet = new GallerySheetVm
                {
                    Index = i,
                    SheetNo = flat.SheetIndex + 1,
                    SheetNoLabel = string.Format(CultureInfo.InvariantCulture, "Sheet #{0}", i + 1),
                    StockLabel = string.Format(CultureInfo.InvariantCulture, "{0} × {1} mm", sheet.Stock.WidthMm, sheet.Stock.HeightMm),
                    Composition = flat.Run.Composition,
                    UtilizationLabel = sheet.UtilizationLabel,
                    UtilizationPct = (double)sheet.UtilizationPct,
                    UtilizationTone = sheet.UtilizationPct >= 85.0m ? "Good" : (sheet.UtilizationPct >= 70.0m ? "Mid" : "Low"),
                    PieceCount = sheet.Pieces.Count,
                    PieceCountLabel = string.Format(CultureInfo.InvariantCulture, "{0} pieces", sheet.Pieces.Count),
                    WasteLabel = string.Format(CultureInfo.InvariantCulture, "{0:0.0}% waste", (double)(100.0m - sheet.UtilizationPct)),
                    CanvasWidth = GallerySheetWidth,
                    CanvasHeight = gHeight,
                    IsSelected = i == SelectedSheetIndex
                };

                int pSeq = 1;
                foreach (var p in sheet.Pieces)
                {
                    double pw = System.Math.Max(1, p.WidthMm * gScale);
                    double ph = System.Math.Max(1, p.HeightMm * gScale);
                    SeparateCut(p.X * gScale, p.Y * gScale, pw, ph,
                        (flat.Run.Margins?.KerfMm ?? 0) * gScale, 1.5,
                        out double gx, out double gy, out double gw, out double gh);
                    gallerySheet.Pieces.Add(new PieceRectVm
                    {
                        X = gx,
                        Y = gy,
                        Width = gw,
                        Height = gh,
                        Rotated = p.Rotated,
                        CutNumber = pSeq,
                        DimensionsText = string.Format(CultureInfo.InvariantCulture, "{0}×{1}", p.WidthMm, p.HeightMm),
                        Label = pSeq.ToString(CultureInfo.InvariantCulture),
                        ShowDimensions = pw >= 30 && ph >= 16,
                        ShowSequence = pw >= 20 && ph >= 14
                    });
                    pSeq++;
                }

                AllGallerySheets.Add(gallerySheet);
            }

            UpdatePagedGallery();
        }

        private void UpdatePagedGallery()
        {
            PagedGallerySheets.Clear();
            if (AllGallerySheets.Count == 0)
            {
                GalleryTotalPages = 1;
                GalleryCurrentPage = 1;
                GalleryPaginationSummary = "No sheets available";
                CanGalleryPrev = false;
                CanGalleryNext = false;
                return;
            }

            int pageSize = System.Math.Max(1, GalleryPageSize);
            GalleryTotalPages = (int)System.Math.Ceiling(AllGallerySheets.Count / (double)pageSize);
            if (GalleryCurrentPage > GalleryTotalPages) GalleryCurrentPage = GalleryTotalPages;
            if (GalleryCurrentPage < 1) GalleryCurrentPage = 1;

            int skip = (GalleryCurrentPage - 1) * pageSize;
            var pageItems = AllGallerySheets.Skip(skip).Take(pageSize).ToList();

            foreach (var item in pageItems)
            {
                item.IsSelected = item.Index == SelectedSheetIndex;
                PagedGallerySheets.Add(item);
            }

            int startNum = skip + 1;
            int endNum = System.Math.Min(skip + pageSize, AllGallerySheets.Count);
            GalleryPaginationSummary = string.Format(CultureInfo.InvariantCulture,
                "Showing {0}–{1} of {2} sheets (Page {3} of {4})",
                startNum, endNum, AllGallerySheets.Count, GalleryCurrentPage, GalleryTotalPages);

            CanGalleryPrev = GalleryCurrentPage > 1;
            CanGalleryNext = GalleryCurrentPage < GalleryTotalPages;
        }

        private void RefreshSheetSelection()
        {
            foreach (var row in SheetRows)
                row.IsSelected = row.Index == SelectedSheetIndex;

            foreach (var gal in AllGallerySheets)
                gal.IsSelected = gal.Index == SelectedSheetIndex;
        }
    }
}
