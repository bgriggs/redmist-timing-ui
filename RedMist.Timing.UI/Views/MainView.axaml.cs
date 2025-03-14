using Avalonia.Controls;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.ViewModels;
using RedMist.TimingCommon.Models;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.Views;

public partial class MainView : UserControl, IRecipient<ValueChangedMessage<RouterEvent>>
{
    public MainView()
    {
        InitializeComponent();
        WeakReferenceMessenger.Default.RegisterAll(this);
    }

    public void Receive(ValueChangedMessage<RouterEvent> message)
    {
       
    }
}
