using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Extensions;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Services;
using RedMist.Timing.UI.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels;

/// <summary>
/// Sharing the standings, as a link or as a drawn card.
/// </summary>
/// <remarks>
/// A separate file rather than a separate type: this needs the event, the session, the grouping and
/// the rows, which is most of what the grid is, and threading all of that through a collaborator
/// would cost more than it explains. The grid proper is long enough already.
/// </remarks>
public partial class LiveTimingViewModel : ICarShareHost
{
    /// <summary>
    /// The session this grid is pinned to, or null while it is following the event live.
    /// </summary>
    /// <remarks>
    /// Null - not zero - is what means "no session". The timing feed emits run number 0, so session 0
    /// is a real session and <c>/timing/5/0</c> a real results page; a link built by testing the id
    /// for truthiness would silently become a live link. Set by <see cref="ResultsViewModel"/> when it
    /// opens a stored session, and left null for the live grid so the link keeps following the event
    /// as sessions change.
    /// </remarks>
    public int? PinnedSessionId { get; set; }

    /// <summary>
    /// The rows the viewer can actually see, in the order shown, or null when that cannot be told.
    /// </summary>
    /// <remarks>
    /// Supplied by the view, which is the only thing that knows where the scroll viewer is. Null falls
    /// back to <see cref="DisplayOrderCars"/>; see there for what that costs.
    /// </remarks>
    internal Func<IReadOnlyList<CarViewModel>?>? OnScreenCars { get; set; }

    /// <summary>Set while a share is being handed over, so a second tap cannot stack another on it.</summary>
    [ObservableProperty]
    private bool isSharing;

    /// <summary>The last thing a share did, shown briefly so no button appears to do nothing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShareStatusVisible))]
    private string shareStatus = string.Empty;

    public bool IsShareStatusVisible => !string.IsNullOrEmpty(ShareStatus);

    private IDisposable? shareStatusTimer;

    private static readonly TimeSpan ShareStatusDuration = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How long to wait for a head to say what became of a share before giving up on the answer.
    /// </summary>
    /// <remarks>
    /// Long, because the viewer is choosing a recipient in someone else's UI and a share sheet left
    /// open for a minute is ordinary. It exists for the case where the answer never comes at all:
    /// iOS completes the reply from <c>UIActivityViewController</c>'s completion handler, and UIKit
    /// can decline to present without ever calling it - a scene transition, or a controller already
    /// animating something else. Without this, <see cref="IsSharing"/> would then stay set for the
    /// rest of the session and every later share would be refused in silence.
    /// </remarks>
    private static readonly TimeSpan ShareReplyTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Whether this event may be shared.
    /// </summary>
    /// <remarks>
    /// A private event gets no share button and no share path: the organizer kept it off the public
    /// list, and the only link that would work for a recipient is one carrying the access code, which
    /// is not something to put in a URL destined for a Facebook group. That is a guarantee rather than
    /// a default, so every share entry point below re-checks it instead of trusting the button to have
    /// been hidden.
    ///
    /// Read off <see cref="EventModel"/>, and deliberately off nothing else, so that what may be
    /// shared is decided by the same value the header and the rows are drawn from. Every write to it
    /// is gated on the opening mark (see
    /// <see cref="InitializeLiveAsync(RedMist.TimingCommon.Models.Event, long)"/>), so a superseded
    /// load cannot replace a newer one.
    ///
    /// Two limits worth knowing, neither of which can share a private event:
    /// <list type="bullet">
    /// <item>It follows the last open that reached the UI thread, not the event the user last tapped.
    /// <c>MainViewModel.SetupForEventAsync</c> shows the timing tab synchronously while the open runs
    /// on the thread pool, so for that window the whole grid - header, rows and these buttons alike -
    /// is still the previous event, and sharing shares what is on screen.</item>
    /// <item>The flags are whatever the event load returned. Nothing re-reads them, so an event made
    /// private while someone is watching it goes on offering share until the app reopens it.</item>
    /// </list>
    /// </remarks>
    public bool CanShare => EventModel.EventId > 0 && !EventModel.IsPrivate && !EventModel.HideName;

    /// <summary>The session being viewed, or null while the grid is following the event live.</summary>
    private int? ViewedSessionId => IsRealTime ? null : PinnedSessionId;

    /// <summary>
    /// Where a shared link points. Configurable so a build aimed at a test site keeps its links there,
    /// which is what the web app gets for free by reading the address bar.
    /// </summary>
    private string ShareOrigin
    {
        get
        {
            var configured = configuration[ShareLinks.SiteUrlConfigurationKey];
            return string.IsNullOrWhiteSpace(configured) ? ShareLinks.DefaultOrigin : configured;
        }
    }

    public async Task ShareEventLink()
    {
        if (!CanShare || IsSharing)
        {
            return;
        }

        IsSharing = true;
        try
        {
            await HandOverLink(new ShareRequest(EventPayload()));
        }
        catch (Exception ex)
        {
            // Nothing awaits a command, so without this a fault building the payload would surface
            // only as an unobserved task exception - and to the viewer as a button that did nothing.
            Logger.LogError(ex, "Could not build an event link to share");
            Report("Could not share that link");
        }
        finally
        {
            IsSharing = false;
        }
    }

    /// <summary>
    /// Shares an image of the standings as they are on screen, rather than a link.
    /// </summary>
    /// <remarks>
    /// Two reasons this is an image and not a link. A link preview depends on the receiving app
    /// fetching the URL and reading tags out of HTML this client-rendered site never serves a crawler;
    /// an image is the message. And what makes a timing screenshot worth posting is the running order -
    /// which is why the card draws the rows the viewer can actually see rather than one car in
    /// isolation.
    /// </remarks>
    public Task ShareEventCard() => ShareCard(highlightCar: null);

    public async Task ShareCarLinkAsync(CarViewModel car)
    {
        if (!CanShare || car is null || IsSharing)
        {
            return;
        }

        IsSharing = true;
        try
        {
            await HandOverLink(new ShareRequest(CarPayload(car)));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Could not build a car link to share");
            Report("Could not share that link");
        }
        finally
        {
            IsSharing = false;
        }
    }

    /// <summary>The event's card, with the car it was shared from picked out.</summary>
    /// <remarks>
    /// A null car is refused rather than passed through, which would quietly share the event's card
    /// in place of the one that was asked for.
    /// </remarks>
    public Task ShareCarCardAsync(CarViewModel car)
        => car is null ? Task.CompletedTask : ShareCard(car);

    private async Task ShareCard(CarViewModel? highlightCar)
    {
        if (!CanShare || IsSharing)
        {
            return;
        }

        IsSharing = true;
        try
        {
            // Everything the card and its message need is read before the first await: on a live
            // session, positions patched mid-hand-over would otherwise leave a caption claiming P3
            // attached to a card showing P2. Inside the try with the drawing, because building a
            // payload out of free text from the feed is no less able to fail than drawing it.
            var highlightCarNumber = highlightCar?.Number;
            var data = CardData(highlightCarNumber);
            var payload = highlightCar is null ? EventPayload() : CarPayload(highlightCar);
            var fileName = ShareCardName.For(
                highlightCarNumber is null ? null : "car-" + highlightCarNumber,
                string.IsNullOrWhiteSpace(EventModel.EventName) ? "event" : EventModel.EventName);
            var card = StandingsCardRenderer.Render(data);

            await HandOverImage(new ShareImageRequest(payload, card, fileName));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Could not build a timing card to share");
            Report("Could not build that image");
        }
        finally
        {
            IsSharing = false;
        }
    }

    /// <summary>
    /// Hands a link to whichever head is listening, and says what became of it.
    /// </summary>
    /// <remarks>
    /// No recipient leaves the response unset, which is what a head with nothing wired up looks like.
    /// It is reported as a failure rather than passed over in silence, because a share button that
    /// appears to do nothing is the worst outcome available here.
    /// </remarks>
    private async Task HandOverLink(ShareRequest request)
    {
        var outcome = await Answer(request, () => WeakReferenceMessenger.Default.Send(request),
            "Error sharing a timing link");

        // Shared speaks for itself - the share sheet the viewer just used said so - and Dismissed is a
        // decision rather than a failure, so neither is reported.
        if (outcome == ShareOutcome.Copied)
        {
            Report("Link copied");
        }
        else if (outcome == ShareOutcome.Failed)
        {
            Report("Could not share that link");
        }
    }

    /// <inheritdoc cref="HandOverLink"/>
    private async Task HandOverImage(ShareImageRequest request)
    {
        var outcome = await Answer(request, () => WeakReferenceMessenger.Default.Send(request),
            "Error sharing a timing card");

        if (outcome == ShareOutcome.Copied)
        {
            Report("Image copied - paste it into your post");
        }
        else if (outcome == ShareOutcome.Saved)
        {
            Report("Image saved");
        }
        else if (outcome == ShareOutcome.Failed)
        {
            Report("Could not share that image");
        }
    }

    /// <summary>
    /// Sends <paramref name="request"/> and waits for the head to say what became of it.
    /// </summary>
    /// <param name="send">
    /// Done by the caller so the messenger dispatches on the concrete message type rather than on a
    /// type parameter.
    /// </param>
    private async Task<ShareOutcome> Answer(
        CommunityToolkit.Mvvm.Messaging.Messages.AsyncRequestMessage<ShareOutcome> request,
        Action send, string errorMessage)
    {
        try
        {
            send();
            if (!request.HasReceivedResponse)
            {
                return ShareOutcome.Failed;
            }

            return await request.Response.WaitAsync(ShareReplyTimeout);
        }
        catch (TimeoutException)
        {
            // The share may well be going fine - the sheet can still be open - so this says nothing
            // to the viewer. It is here to release the guard, not to report.
            Logger.LogWarning("A share was still unanswered after {Timeout}; giving up on the outcome",
                ShareReplyTimeout);
            return ShareOutcome.Dismissed;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "{Message}", errorMessage);
            return ShareOutcome.Failed;
        }
    }

    private SharePayload EventPayload() => new()
    {
        Title = string.IsNullOrWhiteSpace(EventModel.EventName)
            ? "Red Mist live timing"
            : EventModel.EventName,
        Text = ShareLinks.EventShareText(EventModel.EventName, IsRealTime),
        Url = ShareLinks.EventUrl(ShareOrigin, EventModel.EventId, ViewedSessionId),
    };

    private SharePayload CarPayload(CarViewModel car) => new()
    {
        Title = ShareLinks.CarShareTitle(car.Number, car.ClassPosition, car.Class),
        Text = ShareLinks.CarShareText(car.Number, car.ClassPosition, car.Class,
            EventModel.EventName, IsRealTime),
        Url = ShareLinks.CarUrl(ShareOrigin, EventModel.EventId, ViewedSessionId, car.Number),
    };

    private StandingsCardData CardData(string? highlightCarNumber)
    {
        var field = carCache.Items.ToArray();
        var onScreen = OnScreenCars?.Invoke() ?? DisplayOrderCars();
        var selection = ShareStandings.Select(onScreen, field, highlightCarNumber);

        return new StandingsCardData
        {
            EventName = EventModel.EventName,
            SessionName = SessionName,
            IsLive = IsRealTime,
            Rows = selection.Rows,
            Omitted = selection.Omitted,
            HighlightCarNumber = highlightCarNumber,
            // The same site the link points at, so a build aimed at a test site does not hand out a
            // card advertising production.
            SiteHost = ShareLinks.HostOf(ShareOrigin),
        };
    }

    /// <summary>
    /// The field in the order the table would show it, used when the view cannot say what is on
    /// screen.
    /// </summary>
    /// <remarks>
    /// A fallback, and a slightly different promise from the one the card is making: it yields the top
    /// of the running order rather than the part of it the viewer has scrolled to, so the "+N more
    /// cars" line counts the rest of the field rather than the rest of the screen. Still honest, and
    /// much better than an empty card - but the view's own answer is the one to prefer. This is
    /// reached when the table has not been realized yet, or when the view model is driven without a
    /// view at all.
    ///
    /// Grouping and any search are still honored either way, because both collections are projections
    /// of the same filtered cache the table binds to.
    /// </remarks>
    private IReadOnlyList<CarViewModel> DisplayOrderCars()
    {
        if (IsFlat)
        {
            return [.. Cars];
        }

        var cars = new List<CarViewModel>();
        foreach (var group in GroupedCars)
        {
            cars.AddRange(group);
        }

        return cars;
    }

    /// <summary>Shows <paramref name="message"/> in the timing view for a few seconds.</summary>
    /// <remarks>
    /// Invoked rather than posted: every caller is already on the UI thread - a command, or the
    /// continuation of one - so this runs inline and the viewer sees the answer in the same frame as
    /// the share sheet closing. The clearing timer is the part that does come back from elsewhere,
    /// which is why that one is posted.
    /// </remarks>
    private void Report(string message)
    {
        Dispatcher.UIThread.InvokeOnUIThread(() =>
        {
            ShareStatus = message;
            shareStatusTimer?.Dispose();
            shareStatusTimer = Observable.Timer(ShareStatusDuration)
                .Subscribe(_ => Dispatcher.UIThread.PostSafe(() => ShareStatus = string.Empty, Logger));
        });
    }
}
