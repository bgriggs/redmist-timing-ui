using Avalonia.Threading;
using BigMission.Avalonia.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels;

/// <summary>
/// Available events to select from.
/// </summary>
public partial class EventsListViewModel : ObservableObject, IRecipient<AppResumeNotification>
{
    private readonly EventClient eventClient;
    private readonly OrganizationClient organizationClient;

    private ILogger Logger { get; }

    public LargeObservableCollection<EventViewModel> Events { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string message = string.Empty;

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    public static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? string.Empty;

    [ObservableProperty]
    private bool isLoading = false;


    public EventsListViewModel(EventClient eventClient, OrganizationClient organizationClient, ILoggerFactory loggerFactory)
    {
        this.eventClient = eventClient;
        this.organizationClient = organizationClient;
        Logger = loggerFactory.CreateLogger(GetType().Name);
        WeakReferenceMessenger.Default.RegisterAll(this);
    }


    public async Task Initialize()
    {
        Message = string.Empty;
        IsLoading = true;
        try
        {
            var events = await eventClient.ExecuteWithRetryAsync(eventClient.LoadRecentEventsAsync, 
                nameof(eventClient.LoadRecentEventsAsync), maxRetries: 5);
            if (events != null)
            {
                if (events.Count == 0)
                {
                    Message = "No events found. Try to refresh in a moment.";
                    Logger.LogInformation(Message);
                }
                else
                {
                    // Load icons for organizations
                    var iconTasks = new Dictionary<int, Task<byte[]>>();
                    var orgIds = events.Select(e => e.OrganizationId).Distinct();
                    foreach (var orgId in orgIds)
                    {
                        iconTasks[orgId] = organizationClient.GetOrganizationIconAsync(orgId);
                    }

                    await Task.WhenAll(iconTasks.Values);
                    var orgIconLookup = new Dictionary<int, byte[]>();
                    foreach (var ot in iconTasks)
                    {
                        var orgId = ot.Key;
                        var icon = ot.Value.Result;
                        if (icon != null)
                        {
                            orgIconLookup[orgId] = icon;
                        }
                    }

                    // Order the live events at the top
                    var vms = new List<EventViewModel>();
                    foreach (var e in events.Where(e => e.IsLive).OrderByDescending(e => DateTime.ParseExact(e.EventDate, "yyyy-MM-dd", CultureInfo.InvariantCulture)))
                    {
                        vms.Add(new EventViewModel(e, orgIconLookup[e.OrganizationId]));
                    }
                    foreach (var e in events.Where(e => !e.IsLive).OrderByDescending(e => DateTime.ParseExact(e.EventDate, "yyyy-MM-dd", CultureInfo.InvariantCulture)))
                    {
                        vms.Add(new EventViewModel(e, orgIconLookup[e.OrganizationId]));
                    }

                    Dispatcher.UIThread.Post(() => Events.SetRange(vms));
                }
            }
            else
            {
                Message = "No events found - try to refresh in a moment.";
                Logger.LogInformation(Message);
            }
        }
        catch (Exception ex)
        {
            Message = $"Error loading events: {ex}";
            Logger.LogError(ex, "Error loading events");
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void RefreshEvents()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Initialize();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error refreshing events: {ex}");
            }
        });
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
            System.Diagnostics.Debug.WriteLine($"Error in AppResumeNotification handler for EventsListViewModel: {ex}");
        }
    }

    public void SetDriverMode()
    {
        var routerEvent = new RouterEvent { Path = "InCarDriverSettings" };
        WeakReferenceMessenger.Default.Send(new ValueChangedMessage<RouterEvent>(routerEvent));
    }
}
