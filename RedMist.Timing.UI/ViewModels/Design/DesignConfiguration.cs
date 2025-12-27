using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;

namespace RedMist.Timing.UI.ViewModels.Design;

class DesignConfiguration : IConfiguration
{
    private readonly Dictionary<string, string?> config = new()
    {
        { "Server:Url", "https://localhost:5001" },
        { "Server:EventUrl", "https://api.redmist.racing/status/Events" },
        { "Server:OrganizationUrl", "https://localhost:5001" },
        { "Hub:Url", "https://localhost:5001/hub" },
        { "Keycloak:AuthServerUrl", "https://localhost:5001/auth" },
        { "Keycloak:Realm", "test" },
        { "Keycloak:ClientId", "1" },
        { "Keycloak:ClientSecret", "secret" },
        { "Cdn:BaseUrl", "https://assets.redmist.racing/" },
        { "Cdn:Logos", "logos/" },
    };

    public string? this[string key] { get => config[key]; set => throw new NotImplementedException(); }

    public IEnumerable<IConfigurationSection> GetChildren()
    {
        throw new NotImplementedException();
    }

    public IChangeToken GetReloadToken()
    {
        throw new NotImplementedException();
    }

    public IConfigurationSection GetSection(string key)
    {
        throw new NotImplementedException();
    }
}
