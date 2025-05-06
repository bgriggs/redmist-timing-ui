using Avalonia.Threading;
using BigMission.Avalonia.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
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
public partial class EventsListViewModel : ObservableObject
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
    }


    public async Task Initialize()
    {
        Message = string.Empty;
        IsLoading = true;
        try
        {
            var events = await eventClient.LoadRecentEventsAsync();
            if (events != null)
            {
                if (events.Count == 0)
                {
                    Message = "No events found";
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
                Message = "No events found - null";
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
        _ = Task.Run(Initialize);
    }
}
