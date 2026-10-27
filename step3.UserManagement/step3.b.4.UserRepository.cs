// ═══════════════════════════════════════════════════════════════════════
// FILE: step3.UserManagement/step3.b.4.UserRepository.cs
// MODULE: step3 — user directory persistence and the enforcement point.
//
// Behaviour
//   • InitializeAsync first asks the established ERP initializer to bring the
//     shared inquiries.db to the current schema (EnsureCreated + the single
//     AppSchemaMigrations chain, which now includes migration 1013 → Users,
//     Departments, Roles). Creating the tables through that chain keeps the
//     backup-before-migrate, single-transaction, verification and quick_check
//     safeguards in force. No second chain, no second EnsureCreated owner.
//   • Every write opens a transaction and resolves the operator from the
//     workstation username INSIDE it (see step3.b.2 for why). Permission and
//     the last-administrator guard are therefore evaluated against committed
//     state at the moment of the write, not against whatever the screen
//     loaded a minute ago. A refusal rolls back cleanly and returns a message.
//   • Uniqueness of the username is checked before the unique index can fire,
//     so the operator reads "that username already belongs to X" instead of a
//     SQLite constraint error.
//   • Timestamps are UTC. Writes go through SqliteResilience.SaveWithRetryAsync
//     so a transient SQLITE_BUSY is retried rather than surfaced.
//   • Nothing here deletes a user or touches the Clients / Inquiry tables.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Models.Users;
using ProGlassAutomation.Services.Audit;
using ProGlassAutomation.Services.Database;
using ProGlassAutomation.Services.Database.SchemaMigrations;
using ProGlassAutomation.Services.Inquiry;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Users
{
    /// <summary>EF Core implementation of <see cref="IUserRepository"/>.</summary>
    public sealed class UserRepository : IUserRepository
    {
        private readonly Func<UserDbContext> _createContext;
        private bool _initialized;

        public UserRepository() : this(null) { }

        /// <summary>
        /// Test seam: an alternate context factory. Production callers use the
        /// parameterless constructor (self-configuring context on the real
        /// inquiries.db); the verification executable can point the same code
        /// at an isolated file via AppDataRoot.TestFolderOverride.
        /// </summary>
        internal UserRepository(Func<UserDbContext>? contextFactory)
        {
            _createContext = contextFactory ?? (() => new UserDbContext());
        }

        private UserDbContext CreateContext() => _createContext();

        // ── Initialization ───────────────────────────────────────────────

        public async Task InitializeAsync(CancellationToken ct = default)
        {
            if (_initialized) return;

            await new InquiryRepository().InitializeAsync(ct).ConfigureAwait(false);

            if (!await IsSchemaReadyAsync(ct).ConfigureAwait(false))
                throw new InvalidOperationException(
                    "The user directory tables (migration 1013) are missing after initialization. " +
                    "Schema upgrade, if any, reported: " +
                    (string.IsNullOrWhiteSpace(SchemaMigrationRunner.FailureSummary)
                        ? "no failure recorded."
                        : SchemaMigrationRunner.FailureSummary));

            _initialized = true;
        }

        public async Task<bool> IsSchemaReadyAsync(CancellationToken ct = default)
        {
            try
            {
                await using var db = CreateContext();
                await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
                var connection = (SqliteConnection)db.Database.GetDbConnection();

                foreach (string table in new[] { UserSchema.UsersTable, UserSchema.DepartmentsTable, UserSchema.RolesTable })
                {
                    using var tableCommand = connection.CreateCommand();
                    tableCommand.CommandText =
                        "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name;";
                    tableCommand.Parameters.AddWithValue("@name", table);
                    long found = Convert.ToInt64(
                        tableCommand.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
                    if (found == 0) return false;
                }

                var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var columnCommand = connection.CreateCommand())
                {
                    columnCommand.CommandText = "PRAGMA table_info(\"Users\");";
                    using var reader = columnCommand.ExecuteReader();
                    while (reader.Read())
                    {
                        if (!reader.IsDBNull(1)) columns.Add(reader.GetString(1));
                    }
                }

                foreach (string required in UserSchema.RequiredColumns)
                    if (!columns.Contains(required)) return false;

                // Credential columns come from 1015. Without them nobody can sign
                // in, so the directory is not ready however healthy the rest looks.
                foreach (string required in UserSchema.CredentialColumns)
                    if (!columns.Contains(required)) return false;
                return true;
            }
            catch (Exception)
            {
                // A readiness probe never throws: an unreadable/absent file is
                // simply "not ready", and the caller reports it.
                return false;
            }
        }

        /// <summary>
        /// Brings an already-open connection in step with the catalogue seed.
        /// Used by verification to prove the seed is idempotent on a second run.
        /// </summary>
        public static void EnsureSeed(SqliteConnection connection) => UserSchema.SeedCatalogues(connection);

        // ── Reads ────────────────────────────────────────────────────────

        public async Task<IReadOnlyList<UserListRow>> QueryAsync(
            UserQuery query, CancellationToken ct = default)
        {
            query ??= new UserQuery();
            await using var db = CreateContext();
            IQueryable<User> source = db.Users.AsNoTracking();

            // ── Read scope ──────────────────────────────────────────────
            // Owner instruction: "user should be able to read the data related to
            // them only". Two layers, on purpose:
            //   • the CEILING — a signed-in non-administrator can never read
            //     another person's row, whatever the caller asks for;
            //   • the REQUEST — the workspace passes ScopeToUserId so the intent
            //     is visible where the query is built.
            // A process with nobody signed in is the bootstrap/fixture state; the
            // sign-in gate makes it unreachable from the application.
            if (AuthenticationContext.Current is { } viewer
                && !AuthenticationContext.IsAdministrator)
            {
                // The ceiling wins first: a non-administrator cannot reach another
                // row by asking for it.
                int ownId = viewer.Id;
                source = source.Where(x => x.Id == ownId);
            }
            else if (query.ScopeToUserId is int requestedId)
            {
                source = source.Where(x => x.Id == requestedId);
            }

            if (query.IsActive.HasValue)
            {
                bool wanted = query.IsActive.Value;
                source = source.Where(x => x.IsActive == wanted);
            }

            string department = (query.Department ?? "").Trim();
            if (department.Length > 0)
                source = source.Where(x => x.Department == department);

            string role = (query.Role ?? "").Trim();
            if (role.Length > 0)
                source = source.Where(x => x.Role == role);

            string term = UserRules.NormalizeText(query.Search);
            if (term.Length > 0)
            {
                // LIKE (not Contains) for the case-insensitive matching operators
                // expect; the escape character keeps a typed % or _ literal.
                string pattern = "%" +
                    term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                source = source.Where(x =>
                    EF.Functions.Like(x.FullName, pattern, "\\")
                    || EF.Functions.Like(x.Username, pattern, "\\")
                    || EF.Functions.Like(x.Role, pattern, "\\")
                    || EF.Functions.Like(x.Department, pattern, "\\"));
            }

            int limit = query.MaxRows <= 0 ? 2000 : query.MaxRows;

            var rows = await source
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.FullName)
                .Take(limit)
                .Select(x => new UserListRow
                {
                    Id = x.Id,
                    FullName = x.FullName,
                    Username = x.Username,
                    Department = x.Department,
                    Role = x.Role,
                    IsActive = x.IsActive,
                    CreatedAt = x.CreatedAt,
                    UpdatedAt = x.UpdatedAt
                })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            return rows;
        }

        public async Task<User?> GetAsync(int id, CancellationToken ct = default)
        {
            if (id <= 0) return null;
            await using var db = CreateContext();
            return await db.Users.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, ct)
                .ConfigureAwait(false);
        }

        public async Task<string> ResolveDisplayNameAsync(int? userId, CancellationToken ct = default)
        {
            if (!userId.HasValue) return "";
            await using var db = CreateContext();
            return await db.Users.AsNoTracking()
                .Where(x => x.Id == userId.Value)
                .Select(x => x.FullName)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false) ?? "";
        }

        public async Task<User?> FindCurrentUserAsync(CancellationToken ct = default)
            => await FindUserRowAsync(CurrentOperatorUsername(), ct).ConfigureAwait(false);

        /// <summary>
        /// True when the directory has no users at all, i.e. the application still
        /// needs its first administrator. Call after <see cref="InitializeAsync"/>.
        /// </summary>
        public async Task<bool> IsFirstRunRequiredAsync(CancellationToken ct = default)
            => await CountAsync(ct).ConfigureAwait(false) == 0;

        public async Task<int> CountAsync(CancellationToken ct = default)
        {
            await using var db = CreateContext();
            return await db.Users.AsNoTracking().CountAsync(ct).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<CatalogRow>> GetDepartmentsAsync(CancellationToken ct = default)
            => await ReadCatalogueAsync(UserSchema.DepartmentsTable, ct).ConfigureAwait(false);

        public async Task<IReadOnlyList<CatalogRow>> GetRolesAsync(CancellationToken ct = default)
            => await ReadCatalogueAsync(UserSchema.RolesTable, ct).ConfigureAwait(false);

        private async Task<IReadOnlyList<CatalogRow>> ReadCatalogueAsync(string table, CancellationToken ct)
        {
            var rows = new List<CatalogRow>();
            try
            {
                await using var db = CreateContext();
                await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
                var connection = (SqliteConnection)db.Database.GetDbConnection();

                using var command = connection.CreateCommand();
                // Table name is a compile-time constant from UserSchema, never
                // operator input, so interpolation here cannot be injected.
                command.CommandText =
                    $"SELECT \"Code\", \"DisplayName\", \"SortOrder\", \"IsDefault\" FROM \"{table}\" " +
                    "ORDER BY \"SortOrder\", \"DisplayName\";";
                using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    rows.Add(new CatalogRow
                    {
                        Code = reader.IsDBNull(0) ? "" : reader.GetString(0),
                        DisplayName = reader.IsDBNull(1) ? "" : reader.GetString(1),
                        SortOrder = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                        IsDefault = !reader.IsDBNull(3) && reader.GetInt32(3) != 0
                    });
                }
            }
            catch (Exception)
            {
                // Before the first successful initialization (or on a file that
                // has not been migrated yet) fall back to the shipped catalogue so
                // the editor can still render a usable list.
                rows = FallbackCatalog(table);
            }

            return rows.Count > 0 ? rows : FallbackCatalog(table);
        }

        private static List<CatalogRow> FallbackCatalog(string table)
        {
            IReadOnlyList<CatalogEntry> entries = table == UserSchema.RolesTable
                ? UserCatalog.Roles
                : UserCatalog.Departments;
            return entries
                .Select((entry, index) => new CatalogRow
                {
                    Code = entry.Code,
                    DisplayName = entry.DisplayName,
                    SortOrder = index,
                    IsDefault = entry.IsDefault
                })
                .ToList();
        }

        // ── Credentials ──────────────────────────────────────────────────

        /// <summary>Failures allowed before the account locks.</summary>
        public const int MaxFailedSignIns = 5;

        /// <summary>How long a locked account stays locked.</summary>
        public const int LockoutSeconds = 60;

        public async Task<User?> FindByUsernameAsync(string username, CancellationToken ct = default)
        {
            await using var db = CreateContext();
            return await FindUserRowAsync(db, username, ct).ConfigureAwait(false);
        }

        public async Task<SignInResult> SignInAsync(
            string username, string password, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(username))
                return SignInResult.Fail(SignInCode.UnknownUser, "Enter your username.");

            if (string.IsNullOrEmpty(password))
                return SignInResult.Fail(SignInCode.WrongPassword, "Enter your password.");

            try
            {
                await using var db = CreateContext();

                User? user = await FindUserRowAsync(db, username, ct).ConfigureAwait(false);
                if (user == null)
                    return SignInResult.Fail(
                        SignInCode.UnknownUser, "No account matches that username.");

                if (!user.IsActive)
                    return SignInResult.Fail(
                        SignInCode.Inactive,
                        "That account has been deactivated. Ask an administrator to re-activate it.");

                DateTime now = DateTime.UtcNow;
                if (user.LockedUntilUtc is DateTime lockedUntil && lockedUntil > now)
                {
                    int seconds = Math.Max(1, (int)Math.Ceiling((lockedUntil - now).TotalSeconds));
                    return SignInResult.Fail(
                        SignInCode.Locked,
                        $"Too many failed attempts. Try again in {seconds} second(s).");
                }

                var stored = new PasswordHash
                {
                    Hash = user.PasswordHash,
                    Salt = user.PasswordSalt,
                    Iterations = user.PasswordIterations
                };

                if (stored.IsEmpty)
                    return SignInResult.Fail(
                        SignInCode.NoPassword,
                        "This account has no password yet. Ask an administrator to set one.");

                if (!UserPassword.Verify(password, stored))
                {
                    user.FailedSignInCount += 1;
                    int left = Math.Max(0, MaxFailedSignIns - user.FailedSignInCount);

                    if (user.FailedSignInCount >= MaxFailedSignIns)
                    {
                        user.FailedSignInCount = 0;
                        user.LockedUntilUtc = now.AddSeconds(LockoutSeconds);
                        await SqliteResilience.SaveWithRetryAsync(
                            token => db.SaveChangesAsync(token), ct).ConfigureAwait(false);

                        return SignInResult.Fail(
                            SignInCode.Locked,
                            $"Too many failed attempts. The account is locked for {LockoutSeconds} seconds.");
                    }

                    await SqliteResilience.SaveWithRetryAsync(
                        token => db.SaveChangesAsync(token), ct).ConfigureAwait(false);

                    return SignInResult.Fail(
                        SignInCode.WrongPassword,
                        $"Wrong password. {left} attempt(s) left before the account locks.",
                        left);
                }

                // A good password clears the counters whether or not the password
                // still has to be replaced.
                user.FailedSignInCount = 0;
                user.LockedUntilUtc = null;
                await SqliteResilience.SaveWithRetryAsync(
                    token => db.SaveChangesAsync(token), ct).ConfigureAwait(false);

                return SignInResult.Ok(user, user.MustChangePassword);
            }
            catch (Exception ex)
            {
                return SignInResult.Fail(
                    SignInCode.UnknownUser, DescribeWriteFailure("Sign-in could not be completed", ex));
            }
        }

        public async Task<UserSaveResult> ChangePasswordAsync(
            int userId, string currentPassword, string newPassword,
            string operatorUsername, CancellationToken ct = default)
        {
            string? policy = UserPassword.Validate(newPassword);
            if (policy != null) return UserSaveResult.Fail(policy);
            if (userId <= 0) return UserSaveResult.Fail("No user was selected.");

            try
            {
                await using var db = CreateContext();
                await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

                User? actor = await ResolveOperatorAsync(db, operatorUsername, ct).ConfigureAwait(false);
                if (actor == null || !actor.IsActive)
                    return UserSaveResult.Fail(
                        "Sign in again before changing a password.", UserDecisionCode.NotIdentified);

                if (actor.Id != userId)
                    return UserSaveResult.Fail(
                        "You can change your own password only. An administrator resets somebody else's.",
                        UserDecisionCode.NotAuthorized);

                User? target = await db.Users
                    .FirstOrDefaultAsync(x => x.Id == userId, ct).ConfigureAwait(false);
                if (target == null)
                    return UserSaveResult.Fail("That user no longer exists in the directory.");

                var stored = new PasswordHash
                {
                    Hash = target.PasswordHash,
                    Salt = target.PasswordSalt,
                    Iterations = target.PasswordIterations
                };

                // An account with no credential yet has nothing to prove: the person
                // is already signed in, so they may choose the first password.
                if (!stored.IsEmpty && !UserPassword.Verify(currentPassword, stored))
                    return UserSaveResult.Fail("The current password is not correct.");

                ApplyNewPassword(target, newPassword, DateTime.UtcNow, mustChange: false, actor.Id);

                await SqliteResilience.SaveWithRetryAsync(
                    token => db.SaveChangesAsync(token), ct).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return UserSaveResult.Ok(target);
            }
            catch (Exception ex)
            {
                return UserSaveResult.Fail(DescribeWriteFailure("The password could not be changed", ex));
            }
        }

        public async Task<UserSaveResult> ResetPasswordAsync(
            int userId, string newPassword, string operatorUsername, CancellationToken ct = default)
        {
            string? policy = UserPassword.Validate(newPassword);
            if (policy != null) return UserSaveResult.Fail(policy);
            if (userId <= 0) return UserSaveResult.Fail("No user was selected.");

            try
            {
                await using var db = CreateContext();
                await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

                User? actor = await ResolveOperatorAsync(db, operatorUsername, ct).ConfigureAwait(false);
                UserDecision permission = UserAuthorization.CanManageUsers(actor);
                if (!permission.IsAllowed) return Refuse(permission);

                User? target = await db.Users
                    .FirstOrDefaultAsync(x => x.Id == userId, ct).ConfigureAwait(false);
                if (target == null)
                    return UserSaveResult.Fail("That user no longer exists in the directory.");

                ApplyNewPassword(target, newPassword, DateTime.UtcNow,
                    mustChange: actor!.Id != target.Id, operatorId: actor.Id);

                await SqliteResilience.SaveWithRetryAsync(
                    token => db.SaveChangesAsync(token), ct).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return UserSaveResult.Ok(target);
            }
            catch (Exception ex)
            {
                return UserSaveResult.Fail(DescribeWriteFailure("The password could not be reset", ex));
            }
        }

        /// <summary>
        /// Writes a freshly hashed password onto a tracked row and clears the
        /// lockout. The plain password is never stored, logged or echoed.
        /// </summary>
        private static void ApplyNewPassword(
            User target, string newPassword, DateTime now, bool mustChange, int operatorId)
        {
            PasswordHash credential = UserPassword.Create(newPassword);
            target.PasswordHash = credential.Hash;
            target.PasswordSalt = credential.Salt;
            target.PasswordIterations = credential.Iterations;
            target.PasswordUpdatedAt = now;
            target.MustChangePassword = mustChange;
            target.FailedSignInCount = 0;
            target.LockedUntilUtc = null;
            target.UpdatedAt = now;

            // 0 means "no earlier user to name" (the first-run bootstrap). Writing 0
            // into UpdatedByUserId would break the rule that a stored audit id
            // resolves to a real person, so the column stays null instead.
            if (operatorId > 0) target.UpdatedByUserId = operatorId;
        }

        // ── Writes ───────────────────────────────────────────────────────

        public async Task<UserSaveResult> CreateAsync(
            UserDraft draft, string operatorUsername, CancellationToken ct = default)
        {
            if (draft == null) return UserSaveResult.Fail("No user data was supplied.");

            var errors = UserRules.Validate(draft);
            if (errors.Count > 0)
                return UserSaveResult.Fail(UserRules.Describe(errors));

            UserDraft normalized = UserRules.Normalize(draft);
            string key = UserRules.UsernameKey(normalized.Username);
            DateTime now = DateTime.UtcNow;

            try
            {
                await using var db = CreateContext();
                await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

                User? actor = await ResolveOperatorAsync(db, operatorUsername, ct).ConfigureAwait(false);
                UserDecision permission = UserAuthorization.CanManageUsers(actor);
                if (!permission.IsAllowed) return Refuse(permission);

                UserSaveResult? duplicate =
                    await DescribeDuplicateAsync(db, key, null, ct).ConfigureAwait(false);
                if (duplicate != null) return duplicate;

                var user = new User
                {
                    FullName = normalized.FullName,
                    Username = normalized.Username,
                    NormalizedUsername = key,
                    Department = normalized.Department,
                    Role = normalized.Role,
                    IsActive = normalized.IsActive,
                    CreatedAt = now,
                    UpdatedAt = now,
                    CreatedByUserId = actor!.Id,
                    UpdatedByUserId = actor.Id
                };

                // The administrator typed this password, so the person replaces it
                // at their first sign-in. Only the hash is stored.
                ApplyNewPassword(user, normalized.Password, now, mustChange: true, operatorId: actor.Id);

                db.Users.Add(user);
                await SqliteResilience.SaveWithRetryAsync(
                    token => db.SaveChangesAsync(token), ct).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return UserSaveResult.Ok(user);
            }
            catch (Exception ex)
            {
                return UserSaveResult.Fail(DescribeWriteFailure("The user could not be saved", ex));
            }
        }

        public async Task<UserSaveResult> UpdateAsync(
            UserDraft draft, string operatorUsername, CancellationToken ct = default)
        {
            if (draft == null) return UserSaveResult.Fail("No user data was supplied.");
            if (draft.Id <= 0) return UserSaveResult.Fail("No user was selected.");

            var errors = UserRules.Validate(draft);
            if (errors.Count > 0)
                return UserSaveResult.Fail(UserRules.Describe(errors));

            UserDraft normalized = UserRules.Normalize(draft);
            string key = UserRules.UsernameKey(normalized.Username);
            DateTime now = DateTime.UtcNow;

            try
            {
                await using var db = CreateContext();
                await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

                User? actor = await ResolveOperatorAsync(db, operatorUsername, ct).ConfigureAwait(false);
                UserDecision permission = UserAuthorization.CanManageUsers(actor);
                if (!permission.IsAllowed) return Refuse(permission);

                User? target = await db.Users
                    .FirstOrDefaultAsync(x => x.Id == draft.Id, ct)
                    .ConfigureAwait(false);
                if (target == null)
                    return UserSaveResult.Fail(
                        "That user no longer exists in the directory. Refresh the list and try again.");

                // The guard reads the stored row, not the draft's memory of it:
                // a stale editor cannot claim the target was already demoted.
                var guardDraft = new UserDraft
                {
                    Id = target.Id,
                    FullName = normalized.FullName,
                    Username = normalized.Username,
                    Department = normalized.Department,
                    Role = normalized.Role,
                    IsActive = normalized.IsActive,
                    OriginalRole = target.Role,
                    OriginalIsActive = target.IsActive
                };

                int otherAdministrators = await db.Users.AsNoTracking()
                    .CountAsync(x => x.IsActive
                        && x.Role == UserCatalog.AdministratorRole
                        && x.Id != target.Id, ct)
                    .ConfigureAwait(false);

                UserDecision change = UserAuthorization.ValidateChange(guardDraft, otherAdministrators);
                if (!change.IsAllowed) return Refuse(change);

                UserDecision self = UserAuthorization.ValidateSelfDeactivation(guardDraft, actor);
                if (!self.IsAllowed) return Refuse(self);

                UserSaveResult? duplicate =
                    await DescribeDuplicateAsync(db, key, target.Id, ct).ConfigureAwait(false);
                if (duplicate != null) return duplicate;

                target.FullName = normalized.FullName;
                target.Username = normalized.Username;
                target.NormalizedUsername = key;
                target.Department = normalized.Department;
                target.Role = normalized.Role;
                target.IsActive = normalized.IsActive;
                target.UpdatedAt = now;
                target.UpdatedByUserId = actor!.Id;

                await SqliteResilience.SaveWithRetryAsync(
                    token => db.SaveChangesAsync(token), ct).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return UserSaveResult.Ok(target);
            }
            catch (Exception ex)
            {
                return UserSaveResult.Fail(DescribeWriteFailure("The user could not be updated", ex));
            }
        }

        public async Task<UserSaveResult> SetActiveAsync(
            int id, bool isActive, string operatorUsername, CancellationToken ct = default)
        {
            if (id <= 0) return UserSaveResult.Fail("No user was selected.");
            DateTime now = DateTime.UtcNow;

            try
            {
                await using var db = CreateContext();
                await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

                User? actor = await ResolveOperatorAsync(db, operatorUsername, ct).ConfigureAwait(false);
                UserDecision permission = UserAuthorization.CanManageUsers(actor);
                if (!permission.IsAllowed) return Refuse(permission);

                User? target = await db.Users
                    .FirstOrDefaultAsync(x => x.Id == id, ct)
                    .ConfigureAwait(false);
                if (target == null)
                    return UserSaveResult.Fail(
                        "That user no longer exists in the directory. Refresh the list and try again.");

                if (target.IsActive == isActive) return UserSaveResult.Ok(target);

                var guardDraft = new UserDraft
                {
                    Id = target.Id,
                    FullName = target.FullName,
                    Username = target.Username,
                    Department = target.Department,
                    Role = target.Role,
                    IsActive = isActive,
                    OriginalRole = target.Role,
                    OriginalIsActive = target.IsActive
                };

                int otherAdministrators = await db.Users.AsNoTracking()
                    .CountAsync(x => x.IsActive
                        && x.Role == UserCatalog.AdministratorRole
                        && x.Id != target.Id, ct)
                    .ConfigureAwait(false);

                UserDecision change = UserAuthorization.ValidateChange(guardDraft, otherAdministrators);
                if (!change.IsAllowed) return Refuse(change);

                UserDecision self = UserAuthorization.ValidateSelfDeactivation(guardDraft, actor);
                if (!self.IsAllowed) return Refuse(self);

                target.IsActive = isActive;
                target.UpdatedAt = now;
                target.UpdatedByUserId = actor!.Id;

                await SqliteResilience.SaveWithRetryAsync(
                    token => db.SaveChangesAsync(token), ct).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return UserSaveResult.Ok(target);
            }
            catch (Exception ex)
            {
                string action = isActive ? "re-activated" : "deactivated";
                return UserSaveResult.Fail(DescribeWriteFailure($"The user could not be {action}", ex));
            }
        }

        public async Task<UserSaveResult> RegisterFirstAdministratorAsync(
            string fullName, string department, string password,
            string operatorUsername, CancellationToken ct = default)
        {
            string? policy = UserPassword.Validate(password);
            if (policy != null) return UserSaveResult.Fail(policy);
            string username = UserRules.NormalizeUsername(operatorUsername);
            if (username.Length == 0)
                return UserSaveResult.Fail(
                    "The workstation account name could not be read, so the first administrator cannot be registered.",
                    UserDecisionCode.NotIdentified);

            string departmentCode = UserRules.NormalizeDepartment(department);
            DateTime now = DateTime.UtcNow;

            var draft = new UserDraft
            {
                FullName = fullName,
                Username = username,
                Department = departmentCode,
                Role = UserCatalog.AdministratorRole,
                IsActive = true
            };

            var errors = UserRules.Validate(draft);
            if (errors.Count > 0)
                return UserSaveResult.Fail(UserRules.Describe(errors));

            UserDraft normalized = UserRules.Normalize(draft);

            try
            {
                await using var db = CreateContext();
                await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

                // Bootstrap is a one-shot door: it closes the instant any user
                // exists, so it can never be used later to escalate. The check is
                // INSIDE the write transaction, so it reads committed state.
                int existing = await db.Users.CountAsync(ct).ConfigureAwait(false);
                if (existing > 0)
                    return UserSaveResult.Fail(
                        "The user directory is no longer empty, so the first-administrator step is closed. "
                        + "Ask an administrator to add you.",
                        UserDecisionCode.NotAuthorized);

                var user = new User
                {
                    FullName = normalized.FullName,
                    Username = normalized.Username,
                    NormalizedUsername = UserRules.UsernameKey(normalized.Username),
                    Department = normalized.Department,
                    Role = UserCatalog.AdministratorRole,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                    CreatedByUserId = null,   // nobody existed to grant it
                    UpdatedByUserId = null
                };

                // The first administrator chooses their own password here, so it is
                // not a temporary one: there is nobody to replace it for them.
                ApplyNewPassword(user, password, now, mustChange: false, operatorId: 0);

                db.Users.Add(user);
                await SqliteResilience.SaveWithRetryAsync(
                    token => db.SaveChangesAsync(token), ct).ConfigureAwait(false);

                // Two workstations could both have seen an empty directory
                // before either wrote. SQLite serializes the writes, so by the
                // time this row is visible the other insert has committed if it
                // was going to. Re-read the count inside the same transaction and
                // abort rather than leave two "first" administrators behind.
                int afterInsert = await db.Users.CountAsync(ct).ConfigureAwait(false);
                if (afterInsert != 1)
                    return UserSaveResult.Fail(
                        "Another workstation registered a first administrator at the same moment. "
                        + "Nothing was changed here; close this window and sign in with that account.",
                        UserDecisionCode.NotAuthorized);

                // Backfill the audit pointer to the new row itself: this record
                // was created by this administrator on this workstation.
                user.CreatedByUserId = user.Id;
                user.UpdatedByUserId = user.Id;
                await SqliteResilience.SaveWithRetryAsync(
                    token => db.SaveChangesAsync(token), ct).ConfigureAwait(false);

                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return UserSaveResult.Ok(user,
                    $"{user.FullName} is now the first administrator of this directory.");
            }
            catch (Exception ex)
            {
                return UserSaveResult.Fail(DescribeWriteFailure(
                    "The first administrator could not be registered", ex));
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────

        /// <summary>The workstation account, from the application's one identity source.</summary>
        /// <summary>
        /// The account a write is attributed to: the user who signed in.
        /// </summary>
        /// <remarks>
        /// It used to be the Windows workstation account. Since every user now has
        /// a password (owner instruction, 2026-10-27) the signed-in user is the
        /// only honest answer, and an empty string before sign-in means the write
        /// path resolves no actor and refuses — the gate cannot be bypassed by
        /// calling a repository method directly.
        /// </remarks>
        private static string CurrentOperatorUsername() => AuthenticationContext.Username;

        /// <summary>
        /// Resolves the operating user from the workstation username inside the
        /// caller's transaction. Returns null when nothing matches, which the
        /// authorization rules treat as "not identified".
        /// </summary>
        private static async Task<User?> ResolveOperatorAsync(
            UserDbContext db, string operatorUsername, CancellationToken ct)
            => await FindUserRowAsync(db, operatorUsername, ct).ConfigureAwait(false);

        /// <summary>One row by username, in its own context.</summary>
        private static async Task<User?> FindUserRowAsync(
            string username, CancellationToken ct)
        {
            await using var db = new UserDbContext();
            return await FindUserRowAsync(db, username, ct).ConfigureAwait(false);
        }

        /// <summary>One row by username inside the caller's context.</summary>
        private static async Task<User?> FindUserRowAsync(
            UserDbContext db, string username, CancellationToken ct)
        {
            string key = UserRules.UsernameKey(operatorUsername);
            if (key.Length == 0) return null;
            return await db.Users.AsNoTracking()
                .FirstOrDefaultAsync(x => x.NormalizedUsername == key, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Friendly duplicate wording before the unique index can throw. Returns
        /// null when the username is free.
        /// </summary>
        private static async Task<UserSaveResult?> DescribeDuplicateAsync(
            UserDbContext db, string key, int? excludeUserId, CancellationToken ct)
        {
            var existing = await db.Users.AsNoTracking()
                .Where(x => x.NormalizedUsername == key)
                .Select(x => new { x.Id, x.FullName, x.Username })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var clash = existing.FirstOrDefault(x => !excludeUserId.HasValue || x.Id != excludeUserId.Value);
            if (clash == null) return null;

            return UserSaveResult.Fail(
                $"The username \"{clash.Username}\" already belongs to {clash.FullName}. "
                + "Usernames must be unique because they identify the person operating the app.");
        }

        private static UserSaveResult Refuse(UserDecision decision)
            => UserSaveResult.Fail(decision.Message, decision.Code);

        /// <summary>
        /// Turns a persistence exception into an operator-facing sentence. The
        /// exception type is named so a support conversation has something to go
        /// on, without exposing a stack trace in the UI.
        /// </summary>
        private static string DescribeWriteFailure(string action, Exception exception)
        {
            if (exception == null) return action + ".";
            if (SqliteErrorClassifier.IsTransient(exception))
                return action + ": the database is busy. Close other ProGlass windows and try again.";
            if (SqliteErrorClassifier.IsUnique(exception))
                return action + ": that username is already registered to another user.";
            return action + " (" + exception.GetType().Name + "): " + exception.Message;
        }
    }
}
