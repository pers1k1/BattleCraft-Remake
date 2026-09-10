using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace CustomLauncher.Core
{
    internal static class ManagedConfigTemplates
    {
        private static readonly JObject Defaults = ReadDefaults();

        public static string? Find(string relative, JObject keys, JObject? supplied)
        {
            string normalized = relative.Replace('\\', '/');
            if (supplied?[normalized]?.Type == JTokenType.String)
                return supplied[normalized]!.Value<string>();
            if (Defaults[normalized]?.Type == JTokenType.String)
                return Defaults[normalized]!.Value<string>();
            if (!normalized.EndsWith(".properties", StringComparison.OrdinalIgnoreCase))
                return null;

            StringBuilder lines = new();
            foreach (System.Collections.Generic.KeyValuePair<string, JToken?> entry in keys)
                lines.Append(entry.Key).Append('=').AppendLine(entry.Value?.ToString());
            return lines.ToString();
        }

        private static JObject ReadDefaults()
        {
            using Stream? source = typeof(ManagedConfigTemplates).Assembly
                .GetManifestResourceStream("CustomLauncher.ManagedConfigTemplates.json");
            if (source == null) throw new InvalidOperationException("Managed config templates are missing");

            using StreamReader reader = new(source, Encoding.UTF8);
            return JObject.Parse(reader.ReadToEnd());
        }
    }
}
