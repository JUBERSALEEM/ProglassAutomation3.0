// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/ViewModels/InquiryViewModelBase.cs
// MODULE: Inquiry — INPC base for the module's view models.
//
// Deliberately small and self-contained: the module does not depend on
// the shell's theme-studio VM plumbing.
// ═══════════════════════════════════════════════════════════════════════

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ProGlassAutomation.ViewModels.Inquiry
{
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
