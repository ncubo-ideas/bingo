using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using BingoEngine;
using Choreography.Theater;
using Puppeteer;

namespace BingoWeb
{
    // El jugador del bingo corriendo ADENTRO del navegador: el mismo actor PerformanceV2, los
    // mismos scripts DSL y los mismos nombres que BingoApp/Player/LivePlayer.cs.
    //
    // El diario va en FileSystem sobre /journal, que en el browser es el sistema de archivos
    // en memoria del runtime (MEMFS). La pagina PRECARGA ese arbol desde su
    // destino duradero ANTES de ConfigureStorage + Start (el replay es sincronico), y despues
    // de cada comando VUELCA el arbol entero al destino. El destino aca es IndexedDB, que es lo
    // unico duradero que existe sobre http plano.
    [SupportedOSPlatform("browser")]
    public static partial class Player
    {
        private const string Raiz = "/journal";

        private static PerformanceV2 _journal;
        private static readonly List<string> _voz = new List<string>();
        private static int _asientos;
        private static bool _dirty;
        private static int _precargados;
        private static long _bytesPrecargados;
        private static string _game;
        private static string _slot;

        private sealed class Voz : IPuppeteerLogger
        {
            public void Error(string message, Exception exception) =>
                _voz.Add("QUEJA: " + message + (exception is null ? "" : " | " + exception.GetType().Name + ": " + exception.Message));

            public void Debug(string message) => _voz.Add("diario: " + message);
        }

        public static void Main() { }

        // ── la memoria del jugador: precarga y volcado ───────────────────────

        // Un archivo del journal guardado, de vuelta al MEMFS. Se llama ANTES de Arrancar.
        [JSExport]
        public static string Precargar(string ruta, string base64)
        {
            try
            {
                string full = Path.Combine(Raiz, ruta.TrimStart('/'));
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                byte[] bytes = Convert.FromBase64String(base64);
                File.WriteAllBytes(full, bytes);
                _precargados++;
                _bytesPrecargados += bytes.Length;
                return "ok";
            }
            catch (Exception failure)
            {
                return "ERROR " + failure.GetType().Name + ": " + failure.Message;
            }
        }

        // El arbol entero del journal, para llevarlo al destino duradero. Null si no hubo
        // comandos nuevos desde el ultimo volcado: no hay nada que escribir.
        [JSExport]
        public static string Volcar()
        {
            if (!_dirty || !Directory.Exists(Raiz)) return null;

            Dictionary<string, string> arbol = new Dictionary<string, string>();
            foreach (string f in Directory.EnumerateFiles(Raiz, "*", SearchOption.AllDirectories))
                arbol[Path.GetRelativePath(Raiz, f).Replace('\\', '/')] = Convert.ToBase64String(File.ReadAllBytes(f));

            _dirty = false;
            return JsonSerializer.Serialize(arbol);
        }

        // El arbol entero del journal para LLEVARSELO (al comprobante): no toca _dirty.
        [JSExport]
        public static string ExportarJournal()
        {
            if (!Directory.Exists(Raiz)) return "{}";
            Dictionary<string, string> arbol = new Dictionary<string, string>();
            foreach (string f in Directory.EnumerateFiles(Raiz, "*", SearchOption.AllDirectories))
                arbol[Path.GetRelativePath(Raiz, f).Replace('\\', '/')] = Convert.ToBase64String(File.ReadAllBytes(f));
            return JsonSerializer.Serialize(arbol);
        }

        [JSExport]
        public static string Arrancar()
        {
            try
            {
                Directory.CreateDirectory(Raiz);

                _journal = new PerformanceV2("jugador", typeof(BingoGame).Assembly)
                    .ConfigureStorage(DatabaseType.FileSystem, "path=" + Raiz)
                    .Logger(new Voz())
                    .Start();

                int archivos = Directory.EnumerateFiles(Raiz, "*", SearchOption.AllDirectories).Count();

                return JsonSerializer.Serialize(new
                {
                    ok = true,
                    version = Environment.Version.ToString(),
                    archivos,
                    precargados = _precargados,
                });
            }
            catch (Exception failure)
            {
                return JsonSerializer.Serialize(new { ok = false, error = failure.GetType().Name + ": " + failure.Message });
            }
        }

        // Los MISMOS nombres que PlayerEntry: juego_<id seguro>, ronda_<id seguro>, funcion-<id>.
        private static string Safe(string festivalId) =>
            new string((festivalId ?? "sinfuncion").Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

        private static string UpgradeName(string festivalId)
        {
            char[] clean = ("funcion-" + (festivalId ?? "suelta")).ToCharArray();
            for (int i = 0; i < clean.Length; i++)
                if (!char.IsLetterOrDigit(clean[i]) && clean[i] != '-') clean[i] = '-';
            return new string(clean);
        }

        // Apuntar al bingo sin tocar el diario: para leer lo rehidratado antes de hablar con nadie.
        [JSExport]
        public static string Preparar(string festivalId)
        {
            string safe = Safe(festivalId);
            _game = "juego_" + safe;
            _slot = "ronda_" + safe;
            return _game;
        }

        [JSExport]
        public static string Entrar(string festivalId, string cartonesJson, string sejuega, int ronda, string bolitasJson)
        {
            try
            {
                Preparar(festivalId);

                // Un diario guardado con las figuras pegadas al cuerpo del upgrade reventaria
                // con este texto nuevo; si la mesa ya esta, no hay upgrade que mandar.
                if (!AlreadyHasTheGame())
                {
                    Cmd("upgrade('" + UpgradeName(festivalId) + "') { " + _game + " = BingoGame(); "
                        + _game + ".StartRound(@figurasEnJuego, 'OnClaim'); }",
                        p => p.UserParameter<IEnumerable<string>>("figurasEnJuego", Figuras(sejuega).ToList()));
                }

                Cmd(_slot + " = @ronda;", p => p.UserParameter<int>("ronda", ronda));

                return PonerAlDia(cartonesJson, sejuega, ronda, bolitasJson);
            }
            catch (Exception failure)
            {
                return Error("Entrar", failure);
            }
        }

        [JSExport]
        public static string PonerAlDia(string cartonesJson, string sejuega, int ronda, string bolitasJson)
        {
            try
            {
                long[] cards = JsonSerializer.Deserialize<long[]>(cartonesJson) ?? Array.Empty<long>();
                int[] balls = JsonSerializer.Deserialize<int[]>(bolitasJson) ?? Array.Empty<int>();
                string told = string.Join(",", Figuras(sejuega));

                int recovered = 0;
                HashSet<long> have = new HashSet<long>(CardsInPlay());
                foreach (long card in cards)
                {
                    if (have.Contains(card)) continue;
                    Cmd(_game + ".PutCardInPlay(@numero);", p => p.UserParameter<int>("numero", (int)card));
                    recovered++;
                }

                string current = string.Join(",", FiguresInPlay());
                int localRound = AsInt(Qry("print " + _slot + " r;"), "r");
                bool startOver = recovered > 0
                    || !string.Equals(current, told, StringComparison.OrdinalIgnoreCase)
                    || ronda != localRound;

                if (startOver)
                {
                    Cmd(_game + ".MoveOnToRound(@figuras, 'OnClaim');", p => p.UserParameter<string>("figuras", told));
                    Cmd(_slot + " = @ronda;", p => p.UserParameter<int>("ronda", ronda));
                }

                int already = AsInt(Qry("print " + _game + ".DrawnCount n;"), "n");
                int nuevas = 0;
                for (int i = already; i < balls.Length; i++)
                {
                    Cmd(_game + ".RegisterDraw(@numero);", p => p.UserParameter<int>("numero", balls[i]));
                    nuevas++;
                }

                return Tablero(recovered, startOver, nuevas);
            }
            catch (Exception failure)
            {
                return Error("PonerAlDia", failure);
            }
        }

        [JSExport]
        public static string Estado()
        {
            try { return Tablero(0, false, 0); }
            catch (Exception failure) { return Error("Estado", failure); }
        }

        // Las figuras que ofrece la app, con el nombre y la pista que ve la gente (BingoApp/OnOffer.cs).
        private static readonly (string Figure, string Called, string Says)[] Catalogo =
        {
            ("AnyLine", "Una linea", "una linea cualquiera: fila, columna o diagonal"),
            ("AnyRow", "Una fila", "una fila horizontal cualquiera (5 numeros)"),
            ("AnyColumn", "Una columna", "una columna cualquiera (5 numeros)"),
            ("Diagonal", "Una diagonal", "cualquiera de las dos diagonales"),
            ("FourCorners", "Las cuatro esquinas", "las cuatro esquinas (4 numeros)"),
            ("AnySquare", "Un cuadro", "un cuadro de 2x2 en cualquier parte (4 numeros)"),
            ("L", "La ele", "la ele: la primera columna y la ultima fila"),
            ("FullCard", "Carton lleno", "el carton lleno (24 numeros, la mas larga)"),
        };

        // El mensaje "figures" que la app le manda a la pantalla del jugador.
        [JSExport]
        public static string FigurasConocidas()
        {
            var known = Catalogo.Where(one => BingoGame.KnowsFigure(one.Figure)).ToList();
            return JsonSerializer.Serialize(new
            {
                kind = "figures",
                figures = known.Select(one => one.Figure).ToList(),
                names = known.ToDictionary(one => one.Figure, one => one.Called),
                hints = known.ToDictionary(one => one.Figure, one => one.Says),
            });
        }

        // El tablero en la forma que arma PlayerScreen.StateJson: grilla por columnas con el centro en 0, y
        // por figura cuanto tiene, cuanto le falta y que numeros espera (BingoGame.ClosestFor).
        [JSExport]
        public static string Pantalla()
        {
            try
            {
                int draws = AsInt(Qry("print " + _game + ".DrawnCount n;"), "n");
                List<int> order = new List<int>();
                for (int i = 1; i <= draws; i++)
                    order.Add(AsInt(Qry("print " + _game + ".DrawnAt(@posicion) n;", p => p.UserParameter<int>("posicion", i)), "n"));
                HashSet<int> drawn = new HashSet<int>(order);
                List<string> figs = FiguresInPlay();
                var cards = CardsInPlay().Select(card =>
                {
                    Cardface face = Cardface.Of(new CardNumber(card));
                    int[][] grid = new int[5][];
                    for (int band = 0; band < 5; band++)
                    {
                        IReadOnlyList<int> column = face.Column(band);
                        grid[band] = new int[5];
                        for (int row = 0; row < 5; row++)
                            grid[band][row] = band == 2 && row == 2 ? 0 : column[band == 2 && row > 2 ? row - 1 : row];
                    }
                    var figures = figs.Select(figure =>
                    {
                        IReadOnlyList<int> needs = BingoGame.ClosestFor(card, figure, drawn);
                        bool called = AsBool(Qry("print " + _game + ".HasCompleted(@carton, @figura) g;", p => { p.UserParameter<long>("carton", card); p.UserParameter<string>("figura", figure); }), "g");
                        return new
                        {
                            name = figure,
                            have = needs.Count(n => drawn.Contains(n)),
                            need = needs.Count,
                            called,
                            waiting = needs.Where(n => !drawn.Contains(n)).ToList(),
                        };
                    }).ToList();
                    return new { number = card, grid, figures };
                }).ToList();
                return JsonSerializer.Serialize(new
                {
                    ok = true,
                    drawn = drawn.OrderBy(n => n).ToList(),
                    drawOrder = order,
                    lastDraw = order.Count > 0 ? order[order.Count - 1] : 0,
                    playing = figs,
                    cards,
                });
            }
            catch (Exception failure)
            {
                return Error("Pantalla", failure);
            }
        }

        private static string Tablero(int recovered, bool startOver, int nuevas)
        {
            string read = Qry("{ print " + _game + ".CardsInPlayCount cards; print " + _game + ".DrawnCount draws; "
                + "print " + _game + ".RoundNumber round; print " + _game + ".Pulse pulse; }");

            int draws = AsInt(read, "draws");
            // DrawnAt, CardInPlayAt y FigureInPlayAt cuentan desde 1 (BingoGame.cs:304-322).
            List<int> drawn = new List<int>();
            for (int i = 1; i <= draws; i++)
                drawn.Add(AsInt(Qry("print " + _game + ".DrawnAt(@posicion) n;", p => p.UserParameter<int>("posicion", i)), "n"));

            List<string> figs = FiguresInPlay();

            var cartones = CardsInPlay().Select(card =>
            {
                Cardface face = Cardface.Of(new CardNumber(card));
                int[][] columnas = new int[Cardface.BandCount][];
                for (int band = 0; band < Cardface.BandCount; band++) columnas[band] = face.Column(band).ToArray();

                Dictionary<string, bool> completa = new Dictionary<string, bool>();
                foreach (string f in figs)
                    completa[f] = AsBool(Qry("print " + _game + ".HasCompleted(@carton, @figura) g;", p => { p.UserParameter<long>("carton", card); p.UserParameter<string>("figura", f); }), "g");

                return new { numero = card, columnas, completa };
            }).ToList();

            return JsonSerializer.Serialize(new
            {
                ok = true,
                ronda = AsInt(read, "round"),
                rondaGuardada = AsInt(Qry("print " + _slot + " r;"), "r"),
                pulso = Read(read, "pulse"),
                bolitas = drawn,
                figuras = figs,
                cartones,
                asientos = _asientos,
                precargados = _precargados,
                repuestos = recovered,
                rehizoLaRonda = startOver,
                bolitasNuevas = nuevas,
                voz = _voz.Skip(Math.Max(0, _voz.Count - 40)).ToList(),
            });
        }

        // ── el diario ────────────────────────────────────────────────────────

        private static void Cmd(string script, Action<Parameters> parameters = null)
        {
            var invocation = _journal.Using(script);
            if (parameters is not null) invocation = invocation.WithParameters(parameters);
            invocation.PerformCommand();
            _asientos++;
            _dirty = true;
        }

        private static string Qry(string script, Action<Parameters> parameters = null) =>
            parameters is null
                ? _journal.PerformQry(script)
                : _journal.Using(script).WithParameters(parameters).PerformQuery();

        private static bool AlreadyHasTheGame()
        {
            try
            {
                Qry("print " + _game + ".RoundNumber r;");
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static List<long> CardsInPlay()
        {
            int howMany = AsInt(Qry("print " + _game + ".CardsInPlayCount n;"), "n");
            List<long> mine = new List<long>();
            for (int i = 1; i <= howMany; i++)
                mine.Add(AsLong(Qry("print " + _game + ".CardInPlayAt(@posicion) n;", p => p.UserParameter<int>("posicion", i)), "n"));
            return mine;
        }

        private static List<string> FiguresInPlay()
        {
            int howMany = AsInt(Qry("print " + _game + ".FiguresInPlayCount n;"), "n");
            List<string> figs = new List<string>();
            for (int i = 1; i <= howMany; i++)
                figs.Add(Read(Qry("print " + _game + ".FigureInPlayAt(@posicion) f;", p => p.UserParameter<int>("posicion", i)), "f"));
            return figs;
        }

        private static IEnumerable<string> Figuras(string sejuega) =>
            (sejuega ?? "AnyLine").Split(',').Select(f => f.Trim()).Where(f => f.Length > 0);

        private static string Error(string where, Exception failure)
        {
            _voz.Add("ERROR en " + where + ": " + failure.GetType().Name + ": " + failure.Message);
            return JsonSerializer.Serialize(new { ok = false, donde = where, error = failure.GetType().Name + ": " + failure.Message,
                voz = _voz.Skip(Math.Max(0, _voz.Count - 40)).ToList(), asientos = _asientos, precargados = _precargados });
        }

        private static string Read(string json, string label)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                return doc.RootElement.TryGetProperty(label, out JsonElement value)
                    ? (value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString())
                    : null;
            }
            catch (Exception) { return null; }
        }

        private static int AsInt(string json, string label) => int.TryParse(Read(json, label), out int n) ? n : 0;
        private static long AsLong(string json, string label) => long.TryParse(Read(json, label), out long n) ? n : 0L;
        private static bool AsBool(string json, string label) => string.Equals(Read(json, label), "true", StringComparison.OrdinalIgnoreCase);
    }
}
