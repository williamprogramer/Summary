using CommunityToolkit.Mvvm.ComponentModel;

namespace Summary.Services
{
    public partial class BusyService : ObservableObject
    {
        [ObservableProperty]
        public partial bool IsBusy { get; set; }
    }
}