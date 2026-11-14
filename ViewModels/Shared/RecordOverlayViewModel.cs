// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/Shared/RecordOverlayViewModel.cs
// PROJECT: ProGlassAutomation
// MODULE: Recording HUD overlay (compact topmost card).
//
// 2026-09-02 (Phase 3.7): the HUD's state + actions moved out of
// Views/Shared/RecordOverlayWindow.xaml.cs into this VM. The window keeps
// only its chrome concerns (DragMove, auto-placement, the 250 ms
// DispatcherTimer which drives _vm.Tick() then Place()) and forwards its
// public API (Bind / SetStage / SetFinal / EnableOpenFolder) here, so
// DemoRecordingChrome is untouched.
//
// All visuals are bound: time, badge (+brush), dot (brush + opacity),
// pause glyph, stage line, button visibilities, stop enablement.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Agent.Demo.Recording;
using ProGlassAutomation.Commands;
using ProGlassAutomation.Services.Logging;
using System;
using ProGlassAutomation.Utilities;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ProGlassAutomation.ViewModels.Shared
{
    /// <summary>State + actions for the recording HUD card.</summary>
    public sealed class RecordOverlayViewModel : ViewModelBase
    {
        private WindowScreenRecorder? _recorder;
        private Action? _onStop;
        private Action? _onPause;
        private DateTime _started;
        private string? _savedPathForOpenFolder;

        // Frozen brushes reused by every Tick — the old code allocated two
        // new SolidColorBrushes every 250 ms for the whole recording.
        private static readonly SolidColorBrush BadgePausedBrush = Frozen(Color.FromRgb(251, 191, 36));
        private static readonly SolidColorBrush BadgeRecBrush = Frozen(Color.FromRgb(252, 165, 165));
        private static readonly SolidColorBrush DotFailBrush = Frozen(Color.FromRgb(239, 68, 68));
        private static readonly SolidColorBrush DotOkBrush = Frozen(Color.FromRgb(34, 197, 94));
        private static readonly SolidColorBrush DotStopBrush = Frozen(Color.FromRgb(251, 191, 36));

        private static SolidColorBrush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        // ── Bound HUD state ────────────────────────────────────────────
        public string TimeText { get; private set; } = "00:00";
        public string BadgeText { get; private set; } = "REC";
        public Brush BadgeBrush { get; private set; } = BadgeRecBrush;
        public Brush DotBrush { get; private set; } = DotFailBrush;
        public double DotOpacity { get; private set; } = 1.0;

        /// <summary>Pause button glyph: "\uE769" (pause) / "\uE768" (resume).</summary>
        public string PauseGlyph { get; private set; } = "\uE769";

        public Visibility PauseVisibility { get; private set; } = Visibility.Visible;
        public Visibility StopVisibility { get; private set; } = Visibility.Visible;
        public Visibility CloseVisibility { get; private set; } = Visibility.Collapsed;
        public Visibility OpenFolderVisibility { get; private set; } = Visibility.Collapsed;
        public Visibility StageVisibility { get; private set; } = Visibility.Collapsed;
        public string StageText { get; private set; } = "";
        public bool IsStopEnabled { get; private set; } = true;

        // ── Commands ───────────────────────────────────────────────────
        public ICommand PauseCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand CloseCommand { get; }

        /// <summary>View wiring point: invoked by CloseCommand so the
        /// window can Hide() itself.</summary>
        public Action? RequestHide { get; set; }

        /// <summary>Raised when SetFinal stops a recording session — the
        /// window stops its 250 ms timer (which would otherwise keep
        /// ticking the idle clock).</summary>
        public event Action? Finalized;

        public RecordOverlayViewModel()
        {
            PauseCommand = new RelayCommand(_ => Pause());
            StopCommand = new RelayCommand(_ => Stop());
            OpenFolderCommand = new RelayCommand(_ => OpenFolder());
            CloseCommand = new RelayCommand(_ => Close());
        }

        /// <summary>Starts a recording session (called by the window's
        /// Bind forwarder; DemoRecordingChrome contract unchanged).</summary>
        public void Bind(
            WindowScreenRecorder recorder,
            Action? onStop,
            Action? onPause)
        {
            _recorder = recorder;
            _onStop = onStop;
            _onPause = onPause;
            _started = DateTime.UtcNow;

            PauseVisibility = onPause == null ? Visibility.Collapsed : Visibility.Visible;
            StageText = "";
            StageVisibility = Visibility.Collapsed;
            CloseVisibility = Visibility.Collapsed;
            StopVisibility = Visibility.Visible;
            IsStopEnabled = true;   // re-arm after a previous stop/cancel

            Tick();
        }

        public void SetStage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                StageVisibility = Visibility.Collapsed;
                OnPropertyChanged(nameof(StageVisibility));
                return;
            }

            StageText = message;
            StageVisibility = Visibility.Visible;
            OnPropertyChanged(nameof(StageText));
            OnPropertyChanged(nameof(StageVisibility));
        }

        public void SetFinal(string badge, string stats, string stage, bool failed, Color? dotColor = null)
        {
            _recorder = null;

            BadgeText = badge;
            DotBrush = dotColor != null
                ? Frozen(dotColor.Value)
                : (failed ? DotFailBrush : DotOkBrush);

            // Final state is a single status line + a close button; the
            // full detail (path, frames, audio) is shown in the saved dialog.
            string summary = stats ?? "";
            if (!string.IsNullOrWhiteSpace(stage) &&
                !string.Equals(stage, summary, StringComparison.Ordinal))
            {
                summary = string.IsNullOrWhiteSpace(summary)
                    ? stage
                    : summary + "\n" + stage;
            }
            StageText = summary ?? "";
            StageVisibility = string.IsNullOrWhiteSpace(summary)
                ? Visibility.Collapsed
                : Visibility.Visible;

            PauseVisibility = Visibility.Collapsed;
            StopVisibility = Visibility.Collapsed;
            CloseVisibility = Visibility.Visible;

            OnPropertyChanged(nameof(BadgeText));
            OnPropertyChanged(nameof(DotBrush));
            OnPropertyChanged(nameof(StageText));
            OnPropertyChanged(nameof(StageVisibility));
            OnPropertyChanged(nameof(PauseVisibility));
            OnPropertyChanged(nameof(StopVisibility));
            OnPropertyChanged(nameof(CloseVisibility));

            Finalized?.Invoke();
        }

        /// <summary>Wires the "open folder" HUD button to point at the
        /// just-saved file (called by DemoRecordingChrome.ShowSaved).</summary>
        public void EnableOpenFolder(string? savedPath)
        {
            _savedPathForOpenFolder = savedPath;
            OpenFolderVisibility = string.IsNullOrEmpty(savedPath)
                ? Visibility.Collapsed
                : Visibility.Visible;
            OnPropertyChanged(nameof(OpenFolderVisibility));
        }

        /// <summary>Recomputes the HUD state; the window calls this from
        /// its 250 ms timer (and once from Bind).</summary>
        public void Tick()
        {
            var rec = _recorder;
            var elapsed = rec != null ? rec.Elapsed : DateTime.UtcNow - _started;
            TimeText = RecordingTime.Format(elapsed);

            bool paused = rec?.IsPaused == true;
            BadgeText = rec == null ? "IDLE" : paused ? "PAUSED" : "REC";
            BadgeBrush = paused ? BadgePausedBrush : BadgeRecBrush;
            DotOpacity = paused ? 0.4 : 1.0;

            // Pause button flips between pause (⏸) and resume (▶) glyphs.
            if (PauseVisibility == Visibility.Visible)
                PauseGlyph = paused ? "\uE768" : "\uE769";

            // Only surface the encoder stage while it is meaningful; the
            // idle HUD stays a single quiet card.
            if (rec != null && !string.IsNullOrWhiteSpace(rec.EncodeStage))
                SetStage(rec.EncodeStage +
                    (rec.EncodePercent > 0 ? $"  {rec.EncodePercent}%" : ""));

            OnPropertyChanged(nameof(TimeText));
            OnPropertyChanged(nameof(BadgeText));
            OnPropertyChanged(nameof(BadgeBrush));
            OnPropertyChanged(nameof(DotOpacity));
            OnPropertyChanged(nameof(PauseGlyph));
        }

        private void Pause()
        {
            try { _onPause?.Invoke(); } catch (Exception ex) { AppLogService.Warning($"Suppressed exception: {ExceptionMessages.Deep(ex)}"); }
            Tick();
        }

        private void Stop()
        {
            // 2026-08-25 senior fix: immediate feedback. The teardown
            // (final encodes, audio close, MP4 pack) legitimately takes a
            // few seconds; without this the HUD looked dead after STOP.
            IsStopEnabled = false;
            OnPropertyChanged(nameof(IsStopEnabled));
            SetStage("Stopping…");

            try { _onStop?.Invoke(); } catch (Exception ex) { AppLogService.Warning($"Suppressed exception: {ExceptionMessages.Deep(ex)}"); }
        }

        private void OpenFolder()
        {
            try
            {
                var p = _savedPathForOpenFolder;
                if (string.IsNullOrEmpty(p)) return;
                if (File.Exists(p))
                {
                    // /select highlights the file in Explorer.
                    var psi = new ProcessStartInfo("explorer.exe",
                        "/select,\"" + p + "\"") { UseShellExecute = true };
                    Process.Start(psi);
                }
                else
                {
                    var dir = Path.GetDirectoryName(p);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
                }
            }
            catch (Exception ex) { AppLogService.Warning("Suppressed exception: " + ExceptionMessages.Deep(ex)); }
        }

        private void Close()
        {
            try { RequestHide?.Invoke(); } catch (Exception ex) { AppLogService.Warning($"Suppressed exception: {ExceptionMessages.Deep(ex)}"); }
        }
    }
}
