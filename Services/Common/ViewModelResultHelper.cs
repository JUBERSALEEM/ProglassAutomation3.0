// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Common/ViewModelResultHelper.cs
// PROJECT: ProGlassAutomation
// MODULE: Core — ViewModel helpers for Result pattern (Phase 2)
// VERSION: Phase 2 — 2026-08-21
//
// PURPOSE:
//   Helper for ViewModels to handle Result<T> with user-friendly dialogs
//   that show ErrorId + CorrelationId for support.
//
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Services.Logging;
using System;
using System.Threading.Tasks;
using System.Windows;
using ProGlassAutomation.Services.Dialogs;

namespace ProGlassAutomation.Services.Common
{
    public static class ViewModelResultHelper
    {
        /// <summary>
        /// Handles a Result with UI dialog on failure, logs with correlation.
        /// Returns true if success, false if failure (and shows dialog).
        /// </summary>
        public static bool HandleResult(Result result, string operation, LogContext? context = null)
        {
            if (result.IsSuccess) return true;

            var ctx = context ?? LogContext.FromCurrent();
            if (string.IsNullOrWhiteSpace(ctx.CorrelationId) && !string.IsNullOrWhiteSpace(result.CorrelationId))
                ctx.CorrelationId = result.CorrelationId;
            if (string.IsNullOrWhiteSpace(ctx.Operation))
                ctx.Operation = operation;

            AppLogService.Warning(
                $"Operation '{operation}' failed: {result.Error} (ErrorId={result.ErrorId}, Correlation={result.CorrelationId})",
                ctx);

            var msg = $"{operation} failed.\n\n" +
                      $"Error: {result.Error}\n" +
                      $"Error ID: {result.ErrorId}\n" +
                      $"Correlation: {result.CorrelationId}\n\n" +
                      $"Share Error ID or Correlation with support — full trace in logs.";

            try
            {
                Prompts.Show(msg, $"{operation} — Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (System.Exception ex) { ProGlassAutomation.Services.Logging.AppLogService.Warning("[ViewModelResultHelper] Error dialog could not be shown: " + ex.Message); }

            return false;
        }

        public static bool HandleResult<T>(Result<T> result, string operation, LogContext? context = null)
        {
            if (result.IsSuccess) return true;

            var ctx = context ?? LogContext.FromCurrent();
            if (string.IsNullOrWhiteSpace(ctx.CorrelationId) && !string.IsNullOrWhiteSpace(result.CorrelationId))
                ctx.CorrelationId = result.CorrelationId;
            if (string.IsNullOrWhiteSpace(ctx.Operation))
                ctx.Operation = operation;

            AppLogService.Warning(
                $"Operation '{operation}' failed: {result.Error} (ErrorId={result.ErrorId}, Correlation={result.CorrelationId})",
                ctx);

            var msg = $"{operation} failed.\n\n" +
                      $"Error: {result.Error}\n" +
                      $"Error ID: {result.ErrorId}\n" +
                      $"Correlation: {result.CorrelationId}\n\n" +
                      $"Share Error ID or Correlation with support.";

            try
            {
                Prompts.Show(msg, $"{operation} — Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (System.Exception ex) { ProGlassAutomation.Services.Logging.AppLogService.Warning("[ViewModelResultHelper] Error dialog could not be shown: " + ex.Message); }

            return false;
        }

        /// <summary>
        /// Executes an operation with correlation scope and Result handling.
        /// </summary>
        public static async Task<Result<T>> ExecuteWithCorrelationAsync<T>(
            Func<string, Task<Result<T>>> operation,
            string documentType,
            string documentId,
            string operationName)
        {
            var corrId = CorrelationContext.NewIdFor(documentType, documentId);
            using var scope = CorrelationContext.BeginDocumentScope(documentType, documentId, corrId);

            try
            {
                AppLogService.Info(
                    $"Starting {operationName} for {documentType} {documentId} [{corrId}]",
                    new LogContext
                    {
                        Module = documentType,
                        Operation = operationName,
                        DocumentId = documentId,
                        DocumentType = documentType,
                        CorrelationId = corrId
                    });

                var result = await operation(corrId);

                if (result.IsSuccess)
                {
                    AppLogService.Info(
                        $"{operationName} succeeded for {documentType} {documentId} [{corrId}]",
                        new LogContext
                        {
                            Module = documentType,
                            Operation = operationName,
                            DocumentId = documentId,
                            DocumentType = documentType,
                            CorrelationId = corrId
                        });
                }

                return result;
            }
            catch (Exception ex)
            {
                var errorId = AppLogService.Error(
                    $"{operationName} exception for {documentType} {documentId}: {ex.Message}",
                    ex,
                    new LogContext
                    {
                        Module = documentType,
                        Operation = operationName,
                        DocumentId = documentId,
                        DocumentType = documentType,
                        CorrelationId = corrId
                    });
                return Result<T>.Fail($"{operationName} failed: {ex.Message}", ex, corrId, errorId);
            }
        }
    }
}
