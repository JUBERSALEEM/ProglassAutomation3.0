using ProGlassAutomation.Agent.Demo.Recording;
using ProGlassAutomation.Agent.Demo.Video;
using ProGlassAutomation.Commands;
using ProGlassAutomation.Services.Recording;
using System;
using ProGlassAutomation.Utilities;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using ProGlassAutomation.Services.Localization;

using ProGlassAutomation.Services.Dialogs;
using Microsoft.Extensions.DependencyInjection;
namespace ProGlassAutomation.ViewModels.Shared
{
    /// <summary>
    /// Top-bar screen-record popup. Shares the same engine as Demo
    /// Center's "Record this session" card.
    /// </summary>
    public sealed class ScreenRecordPanelViewModel : ViewModelBase
    {
        private readonly IDialogService _dialogs = null!;

        // ERR-633EEB hardening: this DI overload used to leave every
        // command and collection NULL (all initialization lived in the
        // parameterless ctor). The container always picks the GREEDIEST
        // resolvable ctor, so DI-built instances were half-constructed.
        // It now chains into the fully-initializing ctor.
        public ScreenRecordPanelViewModel(IDialogService? dialogs) : this(dialogs, initialize: true)
        {
        }

        public static ScreenRecordPanelViewModel Shared { get; } = new();

        private readonly UserScreenRecordService _svc = UserScreenRecordService.Shared;

        public ScreenRecordPanelViewModel() : this(null, initialize: true)
        {
        }

        private ScreenRecordPanelViewModel(IDialogService? dialogs, bool initialize)
        {
            _dialogs = dialogs
                ?? App.ServiceProvider?.GetService<IDialogService>()
                ?? new DialogService();
            _ = initialize; // discriminator only — keeps the two ctors distinct

            SeedEncoderOptions();
            SelectedQualityTier = ScreenRecordSettings.QualityTiers
                .FirstOrDefault(t => t.Tier == _svc.Settings.QualityTier)
                ?? ScreenRecordSettings.QualityTiers[1];
            SelectedQuality = ScreenRecordSettings.Qualities
                .FirstOrDefault(q => q.Quality == _svc.Settings.Quality)
                ?? ScreenRecordSettings.Qualities[1];
            SelectedFps = ScreenRecordSettings.FpsOptions
                .FirstOrDefault(f => f.Fps == _svc.Settings.Fps)
                ?? ScreenRecordSettings.FpsOptions[0];

            // PHASE 5: probe the hardware encoders in the BACKGROUND
            // (test-encode proof + throughput measurement). Never blocks
            // the panel; results refresh the Encoder combo via the
            // Changed event. A probe that never completes just means
            // "hardware unproven" — Auto falls back to libx264.
            ProGlassAutomation.Agent.Demo.Video.EncoderProbe.Changed += OnEncoderProbeChanged;
            RequestProbeForCurrentSelection();
            _captureLiveAudio = _svc.Settings.CaptureLiveAudio;
            _captureMicrophone = _svc.Settings.CaptureMicrophone;

            StartCommand = new RelayCommand(
                _ => Start(),
                _ => !_svc.IsBusy);
            // STOP: fire-and-forget on purpose.
            //
            // `async _ => await ...` inside a RelayCommand is an async void
            // in practice: the command returns at the first await, so any
            // later fault is unobserved, and on a congested UI thread the
            // continuation can be delayed behind pending work. Recording
            // stop must be immediate and must never surface an exception
            // through the command plumbing.
            //
            // Cancellation is also requested SYNCHRONOUSLY first, so the
            // capture loop stops issuing UI-blocking captures the instant
            // the button is pressed rather than after the save pipeline
            // has been scheduled.
            StopCommand = new RelayCommand(
                _ =>
                {
                    try { _svc.RequestStopNow(); }
                    catch (Exception ex)
                    {
                        Services.Logging.AppLogService.Debug(
                            $"[ScreenRecord] stop request: {ExceptionMessages.Deep(ex)}");
                    }

                    ProGlassAutomation.Utilities.AsyncUtility.SafeFireAndForget(
                        () => _svc.StopAndSaveAsync(), "ScreenRecord Stop");
                },
                _ => _svc.IsRecording);
            PauseCommand = new RelayCommand(
                _ => _svc.TogglePause(),
                _ => _svc.IsRecording);
            OpenOutputCommand          = new RelayCommand(_ => _svc.OpenOutput());
            OpenRecordingsFolderCommand = new RelayCommand(_ => _svc.OpenRecordingsFolder());

            _svc.Changed += RaiseStatus;
        }

        public IReadOnlyList<ScreenRecordQualityTierOption> QualityTiers =>
            ScreenRecordSettings.QualityTiers;

        public IReadOnlyList<ScreenRecordQualityOption> Qualities =>
            ScreenRecordSettings.Qualities;

        public IReadOnlyList<ScreenRecordFpsOption> FpsChoices =>
            ScreenRecordSettings.FpsOptions;

        // ── PHASE 5 (Step 22): encoder selection ──────────────────────
        // Auto and CPU are always available; the NVIDIA/Intel rows appear
        // only when the background probe PROVES the encoder initializes
        // (test encode, not a name check). If a selected hardware row
        // disappears (probe found it unusable), the selection drops to
        // Auto — the recording itself can never break over this.
        private readonly List<ScreenRecordEncoderOption> _encoderOptions = new();
        public IReadOnlyList<ScreenRecordEncoderOption> EncoderOptions => _encoderOptions;

        private ScreenRecordEncoderOption _selectedEncoder = ScreenRecordSettings.EncoderOptions[0];
        public ScreenRecordEncoderOption SelectedEncoder
        {
            get => _selectedEncoder;
            set
            {
                if (value == null || ReferenceEquals(_selectedEncoder, value)) return;
                _selectedEncoder = value;
                PushSettings();
                OnPropertyChanged();
                OnPropertyChanged(nameof(Hint));
                RequestProbeForCurrentSelection();
            }
        }

        private void SeedEncoderOptions()
        {
            _encoderOptions.Clear();
            var cap = ProGlassAutomation.Agent.Demo.Video.EncoderProbe.GetSnapshot();
            foreach (var opt in ScreenRecordSettings.EncoderOptions)
            {
                if (opt.Hardware)
                {
                    bool usable = opt.Encoder == ScreenRecordEncoder.Nvenc
                        ? cap.NvencUsable : cap.QsvUsable;
                    if (!usable) continue;
                }
                _encoderOptions.Add(opt);
            }
            _selectedEncoder = _encoderOptions
                .FirstOrDefault(e => e.Encoder == _svc.Settings.Encoder) ?? _encoderOptions[0];
        }

        /// <summary>
        /// (Re)request the background probe for the encoder/resolution
        /// the user is currently looking at. Idempotent and non-blocking.
        /// </summary>
        private void RequestProbeForCurrentSelection()
        {
            try
            {
                var cap = ProGlassAutomation.Agent.Demo.Video.EncoderProbe.GetSnapshot();
                var sel = ProGlassAutomation.Agent.Demo.Video.VideoEncoder.Resolve(
                    _svc.Settings.Encoder, cap.NvencUsable, cap.QsvUsable).Selection;
                var (w, h) = _svc.Settings.OutputSize;
                ProGlassAutomation.Agent.Demo.Video.EncoderProbe.RequestProbe(sel.Name, w, h, _svc.Settings.Fps);
            }
            catch (System.Exception ex) { ProGlassAutomation.Services.Logging.AppLogService.Debug($"[ScreenRecord] probe request: {ex.Message}"); }
        }

        /// <summary>
        /// Probe results land on a background thread — marshal to the UI
        /// thread before touching bound properties.
        /// </summary>
        private void OnEncoderProbeChanged()
        {
            var apply = new Action(() =>
            {
                var cap = ProGlassAutomation.Agent.Demo.Video.EncoderProbe.GetSnapshot();
                bool added = false;
                if (cap.NvencUsable && _encoderOptions.All(e => e.Encoder != ScreenRecordEncoder.Nvenc))
                {
                    _encoderOptions.Add(ScreenRecordSettings.EncoderOptions.First(e => e.Encoder == ScreenRecordEncoder.Nvenc));
                    added = true;
                }
                if (cap.QsvUsable && _encoderOptions.All(e => e.Encoder != ScreenRecordEncoder.Qsv))
                {
                    _encoderOptions.Add(ScreenRecordSettings.EncoderOptions.First(e => e.Encoder == ScreenRecordEncoder.Qsv));
                    added = true;
                }
                // A selected hardware encoder that the probe rejected → Auto.
                bool selectedHardwareGone =
                    _selectedEncoder?.Encoder == ScreenRecordEncoder.Nvenc && !cap.NvencUsable ||
                    _selectedEncoder?.Encoder == ScreenRecordEncoder.Qsv && !cap.QsvUsable;
                if (selectedHardwareGone) _selectedEncoder = _encoderOptions[0];

                if (added || selectedHardwareGone)
                {
                    OnPropertyChanged(nameof(EncoderOptions));
                    OnPropertyChanged(nameof(SelectedEncoder));
                    OnPropertyChanged(nameof(Hint));
                }
            });
            var disp = System.Windows.Application.Current?.Dispatcher;
            if (disp != null && !disp.CheckAccess()) disp.BeginInvoke(apply);
            else apply();
        }

        private ScreenRecordQualityTierOption _selectedQualityTier =
            ScreenRecordSettings.QualityTiers[1];
        public ScreenRecordQualityTierOption SelectedQualityTier
        {
            get => _selectedQualityTier;
            set
            {
                if (value == null || ReferenceEquals(_selectedQualityTier, value)) return;
                _selectedQualityTier = value;
                PushSettings();
                OnPropertyChanged();
                OnPropertyChanged(nameof(Hint));
            }
        }

        private ScreenRecordQualityOption _selectedQuality = ScreenRecordSettings.Qualities[1];
        public ScreenRecordQualityOption SelectedQuality
        {
            get => _selectedQuality;
            set
            {
                if (value == null || ReferenceEquals(_selectedQuality, value)) return;
                _selectedQuality = value;
                PushSettings();
                OnPropertyChanged();
                OnPropertyChanged(nameof(Hint));
                // Throughput scales with resolution — refresh the probe
                // estimate so the FPS guard has data for the new size.
                RequestProbeForCurrentSelection();
            }
        }

        private ScreenRecordFpsOption _selectedFps = ScreenRecordSettings.FpsOptions[0];
        public ScreenRecordFpsOption SelectedFps
        {
            get => _selectedFps;
            set
            {
                if (value == null || ReferenceEquals(_selectedFps, value)) return;
                _selectedFps = value;
                PushSettings();
                OnPropertyChanged();
                OnPropertyChanged(nameof(Hint));
            }
        }

        private bool _captureLiveAudio = true;
        public bool CaptureLiveAudio
        {
            get => _captureLiveAudio;
            set
            {
                if (_captureLiveAudio == value) return;
                _captureLiveAudio = value;
                PushSettings();
                OnPropertyChanged();
                OnPropertyChanged(nameof(Hint));
            }
        }

        private bool _captureMicrophone = true;
        public bool CaptureMicrophone
        {
            get => _captureMicrophone;
            set
            {
                if (_captureMicrophone == value) return;
                _captureMicrophone = value;
                PushSettings();
                OnPropertyChanged();
                OnPropertyChanged(nameof(Hint));
            }
        }

        public bool IsRecording => _svc.IsRecording;
        public bool IsPaused => _svc.IsPaused;
        public bool IsEncoding => _svc.IsEncoding;
        public bool IsBusy => _svc.IsBusy;
        public bool IsIdle => !_svc.IsBusy;
        public string Status => _svc.Status;
        public string LastMp4Text =>
            string.IsNullOrEmpty(_svc.LastMp4Path)
                ? "No recording yet."
                : _svc.LastMp4Path!;

        public string Hint =>
            "Records only this app window at " +
            (SelectedQuality?.Title ?? "1080p") + " / " +
            (SelectedFps?.Title ?? "30 fps") + " / " +
            (SelectedQualityTier?.Title ?? "High") + " quality / " +
            (SelectedEncoder?.Title ?? "Auto") +
            ". A REC bar with duration and STOP appears on the window." +
            (CaptureLiveAudio ? " Internal audio on." : "") +
            (CaptureMicrophone ? " Microphone on." : "");

        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand PauseCommand { get; }
        public ICommand OpenOutputCommand { get; }

        /// <summary>
        /// Opens %AppData%\ProGlassAutomation\Recordings in Explorer
        /// regardless of whether a recording has finished yet. Bound
        /// to the "Open recordings folder" button in the top-bar
        /// recorder popup (Aug 2026 senior audit — the user asked
        /// for a visible "where is my file" affordance).
        /// </summary>
        public ICommand OpenRecordingsFolderCommand { get; }

        /// <summary>
        /// Pushes the four UI-exposed settings to the service.
        ///
        /// BUG FIXED (2026-09-02): this built a BRAND NEW
        /// ScreenRecordSettings and assigned only the four properties the
        /// popup shows. Every other setting — Supersample, DrawCursor,
        /// JpegQuality, LivePreviewEncode — was silently reset to its
        /// default on any quality/fps/audio change, discarding whatever
        /// had been configured elsewhere.
        ///
        /// It now MUTATES A CLONE of the current settings, so unrelated
        /// values survive.
        /// </summary>
        private void PushSettings()
        {
            var next = (_svc.Settings ?? new ScreenRecordSettings()).Clone();

            next.QualityTier = SelectedQualityTier?.Tier ?? ScreenRecordQualityTier.High;
            next.Quality = SelectedQuality?.Quality ?? ScreenRecordQuality.FullHd;
            next.Encoder = SelectedEncoder?.Encoder ?? ScreenRecordEncoder.Auto;
            next.Fps = SelectedFps?.Fps ?? 30;
            next.CaptureLiveAudio = CaptureLiveAudio;
            next.CaptureMicrophone = CaptureMicrophone;

            _svc.Settings = next.Clamp();
        }

        private void Start()
        {
            PushSettings();
            string? why = _svc.TryStart(_svc.Settings);
            if (why != null)
            {
                try
                {
                    _dialogs.Show(why, LocalizationService.Instance.T("Shared.ScreenRecording"),
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch { /* status still updated */ }
            }
            RaiseStatus();
        }

        private void RaiseStatus()
        {
            OnPropertyChanged(nameof(IsRecording));
            OnPropertyChanged(nameof(IsEncoding));
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(IsIdle));
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(LastMp4Text));
            ProGlassAutomation.Utilities.AsyncUtility.InvalidateCommandsThrottled();
        }
    }
}
