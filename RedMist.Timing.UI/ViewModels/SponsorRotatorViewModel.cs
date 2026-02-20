using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Extensions;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Services;
using RedMist.TimingCommon.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels;

public partial class SponsorRotatorViewModel : ObservableObject, IDisposable
{
    private const string TrackingSource = "LiveTiming";
    private const int ViewableImpressionThresholdMs = 1000;

    private readonly SponsorsService sponsorsService;
    private readonly SponsorIconCacheService sponsorIconCacheService;
    private readonly SponsorClient sponsorClient;
    private readonly ILogger logger;

    private List<SponsorInfo> sortedSponsors = [];
    private int currentIndex = -1;
    private CancellationTokenSource? rotationCts;
    private readonly Stopwatch viewableStopwatch = new();
    private readonly Stopwatch engagementStopwatch = new();
    private bool viewableImpressionTracked;
    private string currentEventId = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSponsorVisible))]
    private Bitmap? currentSponsorImage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSponsorVisible))]
    private SponsorInfo? currentSponsor;

    public bool IsSponsorVisible => CurrentSponsorImage != null && CurrentSponsor != null;

    public SponsorRotatorViewModel(SponsorsService sponsorsService, SponsorIconCacheService sponsorIconCacheService,
        SponsorClient sponsorClient, ILoggerFactory loggerFactory)
    {
        this.sponsorsService = sponsorsService;
        this.sponsorIconCacheService = sponsorIconCacheService;
        this.sponsorClient = sponsorClient;
        logger = loggerFactory.CreateLogger<SponsorRotatorViewModel>();
    }

    public async Task StartAsync(string eventId)
    {
        Stop();
        currentEventId = eventId;

        try
        {
            await sponsorsService.InitializeAsync();

            sortedSponsors = sponsorsService.Sponsors
                .OrderBy(s => s.DisplayPriority)
                .ToList();

            if (sortedSponsors.Count == 0)
            {
                logger.LogDebug("No sponsors available");
                return;
            }

            rotationCts = new CancellationTokenSource();
            _ = RotateAsync(rotationCts.Token);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to start sponsor rotation");
        }
    }

    public void Stop()
    {
        try
        {
            TrackEngagementDuration();
            rotationCts?.Cancel();
            rotationCts?.Dispose();
            rotationCts = null;
        }
        catch { }

        Dispatcher.UIThread.InvokeOnUIThread(() =>
        {
            CurrentSponsor = null;
            CurrentSponsorImage = null;
        });

        currentIndex = -1;
        sortedSponsors = [];
    }

    private async Task RotateAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var (sponsor, image) = GetNextSponsorWithImage();
            if (sponsor == null)
            {
                logger.LogDebug("No sponsors with cached images available");
                break;
            }

            // Track engagement duration for the previous sponsor
            TrackEngagementDuration();

            // Update current sponsor on UI thread
            Dispatcher.UIThread.InvokeOnUIThread(() =>
            {
                CurrentSponsor = sponsor;
                CurrentSponsorImage = image;
            });

            // Reset tracking for new sponsor
            viewableImpressionTracked = false;
            viewableStopwatch.Restart();
            engagementStopwatch.Restart();

            // Track impression
            TrackImpression(sponsor);

            // Wait for display duration, checking viewable impression periodically
            var displayDuration = sponsor.DisplayDurationMs > 0 ? sponsor.DisplayDurationMs : 5000;
            var elapsed = 0;
            var checkInterval = 250;

            while (elapsed < displayDuration && !ct.IsCancellationRequested)
            {
                var waitTime = Math.Min(checkInterval, displayDuration - elapsed);
                try
                {
                    await Task.Delay(waitTime, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                elapsed += waitTime;

                // Check viewable impression threshold (≥1s visible)
                if (!viewableImpressionTracked && viewableStopwatch.ElapsedMilliseconds >= ViewableImpressionThresholdMs)
                {
                    viewableImpressionTracked = true;
                    TrackViewableImpression(sponsor);
                }
            }
        }
    }

    private (SponsorInfo? sponsor, Bitmap? image) GetNextSponsorWithImage()
    {
        if (sortedSponsors.Count == 0)
            return (null, null);

        // Try each sponsor starting from the next index
        for (int i = 0; i < sortedSponsors.Count; i++)
        {
            currentIndex = (currentIndex + 1) % sortedSponsors.Count;
            var sponsor = sortedSponsors[currentIndex];

            if (string.IsNullOrEmpty(sponsor.ImageUrl))
                continue;

            var image = sponsorIconCacheService.GetCachedSponsorImage(sponsor.ImageUrl);
            if (image != null)
                return (sponsor, image);
        }

        return (null, null);
    }

    public void OnSponsorClicked()
    {
        var sponsor = CurrentSponsor;
        if (sponsor == null || string.IsNullOrEmpty(sponsor.TargetUrl))
            return;

        // Track click-through
        TrackClickThrough(sponsor);

        // Launch browser
        WeakReferenceMessenger.Default.Send(new LauncherEvent(sponsor.TargetUrl));
    }

    #region Tracking

    private void TrackImpression(SponsorInfo sponsor)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await sponsorClient.SaveImpressionAsync(TrackingSource, sponsor.Id.ToString(), currentEventId);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to track impression for sponsor {SponsorId}", sponsor.Id);
            }
        });
    }

    private void TrackViewableImpression(SponsorInfo sponsor)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await sponsorClient.SaveViewableImpressionAsync(TrackingSource, sponsor.Id.ToString(), currentEventId);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to track viewable impression for sponsor {SponsorId}", sponsor.Id);
            }
        });
    }

    private void TrackClickThrough(SponsorInfo sponsor)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await sponsorClient.SaveClickThroughAsync(TrackingSource, sponsor.Id.ToString(), currentEventId);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to track click-through for sponsor {SponsorId}", sponsor.Id);
            }
        });
    }

    private void TrackEngagementDuration()
    {
        var sponsor = CurrentSponsor;
        if (sponsor == null || !engagementStopwatch.IsRunning)
            return;

        engagementStopwatch.Stop();
        var durationMs = (int)engagementStopwatch.ElapsedMilliseconds;
        if (durationMs <= 0)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await sponsorClient.SaveEngagementDurationAsync(TrackingSource, sponsor.Id.ToString(), durationMs, currentEventId);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to track engagement duration for sponsor {SponsorId}", sponsor.Id);
            }
        });
    }

    #endregion

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
