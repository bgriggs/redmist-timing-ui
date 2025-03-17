using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using DynamicData;
using DynamicData.Binding;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Models;
using RedMist.TimingCommon.Models;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels;

public partial class ControlLogViewModel : ObservableObject, IRecipient<ControlLogNotification>
{
    public ObservableCollection<ControlLogEntryViewModel> ControlLog { get; } = [];
    protected readonly SourceCache<ControlLogEntryViewModel, string> logCache = new(ToKey);

    public Event EventModel { get; }
    private readonly HubClient hubClient;

    public string Name => EventModel.EventName;
    public string OrganizationName => EventModel.OrganizationName;
    public Bitmap? OrganizationLogo
    {
        get
        {
            if (EventModel.OrganizationLogo is not null)
            {
                using MemoryStream ms = new(EventModel.OrganizationLogo);
                return Bitmap.DecodeToWidth(ms, 55);
            }
            return null;
        }
    }


    public ControlLogViewModel(Event EventModel, HubClient hubClient)
    {
        WeakReferenceMessenger.Default.RegisterAll(this);
        this.EventModel = EventModel;
        this.hubClient = hubClient;

        logCache.Connect()
            .AutoRefresh(t => t.Timestamp)
            .SortAndBind(ControlLog, SortExpressionComparer<ControlLogEntryViewModel>.Descending(t => t.LogEntry.Timestamp))
            .DisposeMany()
            .Subscribe();
    }


    public void Receive(ControlLogNotification message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                foreach (var log in message.Value.ControlLogEntries)
                {
                    var logVm = logCache.Lookup(ToKey(log));
                    if (logVm == null)
                    {
                        var vm = new ControlLogEntryViewModel(log);
                        logCache.AddOrUpdate(vm);
                    }
                    else
                    {
                        logVm.Value.ApplyChanges(log);
                    }
                }

                // Remove logs not in entries
                foreach (var k in logCache.Keys)
                {
                    if (!message.Value.ControlLogEntries.Any(e => ToKey(e) == k))
                    {
                        logCache.RemoveKey(k);
                    }
                }
            }
            catch { }
        }, DispatcherPriority.Background);
    }

    public void Back()
    {
        var routerEvent = new RouterEvent { Path = "EventsList" };
        WeakReferenceMessenger.Default.Send(new ValueChangedMessage<RouterEvent>(routerEvent));
    }

    public async Task SubscribeToControlLogs()
    {
        await hubClient.SubscribeToControlLogs(EventModel.EventId);
    }

    public async Task UnsubscribeFromControlLogs()
    {
        await hubClient.UnsubscribeFromControlLogs(EventModel.EventId);
    }

    private static string ToKey(ControlLogEntryViewModel entry)
    {
        return entry.LogEntry.Timestamp.ToString() + entry.Note;
    }

    private static string ToKey(ControlLogEntry entry)
    {
        return entry.Timestamp.ToString() + entry.Note;
    }
}
