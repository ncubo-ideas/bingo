using System;
using System.Collections.Generic;
using System.Text.Json;

namespace BingoApp.Transport
{
    /// <summary>
    /// El aviso de ronda: para las PANTALLAS de todos (jugadores y colaboradores), sin
    /// diario. La mecanica del juego NO viaja por aca -- esa va como tell "Ronda" por el
    /// topico del canto, para que el corte de ronda quede ordenado con las bolitas.
    /// Este aviso solo pinta: que ronda suena, que promete cada figura, o que ya no
    /// suena nada.
    /// </summary>
    public static class RoundNotice
    {
        public const string Starting = "arranca";

        public const string Settled = "cerrada";

        public const string Over = "se_acabo";

        public static string TopicFor(string festivalId) =>
            "funcion." + (festivalId ?? string.Empty).Trim().ToLowerInvariant() + ".ronda";

        public static string BodyStarting(int position, string playing,
            IEnumerable<(string Figure, string Prize, bool IsCash)> promises)
        {
            List<object> prizes = new List<object>();

            if (promises != null)
            {
                // Las claves del JSON van escritas a mano y NO salen del nombre de la
                // variable. Es el cable: 'figura', 'premio', 'esCash' y 'premios' es lo que
                // el otro telefono lee, asi que renombrar una variable no lo puede mover.
                foreach ((string figure, string prize, bool isCash) in promises)
                    prizes.Add(new { figura = figure, premio = prize, esCash = isCash });
            }

            return JsonSerializer.Serialize(new
            {
                que = Starting,
                ronda = position,
                sejuega = playing ?? string.Empty,
                premios = prizes,
            });
        }

        public static string BodySettled(int position) =>
            JsonSerializer.Serialize(new { que = Settled, ronda = position });

        public static string BodyOver() =>
            JsonSerializer.Serialize(new { que = Over });

        public static string What(string body) => Text(body, "que");

        public static int Position(string body) =>
            int.TryParse(Text(body, "ronda"), out int n) ? n : 0;

        public static string Playing(string body) => Text(body, "sejuega");

        /// <summary>Lo que promete cada figura de la ronda: la figura, que da, y si eso que
        /// da es plata o una cosa.</summary>
        public static IReadOnlyList<(string Figure, string Prize, bool IsCash)> Promises(string body)
        {
            List<(string, string, bool)> promises = new List<(string, string, bool)>();

            if (string.IsNullOrWhiteSpace(body)) return promises;

            try
            {
                using JsonDocument doc = JsonDocument.Parse(body);

                if (!doc.RootElement.TryGetProperty("premios", out JsonElement list)) return promises;
                if (list.ValueKind != JsonValueKind.Array) return promises;

                foreach (JsonElement one in list.EnumerateArray())
                {
                    string figure = one.TryGetProperty("figura", out JsonElement f)
                        && f.ValueKind == JsonValueKind.String ? f.GetString() : null;

                    if (string.IsNullOrWhiteSpace(figure)) continue;

                    string prize = one.TryGetProperty("premio", out JsonElement z)
                        && z.ValueKind == JsonValueKind.String ? z.GetString() : string.Empty;

                    bool isCash = one.TryGetProperty("esCash", out JsonElement c)
                        && c.ValueKind == JsonValueKind.True;

                    promises.Add((figure, prize ?? string.Empty, isCash));
                }
            }
            catch (Exception)
            {
                return new List<(string, string, bool)>();
            }

            return promises;
        }

        /// <summary>"Una fila $10 · Carton lleno una canasta" — la linea que pintan
        /// las cabeceras de los telefonos.</summary>
        public static string PrizeLine(string body)
        {
            List<string> parts = new List<string>();

            foreach ((string figure, string prize, bool isCash) in Promises(body))
                parts.Add(OnOffer.Called(figure) + " " + (isCash ? "$" : string.Empty) + prize);

            return string.Join("  ·  ", parts);
        }

        private static string Text(string body, string field)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;

            try
            {
                using JsonDocument doc = JsonDocument.Parse(body);

                return doc.RootElement.TryGetProperty(field, out JsonElement value)
                    ? (value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString())
                    : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
