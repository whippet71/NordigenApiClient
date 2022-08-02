﻿using System.Diagnostics;
using System.Text.Json;
using RobinTTY.NordigenApiClient.Endpoints;
using RobinTTY.NordigenApiClient.JsonConverters;
using RobinTTY.NordigenApiClient.Models;
using RobinTTY.NordigenApiClient.Models.Jwt;
using RobinTTY.NordigenApiClient.Utility;

namespace RobinTTY.NordigenApiClient;

public class NordigenClient
{
    private readonly HttpClient _httpClient;
    private readonly JsonWebTokenPair? _jwtTokenPair;
    private readonly JsonSerializerOptions _serializerOptions;
    internal readonly NordigenClientCredentials Credentials;

    /// <summary>
    /// TODO
    /// </summary>
    public TokenEndpoint TokenEndpoint { get; }
    /// <summary>
    /// TODO
    /// </summary>
    public InstitutionsEndpoint InstitutionsEndpoint { get; }
    /// <summary>
    /// TODO
    /// </summary>
    public AgreementsEndpoint AgreementsEndpoint { get; }

    /// <summary>
    /// Creates a new instance of <see cref="NordigenClient"/>.
    /// </summary>
    /// <param name="httpClient">The <see cref="HttpClient"/> to use.</param>
    /// <param name="credentials">The Nordigen credentials for API access.</param>
    /// <param name="jwtTokenPair">An optional JSON web token pair consisting of access and refresh token to use.</param>
    public NordigenClient(HttpClient httpClient, NordigenClientCredentials credentials, JsonWebTokenPair? jwtTokenPair = null)
    {
        _httpClient = httpClient;
        _jwtTokenPair = jwtTokenPair;
        _serializerOptions = new JsonSerializerOptions
        {
            Converters = { new JsonWebTokenConverter() }
        };

        Credentials = credentials;
        TokenEndpoint = new TokenEndpoint(this);
        InstitutionsEndpoint = new InstitutionsEndpoint(this);
        AgreementsEndpoint = new AgreementsEndpoint(this);
    }

    internal async Task<NordigenApiResponse<TResponse, TError>> MakeRequest<TResponse, TError>(
        string uri,
        HttpMethod method,
        CancellationToken cancellationToken,
        IEnumerable<KeyValuePair<string, string>>? query = null,
        HttpContent? body = null,
        bool useAuthentication = true
        ) where TResponse : class where TError : class
    {
        var requestUri = query != null ? UriQueryBuilder.BuildUriWithQueryString(uri, query) : uri;
        var authToken = useAuthentication ? await TryGetValidTokenPair(cancellationToken) : null;
        var client = useAuthentication ? _httpClient.UseNordigenAuthenticationHeader(authToken) : _httpClient;

        HttpResponseMessage ? response;
        if (method == HttpMethod.Get)
            response = await client.GetAsync(requestUri, cancellationToken);
        else if (method == HttpMethod.Post)
            response = await client.PostAsync(requestUri, body, cancellationToken);
        else
            throw new NotImplementedException();

        var str = await response.Content.ReadAsStringAsync(cancellationToken);
        Debug.WriteLine(str);
        return await NordigenApiResponse<TResponse, TError>.FromHttpResponse(response, cancellationToken, _serializerOptions);
    }

    /// <summary>
    /// Tries to retrieve a valid <see cref="JsonWebTokenPair"/>.
    /// </summary>
    /// <param name="cancellationToken">An optional token to signal cancellation of the operation.</param>
    /// <returns>A valid <see cref="JsonWebTokenPair"/> if the operation was successful.
    /// Otherwise returns null.</returns>
    private async Task<JsonWebTokenPair?> TryGetValidTokenPair(CancellationToken cancellationToken = default)
    {
        // Request a new token if it is null or if the refresh token has expired
        if (_jwtTokenPair == null || _jwtTokenPair.RefreshToken.IsExpired(TimeSpan.FromMinutes(1)))
        {
            var response = await TokenEndpoint.GetToken(cancellationToken);
            return response.IsSuccess ? response.Result : null;
        }

        // Refresh the current access token if it's expired (or valid for less than a minute)
        if (_jwtTokenPair.AccessToken.IsExpired(TimeSpan.FromMinutes(1)))
        {
            var response = await TokenEndpoint.RefreshToken(_jwtTokenPair.RefreshToken, cancellationToken);
            if (!response.IsSuccess) return null;
            
            // Update the token pair with the response
            _jwtTokenPair.AccessToken = response.Result!.AccessToken;
            _jwtTokenPair.AccessExpires = response.Result!.AccessExpires;
            return _jwtTokenPair;
        }

        // Token pair is still valid and can be returned
        return _jwtTokenPair;
    }
}
