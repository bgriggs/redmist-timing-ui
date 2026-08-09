using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using RedMist.TimingCommon.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.Services;

public class SponsorsService
{
    private readonly SponsorClient sponsorClient;
    private readonly SponsorIconCacheService sponsorIconCacheService;
    private readonly ILogger<SponsorsService> logger;

    public List<SponsorInfo> Sponsors { get; private set; } = [];

    public SponsorsService(SponsorClient sponsorClient, SponsorIconCacheService sponsorIconCacheService, ILoggerFactory loggerFactory)
    {
        this.sponsorClient = sponsorClient;
        this.sponsorIconCacheService = sponsorIconCacheService;
        logger = loggerFactory.CreateLogger<SponsorsService>();
    }


    /// <summary>
    /// Loads the sponsors to display. Pass the event being viewed so sponsors excluded by the organization
    /// running it are left out; pass nothing for contexts with no event, which get every active sponsor.
    /// </summary>
    public async Task InitializeAsync(string eventId = "")
    {
        try
        {
            Sponsors = await sponsorClient.GetSponsorsAsync(eventId);
        }
        catch (Exception ex)
        {
            // Drop the previous event's list rather than leave it to be displayed under this one:
            // it may hold sponsors this event's organization excludes.
            Sponsors = [];
            logger.LogWarning(ex, "Failed to load sponsors for event {EventId}", eventId);
            return;
        }

        try
        {
            // Load sponsor images into cache
            var tasks = new List<Task>(Sponsors.Count);
            foreach (var sponsor in Sponsors)
            {
                if (!string.IsNullOrEmpty(sponsor.ImageUrl))
                {
                    tasks.Add(sponsorIconCacheService.GetSponsorImageAsync(sponsor.ImageUrl));
                }
            }
            await Task.WhenAll(tasks);

            logger.LogInformation("Loaded {Count} sponsor images", Sponsors.Count);
        }
        catch (Exception ex)
        {
            // The rotation skips sponsors with no cached image, so a caching failure is not
            // a reason to throw the loaded list away.
            logger.LogWarning(ex, "Failed to cache sponsor images");
        }
    }
}
