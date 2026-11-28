using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AIA.Models;
using AIA.Sync.Contracts;
using AIA.Sync.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AIA.Services.MobileSync
{
    /// <summary>
    /// Embedded Kestrel API for the Android app. Intended to sit behind a TLS-terminating
    /// tunnel or reverse proxy when exposed publicly.
    /// </summary>
    public sealed class MobileSyncServer : IAsyncDisposable
    {
        private static readonly JsonSerializerOptions HttpJson = CreateHttpJsonOptions();

        private readonly MobileSyncSettings _settings;
        private readonly SyncCoordinator _coordinator;
        private readonly DeviceTokenStore _devices;
        private readonly PairingService _pairing = new();
        private readonly string _settingsFolder;

        private WebApplication? _app;
        private CancellationTokenSource? _shutdown;

        public MobileSyncServer(MobileSyncSettings settings, SyncCoordinator coordinator, string settingsFolder)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _settingsFolder = settingsFolder ?? throw new ArgumentNullException(nameof(settingsFolder));
            _devices = new DeviceTokenStore(Path.Combine(settingsFolder, "mobile-sync-devices.json"));
        }

        public bool IsRunning => _app != null;
        public string? LastError { get; private set; }
        public PairingService Pairing => _pairing;
        public DeviceTokenStore Devices => _devices;

        public string LocalBaseUrl => _settings.LocalBaseUrl;
        public string EffectiveBaseUrl => _settings.EffectiveBaseUrl;

        public async Task StartAsync()
        {
            if (_app != null)
                return;

            LastError = null;
            try
            {
                var application = BuildApplication();
                _shutdown = new CancellationTokenSource();
                await application.StartAsync(_shutdown.Token).ConfigureAwait(false);
                _app = application;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                System.Diagnostics.Debug.WriteLine($"Mobile sync server failed to start: {ex}");
            }
        }

        public async Task StopAsync()
        {
            var app = _app;
            _app = null;

            if (app == null)
                return;

            try
            {
                _shutdown?.Cancel();
                await app.StopAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Mobile sync server stop failed: {ex.Message}");
            }
            finally
            {
                await app.DisposeAsync().ConfigureAwait(false);
                _shutdown?.Dispose();
                _shutdown = null;
            }
        }

        public async Task RestartAsync()
        {
            await StopAsync().ConfigureAwait(false);
            await StartAsync().ConfigureAwait(false);
        }

        public string CreatePairingPayload()
        {
            var (code, _) = _pairing.CreateCode();
            var baseUrl = Uri.EscapeDataString(EffectiveBaseUrl);
            return $"aia://pair?url={baseUrl}&code={code}";
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
        }

        private WebApplication BuildApplication()
        {
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                ApplicationName = "AIA.MobileSync",
                EnvironmentName = Environments.Production,
                ContentRootPath = AppContext.BaseDirectory
            });

            builder.Logging.ClearProviders();
            builder.Logging.AddDebug();
            builder.Logging.SetMinimumLevel(LogLevel.Warning);

            var bindAddress = ResolveBindAddress(_settings.BindAddress);
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Limits.MaxRequestBodySize = 1024 * 1024;
                options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
                options.Listen(bindAddress, _settings.Port, listen =>
                {
                    if (_settings.UseHttps)
                    {
                        if (string.IsNullOrWhiteSpace(_settings.CertificatePath))
                            throw new InvalidOperationException("HTTPS is enabled but no certificate path is configured.");

                        var password = SecretProtector.Unprotect(_settings.CertificatePassword);
                        listen.UseHttps(_settings.CertificatePath, string.IsNullOrEmpty(password) ? null : password);
                    }
                });
            });

            var allowedHosts = BuildAllowedHosts(bindAddress);
            if (allowedHosts.Count > 0)
                builder.WebHost.UseSetting("AllowedHosts", string.Join(';', allowedHosts));

            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.PropertyNameCaseInsensitive = true;
                options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });

            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.AddFixedWindowLimiter("api", limiter =>
                {
                    limiter.PermitLimit = 120;
                    limiter.Window = TimeSpan.FromMinutes(1);
                    limiter.QueueLimit = 0;
                });
                options.AddFixedWindowLimiter("pair", limiter =>
                {
                    limiter.PermitLimit = 20;
                    limiter.Window = TimeSpan.FromMinutes(10);
                    limiter.QueueLimit = 0;
                });
            });

            var app = builder.Build();

            app.Use(async (context, next) =>
            {
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers["Cache-Control"] = "no-store";
                context.Response.Headers["X-Frame-Options"] = "DENY";

                try
                {
                    await next(context).ConfigureAwait(false);
                }
                catch (SyncRequestException ex)
                {
                    if (!context.Response.HasStarted)
                    {
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        await context.Response.WriteAsJsonAsync(new ApiError { Error = ex.Message }, HttpJson).ConfigureAwait(false);
                    }
                }
                catch (JsonException ex)
                {
                    if (!context.Response.HasStarted)
                    {
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        await context.Response.WriteAsJsonAsync(new ApiError { Error = $"Invalid JSON: {ex.Message}" }, HttpJson).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Mobile sync request failed: {ex}");
                    if (!context.Response.HasStarted)
                    {
                        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                        await context.Response.WriteAsJsonAsync(new ApiError { Error = "Internal server error." }, HttpJson).ConfigureAwait(false);
                    }
                }
            });

            app.UseRateLimiter();

            MapEndpoints(app);
            return app;
        }

        private void MapEndpoints(WebApplication app)
        {
            app.MapGet("/api/v1/health", async (HttpContext context) =>
            {
                var revision = await _coordinator.GetCurrentRevisionAsync(context.RequestAborted).ConfigureAwait(false);
                return Results.Json(new HealthResponse
                {
                    Status = "ok",
                    Version = typeof(MobileSyncServer).Assembly.GetName().Version?.ToString() ?? "1.0",
                    ServerRevision = revision
                }, HttpJson);
            }).RequireRateLimiting("api");

            app.MapPost("/api/v1/pair", async (HttpContext context) =>
            {
                var request = await context.Request.ReadFromJsonAsync<PairRequest>(HttpJson, context.RequestAborted).ConfigureAwait(false);
                if (request == null)
                    return Results.Json(new ApiError { Error = "Request body is required." }, HttpJson, statusCode: StatusCodes.Status400BadRequest);

                var address = context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
                var pairing = _pairing.TryConsume(request.Code, address);
                if (!pairing.Success)
                    return Results.Json(new ApiError { Error = pairing.Error }, HttpJson, statusCode: StatusCodes.Status400BadRequest);

                var response = await _devices.CreateDeviceAsync(request.DeviceName, address).ConfigureAwait(false);
                return Results.Json(response, HttpJson);
            }).RequireRateLimiting("pair");

            app.MapGet("/api/v1/sync", async (HttpContext context, long? since) =>
            {
                var device = await AuthenticateAsync(context).ConfigureAwait(false);
                if (device == null)
                    return Results.Unauthorized();

                var response = await _coordinator.SyncAsync(new SyncRequest
                {
                    SinceRevision = Math.Max(0, since ?? 0),
                    ClientId = device.DeviceId
                }, context.RequestAborted).ConfigureAwait(false);

                return Results.Json(response, HttpJson);
            }).RequireRateLimiting("api");

            app.MapPost("/api/v1/sync", async (HttpContext context) =>
            {
                var device = await AuthenticateAsync(context).ConfigureAwait(false);
                if (device == null)
                    return Results.Unauthorized();

                var request = await context.Request.ReadFromJsonAsync<SyncRequest>(HttpJson, context.RequestAborted).ConfigureAwait(false);
                if (request == null)
                    return Results.Json(new ApiError { Error = "Request body is required." }, HttpJson, statusCode: StatusCodes.Status400BadRequest);

                if (string.IsNullOrWhiteSpace(request.ClientId))
                    request.ClientId = device.DeviceId;

                var response = await _coordinator.SyncAsync(request, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(response, HttpJson);
            }).RequireRateLimiting("api");
        }

        private async Task<MobileDeviceRecord?> AuthenticateAsync(HttpContext context)
        {
            var header = context.Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return null;

            var token = header.Substring("Bearer ".Length).Trim();
            if (string.IsNullOrWhiteSpace(token))
                return null;

            var address = context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
            return await _devices.ValidateAsync(token, address).ConfigureAwait(false);
        }

        private HashSet<string> BuildAllowedHosts(IPAddress bindAddress)
        {
            var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "localhost",
                "127.0.0.1",
                "[::1]",
                Dns.GetHostName()
            };

            if (_settings.AllowLan)
                hosts.Add("*");
            else
            {
                hosts.Add(bindAddress.ToString());
                try
                {
                    foreach (var address in Dns.GetHostAddresses(Dns.GetHostName()))
                    {
                        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            hosts.Add(address.ToString());
                    }
                }
                catch
                {
                    // Host filtering still has localhost and the bind address.
                }
            }

            if (Uri.TryCreate(_settings.PublicBaseUrl, UriKind.Absolute, out var publicUri))
                hosts.Add(publicUri.Host);

            return hosts;
        }

        private static IPAddress ResolveBindAddress(string? address)
        {
            var value = (address ?? string.Empty).Trim();
            if (value is "0.0.0.0" or "*")
                return IPAddress.Any;

            if (value is "::" or "[::]")
                return IPAddress.IPv6Any;

            return IPAddress.TryParse(value, out var parsed) ? parsed : IPAddress.Loopback;
        }

        private static JsonSerializerOptions CreateHttpJsonOptions()
        {
            var options = new JsonSerializerOptions(SyncJson.Options)
            {
                WriteIndented = false
            };
            return options;
        }
    }
}
