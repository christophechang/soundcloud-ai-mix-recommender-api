using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Infrastructure.Services.SoundCloud.Engagement.Dtos;
using Microsoft.Extensions.Logging;

namespace Changsta.Ai.Infrastructure.Services.SoundCloud.Engagement
{
    /// <summary>
    /// Reads play and like counts for every public track on one SoundCloud account through the
    /// paged <c>/users/{id}/tracks</c> call, with the cached app token. Any failure — an HTTP error,
    /// a token failure, malformed or structurally incomplete JSON, an off-host <c>next_href</c> or
    /// the page cap — returns <see langword="null"/>: a partial page set is never returned. No retry.
    /// Only cancellation of the caller's token propagates. Failures are logged by status code only.
    /// </summary>
    internal sealed class SoundCloudEngagementClient : ISoundCloudEngagementClient
    {
        private const string ApiHost = "api.soundcloud.com";
        private const int MaxPages = 20;

        private readonly HttpClient _httpClient;
        private readonly SoundCloudAppTokenCache _tokenCache;
        private readonly ILogger<SoundCloudEngagementClient> _logger;

        public SoundCloudEngagementClient(
            HttpClient httpClient,
            SoundCloudAppTokenCache tokenCache,
            ILogger<SoundCloudEngagementClient> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _tokenCache = tokenCache ?? throw new ArgumentNullException(nameof(tokenCache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlyList<MixEngagement>?> FetchAllAsync(long userId, CancellationToken cancellationToken)
        {
            try
            {
                return await FetchPagesAsync(userId, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("SoundCloud engagement fetch timed out.");
                return null;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(
                    "SoundCloud engagement fetch failed with status {StatusCode}.",
                    ex.StatusCode is { } status ? (int)status : null);
                return null;
            }
            catch (JsonException)
            {
                _logger.LogWarning("SoundCloud engagement fetch returned malformed JSON.");
                return null;
            }
            catch (InvalidOperationException)
            {
                // The token cache throws this when the token response has no access_token.
                _logger.LogWarning("SoundCloud engagement fetch could not obtain an access token.");
                return null;
            }
        }

        private static bool IsAllowedApiUrl(string? url, out Uri? uri)
        {
            uri = null;

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed))
            {
                return false;
            }

            if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
                || !string.Equals(parsed.Host, ApiHost, StringComparison.OrdinalIgnoreCase)
                || !parsed.IsDefaultPort
                || !string.IsNullOrEmpty(parsed.UserInfo))
            {
                return false;
            }

            uri = parsed;
            return true;
        }

        private async Task<IReadOnlyList<MixEngagement>?> FetchPagesAsync(long userId, CancellationToken cancellationToken)
        {
            string token = await _tokenCache.GetTokenAsync(cancellationToken).ConfigureAwait(false);

            var results = new List<MixEngagement>();
            string? next = string.Create(
                CultureInfo.InvariantCulture,
                $"https://{ApiHost}/users/{userId}/tracks?limit=50&linked_partitioning=true");

            for (int page = 0; page < MaxPages; page++)
            {
                // The token is only ever sent to https://api.soundcloud.com.
                if (!IsAllowedApiUrl(next, out Uri? uri))
                {
                    _logger.LogWarning("SoundCloud engagement fetch refused a next_href outside the API host.");
                    return null;
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", token);

                using HttpResponseMessage response = await _httpClient
                    .SendAsync(request, cancellationToken)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        _tokenCache.Invalidate();
                    }

                    _logger.LogWarning(
                        "SoundCloud engagement page request failed with status {StatusCode}.",
                        (int)response.StatusCode);
                    return null;
                }

                using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                TrackPageDto? dto = await JsonSerializer
                    .DeserializeAsync<TrackPageDto>(body, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (dto?.Collection is null)
                {
                    _logger.LogWarning("SoundCloud engagement page had no collection.");
                    return null;
                }

                foreach (TrackEngagementDto? track in dto.Collection)
                {
                    if (track?.Id is not > 0)
                    {
                        _logger.LogWarning("SoundCloud engagement page held a track without a positive id.");
                        return null;
                    }

                    results.Add(new MixEngagement
                    {
                        TrackId = track.Id.Value,
                        Plays = track.PlaybackCount,
                        Likes = track.FavoritingsCount,
                    });
                }

                if (string.IsNullOrEmpty(dto.NextHref))
                {
                    return results;
                }

                next = dto.NextHref;
            }

            _logger.LogWarning("SoundCloud engagement fetch stopped at the {MaxPages}-page cap.", MaxPages);
            return null;
        }
    }
}
