using System;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace AIA.Models
{
    public class MobileSyncSettings : INotifyPropertyChanged
    {
        private bool _enabled;
        private string _bindAddress = "127.0.0.1";
        private int _port = 47921;
        private string _publicBaseUrl = string.Empty;
        private bool _useHttps;
        private string _certificatePath = string.Empty;
        private string _certificatePassword = string.Empty;
        private bool _allowLan;
        private bool _mirrorDueReminders = true;

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(nameof(Enabled)); }
        }

        /// <summary>
        /// Address Kestrel binds to. Use 127.0.0.1 behind a tunnel; 0.0.0.0 for LAN access.
        /// </summary>
        public string BindAddress
        {
            get => _bindAddress;
            set { _bindAddress = string.IsNullOrWhiteSpace(value) ? "127.0.0.1" : value.Trim(); OnPropertyChanged(nameof(BindAddress)); }
        }

        public int Port
        {
            get => _port;
            set { _port = Math.Max(1, Math.Min(65535, value)); OnPropertyChanged(nameof(Port)); }
        }

        /// <summary>
        /// Public HTTPS base URL advertised in pairing QR codes. Empty falls back to the local URL.
        /// </summary>
        public string PublicBaseUrl
        {
            get => _publicBaseUrl;
            set { _publicBaseUrl = value?.Trim() ?? string.Empty; OnPropertyChanged(nameof(PublicBaseUrl)); }
        }

        public bool UseHttps
        {
            get => _useHttps;
            set { _useHttps = value; OnPropertyChanged(nameof(UseHttps)); }
        }

        public string CertificatePath
        {
            get => _certificatePath;
            set { _certificatePath = value?.Trim() ?? string.Empty; OnPropertyChanged(nameof(CertificatePath)); }
        }

        /// <summary>
        /// DPAPI-protected certificate password. Use SecretProtector before persisting.
        /// </summary>
        public string CertificatePassword
        {
            get => _certificatePassword;
            set { _certificatePassword = value ?? string.Empty; OnPropertyChanged(nameof(CertificatePassword)); }
        }

        /// <summary>
        /// When false, the server should only accept loopback traffic even if BindAddress is broader.
        /// </summary>
        public bool AllowLan
        {
            get => _allowLan;
            set { _allowLan = value; OnPropertyChanged(nameof(AllowLan)); }
        }

        public bool MirrorDueReminders
        {
            get => _mirrorDueReminders;
            set { _mirrorDueReminders = value; OnPropertyChanged(nameof(MirrorDueReminders)); }
        }

        public string LocalBaseUrl => $"{(UseHttps ? "https" : "http")}://{ResolveAdvertisedHost()}:{Port}";

        public string EffectiveBaseUrl => string.IsNullOrWhiteSpace(PublicBaseUrl) ? LocalBaseUrl : PublicBaseUrl.TrimEnd('/');

        private string ResolveAdvertisedHost()
        {
            if (string.IsNullOrWhiteSpace(BindAddress) || BindAddress is "0.0.0.0" or "::" or "[::]")
            {
                try
                {
                    var address = Dns.GetHostAddresses(Dns.GetHostName())
                        .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
                    if (address != null)
                        return address.ToString();
                }
                catch
                {
                    // Fall back to loopback below.
                }

                return "127.0.0.1";
            }

            return BindAddress;
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
