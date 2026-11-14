// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/ProformaInvoice/ProformaInvoiceNavigator.cs
// PROJECT: ProGlassAutomation
// MODULE: Proforma Invoice — Wizard navigation logic
//
// THE PROBLEM THIS SOLVES
//   ProformaInvoiceViewModel was 797 lines with navigation, inquiry
//   integration, persistence, and step synchronization all mixed together.
//   This class extracts the navigation logic so the main VM can focus
//   on orchestration.
//
// CONTRACT
//   • Manages WizardSteps collection and CurrentStep property
//   • Tracks furthestStepReached for progress indicators
//   • Fires navigation events for step-specific logic
//   • No WPF dependencies — pure navigation state machine
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.ObjectModel;
using System.Linq;
using ProGlassAutomation.ViewModels.ProformaInvoice;

namespace ProGlassAutomation.Services.ProformaInvoice
{
    /// <summary>
    /// Event args for step navigation events.
    /// </summary>
    public sealed class StepNavigationEventArgs : EventArgs
    {
        public int StepNumber { get; init; }
        public int PreviousStepNumber { get; init; }
        public bool IsForward => StepNumber > PreviousStepNumber;
    }

    /// <summary>
    /// Manages wizard navigation state: current step, furthest reached,
    /// and step completion tracking. Extracted from ProformaInvoiceViewModel
    /// to improve testability and separation of concerns.
    /// </summary>
    public sealed class ProformaInvoiceNavigator
    {
        private readonly ObservableCollection<WizardStep> _steps;
        private WizardStep _currentStep;
        private int _furthestStepReached = 1;

        /// <summary>
        /// Fired when the current step changes. Subscribers can perform
        /// step-specific initialization (e.g., load data, refresh UI).
        /// </summary>
        public event EventHandler<StepNavigationEventArgs>? StepChanged;

        /// <summary>
        /// The collection of wizard steps. Typically bound to the UI.
        /// </summary>
        public ObservableCollection<WizardStep> Steps => _steps;

        /// <summary>
        /// The currently active step.
        /// </summary>
        public WizardStep CurrentStep => _currentStep;

        /// <summary>
        /// The furthest step the user has reached in this session.
        /// Used for progress indicators and step completion tracking.
        /// </summary>
        public int FurthestStepReached => _furthestStepReached;

        /// <summary>
        /// The 1-based index of the current step.
        /// </summary>
        public int CurrentStepNumber => _currentStep?.StepNumber ?? 1;

        /// <summary>
        /// Whether the user can navigate backward.
        /// </summary>
        public bool CanGoBack => CurrentStepNumber > 1;

        /// <summary>
        /// Whether the user can navigate forward.
        /// </summary>
        public bool CanGoForward => CurrentStepNumber < _steps.Count;

        /// <summary>
        /// Whether the current step is the last step.
        /// </summary>
        public bool IsLastStep => CurrentStepNumber == _steps.Count;

        public ProformaInvoiceNavigator(ObservableCollection<WizardStep> steps)
        {
            _steps = steps ?? throw new ArgumentNullException(nameof(steps));
            _currentStep = steps.FirstOrDefault()
                ?? throw new ArgumentException("Steps collection cannot be empty", nameof(steps));
        }

        /// <summary>
        /// Navigates to the specified step number. Updates furthestStepReached
        /// and fires the StepChanged event.
        /// </summary>
        public void GoToStep(int stepNumber)
        {
            var step = _steps.FirstOrDefault(s => s.StepNumber == stepNumber);
            if (step == null) return;

            int previousStepNumber = CurrentStepNumber;
            _currentStep = step;
            _furthestStepReached = Math.Max(_furthestStepReached, stepNumber);

            // Update step completion indicators
            foreach (var ws in _steps)
                ws.IsCompleted = ws.StepNumber < _furthestStepReached;

            StepChanged?.Invoke(this, new StepNavigationEventArgs
            {
                StepNumber = stepNumber,
                PreviousStepNumber = previousStepNumber
            });
        }

        /// <summary>
        /// Navigates to the next step if possible.
        /// </summary>
        public void GoForward()
        {
            if (CanGoForward)
                GoToStep(CurrentStepNumber + 1);
        }

        /// <summary>
        /// Navigates to the previous step if possible.
        /// </summary>
        public void GoBack()
        {
            if (CanGoBack)
                GoToStep(CurrentStepNumber - 1);
        }

        /// <summary>
        /// Resets the navigator to the first step and clears progress.
        /// Used when starting a new invoice.
        /// </summary>
        public void Reset()
        {
            _furthestStepReached = 1;
            GoToStep(1);
            foreach (var ws in _steps)
                ws.IsCompleted = false;
        }

        /// <summary>
        /// Restores the furthestStepReached from a saved state.
        /// Used when loading an existing invoice.
        /// </summary>
        public void RestoreProgress(int furthestStep)
        {
            _furthestStepReached = Math.Max(1, furthestStep);
            GoToStep(1); // Always start at step 1 when loading
            foreach (var ws in _steps)
                ws.IsCompleted = ws.StepNumber < _furthestStepReached;
        }
    }
}
