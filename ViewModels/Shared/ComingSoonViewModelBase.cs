using ProGlassAutomation.ViewModels;

namespace ProGlassAutomation.ViewModels.Shared
{
    public abstract class ComingSoonViewModelBase : ViewModelBase
    {
        public abstract string ModuleName { get; }
        public abstract string Description { get; }
        public abstract string Icon { get; }
    }
}