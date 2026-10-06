using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace BingoApp.Transport
{
    public static class CustodyNotice
    {
        public static string TopicFor(string festivalId, string collaborator) =>
            "funcion." + (festivalId ?? string.Empty).Trim().ToLowerInvariant()
            + ".custodia." + (collaborator ?? string.Empty).Trim().ToLowerInvariant();

        public static string Body(IEnumerable<long> cards) => Body(cards, null);

        public static string BodyTaking(IEnumerable<long> cards) => Body(null, cards);

        public static string Body(IEnumerable<long> arriving, IEnumerable<long> leaving) =>
            "{\"cartones\":[" + Numbers(arriving) + "],\"quitar\":[" + Numbers(leaving) + "]}";

        public static IReadOnlyList<long> Read(string body) => Read(body, "cartones");

        public static IReadOnlyList<long> ReadTaken(string body) => Read(body, "quitar");

        private static string Numbers(IEnumerable<long> cards)
        {
            List<string> numbers = new List<string>();

            if (cards != null)
            {
                foreach (long card in cards)
                    numbers.Add(card.ToString(CultureInfo.InvariantCulture));
            }

            return string.Join(",", numbers);
        }

        private static IReadOnlyList<long> Read(string body, string field)
        {
            List<long> cards = new List<long>();

            if (string.IsNullOrWhiteSpace(body)) return cards;

            try
            {
                using JsonDocument doc = JsonDocument.Parse(body);

                if (!doc.RootElement.TryGetProperty(field, out JsonElement list)) return cards;
                if (list.ValueKind != JsonValueKind.Array) return cards;

                foreach (JsonElement one in list.EnumerateArray())
                {
                    if (one.ValueKind == JsonValueKind.Number && one.TryGetInt64(out long card))
                        cards.Add(card);
                }
            }
            catch (Exception)
            {
                return new List<long>();
            }

            return cards;
        }
    }
}
