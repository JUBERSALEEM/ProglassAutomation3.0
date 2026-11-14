// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Security/SecretVault.cs
// PROJECT: ProGlassAutomation
// MODULE: Core — secret storage (API keys)
//
// API keys are never stored in plain text. They are encrypted with
// Windows DPAPI (ProtectedData, CurrentUser scope) into secrets.dat:
// readable only by the same Windows user account on this machine.
//
// Behaviour:
//   • Store(key, secret)    — encrypt + persist
//   • TryRetrieve(key, out) — decrypt when needed
//   • Remove / RemoveAll    — user-driven cleanup (Settings)
//
// If "Keep keys saved" is turned off in Settings, the vault is cleared
// and keys exist only in memory for the running session.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Services.Common;
using ProGlassAutomation.Services.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProGlassAutomation.Services.Security
{
    /// <summary>DPAPI-backed secret store. Static facade, never throws.</summary>
    public static class SecretVault
    {
        private static readonly object _lock = new();

        // App-level entropy mixed into DPAPI so blobs from other apps
        // on the same machine cannot be replayed into this vault.
        // Phase 8: Entropy versioned for rotation — old blobs can be migrated.
        private static readonly byte[] Entropy =
            Encoding.UTF8.GetBytes("ProGlassAutomation.v2.vault");

        private static readonly byte[] EntropyV1 =
            Encoding.UTF8.GetBytes("ProGlassAutomation.v1.vault");

        private static string VaultPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ProGlassAutomation", "secrets.dat");

        private static string VaultBackupPath => VaultPath + ".bak";

        // ── Rotation & Health ──────────────────────────────────────────

        /// <summary>
        /// Phase 8: Checks vault health — can decrypt all entries, no corruption.
        /// Returns Result with count of valid entries.
        /// </summary>
        public static Result<int> CheckHealth(string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.NewId();
            using var scope = CorrelationContext.BeginScope(corrId);

            try
            {
                var map = Load();
                int valid = 0;
                foreach (var kvp in map)
                {
                    try
                    {
                        var cipher = Convert.FromBase64String(kvp.Value);
                        var plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
                        if (plain.Length > 0) valid++;
                    }
                    catch
                    {
                        // Try old entropy
                        try
                        {
                            var cipher = Convert.FromBase64String(kvp.Value);
                            var plain = ProtectedData.Unprotect(cipher, EntropyV1, DataProtectionScope.CurrentUser);
                            if (plain.Length > 0) valid++;
                        }
                        catch (System.Exception ex) { ProGlassAutomation.Services.Logging.AppLogService.Debug("[SecretVault] Health-check: legacy-entropy unprotect failed for one entry: " + ex.Message); }
                    }
                }

                AppLogService.Info($"SecretVault health: {valid}/{map.Count} valid [{corrId}]",
                    new LogContext { Module = "Security", Operation = "CheckHealth", CorrelationId = corrId });

                return Result<int>.Ok(valid, corrId);
            }
            catch (Exception ex)
            {
                var errId = AppLogService.Error($"SecretVault health check failed: {ex.Message}", ex,
                    new LogContext { Module = "Security", Operation = "CheckHealth", CorrelationId = corrId });
                return Result<int>.Fail(ex.Message, ex, corrId, errId);
            }
        }

        /// <summary>
        /// Phase 8: Rotates vault — re-encrypts all entries with current entropy.
        /// Migrates from V1 entropy if found. Creates backup before rotation.
        /// </summary>
        public static Result<int> Rotate(string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.NewId();
            using var scope = CorrelationContext.BeginScope(corrId);

            try
            {
                var map = Load();
                if (map.Count == 0) return Result<int>.Ok(0, corrId);

                // Backup
                try
                {
                    lock (_lock)
                    {
                        if (File.Exists(VaultPath))
                            File.Copy(VaultPath, VaultBackupPath, overwrite: true);
                    }
                }
                catch (Exception ex)
                {
                    AppLogService.Warning($"SecretVault backup failed: {ex.Message}",
                        new LogContext { Module = "Security", Operation = "Rotate", CorrelationId = corrId });
                }

                var newMap = new Dictionary<string, string>();
                int migrated = 0;

                foreach (var kvp in map)
                {
                    string? plainText = null;

                    // Try current entropy
                    try
                    {
                        var cipher = Convert.FromBase64String(kvp.Value);
                        var plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
                        plainText = Encoding.UTF8.GetString(plain);
                    }
                    catch
                    {
                        // Try old entropy
                        try
                        {
                            var cipher = Convert.FromBase64String(kvp.Value);
                            var plain = ProtectedData.Unprotect(cipher, EntropyV1, DataProtectionScope.CurrentUser);
                            plainText = Encoding.UTF8.GetString(plain);
                            migrated++;
                        }
                        catch (System.Exception ex) { ProGlassAutomation.Services.Logging.AppLogService.Debug("[SecretVault] Legacy key migration skipped for one entry: " + ex.Message); }
                    }

                    if (!string.IsNullOrEmpty(plainText))
                    {
                        var plain = Encoding.UTF8.GetBytes(plainText);
                        var cipher = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
                        newMap[kvp.Key] = Convert.ToBase64String(cipher);
                    }
                }

                Save(newMap);

                AppLogService.Info($"SecretVault rotated: {newMap.Count} entries, {migrated} migrated from V1 [{corrId}]",
                    new LogContext { Module = "Security", Operation = "Rotate", CorrelationId = corrId, Extra = $"Migrated={migrated}" });

                return Result<int>.Ok(newMap.Count, corrId);
            }
            catch (Exception ex)
            {
                var errId = AppLogService.Error($"SecretVault rotation failed: {ex.Message}", ex,
                    new LogContext { Module = "Security", Operation = "Rotate", CorrelationId = corrId });
                return Result<int>.Fail(ex.Message, ex, corrId, errId);
            }
        }

        /// <summary>Encrypts and stores one secret under a logical key.</summary>
        public static void Store(string key, string secret)
        {
            try
            {
                var map = Load();

                if (string.IsNullOrEmpty(secret))
                    map.Remove(key);
                else
                {
                    var plain = Encoding.UTF8.GetBytes(secret);
                    var cipher = ProtectedData.Protect(
                        plain, Entropy, DataProtectionScope.CurrentUser);
                    map[key] = Convert.ToBase64String(cipher);
                }

                Save(map);
            }
            catch (Exception ex)
            {
                AppLogService.Warning(
                    $"SecretVault.Store failed for '{key}': {ex.Message}",
                    new LogContext { Module = "Security", Class = nameof(SecretVault) });
            }
        }

        /// <summary>Decrypts one secret. Returns false when absent/unreadable.</summary>
        public static bool TryRetrieve(string key, out string secret)
        {
            secret = "";

            try
            {
                var map = Load();
                if (!map.TryGetValue(key, out var base64) ||
                    string.IsNullOrEmpty(base64))
                    return false;

                var cipher = Convert.FromBase64String(base64);
                var plain = ProtectedData.Unprotect(
                    cipher, Entropy, DataProtectionScope.CurrentUser);

                secret = Encoding.UTF8.GetString(plain);
                return true;
            }
            catch (Exception ex)
            {
                AppLogService.Warning(
                    $"SecretVault.TryRetrieve failed for '{key}': {ex.Message}",
                    new LogContext { Module = "Security", Class = nameof(SecretVault) });
                return false;
            }
        }

        /// <summary>Deletes one secret.</summary>
        public static void Remove(string key)
        {
            try
            {
                var map = Load();
                if (map.Remove(key))
                    Save(map);
            }
            catch (Exception ex) { AppLogService.Warning($"Suppressed exception: {ex.Message}"); }
        }

        /// <summary>Deletes all secrets (Settings → "Remove saved keys").</summary>
        public static void RemoveAll()
        {
            try
            {
                lock (_lock)
                {
                    if (File.Exists(VaultPath))
                        File.Delete(VaultPath);
                }
            }
            catch (Exception ex) { AppLogService.Warning($"Suppressed exception: {ex.Message}"); }
        }

        // ── Persistence (encrypted values only — no plain text) ────────

        private static Dictionary<string, string> Load()
        {
            lock (_lock)
            {
                if (!File.Exists(VaultPath))
                    return new Dictionary<string, string>();

                try
                {
                    return JsonSerializer
                        .Deserialize<Dictionary<string, string>>(
                            File.ReadAllText(VaultPath))
                        ?? new Dictionary<string, string>();
                }
                catch
                {
                    return new Dictionary<string, string>();
                }
            }
        }

        private static void Save(Dictionary<string, string> map)
        {
            lock (_lock)
            {
                var folder = Path.GetDirectoryName(VaultPath)!;
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                File.WriteAllText(
                    VaultPath,
                    JsonSerializer.Serialize(map));
            }
        }
    }
}
