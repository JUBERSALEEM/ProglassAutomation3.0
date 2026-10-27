// ═══════════════════════════════════════════════════════════════════════
// FILE: step3.UserManagement/step3.b.6.FirstRunService.cs
// MODULE: step3 — the first-run flow, in ONE place.
//
// Why this file exists
//   "There are no users, therefore ask an administrator" is a circular
//   instruction: on a new installation no administrator can exist yet. The
//   bootstrap write already lives in the repository (atomic, and closed the
//   moment any user exists), but the *decision* around it — is setup needed,
//   which Windows account is this, did it actually work — was spread between
//   the shell and the Users page, and the operator could end up looking at a
//   screen that said "unavailable" with no way forward.
//
//   This service is the single implementation of that decision. The startup
//   gate and the Users page both call it; neither talks to the bootstrap write
//   directly any more.
//
// What it guarantees
//   • The account it registers is always the Windows account the process is
//     running under (Inquiry/Services/Audit/CurrentOperator). It is never typed,
//     so the row created is guaranteed to be the row this workstation will
//     match on the next launch.
//   • It refuses to run when the directory already has a user; the repository
//     refuses again inside the insert transaction, so a second caller cannot
//     win a race.
//   • It verifies the result by re-reading the directory and matching the
//     workstation account. If a row exists that this account still does not
//     match, it says so with both names instead of reporting success — that
//     state is the deadlock the owner hit, and it must never be silent.
//   • It never reactivates an inactive person, never creates a default account
//     and never grants anonymous access.
//   • It owns no credential: there is no password, hash or token in this flow.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Users;
using ProGlassAutomation.Services.Audit;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Users
{
    /// <summary>What the application finds when it asks "is this a first run?".</summary>
    public sealed class FirstRunSnapshot
    {
        /// <summary>The user table could be read (schema present, query succeeded).</summary>
        public bool DirectoryAvailable { get; init; }

        /// <summary>No user rows at all: the first administrator may be registered.</summary>
        public bool IsSetupRequired { get; init; }

        /// <summary>Total users found, for wording and for the closing test.</summary>
        public int UserCount { get; init; }

        /// <summary>The workstation account, exactly as the application reads it.</summary>
        public string OperatorUsername { get; init; } = "";

        /// <summary>The row this workstation account matches, if any (active or not).</summary>
        public User? MatchingUser { get; init; }

        /// <summary>Departments for the setup form.</summary>
        public IReadOnlyList<CatalogRow> Departments { get; init; } = Array.Empty<CatalogRow>();

        /// <summary>Operator-facing wording when the directory could not be read.</summary>
        public string Problem { get; init; } = "";

        /// <summary>True when the workstation account is an active Administrator.</summary>
        public bool IsOperatorAdministrator =>
            MatchingUser is { IsActive: true } && UserCatalog.IsAdministrator(MatchingUser.Role);
    }

    /// <summary>The first-run flow: inspect, then register the workstation account.</summary>
    public interface IFirstRunService
    {
        /// <summary>Reads the directory and reports whether setup is needed.</summary>
        Task<FirstRunSnapshot> InspectAsync(CancellationToken ct = default);

        /// <summary>
        /// Registers the workstation account as the first Administrator, then
        /// re-reads the directory to prove the account is now recognised.
        /// </summary>
        Task<UserSaveResult> RegisterAsync(string fullName, string department, CancellationToken ct = default);
    }

    /// <summary>Default implementation over <see cref="IUserRepository"/>.</summary>
    public sealed class FirstRunService : IFirstRunService
    {
        private readonly IUserRepository _repository;

        public FirstRunService(IUserRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task<FirstRunSnapshot> InspectAsync(CancellationToken ct = default)
        {
            string account = CurrentOperator.Username;

            try
            {
                await _repository.InitializeAsync(ct).ConfigureAwait(false);

                int count = await _repository.CountAsync(ct).ConfigureAwait(false);
                User? matching = await _repository.FindCurrentUserAsync(ct).ConfigureAwait(false);
                var departments = await _repository.GetDepartmentsAsync(ct).ConfigureAwait(false);

                return new FirstRunSnapshot
                {
                    DirectoryAvailable = true,
                    IsSetupRequired = count == 0,
                    UserCount = count,
                    OperatorUsername = account,
                    MatchingUser = matching,
                    Departments = departments
                };
            }
            catch (Exception ex)
            {
                return new FirstRunSnapshot
                {
                    DirectoryAvailable = false,
                    OperatorUsername = account,
                    Problem = "The user directory could not be opened ("
                        + ex.GetType().Name + "): " + ex.Message
                };
            }
        }

        public async Task<UserSaveResult> RegisterAsync(
            string fullName, string department, CancellationToken ct = default)
        {
            FirstRunSnapshot before = await InspectAsync(ct).ConfigureAwait(false);

            if (!before.DirectoryAvailable)
                return UserSaveResult.Fail(
                    string.IsNullOrWhiteSpace(before.Problem)
                        ? "The user directory could not be read, so setup cannot continue. Try again."
                        : before.Problem,
                    UserDecisionCode.InvalidTarget);

            if (!before.IsSetupRequired)
                return UserSaveResult.Fail(
                    "This directory already has users, so the first-administrator step is closed. "
                    + "Sign in with a registered Administrator account and ask them to add you.",
                    UserDecisionCode.NotAuthorized);

            string account = before.OperatorUsername.Trim();
            if (account.Length == 0)
                return UserSaveResult.Fail(
                    "The Windows account this workstation is running under could not be read, "
                    + "so it cannot be registered. Restart the application from a signed-in session.",
                    UserDecisionCode.NotIdentified);

            // Validate the name here as well as in the repository: the operator
            // gets the field message on the form instead of a write that fails.
            string? nameError = UserRules.ValidateFullName(fullName);
            if (nameError != null)
                return UserSaveResult.Fail(nameError, UserDecisionCode.InvalidTarget);

            UserSaveResult result = await _repository
                .RegisterFirstAdministratorAsync(fullName, department, account, ct)
                .ConfigureAwait(false);

            if (!result.Succeeded) return result;

            // Prove it, do not assume it. The row must now be found by the very
            // account this process reports; if it is not, the operator would be
            // locked out of the page they just set up, so say exactly why.
            User? matched = await _repository.FindCurrentUserAsync(ct).ConfigureAwait(false);
            if (matched == null || !matched.IsActive)
            {
                return UserSaveResult.Fail(
                    $"The account was saved as \"{result.User?.Username}\", but this workstation reports "
                    + $"\"{account}\", so the application still cannot recognise you. "
                    + "Open Users, edit that row, and set its username to the value this workstation reports.",
                    UserDecisionCode.NotIdentified);
            }

            return result;
        }
    }
}
