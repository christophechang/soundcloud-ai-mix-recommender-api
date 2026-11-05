using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Changsta.Ai.Infrastructure.Services.SoundCloud.Engagement.Dtos;
using Microsoft.Extensions.Logging;

namespace Changsta.Ai.Infrastructure.Services.SoundCloud.Engagement
{
    /// <summary>
    /// Caches the app-level <c>client_credentials</c> token until 60 seconds before it expires.
    /// Ported from Setfolio's <c>AppTokenCache</c>, with two differences: a token whose
    /// <c>expires_in</c> is missing or 60s or less is used once and not cached, and
    /// <see cref="Invalidate"/> lets the API client drop a token SoundCloud has rejected.
    /// Neither the client secret nor the token is ever logged or put in an exception message.
    /// </summary>
    internal sealed class SoundCloudAppTokenCache : IDisposable
    {
        private const string TokenEndpoint = "https://secure.soundcloud.com/oauth/token";
        private const long ExpirySkewSeconds = 60;

        private readonly HttpClient _httpClient;
        private readonly SoundCloudEngagementSettings _settings;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<SoundCloudAppTokenCache> _logger;
        private readonly SemaphoreSlim _lock = new(1, 1);

        private volatile CachedToken? _cached;

        public SoundCloudAppTokenCache(
            HttpClient httpClient,
            SoundCloudEngagementSettings settings,
            TimeProvider timeProvider,
            ILogger<SoundCloudAppTokenCache> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
        {
            string? cached = TryGetCached();
            if (cached is not null)
            {
                return cached;
            }

            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cached = TryGetCached();
                if (cached is not null)
                {
                    return cached;
                }

                return await RequestTokenAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _lock.Release();
            }
        }

        public void Invalidate() => _cached = null;

        public void Dispose() => _lock.Dispose();

        private string? TryGetCached()
        {
            CachedToken? entry = _cached;
            return entry is not null && _timeProvider.GetUtcNow() < entry.ExpiresAt ? entry.Token : null;
        }

        private async Task<string> RequestTokenAsync(CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                }),
            };

            string basic = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

            using HttpResponseMessage response = await _httpClient
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                int status = (int)response.StatusCode;
                _logger.LogWarning("SoundCloud token request failed with status {StatusCode}.", status);
                throw new HttpRequestException(
                    $"SoundCloud token request failed with status {status}.",
                    inner: null,
                    statusCode: response.StatusCode);
            }

            using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            TokenResponseDto? dto = await JsonSerializer
                .DeserializeAsync<TokenResponseDto>(body, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            string? token = dto?.AccessToken;
            if (string.IsNullOrWhiteSpace(token))
            {
                _logger.LogWarning("SoundCloud token response had no access_token.");
                throw new InvalidOperationException("SoundCloud token response had no access_token.");
            }

            long lifetimeSeconds = Math.Max(0, (dto!.ExpiresIn ?? 0) - ExpirySkewSeconds);
            _cached = lifetimeSeconds > 0
                ? new CachedToken(token, _timeProvider.GetUtcNow().AddSeconds(lifetimeSeconds))
                : null;

            return token;
        }

        private sealed class CachedToken
        {
            public CachedToken(string token, DateTimeOffset expiresAt)
            {
                Token = token;
                ExpiresAt = expiresAt;
            }

            public string Token { get; }

            public DateTimeOffset ExpiresAt { get; }
        }
    }
}
