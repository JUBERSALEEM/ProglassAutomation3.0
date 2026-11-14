// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Common/Result.cs
// PROJECT: ProGlassAutomation
// MODULE: Core — Result Pattern (Phase 2: Error Handling Hardening)
// VERSION: Phase 2 — 2026-08-21
//
// PURPOSE:
//   Explicit error handling without exceptions for control flow.
//   Every billing path (PI → JO → DN → TI → Statement) should return
//   Result<T> instead of throwing or returning null.
//
//   Benefits:
//   • No hidden nulls — caller must check IsSuccess
//   • CorrelationId flows from LogContext to UI error dialog
//   • ErrorId links user-facing message to log file
//   • Stack trace preserved via Exception property
//
// USAGE:
//   var result = await _proformaRepo.SaveAsync(invoice);
//   if (!result.IsSuccess)
//       return Result<DeliveryNote>.Fail(result.Error, result.CorrelationId);
//
//   var ok = Result<Invoice>.Ok(invoice, correlationId);
//   var fail = Result<Invoice>.Fail("Customer name required", correlationId);
//
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Diagnostics.CodeAnalysis;

namespace ProGlassAutomation.Services.Common
{
    /// <summary>
    /// Non-generic result — for operations that return no value (e.g. Save, Delete).
    /// </summary>
    public sealed class Result
    {
        public bool IsSuccess { get; }
        public bool IsFailure => !IsSuccess;
        public string Error { get; }
        public string ErrorId { get; }
        public string CorrelationId { get; }
        public Exception? Exception { get; }

        private Result(bool isSuccess, string error, string errorId, string correlationId, Exception? ex)
        {
            IsSuccess = isSuccess;
            Error = error ?? "";
            ErrorId = errorId ?? "";
            CorrelationId = correlationId ?? "";
            Exception = ex;
        }

        public static Result Ok(string? correlationId = null) =>
            new(true, "", "", correlationId ?? CorrelationContext.CurrentId ?? "", null);

        public static Result Fail(string error, string? correlationId = null, Exception? ex = null, string? errorId = null) =>
            new(false, error, errorId ?? "", correlationId ?? CorrelationContext.CurrentId ?? "", ex);

        // Phase 10 fix: the 4-arg (error, ex, correlationId, errorId) form
        // is used across Services; the previous 3-arg overload left the
        // 4th argument unmatched (CS1501). errorId is optional so existing
        // 2- and 3-arg calls remain valid.
        public static Result Fail(string error, Exception ex, string? correlationId = null, string? errorId = null) =>
            new(false, error, errorId ?? "", correlationId ?? CorrelationContext.CurrentId ?? "", ex);

        public Result WithCorrelation(string correlationId) =>
            new(IsSuccess, Error, ErrorId, correlationId, Exception);
    }

    /// <summary>
    /// Generic result — for operations that return a value.
    /// </summary>
    public sealed class Result<T>
    {
        public bool IsSuccess { get; }
        public bool IsFailure => !IsSuccess;

        [MaybeNull]
        public T Value { get; }

        public string Error { get; }
        public string ErrorId { get; }
        public string CorrelationId { get; }
        public Exception? Exception { get; }

        private Result(bool isSuccess, T? value, string error, string errorId, string correlationId, Exception? ex)
        {
            IsSuccess = isSuccess;
            Value = value!;
            Error = error ?? "";
            ErrorId = errorId ?? "";
            CorrelationId = correlationId ?? "";
            Exception = ex;
        }

        // ── Factories ──────────────────────────────────────────────────

        public static Result<T> Ok(T value, string? correlationId = null) =>
            new(true, value, "", "", correlationId ?? CorrelationContext.CurrentId ?? "", null);

        public static Result<T> Ok(T value, string correlationId, string errorId) =>
            new(true, value, "", errorId, correlationId, null);

        public static Result<T> Fail(string error, string? correlationId = null, Exception? ex = null, string? errorId = null) =>
            new(false, default, error, errorId ?? "", correlationId ?? CorrelationContext.CurrentId ?? "", ex);

        // Phase 10 fix: add optional errorId so the common 4-arg
        // (error, ex, correlationId, errorId) call form resolves.
        public static Result<T> Fail(string error, Exception ex, string? correlationId = null, string? errorId = null) =>
            new(false, default, error, errorId ?? "", correlationId ?? CorrelationContext.CurrentId ?? "", ex);

        public static Result<T> Fail(Result other) =>
            new(false, default, other.Error, other.ErrorId, other.CorrelationId, other.Exception);

        public static Result<T> Fail<U>(Result<U> other) =>
            new(false, default, other.Error, other.ErrorId, other.CorrelationId, other.Exception);

        // ── Transformations ────────────────────────────────────────────

        public Result<U> Map<U>(Func<T, U> mapper)
        {
            if (IsFailure) return Result<U>.Fail(this);
            try { return Result<U>.Ok(mapper(Value!), CorrelationId); }
            catch (Exception ex) { return Result<U>.Fail(ex.Message, ex, CorrelationId); }
        }

        public Result<U> Bind<U>(Func<T, Result<U>> binder)
        {
            if (IsFailure) return Result<U>.Fail(this);
            try { return binder(Value!); }
            catch (Exception ex) { return Result<U>.Fail(ex.Message, ex, CorrelationId); }
        }

        public Result<T> WithCorrelation(string correlationId) =>
            new(IsSuccess, Value, Error, ErrorId, correlationId, Exception);

        // ── Deconstruction ─────────────────────────────────────────────

        public void Deconstruct(out bool isSuccess, out T? value, out string error)
        {
            isSuccess = IsSuccess;
            value = Value;
            error = Error;
        }

        public override string ToString() =>
            IsSuccess ? $"Ok({Value}) [{CorrelationId}]" : $"Fail({Error}) [{CorrelationId}] ({ErrorId})";
    }

    /// <summary>
    /// Extension helpers for Result.
    /// </summary>
    public static class ResultExtensions
    {
        public static Result<T> Ensure<T>(this Result<T> result, Func<T, bool> predicate, string error)
        {
            if (result.IsFailure) return result;
            return predicate(result.Value!) ? result : Result<T>.Fail(error, result.CorrelationId);
        }

        public static Result<T> Tap<T>(this Result<T> result, Action<T> action)
        {
            if (result.IsSuccess) action(result.Value!);
            return result;
        }

        public static Result<T> TapError<T>(this Result<T> result, Action<string> action)
        {
            if (result.IsFailure) action(result.Error);
            return result;
        }
    }
}
