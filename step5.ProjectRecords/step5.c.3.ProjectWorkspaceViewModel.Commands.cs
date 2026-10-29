// ═══════════════════════════════════════════════════════════════════════
// FILE: step5.ProjectRecords/step5.c.3.ProjectWorkspaceViewModel.Commands.cs
// MODULE: step5 — project workspace, commands.
//
// There is no delete command anywhere in this module. A project is closed, never
// removed; the inquiry link is attached and detached, never rewritten.
//
// Split from step5.c.1 so no file carries more than one job: the presenter owns
// its state and commands, this owns commands and nothing else.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProGlassAutomation.Diagnostics;
using ProGlassAutomation.Models.Projects;
using ProGlassAutomation.Services.Projects;

namespace ProGlassAutomation.ViewModels.Projects
{
    public sealed partial class ProjectWorkspaceViewModel
    {
        // ── Commands ────────────────────────────────────────────────────

        private void SelectStatusTab(ProjectStatusTab tab)
        {
            SelectedStatus = tab.Key;
            foreach (var item in StatusTabs) item.IsSelected = item.Key == tab.Key;
            ApplyStatusFilter(_selectedRow?.Id);
            OnPropertyChanged(nameof(RegisterSummary));
        }

        private async Task OpenEditorForNewAsync()
        {
            _isNewProject = true;
            _editingId = 0;
            EditNumber = "";
            EditName = "";
            EditClient = null;
            EditClientName = "";
            EditLocation = "";
            EditDescription = "";
            ErrorMessage = "";
            StatusMessage = "";
            IsEditorOpen = true;
            OnPropertyChanged(nameof(IsNewProject));
            OnPropertyChanged(nameof(EditorHeading));

            try
            {
                // The suggestion is shown, not imposed: the operator may type a
                // number, and a blank one is allocated at save time.
                EditNumber = await _repository.SuggestNumberAsync().ConfigureAwait(true);
            }
            catch (Exception error)
            {
                ErrorMessage = "The next project number could not be suggested: " + error.Message;
            }
        }

        /// <summary>
        /// Loads the FULL project, not the register row: the register does not
        /// carry the description, so editing from a row would have saved an empty
        /// one over the real text — a silent data loss on every edit.
        /// </summary>
        private async Task OpenEditorForSelectedAsync()
        {
            if (_selectedRow == null) return;

            _isNewProject = false;
            _editingId = _selectedRow.Id;
            EditNumber = _selectedRow.ProjectNumber;
            EditName = _selectedRow.ProjectName;
            EditClientName = _selectedRow.ClientName;
            EditClient = null;
            EditLocation = _selectedRow.Location;
            EditDescription = "";
            ErrorMessage = "";
            StatusMessage = "";
            IsEditorOpen = true;
            OnPropertyChanged(nameof(IsNewProject));
            OnPropertyChanged(nameof(EditorHeading));

            try
            {
                var project = await _repository.GetAsync(_selectedRow.Id).ConfigureAwait(true);
                if (project == null)
                {
                    ErrorMessage = "That project no longer exists in the register. Refresh the list.";
                    return;
                }
                EditNumber = project.ProjectNumber;
                EditName = project.ProjectName;
                EditClientName = project.ClientName;
                EditLocation = project.Location;
                EditDescription = project.Description;
                EditClient = project.ClientId == null
                    ? null
                    : Clients.FirstOrDefault(client => client.Id == project.ClientId.Value);
            }
            catch (Exception error)
            {
                ErrorMessage = "The project could not be loaded for editing: " + error.Message;
            }
        }

        private void CloseEditor()
        {
            IsEditorOpen = false;
            ErrorMessage = "";
            StatusMessage = "";
        }

        public async Task SaveAsync()
        {
            var draft = new ProjectDraft
            {
                ProjectNumber = EditNumber,
                ProjectName = EditName,
                ClientId = EditClient?.Id,
                ClientName = EditClientName,
                Location = EditLocation,
                Description = EditDescription,
                Status = ProjectStatus.Active
            };

            IsBusy = true;
            try
            {
                var result = _isNewProject
                    ? await _repository.CreateAsync(draft).ConfigureAwait(true)
                    : await _repository.UpdateAsync(_editingId, draft).ConfigureAwait(true);

                if (!result.Succeeded)
                {
                    ErrorMessage = result.Message;
                    return;
                }

                StatusMessage = _isNewProject
                    ? $"{result.Project?.ProjectNumber} was created."
                    : $"{result.Project?.ProjectNumber} was saved.";
                ErrorMessage = "";
                CloseEditor();

                // The row is already written at this point. Whatever the refresh
                // does, it cannot turn a successful save into a failed one — the
                // operator would retype and create a duplicate.
                try
                {
                    // NOT the saved id. A selection is what opens the inquiry
                    // panel, so selecting the row we just wrote would pop the
                    // panel open on a row nobody clicked (owner, round 44). The
                    // status sentence above names the record instead.
                    await ReloadAsync().ConfigureAwait(true);
                }
                catch (Exception refresh)
                {
                    StatusMessage = $"{result.Project?.ProjectNumber} was saved, but the register "
                        + "could not be refreshed: " + refresh.Message;
                    AppLog.Error("step5.projects.refresh-after-save", refresh,
                        "the project was saved; the register refresh failed");
                }
            }
            catch (Exception error)
            {
                ErrorMessage = "The project could not be saved: " + error.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task ToggleStatusAsync()
        {
            if (_selectedRow == null) return;
            string target = _selectedRow.IsClosed ? ProjectStatus.Active : ProjectStatus.Closed;

            IsBusy = true;
            try
            {
                var result = await _repository.SetStatusAsync(_selectedRow.Id, target).ConfigureAwait(true);
                if (!result.Succeeded)
                {
                    ErrorMessage = result.Message;
                    return;
                }
                StatusMessage = $"{result.Project?.ProjectNumber} is now {target.ToLowerInvariant()}.";
                await ReloadAsync().ConfigureAwait(true);
            }
            catch (Exception error)
            {
                ErrorMessage = "The project status could not be changed: " + error.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task AttachAsync()
        {
            if (_selectedCandidate == null || _selectedRow == null) return;
            IsBusy = true;
            try
            {
                var result = await _repository
                    .AttachInquiryAsync(_selectedCandidate.Id, _selectedRow.Id).ConfigureAwait(true);
                if (!result.Succeeded)
                {
                    ErrorMessage = result.Message;
                    return;
                }
                StatusMessage = "Inquiry linked to " + _selectedRow.ProjectNumber + ".";
                ErrorMessage = "";
                await LoadLinkedInquiriesAsync().ConfigureAwait(true);
                await ReloadAsync().ConfigureAwait(true);
            }
            catch (Exception error)
            {
                ErrorMessage = "The inquiry could not be linked: " + error.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task DetachAsync()
        {
            if (_selectedLinked == null) return;
            IsBusy = true;
            try
            {
                var result = await _repository.DetachInquiryAsync(_selectedLinked.Id).ConfigureAwait(true);
                if (!result.Succeeded)
                {
                    ErrorMessage = result.Message;
                    return;
                }
                StatusMessage = "Inquiry unlinked.";
                ErrorMessage = "";
                await LoadLinkedInquiriesAsync().ConfigureAwait(true);
                await ReloadAsync().ConfigureAwait(true);
            }
            catch (Exception error)
            {
                ErrorMessage = "The inquiry could not be unlinked: " + error.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void Dispose()
        {
            // Same rule as ReloadAsync: take the source out of the field first,
            // then cancel, then dispose — and never touch it again afterwards.
            var pending = _loadCts;
            _loadCts = null;
            if (pending != null)
            {
                try { pending.Cancel(); }
                catch (ObjectDisposedException) { /* already retired */ }
                pending.Dispose();
            }
            if (_ownsRepository && _repository is IDisposable disposable) disposable.Dispose();
        }
    }
}
