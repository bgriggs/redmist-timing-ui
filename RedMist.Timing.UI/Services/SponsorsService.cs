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


    public async Task InitializeAsync()
    {
        try
        {
            Sponsors = await sponsorClient.GetSponsorsAsync();

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
            logger.LogWarning(ex, "Failed to initialize sponsors");
        }
    }
}
