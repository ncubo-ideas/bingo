using System;
using System.Text.Json;

namespace BingoApp.Transport
{
    /// <summary>
    /// La bolita que salio, para las PANTALLAS de los colaboradores. No es el camino del
    /// juego: a los jugadores la bolita les llega como tell al diario, y de ahi sale
    /// marcada en su carton. Este aviso no marca nada -- solo pinta.
    ///
    /// Hace falta porque los dos que cantan (el organizador y quien ayuda) cantan sobre
    /// la MISMA tombola: sin esto, cada uno veria apagadas nada mas las bolitas que canto
    /// el, y los dos estarian tocando numeros que el otro ya saco.
    /// </summary>
    public static class DrawNotice
    {
        public static string TopicFor(string festivalId) =>
            "funcion." + (festivalId ?? string.Empty).Trim().ToLowerInvariant() + ".bolitas";

        /// <summary>La bolita que acaba de salir.</summary>
        public static string Body(int number) =>
            JsonSerializer.Serialize(new { numero = number, envezde = 0 });

        /// <summary>Me equivoque de tecla: la mala se cambia por la buena.</summary>
        public static string BodyCorrecting(int wrong, int right) =>
            JsonSerializer.Serialize(new { numero = right, envezde = wrong });

        public static int Number(string body) => Whole(body, "numero");

        /// <summary>A cual reemplaza. Cero cuando es una bolita normal.</summary>
        public static int Instead(string body) => Whole(body, "envezde");

        private static int Whole(string body, string field)
        {
            if (string.IsNullOrWhiteSpace(body)) return 0;

            try
            {
                using JsonDocument doc = JsonDocument.Parse(body);

                return doc.RootElement.TryGetProperty(field, out JsonElement value)
                    && value.ValueKind == JsonValueKind.Number
                    && value.TryGetInt32(out int number)
                    ? number
                    : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
