using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIA.Sync.Contracts
{
    public static class SyncJson
    {
        public static JsonSerializerOptions Options { get; } = CreateOptions();

        private static JsonSerializerOptions CreateOptions()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }
    }
}
