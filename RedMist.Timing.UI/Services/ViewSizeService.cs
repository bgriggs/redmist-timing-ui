using Avalonia;
using CommunityToolkit.Mvvm.Messaging;
using RedMist.Timing.UI.Models;

namespace RedMist.Timing.UI.Services;

public class ViewSizeService : IRecipient<SizeChangedNotification>
{
    public Size CurrentSize { get; private set; }


    public ViewSizeService()
    {
        WeakReferenceMessenger.Default.RegisterAll(this);
    }


    public void Receive(SizeChangedNotification message)
    {
        CurrentSize = message.Size;
    }
}
