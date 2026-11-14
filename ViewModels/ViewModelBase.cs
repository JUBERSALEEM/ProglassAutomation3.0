using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ProGlassAutomation.ViewModels
{
    public class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Public wrapper for OnPropertyChanged so parent VMs / helpers can trigger
        /// property change notifications from outside the class (e.g., renumbering,
        /// bulk updates, cross-VM sync).
        /// </summary>
        public void OnPropertyChangedPublic(string propertyName)
        {
            OnPropertyChanged(propertyName);
        }
    }
}