using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIA.Sync.Contracts;

namespace AIA.Services.MobileSync
{
    public sealed class MobileDeviceRecord
    {
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string TokenHash { get; set; } = string.Empty;
        public DateTimeOffset CreatedUtc { get; set; }
        public DateTimeOffset LastSeenUtc { get; set; }
        public string LastAddress { get; set; } = string.Empty;
    }

    /// <summary>
    /// Stores hashed bearer tokens in a DPAPI-encrypted file. Plain tokens are returned exactly
    /// once during pairing.
    /// </summary>
    public sealed class DeviceTokenStore
    {
        private readonly string _path;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private List<MobileDeviceRecord>? _devices;

        public DeviceTokenStore(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
        }

        public async Task<PairResponse> CreateDeviceAsync(string deviceName, string address)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var devices = await LoadUnlockedAsync().ConfigureAwait(false);
                var token = CreateToken();
                var record = new MobileDeviceRecord
                {
                    DeviceId = Guid.NewGuid().ToString("N"),
                    DeviceName = string.IsNullOrWhiteSpace(deviceName) ? "Android device" : deviceName.Trim(),
                    TokenHash = HashToken(token),
                    CreatedUtc = DateTimeOffset.UtcNow,
                    LastSeenUtc = DateTimeOffset.UtcNow,
                    LastAddress = address ?? string.Empty
                };

                devices.Add(record);
                await SaveUnlockedAsync(devices).ConfigureAwait(false);

                return new PairResponse
                {
                    DeviceId = record.DeviceId,
                    Token = token,
                    ServerName = "AIA"
                };
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<MobileDeviceRecord?> ValidateAsync(string token, string address)
        {
            if (string.IsNullOrWhiteSpace(token))
                return null;

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var devices = await LoadUnlockedAsync().ConfigureAwait(false);
                var hash = HashToken(token);
                var record = devices.FirstOrDefault(d => FixedTimeEquals(d.TokenHash, hash));
                if (record == null)
                    return null;

                var now = DateTimeOffset.UtcNow;
                if (now - record.LastSeenUtc > TimeSpan.FromMinutes(5))
                {
                    record.LastSeenUtc = now;
                    record.LastAddress = address ?? string.Empty;
                    await SaveUnlockedAsync(devices).ConfigureAwait(false);
                }

                return record;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<List<MobileDeviceRecord>> GetDevicesAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var devices = await LoadUnlockedAsync().ConfigureAwait(false);
                return devices
                    .OrderBy(d => d.DeviceName)
                    .Select(Clone)
                    .ToList();
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<bool> RevokeAsync(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
                return false;

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var devices = await LoadUnlockedAsync().ConfigureAwait(false);
                var removed = devices.RemoveAll(d => string.Equals(d.DeviceId, deviceId, StringComparison.Ordinal));
                if (removed > 0)
                    await SaveUnlockedAsync(devices).ConfigureAwait(false);

                return removed > 0;
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<List<MobileDeviceRecord>> LoadUnlockedAsync()
        {
            if (_devices != null)
                return _devices;

            var json = await SecretProtector.ReadEncryptedFileAsync(_path).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                _devices = new List<MobileDeviceRecord>();
                return _devices;
            }

            try
            {
                _devices = JsonSerializer.Deserialize<List<MobileDeviceRecord>>(json, JsonOptions) ?? new List<MobileDeviceRecord>();
            }
            catch (JsonException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Mobile device store is invalid: {ex.Message}");
                _devices = new List<MobileDeviceRecord>();
            }

            return _devices;
        }

        private async Task SaveUnlockedAsync(List<MobileDeviceRecord> devices)
        {
            _devices = devices;
            var json = JsonSerializer.Serialize(devices, JsonOptions);
            await SecretProtector.WriteEncryptedFileAsync(_path, json).ConfigureAwait(false);
        }

        private static string CreateToken()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static string HashToken(string token)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            var leftBytes = Encoding.UTF8.GetBytes(left);
            var rightBytes = Encoding.UTF8.GetBytes(right);
            return leftBytes.Length == rightBytes.Length &&
                   CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }

        private static MobileDeviceRecord Clone(MobileDeviceRecord source)
        {
            return new MobileDeviceRecord
            {
                DeviceId = source.DeviceId,
                DeviceName = source.DeviceName,
                TokenHash = source.TokenHash,
                CreatedUtc = source.CreatedUtc,
                LastSeenUtc = source.LastSeenUtc,
                LastAddress = source.LastAddress
            };
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };
    }
}
