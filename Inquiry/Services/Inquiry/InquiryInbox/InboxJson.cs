// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/Services/Inquiry/InquiryInbox/InboxJson.cs
// MODULE: Inquiry — shared JSON options for the website inbox protocol.
//
// Every inbox payload (plan rows, manifests, document plans, chat rows)
// is deserialized through InboxJson.Options so the protocol behaviour
// lives in exactly one place.
//
// Number tolerance: the website's RPC payloads can carry numbers as
// strings (e.g. "document_count": "12"). The lenient converters below
// accept a real JSON number or a numeric string — the same behaviour as
// JsonNumberHandling.AllowReadingFromString, implemented explicitly so
// the protocol never depends on that API being present in the
// serializer reference of the machine doing the build.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProGlassAutomation.Services.Inquiry.InquiryInbox
{
    /// <summary>Shared options for all inbox JSON deserialization.</summary>
    public static class InboxJson
    {
        public static readonly JsonSerializerOptions Options = Build();

        private static JsonSerializerOptions Build()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            options.Converters.Add(new LenientIntConverter());
            options.Converters.Add(new LenientLongConverter());
            return options;
        }
    }

    /// <summary>Reads a JSON number or a numeric string as int.</summary>
    internal sealed class LenientIntConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number) return reader.GetInt32();
            if (reader.TokenType == JsonTokenType.String &&
                int.TryParse(reader.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                return value;
            throw new JsonException("Expected a number or a numeric string.");
        }

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(value);
    }

    /// <summary>Reads a JSON number or a numeric string as long.</summary>
    internal sealed class LenientLongConverter : JsonConverter<long>
    {
        public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number) return reader.GetInt64();
            if (reader.TokenType == JsonTokenType.String &&
                long.TryParse(reader.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                return value;
            throw new JsonException("Expected a number or a numeric string.");
        }

        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(value);
    }
}
