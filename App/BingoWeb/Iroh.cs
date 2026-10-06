#if IROH_BROWSER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.JavaScript;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Choreography.StageManager;
using Choreography.Theater;
using Choreography.Told;
using Choreography.Transport.Brokered;
using Choreography.Transport.Iroh.Browser;
using Puppeteer;

namespace BingoWeb
{
    // La entrada por iroh del jugador web: lo mismo que hace LivePlayer con IrohDoor, con el bus del
    // navegador (BrowserIrohMessageBroker) en vez del IrohMessageBroker nativo. Se engancha por una de las
    // ocho puertas, pide el alta por el bus y queda escuchando las bolitas: el organizador se las empuja y
    // la pagina ya no tiene que preguntar cada 3 s.
    public static partial class Player
    {
        private const int Puertas = 8;

        private static BrowserIrohMessageBroker _bus;
        private static TellBindingTable _atados;
        private static readonly List<IDisposable> _oidos = new List<IDisposable>();
        private static string _escuchando;

        [JSImport("globalThis.__bingoNotice")]
        private static partial void Avisar(string que);

        [JSExport]
        public static async Task<string> EntrarPorIroh(string modulo, string ticket, string funcion, string pedido, bool rehacer)
        {
            try
            {
                if (_bus == null)
                {
                    _bus = await BrowserIrohMessageBroker.StartAsync(modulo, new PerformerId(Guid.NewGuid()), new Voz());
                    _bus.DialTimeout = TimeSpan.FromSeconds(6);
                    _bus.LinkClosed += motivo =>
                    {
                        _voz.Add("iroh: se cayo el enlace (" + motivo + ")");
                        if (!_reenganchando) try { Avisar("cerrado"); } catch (Exception) { }
                    };
                }

                _reenganchando = true;
                try
                {
                    // De vuelta de una pestana congelada: el endpoint perdio su relay mientras no podia correr, y
                    // marcar por el espera hasta vencerse. Uno nuevo se engancha al relay en un par de segundos.
                    if (rehacer)
                    {
                        await _bus.RebindAsync();
                        _voz.Add("iroh: endpoint nuevo " + _bus.EndpointId.Substring(0, 10));
                    }

                    // Volver de una pestana congelada: el enlace puede figurar vivo y ya no contestar. Se le
                    // pregunta con poca paciencia; si calla, se cierra y se marca de nuevo por una puerta.
                    bool habia = _bus.LinkCount > 0;
                    if (!habia) await Engancharse(ticket, funcion);

                    string respuesta = await AlistarseSinTirar(funcion, pedido, habia ? 8 : 15);
                    if (respuesta == null && habia)
                    {
                        await _bus.CloseLinksAsync("el enlace no contesto");
                        await Engancharse(ticket, funcion);
                        respuesta = await AlistarseSinTirar(funcion, pedido, 30);
                    }
                    if (respuesta == null) return JsonSerializer.Serialize(new { ok = false, error = "el organizador no contesto por iroh" });

                    Escuchar(funcion);
                    return respuesta;
                }
                finally
                {
                    _reenganchando = false;
                }
            }
            catch (Exception failure)
            {
                return JsonSerializer.Serialize(new { ok = false, error = failure.GetType().Name + ": " + failure.Message });
            }
        }

        private static bool _reenganchando;

        private static async Task<string> AlistarseSinTirar(string funcion, string pedido, int segundos)
        {
            try { return await Alistarse(funcion, pedido, segundos); }
            catch (System.IO.IOException) { return null; }
        }

        [JSExport]
        public static int EnlacesIroh() => _bus?.LinkCount ?? 0;

        private static async Task Engancharse(string ticket, string funcion)
        {
            Exception ultima = null;
            int desde = Random.Shared.Next(Puertas);
            for (int intento = 0; intento < Puertas; intento++)
            {
                int puerta = (desde + intento) % Puertas;
                try
                {
                    await _bus.LinkAsync("iroh://" + ticket + "/128/" + Puerta(funcion, puerta).ToString("N"));
                    _voz.Add("iroh: entre por la puerta " + puerta + " del organizador");
                    return;
                }
                catch (InvalidOperationException ocupada) { ultima = ocupada; }
            }
            throw new InvalidOperationException("ninguna puerta iroh me abrio", ultima);
        }

        // Lo que IrohDoor.EnlistAsync: el pedido va por el bus y la respuesta vuelve en su propio tema.
        private static async Task<string> Alistarse(string funcion, string pedido, int segundos)
        {
            string numero = Guid.NewGuid().ToString("N");
            var oida = new TaskCompletionSource<BrokerRecord>(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable oreja = _bus.Subscribe("funcion." + funcion + ".alistado." + numero, r => oida.TrySetResult(r));

            await _bus.ProduceAsync("funcion." + funcion + ".alistarse", numero,
                new Dictionary<string, string> { ["pedido"] = numero }, pedido);

            if (await Task.WhenAny(oida.Task, Task.Delay(TimeSpan.FromSeconds(segundos))) != oida.Task) return null;

            BrokerRecord r = oida.Task.Result;
            bool ok = r.Headers != null && r.Headers.TryGetValue("ok", out string dijo) && dijo == "1";
            if (ok) return r.Value;

            string porque = r.Headers != null && r.Headers.TryGetValue("no", out string p) ? p : "no_pude";
            return JsonSerializer.Serialize(new { no = porque, dice = r.Value });
        }

        // Las bolitas por el MISMO oyente y con el MISMO cuerpo que LivePlayer: se comparan contra la ronda
        // del organizador guardada en su ranura, y una tardia se anota en vez de tirar.
        private static void Escuchar(string funcion)
        {
            if (_escuchando == funcion) return;
            _escuchando = funcion;
            Preparar(funcion);

            string rol = "Jugador_" + Safe(funcion);
            string tema = "funcion." + funcion + ".canta";
            _atados ??= new TellBindingTable();
            _atados.Bind(rol, tema);

            _oidos.Add(_journal.ListenAs(rol, _atados, _bus)
                .Told("Bolita").With<int>("numero").With<int>("ronda")
                    .Command($"if ({_slot} == @ronda) {{ {_game}.RegisterDraw(@numero); }}"
                        + " else { bolitaTardia = @numero; rondaTardia = @ronda; };")
                .Told("Correccion").With<int>("malo").With<int>("bueno")
                    .Command($"{_game}.CorrectDraw(@malo, @bueno);")
                .Told("Ronda").With<string>("figuras")
                    .Command($"{_game}.MoveOnToRound(@figuras, 'OnClaim');")
                .Start());

            _oidos.Add(_bus.Subscribe(tema, r => { _ = AvisarDespues(); }));
        }

        private static async Task AvisarDespues()
        {
            await Task.Delay(50);
            _dirty = true;
            try { Avisar("aviso"); } catch (Exception) { }
        }

        // El nonce de cada puerta, igual que IrohDoor.DoorNonce: MD5 de "bingo-door|{funcion}|{puerta}".
        // A mano: el MD5 de .NET no existe en el navegador.
        private static Guid Puerta(string funcion, int puerta) =>
            new Guid(Md5(Encoding.UTF8.GetBytes("bingo-door|" + funcion + "|" + puerta)));

        private static readonly int[] Giros =
        {
            7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22,
            5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20,
            4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23,
            6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21,
        };

        private static readonly uint[] Constantes = Enumerable.Range(0, 64)
            .Select(i => (uint)(long)Math.Floor(Math.Abs(Math.Sin(i + 1)) * 4294967296.0)).ToArray();

        private static byte[] Md5(byte[] mensaje)
        {
            int largo = ((mensaje.Length + 8) / 64 + 1) * 64;
            byte[] m = new byte[largo];
            Array.Copy(mensaje, m, mensaje.Length);
            m[mensaje.Length] = 0x80;
            BitConverter.GetBytes((ulong)mensaje.Length * 8).CopyTo(m, largo - 8);

            uint a0 = 0x67452301, b0 = 0xefcdab89, c0 = 0x98badcfe, d0 = 0x10325476;
            for (int bloque = 0; bloque < largo; bloque += 64)
            {
                uint a = a0, b = b0, c = c0, d = d0;
                for (int i = 0; i < 64; i++)
                {
                    uint f; int g;
                    if (i < 16) { f = (b & c) | (~b & d); g = i; }
                    else if (i < 32) { f = (d & b) | (~d & c); g = (5 * i + 1) % 16; }
                    else if (i < 48) { f = b ^ c ^ d; g = (3 * i + 5) % 16; }
                    else { f = c ^ (b | ~d); g = (7 * i) % 16; }
                    f = f + a + Constantes[i] + BitConverter.ToUInt32(m, bloque + g * 4);
                    a = d; d = c; c = b;
                    b += (f << Giros[i]) | (f >> (32 - Giros[i]));
                }
                a0 += a; b0 += b; c0 += c; d0 += d;
            }

            byte[] hash = new byte[16];
            BitConverter.GetBytes(a0).CopyTo(hash, 0);
            BitConverter.GetBytes(b0).CopyTo(hash, 4);
            BitConverter.GetBytes(c0).CopyTo(hash, 8);
            BitConverter.GetBytes(d0).CopyTo(hash, 12);
            return hash;
        }
    }
}
#endif
