using Avalonia.Media.Imaging;
using Avalonia.Threading;
using BigMission.Avalonia.Utilities.Extensions;
using BigMission.Shared.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

public partial class ControlLogViewModel : ObservableObject, IRecipient<ControlLogNotification>, IRecipient<AppResumeNotification>
{
    public ObservableCollection<ControlLogEntryViewModel> ControlLog { get; } = [];
    public bool HasNoControlLog => ControlLog.Count == 0;
    protected readonly SourceCache<ControlLogEntryViewModel, string> logCache = new(ToKey);
    private readonly Debouncer debouncer = new(TimeSpan.FromSeconds(1));

    public Event EventModel { get; }
    private readonly HubClient hubClient;
    private readonly EventClient eventClient;
    private readonly EventContext eventContext;

    public string Name => EventModel.EventName;
    public string OrganizationName => EventModel.OrganizationName;
    public Bitmap? OrganizationLogo
    {
        get
        {
            if (EventModel.OrganizationLogo is not null && EventModel.OrganizationLogo.Length > 0)
            {
                using MemoryStream ms = new(EventModel.OrganizationLogo);
                // Decode at 2x-3x the display size for crisp rendering on high-DPI screens
                return Bitmap.DecodeToWidth(ms, 165);
            }
            return null;
        }
    }

    [ObservableProperty]
    private bool allowEventList = true;
    [ObservableProperty]
    private bool isLoading = false;


    public ControlLogViewModel(Event eventModel, HubClient hubClient, EventClient eventClient, EventContext eventContext)
    {
        EventModel = eventModel;
        this.hubClient = hubClient;
        this.eventClient = eventClient;
        this.eventContext = eventContext;

        logCache.Connect()
            .AutoRefresh(t => t.Timestamp)
            .SortAndBind(ControlLog, SortExpressionComparer<ControlLogEntryViewModel>.Descending(t => t.LogEntry.Timestamp))
            .DisposeMany()
            .Subscribe();

        // Notify when collection changes
        ControlLog.CollectionChanged += (_, __) => OnPropertyChanged(nameof(HasNoControlLog));

        WeakReferenceMessenger.Default.RegisterAll(this);
    }


    public void Receive(ControlLogNotification message)
    {
        _ = debouncer.ExecuteAsync(() => ProcessControlLogs(message));
    }

    /// <summary>
    /// Handle chase where the app was in the background not getting updates and now becomes active again.
    /// </summary>
    public async void Receive(AppResumeNotification message)
    {
        try
        {
            await Initialize();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in AppResumeNotification handler: {ex}");
        }
    }

    private Task ProcessControlLogs(ControlLogNotification message)
    {
        Dispatcher.UIThread.InvokeOnUIThread(() =>
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
        return Task.CompletedTask;
    }

    [RelayCommand]
    public void Back()
    {
        var routerEvent = new RouterEvent { Path = "EventsList" };
        WeakReferenceMessenger.Default.Send(new ValueChangedMessage<RouterEvent>(routerEvent));
    }

    public async Task Initialize()
    {
        Dispatcher.UIThread.InvokeOnUIThread(() => IsLoading = true);
        try
        {
            // First see if there are any logs saved/finalized as the session may be over
            var controlLogEntries = await eventClient.LoadSessionHistoricalControlLogAsync(EventModel.EventId, eventContext.SessionId);
            if (controlLogEntries.Count == 0 && !EventModel.IsArchived)
            {
                // If no finalized logs, try to load live logs
                controlLogEntries = await eventClient.LoadControlLogAsync(EventModel.EventId);
            }

            await ProcessControlLogs(new ControlLogNotification(new CarControlLogs { ControlLogEntries = controlLogEntries }));
            await hubClient.SubscribeToControlLogsAsync(EventModel.EventId);
        }
        catch (Exception)
        {
            // Handle exceptions
        }
        finally
        {
            Dispatcher.UIThread.InvokeOnUIThread(() => IsLoading = false);
        }
    }

    public async Task UnsubscribeFromControlLogs()
    {
        try
        {
            await hubClient.UnsubscribeFromControlLogsAsync(EventModel.EventId);
        }
        catch (Exception)
        {
            // Handle exceptions
        }
    }

    private static string ToKey(ControlLogEntryViewModel entry)
    {
        return (entry?.LogEntry?.Timestamp.ToString() ?? string.Empty) + (entry?.Note ?? string.Empty);
    }

    private static string ToKey(ControlLogEntry entry)
    {
        return (entry?.Timestamp.ToString() ?? string.Empty) + (entry?.Note ?? string.Empty);
    }
}
