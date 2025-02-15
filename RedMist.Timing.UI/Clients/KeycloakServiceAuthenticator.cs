using BigMission.Shared.Auth;
using RestSharp;
using RestSharp.Authenticators;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.Clients;

class KeycloakServiceAuthenticator : AuthenticatorBase
{
    private readonly string authUrl;
    private readonly string realm;
    private readonly string clientId;
    private readonly string clientSecret;

    public KeycloakServiceAuthenticator(string token, string authUrl, string realm, string clientId, string clientSecret) : base(token)
    {
        this.authUrl = authUrl;
        this.realm = realm;
        this.clientId = clientId;
        this.clientSecret = clientSecret;
    }

    protected override async ValueTask<Parameter> GetAuthenticationParameter(string accessToken)
    {
        if (string.IsNullOrEmpty(Token))
        {
            Token = await KeycloakServiceToken.RequestClientToken(authUrl, realm, clientId, clientSecret) ?? string.Empty;
        }
        return new HeaderParameter(KnownHeaders.Authorization, $"Bearer {Token}");
    }
}
