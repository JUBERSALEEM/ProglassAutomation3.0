// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Sharing/ShareClipboard.cs
// PROJECT: ProGlassAutomation
// MODULE: Sharing Center — clipboard file handoff (Stage 3 upgrade)
//
// WHY THIS EXISTS (Sept 2026 senior audit — "the file never attaches")
// ─────────────────────────────────────────────────────────────────────
//   No WhatsApp / mailto link can carry a file — the message body is
//   text-only by platform design. The OLD handoff staged the file and
//   opened Explorer so the operator could HUNT for it and DRAG it into
//   the chat. That is clumsy and disconnected — which is exactly why it
//   felt like "sharing dumps me in a folder and nothing attaches".
//
//   The professional handoff is the CLIPBOARD. Placing the staged file(s)
//   on the clipboard as a file-drop (CF_HDROP) lets the operator simply
//   press Ctrl+V INSIDE the WhatsApp chat (Desktop and Web both accept a
//   pasted file as an attachment) — or into an email draft, Teams, etc.
//   Combined with the prefilled message the flow becomes:
//       Share → chat opens with text prefilled → Ctrl+V → Enter.
//
// THREAD AFFINITY. Clipboard access requires the STA (UI) thread; the
// providers run their launch on a background thread (Task.Run), so this
// helper marshals every clipboard call onto the application Dispatcher.
// Clipboard can also be transiently locked by another process, so writes
// are retried a few times. Never throws — a clipboard failure degrades to
// the Explorer-reveal fallback the provider keeps.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Services.Logging;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace ProGlassAutomation.Services.Sharing
{
    public static class ShareClipboard
    {
        /// <summary>
        /// Places the given existing files on the clipboard as a file-drop
        /// so the operator can paste them (Ctrl+V) straight into the chat /
        /// draft. Runs on the UI (STA) thread, retries transient clipboard
        /// locks, and never throws. Returns true when the files were placed.
        /// </summary>
        public static bool CopyFiles(IEnumerable<string>? filePaths)
        {
            try
            {
                var existing = new StringCollection();
                if (filePaths != null)
                {
                    foreach (string? path in filePaths)
                    {
                        if (!string.IsNullOrWhiteSpace(path)
                            && File.Exists(path))
                            existing.Add(path);
                    }
                }
                if (existing.Count == 0) return false;

                return OnUi(() => TrySetFileDropList(existing));
            }
            catch (Exception ex)
            {
                AppLogService.Debug(
                    "[Sharing.Clipboard] copy files failed: " + ex.Message);
                return false;
            }
        }

        private static bool TrySetFileDropList(StringCollection files)
        {
            // Clipboard can be momentarily held by another process
            // (CLIPBRD_E_CANT_OPEN). Retry a few times before giving up.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    Clipboard.SetFileDropList(files);
                    return true;
                }
                catch (Exception ex)
                {
                    AppLogService.Debug(
                        $"[Sharing.Clipboard] SetFileDropList attempt {attempt + 1} failed: {ex.Message}");
                    Thread.Sleep(60);
                }
            }
            return false;
        }

        /// <summary>
        /// Runs <paramref name="work"/> on the WPF UI (STA) thread and
        /// returns its result. Inline when already on the UI thread or when
        /// no Application/Dispatcher exists (headless test host); otherwise
        /// blocks on Dispatcher.Invoke. Never throws.
        /// </summary>
        private static bool OnUi(Func<bool> work)
        {
            try
            {
                Dispatcher? disp = Application.Current?.Dispatcher;
                if (disp == null || disp.CheckAccess())
                    return work();
                return disp.Invoke(work);
            }
            catch (Exception ex)
            {
                AppLogService.Debug(
                    "[Sharing.Clipboard] UI dispatch failed: " + ex.Message);
                return false;
            }
        }
    }
}
