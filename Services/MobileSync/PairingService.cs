using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace AIA.Services.MobileSync
{
    /// <summary>
    /// One active 8-digit pairing code with a short lifetime and per-address attempt limit.
    /// </summary>
    public sealed class PairingService
    {
        private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(5);
        private const int MaxAttemptsPerAddress = 5;
        private static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(10);

        private readonly object _gate = new();
        private readonly Dictionary<string, (int Attempts, DateTimeOffset ResetAt)> _attempts =
            new(StringComparer.OrdinalIgnoreCase);

        private string? _code;
        private DateTimeOffset _expiresUtc;

        public (string Code, DateTimeOffset ExpiresUtc) CreateCode()
        {
            lock (_gate)
            {
                _code = RandomNumberGenerator.GetInt32(0, 100_000_000).ToString("D8");
                _expiresUtc = DateTimeOffset.UtcNow.Add(CodeLifetime);
                return (_code, _expiresUtc);
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _code = null;
                _expiresUtc = DateTimeOffset.MinValue;
            }
        }

        public (bool Success, string Error) TryConsume(string? code, string address)
        {
            var normalizedAddress = string.IsNullOrWhiteSpace(address) ? "unknown" : address;
            var now = DateTimeOffset.UtcNow;

            lock (_gate)
            {
                if (_code == null || now > _expiresUtc)
                {
                    ClearAttemptsExpired(now);
                    return (false, "Pairing code expired. Generate a new code on the desktop.");
                }

                if (_attempts.TryGetValue(normalizedAddress, out var attempts) && attempts.ResetAt > now)
                {
                    if (attempts.Attempts >= MaxAttemptsPerAddress)
                        return (false, "Too many pairing attempts. Try again later.");
                }
                else
                {
                    _attempts[normalizedAddress] = (0, now.Add(AttemptWindow));
                }

                var supplied = (code ?? string.Empty).Trim();
                if (!FixedTimeEquals(supplied, _code))
                {
                    var current = _attempts[normalizedAddress];
                    _attempts[normalizedAddress] = (current.Attempts + 1, current.ResetAt);
                    return (false, "Invalid pairing code.");
                }

                _code = null;
                _expiresUtc = DateTimeOffset.MinValue;
                _attempts.Remove(normalizedAddress);
                return (true, string.Empty);
            }
        }

        private void ClearAttemptsExpired(DateTimeOffset now)
        {
            foreach (var pair in new List<KeyValuePair<string, (int Attempts, DateTimeOffset ResetAt)>>(_attempts))
            {
                if (pair.Value.ResetAt <= now)
                    _attempts.Remove(pair.Key);
            }
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            var leftBytes = System.Text.Encoding.UTF8.GetBytes(left);
            var rightBytes = System.Text.Encoding.UTF8.GetBytes(right);
            return leftBytes.Length == rightBytes.Length &&
                   CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
    }
}
