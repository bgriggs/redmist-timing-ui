using Avalonia.Threading;
using BigMission.Avalonia.Utilities;
using BigMission.Avalonia.Utilities.Extensions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using MessagePack;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Services;
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
    private readonly OrganizationIconCacheService iconCacheService;

    private ILogger Logger { get; }

    public LargeObservableCollection<EventViewModel> Events { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageTitle), nameof(ToggleButtonText))]
    private bool liveAndUpcomingEventsShown = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string message = string.Empty;

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    public string PageTitle => LiveAndUpcomingEventsShown ? "Latest Events" : "Older Events";

    public string ToggleButtonText => LiveAndUpcomingEventsShown ? "Older Events" : "Latest Events";

    public static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? string.Empty;

    [ObservableProperty]
    private bool isLoading = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreviousPage), nameof(HasNextPage), nameof(DisplayPageNumber))]
    private int currentPage = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNextPage))]
    private bool hasMorePages = false;

    public bool HasPreviousPage => CurrentPage > 0;

    public bool HasNextPage => HasMorePages;

    public int DisplayPageNumber => CurrentPage + 1;

    private const int PageSize = 25;

    /// <summary>
    /// The events currently on screen, encoded, or null when nothing is displayed.
    /// </summary>
    /// <remarks>
    /// Compared against a freshly loaded list to decide whether the view has to be rebuilt at all.
    /// The whole summary is encoded rather than the fields the rows happen to bind today, because a
    /// hand-written comparison is a list that has to be kept in step with the model - and a field it
    /// falls behind on does not fail, it just stops being noticed, so an event quietly stops
    /// updating. This is the same encoding the summaries arrive in, so it costs nothing to trust.
    /// </remarks>
    private byte[]? displayedEvents;

    /// <summary>Set while a quiet reload is running, so the same list is not fetched twice.</summary>
    private bool reloadInFlight;

    /// <summary>
    /// Set when a reload the user asked for was turned away by <see cref="reloadInFlight"/>.
    /// </summary>
    /// <remarks>
    /// The guard exists for the two reloads that fire together when leaving an event, and dropping
    /// one of those costs nothing - they want the same list. A refresh press is different: it is the
    /// one reload somebody is waiting on, and it draws no spinner, so dropping it silently means the
    /// button did nothing at all. Remembered and served when the reload holding the guard finishes.
    /// </remarks>
    private bool reloadOwed;


    public EventsListViewModel(EventClient eventClient, OrganizationClient organizationClient, OrganizationIconCacheService iconCacheService, ILoggerFactory loggerFactory)
    {
        this.eventClient = eventClient;
        this.organizationClient = organizationClient;
        this.iconCacheService = iconCacheService;
        Logger = loggerFactory.CreateLogger(GetType().Name);
        WeakReferenceMessenger.Default.RegisterAll(this);
    }


    public async Task InitializeAsync()
    {
        // Most callers kick this off from a background task. Hop to the UI thread once up front so
        // every bound property write below lands there; the awaits are I/O and resume on the UI
        // thread through the dispatcher's synchronization context.
        if (!Dispatcher.UIThread.CheckAccess())
        {
            await Dispatcher.UIThread.InvokeAsync(InitializeAsync);
            return;
        }

        Message = string.Empty;
        IsLoading = true;
        Events.Clear();
        displayedEvents = null;
        try
        {
            var fetched = await FetchEventsAsync();
            if (fetched.HasMorePages is { } hasMore)
            {
                HasMorePages = hasMore;
            }

            var events = fetched.Events;
            if (events != null)
            {
                if (events.Count == 0)
                {
                    Message = "No events found. Try to refresh in a moment.";
                    Logger.LogInformation(Message);
                }
                else
                {
                    Display(OrderForDisplay(events));
                }
            }
            else
            {
                // Null is the retry helper reporting that it gave up, which is not the same thing as
                // the schedule being empty and must not read like it. It has already logged the
                // reason at error level, so this only has to be honest with the person holding the
                // phone: something is wrong, and refreshing is worth a try.
                Message = "Could not reach the timing server. Check your connection and try again.";
                Logger.LogInformation("Events could not be loaded; the request was given up on.");
            }
        }
        catch (Exception ex)
        {
            // Was the whole exception, ToString and all, printed into the page - stack frames and
            // any internal detail the message happened to carry. That belongs in the log, which is
            // already getting it on the next line and forwarding it to Sentry.
            Message = "Could not load events. Check your connection and try again.";
#if DEBUG
            // Otherwise unreachable on a device: a debug build has no Sentry DSN, so the detail only
            // reaches the in-app log and adb.
            Message += $"\n\nDebug info: {ex.Message}";
#endif
            Logger.LogError(ex, "Error loading events");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Reloads the list without disturbing it, and puts the result on screen only if it differs from
    /// what is already there.
    /// </summary>
    /// <remarks>
    /// <see cref="InitializeAsync"/> empties the list and raises <see cref="IsLoading"/>, and the
    /// view hides the whole list behind "Loading..." while that is set. For a first load that is
    /// right. For a reload it means the schedule blinks out and rebuilds itself every time the app
    /// comes back to the foreground or the refresh button is pressed - usually to arrive at exactly
    /// the list that was already there, since a schedule changes far more slowly than people look
    /// at it. Rebuilding also drops every row's organization logo until the icon pass puts it back,
    /// so the blink is a rebuild of the whole screen rather than a redraw.
    ///
    /// This asks the server first and touches the view second. Nothing changes on screen until
    /// there is something to change it to.
    /// </remarks>
    /// <param name="userRequested">
    /// True for a reload somebody pressed a button for. Those are remembered rather than dropped
    /// when one is already running; see <see cref="reloadOwed"/>.
    /// </param>
    public async Task RefreshIfChangedAsync(bool userRequested = false)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            await Dispatcher.UIThread.InvokeAsync(() => RefreshIfChangedAsync(userRequested));
            return;
        }

        // Leaving an event asks for this twice: the router runs it as it navigates back, and the
        // view runs it again as it loads. Both want the same list, so the second is a duplicate
        // request for it. Only ever contended on the UI thread, which is the one thread this runs
        // on - the contention is re-entrancy across an await, not two threads.
        if (reloadInFlight)
        {
            reloadOwed |= userRequested;
            return;
        }
        reloadInFlight = true;

        try
        {
            do
            {
                reloadOwed = false;
                await ReloadOnceAsync();
            }
            while (reloadOwed);
        }
        finally
        {
            reloadInFlight = false;
        }
    }

    /// <summary>One pass of the quiet reload. See <see cref="RefreshIfChangedAsync"/>.</summary>
    private async Task ReloadOnceAsync()
    {
        {
            // Nothing on screen to compare against: the first load, or one that failed and left the
            // message explaining why. That case wants the ordinary path, spinner and all.
            if (displayedEvents is null || Events.Count == 0)
            {
                await InitializeAsync();
                return;
            }

            // What was asked for, so the answer can be thrown away if it is no longer the question.
            // Toggling to the archive or paging while a reload is in flight changes both, and those
            // paths load the list themselves - so a late answer here would be ordered by the new
            // mode's rules, compared against the new mode's list, and painted over it.
            var askedForLive = LiveAndUpcomingEventsShown;
            var askedForPage = CurrentPage;

            FetchResult fetched;
            try
            {
                fetched = await FetchEventsAsync();
            }
            catch (Exception ex)
            {
                // A quiet reload fails quietly. There is a good list on screen, and replacing it
                // with an error because a check the user did not watch could not reach the server is
                // a downgrade of what they can see. The retry helper has already logged the cause.
                Logger.LogWarning(ex, "Events reload failed; leaving the displayed list alone");
                return;
            }

            if (askedForLive != LiveAndUpcomingEventsShown || askedForPage != CurrentPage)
            {
                Logger.LogInformation("Events reload answered a question that has moved on; discarding it");
                return;
            }

            // Null is the request being given up on. Empty is harder: the server cannot tell "no
            // events" apart from a body that failed to arrive, since the client turns both into an
            // empty list - so while a populated list is on screen, empty is treated as the failure
            // it more often is. The cost is that a schedule that genuinely empties keeps its last
            // events until the mode is toggled or the app restarted.
            if (fetched.Events is null || fetched.Events.Count == 0)
            {
                Logger.LogInformation("Events reload brought back nothing; leaving the displayed list alone");
                return;
            }

            // Only now, having decided the answer is worth trusting. Applied from inside the fetch
            // it would take the Next button away over a full page of results the reload then
            // declined to replace.
            if (fetched.HasMorePages is { } hasMore)
            {
                HasMorePages = hasMore;
            }

            var ordered = OrderForDisplay(fetched.Events);
            if (Encode(ordered).AsSpan().SequenceEqual(displayedEvents))
            {
                return;
            }

            Logger.LogInformation("Events changed; rebuilding the list");
            Display(ordered);
        }
    }

    /// <summary>
    /// What one fetch found.
    /// </summary>
    /// <param name="Events">The events, or null if the request was given up on.</param>
    /// <param name="HasMorePages">
    /// Whether the archive has another page, or null when this fetch has nothing to say about it -
    /// either because it is a live query, or because it failed. Reported rather than applied, so a
    /// caller that decides not to trust the result does not act on half of it anyway.
    /// </param>
    private readonly record struct FetchResult(List<EventListSummary>? Events, bool? HasMorePages);

    /// <summary>
    /// Asks the server for the events the current mode calls for, in the order it returns them.
    /// </summary>
    private async Task<FetchResult> FetchEventsAsync()
    {
        if (LiveAndUpcomingEventsShown)
        {
            var live = await eventClient.ExecuteWithRetryAsync(eventClient.LoadRecentEventsAsync,
                nameof(eventClient.LoadRecentEventsAsync), maxRetries: 5);
            return new FetchResult(live, HasMorePages: null);
        }

        // Load one extra event to determine if there are more pages
        var events = await eventClient.ExecuteWithRetryAsync(() => eventClient.LoadArchivedEventsAsync(CurrentPage * PageSize, PageSize + 1),
            nameof(eventClient.LoadArchivedEventsAsync), maxRetries: 5);

        // Check if there are more pages
        if (events is null)
        {
            // Nothing said, on purpose. Null is the request being given up on, and reading
            // that as "there are no more pages" would take the Next button away on a network
            // blip - a failure mistaken for the end of the archive, which is the same error
            // as mistaking one for an empty schedule.
            return new FetchResult(null, HasMorePages: null);
        }

        if (events.Count > PageSize)
        {
            events.RemoveAt(events.Count - 1); // Remove the extra event
            return new FetchResult(events, HasMorePages: true);
        }

        return new FetchResult(events, HasMorePages: false);
    }

    /// <summary>
    /// Puts the events in the order the list shows them: live first, then newest first.
    /// </summary>
    internal List<EventListSummary> OrderForDisplay(List<EventListSummary> events)
    {
        // TryParse rather than Parse: on the quiet path this runs outside the try that guards the
        // fetch, so a malformed date would escape the reload entirely. An unparseable one sorts to
        // the bottom instead, which is also better than the whole list failing to load.
        static DateTime Date(EventListSummary e) =>
            DateTime.TryParseExact(e.EventDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d
                : DateTime.MinValue;

        if (!LiveAndUpcomingEventsShown)
        {
            // For archived events, just order by date
            return [.. events.OrderByDescending(Date)];
        }

        // Order the live events at the top
        return [.. events.Where(e => e.IsLive).OrderByDescending(Date), .. events.Where(e => !e.IsLive).OrderByDescending(Date)];
    }

    /// <summary>
    /// Rebuilds the list from <paramref name="ordered"/> and records what is now on screen.
    /// </summary>
    private void Display(List<EventListSummary> ordered)
    {
        var vms = new List<EventViewModel>(ordered.Count);
        foreach (var e in ordered)
        {
            vms.Add(new EventViewModel(e, []));
        }

        Message = string.Empty;

        // Display events immediately
        Events.SetRange(vms);

        // Recorded after the rows are up, so this always describes what is actually on screen. The
        // other order looks equivalent and is not: if painting threw, the fingerprint would already
        // claim to describe rows that were never put up, and every later reload would compare equal
        // and return - a list frozen wrong with nothing left to repair it.
        displayedEvents = Encode(ordered);

        // Load icons asynchronously in the background using the cache service
        var orgIds = ordered.Select(e => e.OrganizationId).Distinct().ToArray();
        _ = LoadOrganizationIconsAsync(orgIds, vms);
    }

    /// <summary>
    /// Encodes an ordered list of events so two of them can be compared.
    /// </summary>
    /// <remarks>
    /// Order is part of what is compared, because it is part of what is displayed. Two loads that
    /// differ only in how the server happened to order events sharing a date will read as a change
    /// and rebuild the list, which is what happened on every reload before this existed.
    ///
    /// Today's date is part of it too, and not because the server sent it. A row shows the sessions
    /// belonging to the current day, and <see cref="EventViewModel"/> works that out once, from the
    /// clock, when it is built. Rebuilding on every reload used to recompute that by accident; not
    /// rebuilding would leave a phone left open overnight showing yesterday's sessions all through
    /// the next day - because the schedule the server sends has not changed, which is exactly when
    /// this comparison says there is nothing to do. Folding the date in makes the day turning over
    /// count as a change, at a cost of one rebuild a day.
    /// </remarks>
    private static byte[] Encode(List<EventListSummary> events) => Encode(events, DateTime.Now.Date);

    /// <param name="today">
    /// The day the rows would be built for. A parameter rather than read in here so the date
    /// sensitivity above can be shown rather than asserted.
    /// </param>
    internal static byte[] Encode(List<EventListSummary> events, DateTime today)
    {
        var day = BitConverter.GetBytes(today.Ticks);
        var payload = MessagePackSerializer.Serialize(events);

        var encoded = new byte[day.Length + payload.Length];
        day.CopyTo(encoded, 0);
        payload.CopyTo(encoded, day.Length);
        return encoded;
    }

    private async Task LoadOrganizationIconsAsync(int[] organizationIds, List<EventViewModel> eventViewModels)
    {
        try
        {
            // Preload all icons in parallel using the cache service
            await iconCacheService.PreloadIconsAsync(organizationIds);

            // Update all event view models with their cached icons
            foreach (var vm in eventViewModels)
            {
                var icon = iconCacheService.GetCachedIcon(vm.OrganizationId);
                if (icon != null)
                {
                    Dispatcher.UIThread.InvokeOnUIThread(() => vm.UpdateIcon(icon));
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to load organization icons");
        }
    }

    [RelayCommand]
    public void RefreshEvents()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                // Quiet on purpose: a reload that finds the same schedule leaves the screen exactly
                // as it is rather than blinking it out and rebuilding it to look identical.
                await RefreshIfChangedAsync(userRequested: true);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error refreshing events");
            }
        });
    }

    /// <summary>
    /// Handle case where the app was in the background not getting updates and now becomes active again.
    /// </summary>
    /// <remarks>
    /// The most common reload by far, and the one that used to be most visible: coming back to the
    /// app emptied the schedule and rebuilt it, almost always into the list that was already there.
    /// </remarks>
    public async void Receive(AppResumeNotification message)
    {
        try
        {
            await RefreshIfChangedAsync();
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
                await InitializeAsync();
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
                await InitializeAsync();
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
                await InitializeAsync();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error loading previous page");
            }
        });
    }
}
