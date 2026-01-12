using Avalonia.Threading;
using BigMission.Avalonia.Utilities;
using BigMission.Avalonia.Utilities.Extensions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Models;
using RedMist.TimingCommon.Models;
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
    [NotifyPropertyChangedFor(nameof(PageTitle), nameof(ToggleButtonText))]
    private bool liveAndUpcomingEventsShown = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string message = string.Empty;

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    public string PageTitle => LiveAndUpcomingEventsShown ? "Live and Upcoming" : "Completed Events";

    public string ToggleButtonText => LiveAndUpcomingEventsShown ? "Completed Events" : "Live and Upcoming";

    public static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? string.Empty;

    [ObservableProperty]
    private bool isLoading = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreviousPage), nameof(HasNextPage), nameof(DisplayPageNumber))]
    private int currentPage = 0;

    [ObservableProperty]
    private bool hasMorePages = false;

    public bool HasPreviousPage => CurrentPage > 0;

    public bool HasNextPage => HasMorePages;

    public int DisplayPageNumber => CurrentPage + 1;

    private const int PageSize = 25;


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
        Dispatcher.UIThread.InvokeOnUIThread(Events.Clear);
        try
        {
            List<EventListSummary>? events;

            if (LiveAndUpcomingEventsShown)
            {
                events = await eventClient.ExecuteWithRetryAsync(eventClient.LoadRecentEventsAsync,
                    nameof(eventClient.LoadRecentEventsAsync), maxRetries: 5);
            }
            else
            {
                // Load one extra event to determine if there are more pages
                events = await eventClient.ExecuteWithRetryAsync(() => eventClient.LoadArchivedEventsAsync(CurrentPage * PageSize, PageSize + 1),
                    nameof(eventClient.LoadArchivedEventsAsync), maxRetries: 5);

                // Check if there are more pages
                if (events != null && events.Count > PageSize)
                {
                    HasMorePages = true;
                    events.RemoveAt(events.Count - 1); // Remove the extra event
                }
                else
                {
                    HasMorePages = false;
                }
            }

            if (events != null)
            {
                if (events.Count == 0)
                {
                    Message = "No events found. Try to refresh in a moment.";
                    Logger.LogInformation(Message);
                }
                else
                {
                    var vms = new List<EventViewModel>();

                    if (LiveAndUpcomingEventsShown)
                    {
                        // Order the live events at the top and create ViewModels without icons initially
                        foreach (var e in events.Where(e => e.IsLive).OrderByDescending(e => DateTime.ParseExact(e.EventDate, "yyyy-MM-dd", CultureInfo.InvariantCulture)))
                        {
                            vms.Add(new EventViewModel(e, []));
                        }
                        foreach (var e in events.Where(e => !e.IsLive).OrderByDescending(e => DateTime.ParseExact(e.EventDate, "yyyy-MM-dd", CultureInfo.InvariantCulture)))
                        {
                            vms.Add(new EventViewModel(e, []));
                        }
                    }
                    else
                    {
                        // For archived events, just order by date
                        foreach (var e in events.OrderByDescending(e => DateTime.ParseExact(e.EventDate, "yyyy-MM-dd", CultureInfo.InvariantCulture)))
                        {
                            vms.Add(new EventViewModel(e, []));
                        }
                    }

                    // Display events immediately
                    Dispatcher.UIThread.InvokeOnUIThread(() => Events.SetRange(vms));

                    // Load icons asynchronously in the background
                    var orgIds = events.Select(e => e.OrganizationId).Distinct();
                    foreach (var orgId in orgIds)
                    {
                        _ = LoadOrganizationIconAsync(orgId, vms);
                    }
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

    private async Task LoadOrganizationIconAsync(int organizationId, List<EventViewModel> eventViewModels)
    {
        try
        {
            var icon = await organizationClient.GetOrganizationIconCdnAsync(organizationId);
            if (icon != null && icon.Length > 0)
            {
                // Update all events with this organization's icon
                var eventsToUpdate = eventViewModels.Where(vm => vm.OrganizationId == organizationId);
                foreach (var eventVm in eventsToUpdate)
                {
                    Dispatcher.UIThread.InvokeOnUIThread(() => eventVm.UpdateIcon(icon));
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to load icon for organization {OrganizationId}", organizationId);
        }
    }

    [RelayCommand]
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
                Logger.LogError(ex, "Error refreshing events");
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
            Logger.LogError(ex, "Error in AppResumeNotification handler for EventsListViewModel");
        }
    }

    [RelayCommand]
    public void SetDriverMode()
    {
        var routerEvent = new RouterEvent { Path = "InCarDriverSettings" };
        WeakReferenceMessenger.Default.Send(new ValueChangedMessage<RouterEvent>(routerEvent));
    }

    [RelayCommand]
    public void ToggleLiveArchive()
    {
        LiveAndUpcomingEventsShown = !LiveAndUpcomingEventsShown;
        CurrentPage = 0; // Reset to first page when toggling
        _ = Task.Run(async () =>
        {
            try
            {
                await Initialize();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error toggling live/archive events");
            }
        });
    }

    [RelayCommand]
    public void NextPage()
    {
        if (!HasNextPage)
            return;

        CurrentPage++;
        _ = Task.Run(async () =>
        {
            try
            {
                await Initialize();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error loading next page");
            }
        });
    }

    [RelayCommand]
    public void PreviousPage()
    {
        if (!HasPreviousPage)
            return;

        CurrentPage--;
        _ = Task.Run(async () =>
        {
            try
            {
                await Initialize();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error loading previous page");
            }
        });
    }
}
