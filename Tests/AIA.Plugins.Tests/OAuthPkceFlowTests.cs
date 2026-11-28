using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AIA.Plugins.SDK;
using Xunit;

namespace AIA.Plugins.Tests;

public class OAuthPkceFlowTests
{
    private const string AuthorizationEndpoint = "https://accounts.example.com/o/oauth2/v2/auth";

    private static OAuthClientConfig CreateConfig(string? authorizationEndpoint = null) => new(
        authorizationEndpoint ?? AuthorizationEndpoint,
        "https://accounts.example.com/token",
        "test-client-id",
        ClientSecret: null);

    [Fact]
    public void CreateCodeVerifier_HasValidLengthAndUrlSafeCharacters()
    {
        var verifier = OAuthPkceFlow.CreateCodeVerifier();

        Assert.InRange(verifier.Length, 43, 128);
        Assert.Matches(new Regex("^[A-Za-z0-9\\-_]+$"), verifier);
    }

    [Fact]
    public void CreateCodeVerifier_ReturnsDifferentValuesOnEachCall()
    {
        var first = OAuthPkceFlow.CreateCodeVerifier();
        var second = OAuthPkceFlow.CreateCodeVerifier();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void CreateCodeChallenge_MatchesRfc7636Vector()
    {
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        const string expectedChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

        var challenge = OAuthPkceFlow.CreateCodeChallenge(verifier);

        Assert.Equal(expectedChallenge, challenge);
    }

    [Fact]
    public void BuildAuthorizationUrl_ContainsAllDefaultParameters()
    {
        var url = OAuthPkceFlow.BuildAuthorizationUrl(
            CreateConfig(),
            new[] { "scope.one", "scope.two" },
            "http://127.0.0.1:53682/oauth2/callback",
            "state-value",
            "challenge-value");

        Assert.StartsWith(AuthorizationEndpoint + "?", url);

        var parameters = ParseQuery(url);
        Assert.Equal("code", parameters["response_type"]);
        Assert.Equal("test-client-id", parameters["client_id"]);
        Assert.Equal("http://127.0.0.1:53682/oauth2/callback", parameters["redirect_uri"]);
        Assert.Equal("scope.one scope.two", parameters["scope"]);
        Assert.Equal("state-value", parameters["state"]);
        Assert.Equal("challenge-value", parameters["code_challenge"]);
        Assert.Equal("S256", parameters["code_challenge_method"]);
        Assert.Equal("offline", parameters["access_type"]);
        Assert.Equal("true", parameters["include_granted_scopes"]);
        Assert.Equal("consent", parameters["prompt"]);
    }

    [Fact]
    public void BuildAuthorizationUrl_ExtraParametersOverrideDefaults()
    {
        var extra = new Dictionary<string, string>
        {
            ["prompt"] = "none",
            ["access_type"] = "online",
            ["login_hint"] = "user@example.com"
        };

        var url = OAuthPkceFlow.BuildAuthorizationUrl(
            CreateConfig(),
            new[] { "scope.one" },
            "http://127.0.0.1:53682/oauth2/callback",
            "state-value",
            "challenge-value",
            extra);

        var pairs = ParseQueryPairs(url);
        Assert.Single(pairs, p => p.Key == "prompt");
        Assert.Single(pairs, p => p.Key == "access_type");
        Assert.Single(pairs, p => p.Key == "include_granted_scopes");
        Assert.Equal("none", pairs.Single(p => p.Key == "prompt").Value);
        Assert.Equal("online", pairs.Single(p => p.Key == "access_type").Value);
        Assert.Equal("user@example.com", pairs.Single(p => p.Key == "login_hint").Value);
        Assert.Equal("true", pairs.Single(p => p.Key == "include_granted_scopes").Value);
    }

    [Fact]
    public void BuildAuthorizationUrl_UsesAmpersandWhenEndpointAlreadyHasQuery()
    {
        const string endpoint = AuthorizationEndpoint + "?tenant=common";

        var url = OAuthPkceFlow.BuildAuthorizationUrl(
            CreateConfig(endpoint),
            new[] { "scope.one" },
            "http://127.0.0.1:53682/oauth2/callback",
            "state-value",
            "challenge-value");

        Assert.StartsWith(endpoint + "&response_type=code&", url);
        Assert.Equal(1, url.Count(c => c == '?'));
    }

    [Fact]
    public void BuildAuthorizationUrl_JoinsScopesWithSpacesAndSkipsBlankEntries()
    {
        var url = OAuthPkceFlow.BuildAuthorizationUrl(
            CreateConfig(),
            new[] { "a", " ", "b" },
            "http://127.0.0.1:53682/oauth2/callback",
            "state-value",
            "challenge-value");

        Assert.Equal("a b", ParseQuery(url)["scope"]);
    }

    private static Dictionary<string, string> ParseQuery(string url) =>
        ParseQueryPairs(url).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static List<KeyValuePair<string, string>> ParseQueryPairs(string url)
    {
        var query = new Uri(url).Query.TrimStart('?');
        var pairs = new List<KeyValuePair<string, string>>();

        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            var key = Uri.UnescapeDataString(part[..separator]);
            var value = Uri.UnescapeDataString(part[(separator + 1)..]);
            pairs.Add(new KeyValuePair<string, string>(key, value));
        }

        return pairs;
    }
}
