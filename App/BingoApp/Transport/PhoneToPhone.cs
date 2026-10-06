using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Choreography.Transport.Brokered;

namespace BingoApp.Transport
{
    public sealed class PhoneToPhone : IMessageBroker, IDisposable
    {
        private const string TellPath = "/tell/";

        private static readonly HttpClient Wire = NewClient();

        private static HttpClient NewClient()
        {
            int seconds;
            if (!int.TryParse(Environment.GetEnvironmentVariable("BINGO_TIMEOUT"), out seconds)
                || seconds <= 0) seconds = 15;

            SocketsHttpHandler handler = new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(seconds),
                UseProxy = false,
                AllowAutoRedirect = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            };

            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(seconds) };
        }

        public async Task<bool> CanReachAsync(string peer, CancellationToken token = default)
        {
            try
            {
                using HttpRequestMessage knock =
                    new HttpRequestMessage(HttpMethod.Get, Address(peer));

                using HttpResponseMessage answer =
                    await Wire.SendAsync(knock, token).ConfigureAwait(false);

                return answer.IsSuccessStatusCode && answer.Headers.Contains(SignatureHeader);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool WaitFor(IEnumerable<string> peers, TimeSpan howLong)
        {
            long deadline = Environment.TickCount64 + (long)howLong.TotalMilliseconds;

            foreach (string peer in peers)
            {
                bool reached = false;

                while (Environment.TickCount64 < deadline)
                {
                    if (CanReachAsync(peer).GetAwaiter().GetResult()) { reached = true; break; }
                    Thread.Sleep(400);
                }

                if (!reached)
                {
                    _say("[" + _whoAmI + "] " + Texts.T("transporte.no-logre-saludar", ("a", peer)));
                    return false;
                }

                _say("[" + _whoAmI + "] " + peer + " contesta");
            }

            return true;
        }

        private readonly ConcurrentDictionary<string, List<string>> _routes =
            new ConcurrentDictionary<string, List<string>>(StringComparer.Ordinal);

        private readonly ConcurrentDictionary<string, List<Action<BrokerRecord>>> _local =
            new ConcurrentDictionary<string, List<Action<BrokerRecord>>>(StringComparer.Ordinal);

        private readonly CancellationTokenSource _life = new CancellationTokenSource();

        private readonly string _whoAmI;

        private readonly int _port;

        private readonly string _outbox;

        private readonly Action<string> _say;

        private HttpListener _ears;

        private long _sent;

        private long _received;

        private long _parked;

        private long _rescued;

        private long _dropped;

        public PhoneToPhone(string whoAmI, int port, string outboxDir, Action<string> say = null)
        {
            if (string.IsNullOrWhiteSpace(whoAmI)) throw new ArgumentNullException(nameof(whoAmI));

            _whoAmI = whoAmI;
            _port = port;
            _outbox = outboxDir;
            _say = say ?? (line => { });

            if (!string.IsNullOrWhiteSpace(_outbox)) Directory.CreateDirectory(_outbox);

            SweepTheStrayRoutesFile();
            RestoreRoutes();
        }

        /// <summary>
        /// Saca de la bandeja el rutas.json que dejo una version anterior.
        ///
        /// Durante un rato este archivo se guardo ADENTRO de la bandeja de salida, que se barre
        /// por *.json. El barrido lo tomaba por un mensaje parqueado y lo deserializaba en un
        /// envio con todo en nulo -- no tira al leerlo, revienta mas abajo, el catch se lo traga,
        /// y como nadie lo borra vuelve a pasar cada segundo mientras la app este viva.
        ///
        /// Al telefono que ya corrio esa version no lo arregla mover el archivo nuevo de lugar:
        /// el viejo se queda ahi. Por eso se barre, una vez, al arrancar.
        /// </summary>
        private void SweepTheStrayRoutesFile()
        {
            if (string.IsNullOrWhiteSpace(_outbox)) return;

            try
            {
                string stray = Path.Combine(_outbox, "rutas.json");

                if (!File.Exists(stray)) return;

                // Se aprovecha antes de tirarlo: es exactamente lo que este telefono sabia.
                string moveTo = RoutesFile;

                if (moveTo is not null && !File.Exists(moveTo)) File.Move(stray, moveTo);
                else File.Delete(stray);
            }
            catch (Exception)
            {
            }
        }

        public string WhoAmI => _whoAmI;

        public long Sent => Interlocked.Read(ref _sent);

        public long Received => Interlocked.Read(ref _received);

        public long Parked => Interlocked.Read(ref _parked);

        public long Rescued => Interlocked.Read(ref _rescued);

        public long Dropped => Interlocked.Read(ref _dropped);

        public int PendingInOutbox =>
            string.IsNullOrWhiteSpace(_outbox) || !Directory.Exists(_outbox)
                ? 0
                : Directory.GetFiles(_outbox, "*.json").Length;

        public void RouteTopicTo(string topic, string peer)
        {
            if (string.IsNullOrWhiteSpace(topic)) throw new ArgumentNullException(nameof(topic));
            if (string.IsNullOrWhiteSpace(peer)) throw new ArgumentNullException(nameof(peer));

            List<string> peers = _routes.GetOrAdd(topic, _ => new List<string>());
            lock (peers)
            {
                if (!peers.Contains(peer, StringComparer.OrdinalIgnoreCase)) peers.Add(peer);
            }

            SaveRoutes();
        }

        /// <summary>
        /// AL LADO de la bandeja, NUNCA adentro.
        ///
        /// Adentro fue lo primero que se probo y esta mal: la bandeja se barre por *.json y cada
        /// vuelta levantaba este archivo como si fuera un mensaje parqueado, lo deserializaba en
        /// un envio sin destinatario, reventaba, y el catch se lo tragaba. Para siempre, una vez
        /// por segundo, peleandole el archivo a quien lo estaba escribiendo.
        /// </summary>
        private string RoutesFile =>
            string.IsNullOrWhiteSpace(_outbox) ? null : _outbox.TrimEnd('/', '\\') + "-rutas.json";

        private readonly object _routesGuard = new object();

        /// <summary>
        /// Guardar a quien le hablo por cada topico.
        ///
        /// Estas rutas eran memoria pura, y por eso al organizador que se le reiniciaba la app
        /// se le caia la velada entera sin ruido: volvia sin la direccion de nadie, asi que las
        /// bolitas no le llegaban a ningun jugador y a las colaboradoras no les podia entregar
        /// cartones. Todo lo demas parecia bien -- el diario intacto, la pantalla normal -- y
        /// simplemente no salia nada por el cable.
        ///
        /// La direccion no vive en el diario y no tiene por que: no es un hecho del pueblo,
        /// es donde esta parado un telefono esta noche. Pero alguien la tiene que recordar, y
        /// el sustrato dice que ese alguien es el anfitrion.
        ///
        /// Una direccion vieja no hace daño: mandarle a un telefono que ya no esta falla como
        /// cualquier otro envio, y el dia que esa persona vuelve a presentarse la pisa.
        /// </summary>
        private void SaveRoutes()
        {
            string file = RoutesFile;
            if (file is null) return;

            try
            {
                Dictionary<string, string[]> snapshot = new(StringComparer.Ordinal);

                foreach (KeyValuePair<string, List<string>> each in _routes)
                    lock (each.Value) snapshot[each.Key] = each.Value.ToArray();

                string text = JsonSerializer.Serialize(snapshot);

                // Un candado y escritura en dos tiempos. Esto se llama una vez por topico y por
                // persona --con treinta jugadores entrando son noventa escrituras seguidas, desde
                // hilos del pool-- y el que lee descarta el archivo ENTERO si esta a medio
                // escribir. Sin esto, un mal momento y el organizador arranca sin las rutas de
                // nadie, que es justo lo que este archivo vino a evitar.
                lock (_routesGuard)
                {
                    string half = file + ".medio";

                    File.WriteAllText(half, text, Encoding.UTF8);
                    File.Move(half, file, overwrite: true);
                }
            }
            catch (Exception)
            {
                // Que no se pueda guardar no puede tumbar una velada en curso: lo que hay en
                // memoria sigue sirviendo, y lo unico que se pierde es el reinicio prolijo.
            }
        }

        private void RestoreRoutes()
        {
            string file = RoutesFile;
            if (file is null || !File.Exists(file)) return;

            try
            {
                Dictionary<string, string[]> saved;

                lock (_routesGuard)
                    saved = JsonSerializer.Deserialize<Dictionary<string, string[]>>(
                        File.ReadAllText(file, Encoding.UTF8));

                if (saved is null) return;

                foreach (KeyValuePair<string, string[]> each in saved)
                {
                    if (each.Value is null) continue;

                    List<string> peers = _routes.GetOrAdd(each.Key, _ => new List<string>());
                    lock (peers)
                        foreach (string peer in each.Value)
                            if (!string.IsNullOrWhiteSpace(peer)
                                && !peers.Contains(peer, StringComparer.OrdinalIgnoreCase))
                                peers.Add(peer);
                }

                _say("[" + _whoAmI + "] " + Texts.T("transporte.recorde-topicos", ("n", saved.Count.ToString())));
            }
            catch (Exception)
            {
                // Un archivo roto se ignora entero: mejor arrancar sin rutas --y que cada
                // quien se vuelva a presentar-- que arrancar con media lista.
            }
        }

        public void SendAcksOf(string topic, string peer) => RouteTopicTo(topic + ".acks", peer);

        /// <summary>
        /// Dejar de mandarle a nadie por ese topico. Es el inverso que le faltaba a
        /// RouteTopicTo, que solo acumula: sin esto, a quien el organizador mando a la casa
        /// le seguian llegando cartones a la mano.
        /// Sirve para topicos de UNA persona (su custodia, su carton). El de las bolitas
        /// NO se corta asi: por ahi reciben todos los jugadores del bingo.
        /// </summary>
        public void StopRouting(string topic)
        {
            if (string.IsNullOrWhiteSpace(topic)) return;

            _routes.TryRemove(topic, out _);
            _routes.TryRemove(topic + ".acks", out _);

            SaveRoutes();
        }

        public IReadOnlyList<string> PeersFor(string topic)
        {
            List<string> peers;
            if (!_routes.TryGetValue(topic, out peers)) return Array.Empty<string>();

            lock (peers) return peers.ToArray();
        }

        public string BoundTo { get; private set; }

        public const string EnlistPath = "/alistarse/";

        public Func<string, string> OnEnlist { get; set; }

        public const string SalePath = "/vendi/";

        public Func<string, string> OnSale { get; set; }

        public const string CallPath = "/cante/";

        public Func<string, string> OnCall { get; set; }

        public const string ExchangePath = "/cambio/";

        public Func<string, string> OnExchange { get; set; }

        public const string HandoverPath = "/traspaso/";

        public Func<string, string> OnHandover { get; set; }

        public const string GiveBackPath = "/devuelvo/";

        public Func<string, string> OnGiveBack { get; set; }

        public const string PleaPath = "/anular/";

        public Func<string, string> OnPlea { get; set; }

        /// <summary>Alguien canta BINGO desde su telefono. Es un aviso, no un veredicto:
        /// quien valida sigue siendo la mesa cuando el organizador resuelve.</summary>
        public const string BingoPath = "/bingo/";

        public Func<string, string> OnBingoClaim { get; set; }

        /// <summary>De quien es este numero. Se pregunta ANTES de cobrar, y es lo unico que
        /// convierte un teclazo en un aviso en vez de en un carton huerfano.</summary>
        public const string WhoIsPath = "/quienes/";

        public Func<string, string> OnWhoIs { get; set; }

        /// <summary>El certificado publico de este telefono, para quien quiera verificar
        /// la firma de un codigo. Solo la parte publica -- el DER, sin llave.</summary>
        public const string CertPath = "/certificado/";

        /// <summary>Se sirve solo si alguien lo puso: un telefono sin llave contesta a ese
        /// GET con el 204 de siempre, igual que una version vieja, y quien pregunte sabe
        /// que ahi no hay firma que verificar.</summary>
        public byte[] PublicCertDer { get; set; }

        public void Listen()
        {
            string[] candidates =
            {
                "http://+:" + _port + TellPath,
                "http://*:" + _port + TellPath,
                "http://localhost:" + _port + TellPath,
                "http://127.0.0.1:" + _port + TellPath
            };

            Exception last = null;

            foreach (string prefix in candidates)
            {
                HttpListener candidate = new HttpListener();
                candidate.Prefixes.Add(prefix);

                candidate.Prefixes.Add(prefix.Replace(TellPath, EnlistPath, StringComparison.Ordinal));
                candidate.Prefixes.Add(prefix.Replace(TellPath, SalePath, StringComparison.Ordinal));
                candidate.Prefixes.Add(prefix.Replace(TellPath, CallPath, StringComparison.Ordinal));
                candidate.Prefixes.Add(prefix.Replace(TellPath, ExchangePath, StringComparison.Ordinal));
                candidate.Prefixes.Add(prefix.Replace(TellPath, HandoverPath, StringComparison.Ordinal));
                candidate.Prefixes.Add(prefix.Replace(TellPath, GiveBackPath, StringComparison.Ordinal));
                candidate.Prefixes.Add(prefix.Replace(TellPath, PleaPath, StringComparison.Ordinal));
                candidate.Prefixes.Add(prefix.Replace(TellPath, BingoPath, StringComparison.Ordinal));
                candidate.Prefixes.Add(prefix.Replace(TellPath, WhoIsPath, StringComparison.Ordinal));
                candidate.Prefixes.Add(prefix.Replace(TellPath, CertPath, StringComparison.Ordinal));

                try
                {
                    candidate.Start();
                    _ears = candidate;
                    BoundTo = prefix;
                    break;
                }
                catch (Exception failure)
                {
                    last = failure;
                    try { candidate.Close(); } catch (Exception) { }
                }
            }

            if (_ears == null)
                throw new InvalidOperationException(
                    Texts.T("transporte.no-pude-escuchar", ("puerto", _port.ToString()), ("motivo", last == null ? "" : last.Message)),
                    last);

            _say(Texts.T("transporte.escucho-en", ("donde", BoundTo)));

            _ = Task.Run(() => AcceptLoopAsync(_life.Token));
            if (!string.IsNullOrWhiteSpace(_outbox)) _ = Task.Run(() => OutboxLoopAsync(_life.Token));
        }

        public Task ProduceAsync(
            string topic,
            string key,
            IReadOnlyDictionary<string, string> headers,
            string value,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<string> peers = PeersFor(topic);

            if (peers.Count == 0)
            {
                Interlocked.Increment(ref _dropped);
                _say("[" + _whoAmI + "] " + Texts.T("transporte.sin-ruta", ("topico", topic)));
                return Task.CompletedTask;
            }

            return FanOutAsync(topic, key, headers, value, peers, cancellationToken);
        }

        private async Task FanOutAsync(
            string topic,
            string key,
            IReadOnlyDictionary<string, string> headers,
            string value,
            IReadOnlyList<string> peers,
            CancellationToken token)
        {
            Wired wired = new Wired
            {
                Topic = topic,
                Key = key,
                Headers = headers == null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string>(headers, StringComparer.Ordinal),
                Value = value
            };

            string body = JsonSerializer.Serialize(wired);

            foreach (string peer in peers)
            {
                if (LooksLikeMe(peer))
                {
                    Deliver(wired);
                    continue;
                }

                if (await InsistAsync(peer, body, token).ConfigureAwait(false))
                {
                    Interlocked.Increment(ref _sent);
                    continue;
                }

                Park(peer, body);
            }
        }

        private async Task<bool> InsistAsync(string peer, string body, CancellationToken token)
        {
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                if (await PostAsync(peer, body, token, attempt == 3).ConfigureAwait(false)) return true;

                try
                {
                    await Task.Delay(120 * attempt, token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return false;
                }
            }

            return false;
        }

        private async Task<bool> PostAsync(string peer, string body, CancellationToken token, bool complain)
        {
            long started = Environment.TickCount64;

            try
            {
                using StringContent content = new StringContent(body, Encoding.UTF8, "application/json");
                using HttpResponseMessage answer =
                    await Wire.PostAsync(Address(peer), content, token).ConfigureAwait(false);

                if (answer.IsSuccessStatusCode) return true;

                if (complain)
                    _say("[" + _whoAmI + "] " + peer + " contesto " + (int)answer.StatusCode);

                return false;
            }
            catch (Exception failure)
            {
                if (complain)
                    _say("[" + _whoAmI + "] " + Texts.T("transporte.no-alcance", ("a", peer), ("ms", (Environment.TickCount64 - started).ToString()), ("error", failure.GetType().Name + " / " + Root(failure).GetType().Name + " / " + Root(failure).Message), ("cancelado", token.IsCancellationRequested.ToString())));

                return false;
            }
        }

        private static Exception Root(Exception failure)
        {
            while (failure.InnerException != null) failure = failure.InnerException;
            return failure;
        }

        public IDisposable Subscribe(string topic, Action<BrokerRecord> onRecord)
        {
            if (string.IsNullOrWhiteSpace(topic)) throw new ArgumentNullException(nameof(topic));
            if (onRecord == null) throw new ArgumentNullException(nameof(onRecord));

            List<Action<BrokerRecord>> handlers = _local.GetOrAdd(topic, _ => new List<Action<BrokerRecord>>());
            lock (handlers) handlers.Add(onRecord);

            return new Unsubscribe(handlers, onRecord);
        }

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                HttpListenerContext call;

                try
                {
                    call = await _ears.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    if (token.IsCancellationRequested) return;
                    continue;
                }

                _ = Task.Run(() => Answer(call), CancellationToken.None);
            }
        }

        public const string SignatureHeader = "X-Bingo-Who-Am-I";

        public const string WebPlayerSite = "bingo-velada.pages.dev";

        public const string WebPlayerMirror = "bingo-velada.netlify.app";

        public static readonly string[] WebPlayerSites = { WebPlayerSite, WebPlayerMirror };

        private static bool IsWebPlayerPath(string path) =>
            path.StartsWith(EnlistPath, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(BingoPath, StringComparison.OrdinalIgnoreCase);

        private static bool IsWebPlayerOrigin(string origin)
        {
            if (string.IsNullOrEmpty(origin) || !Uri.TryCreate(origin, UriKind.Absolute, out Uri site)) return false;
            if (site.Scheme != Uri.UriSchemeHttps) return false;
            return WebPlayerSites.Any(known =>
                string.Equals(site.Host, known, StringComparison.OrdinalIgnoreCase)
                || site.Host.EndsWith("." + known, StringComparison.OrdinalIgnoreCase)
                || site.Host.EndsWith("--" + known, StringComparison.OrdinalIgnoreCase));
        }

        private static bool LetTheWebPlayerRead(HttpListenerContext call, string path)
        {
            string origin = call.Request.Headers["Origin"];
            if (!IsWebPlayerPath(path) || !IsWebPlayerOrigin(origin)) return false;

            call.Response.Headers["Access-Control-Allow-Origin"] = origin;
            call.Response.Headers["Vary"] = "Origin";
            call.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
            call.Response.Headers["Access-Control-Expose-Headers"] = SignatureHeader;
            return true;
        }

        private void Answer(HttpListenerContext call)
        {
            try
            {
                string requested = call.Request.Url?.AbsolutePath ?? string.Empty;

                if (string.Equals(call.Request.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    if (LetTheWebPlayerRead(call, requested))
                    {
                        call.Response.Headers["Access-Control-Allow-Methods"] = "POST";
                        call.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
                        call.Response.Headers["Access-Control-Max-Age"] = "600";
                    }
                    call.Response.StatusCode = 204;
                    return;
                }

                LetTheWebPlayerRead(call, requested);

                if (string.Equals(call.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    call.Response.Headers[SignatureHeader] = _whoAmI;

                    string asking = call.Request.Url?.AbsolutePath ?? string.Empty;

                    if (PublicCertDer is not null
                        && asking.StartsWith(CertPath, StringComparison.OrdinalIgnoreCase))
                    {
                        byte[] der = Encoding.ASCII.GetBytes(Convert.ToBase64String(PublicCertDer));

                        call.Response.StatusCode = 200;
                        call.Response.ContentType = "text/plain";
                        call.Response.ContentLength64 = der.Length;
                        call.Response.OutputStream.Write(der, 0, der.Length);
                        return;
                    }

                    // El resto de los GET siguen siendo el saludo de siempre: 204 con el
                    // nombre en el header. CanReachAsync depende de ese contrato.
                    call.Response.StatusCode = 204;
                    return;
                }

                string path = call.Request.Url?.AbsolutePath ?? string.Empty;

                if (path.StartsWith(EnlistPath, StringComparison.OrdinalIgnoreCase))
                {
                    Handle(call, OnEnlist, Texts.T("transporte.fallo.entrar"));
                    return;
                }

                if (path.StartsWith(SalePath, StringComparison.OrdinalIgnoreCase))
                {
                    Handle(call, OnSale, Texts.T("transporte.fallo.venta"));
                    return;
                }

                if (path.StartsWith(CallPath, StringComparison.OrdinalIgnoreCase))
                {
                    Handle(call, OnCall, Texts.T("transporte.fallo.canto"));
                    return;
                }

                if (path.StartsWith(ExchangePath, StringComparison.OrdinalIgnoreCase))
                {
                    Handle(call, OnExchange, Texts.T("transporte.fallo.cambio"));
                    return;
                }

                if (path.StartsWith(HandoverPath, StringComparison.OrdinalIgnoreCase))
                {
                    Handle(call, OnHandover, Texts.T("transporte.fallo.traspaso"));
                    return;
                }

                if (path.StartsWith(GiveBackPath, StringComparison.OrdinalIgnoreCase))
                {
                    Handle(call, OnGiveBack, Texts.T("transporte.fallo.devolucion"));
                    return;
                }

                if (path.StartsWith(PleaPath, StringComparison.OrdinalIgnoreCase))
                {
                    Handle(call, OnPlea, Texts.T("transporte.fallo.pedido"));
                    return;
                }

                if (path.StartsWith(BingoPath, StringComparison.OrdinalIgnoreCase))
                {
                    Handle(call, OnBingoClaim, Texts.T("transporte.fallo.bingo"));
                    return;
                }

                if (path.StartsWith(WhoIsPath, StringComparison.OrdinalIgnoreCase))
                {
                    Handle(call, OnWhoIs, Texts.T("transporte.fallo.quien-es"));
                    return;
                }

                string body;
                using (StreamReader reader = new StreamReader(call.Request.InputStream, Encoding.UTF8))
                    body = reader.ReadToEnd();

                Wired wired = JsonSerializer.Deserialize<Wired>(body);

                if (wired != null && !string.IsNullOrEmpty(wired.Topic))
                {
                    Interlocked.Increment(ref _received);
                    Deliver(wired);
                }

                call.Response.StatusCode = 204;
            }
            catch (Exception failure)
            {
                _say("[" + _whoAmI + "] " + Texts.T("transporte.no-pude-atender", ("motivo", failure.Message)));
                try { call.Response.StatusCode = 500; } catch (Exception) { }
            }
            finally
            {
                try { call.Response.Close(); } catch (Exception) { }
            }
        }

        private void Deliver(Wired wired)
        {
            List<Action<BrokerRecord>> handlers;
            if (!_local.TryGetValue(wired.Topic, out handlers)) return;

            Action<BrokerRecord>[] copy;
            lock (handlers) copy = handlers.ToArray();

            BrokerRecord record = new BrokerRecord(
                wired.Topic,
                wired.Key,
                wired.Headers ?? new Dictionary<string, string>(),
                wired.Value);

            foreach (Action<BrokerRecord> handler in copy)
            {
                try
                {
                    handler(record);
                }
                catch (Exception failure)
                {
                    _say("[" + _whoAmI + "] " + Texts.T("transporte.oyente-fallo", ("topico", wired.Topic), ("motivo", failure.Message)));
                }
            }
        }

        private void Park(string peer, string body)
        {
            if (string.IsNullOrWhiteSpace(_outbox)) return;

            try
            {
                Waiting parked = new Waiting { Peer = peer, Body = body };
                string name = Guid.NewGuid().ToString("N") + ".json";
                File.WriteAllText(
                    Path.Combine(_outbox, name),
                    JsonSerializer.Serialize(parked),
                    Encoding.UTF8);

                Interlocked.Increment(ref _parked);
            }
            catch (Exception failure)
            {
                _say("[" + _whoAmI + "] " + Texts.T("transporte.bandeja-no-guardo", ("motivo", failure.Message)));
            }
        }

        private async Task OutboxLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }

                string[] waiting;
                try
                {
                    waiting = Directory.GetFiles(_outbox, "*.json");
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (string file in waiting)
                {
                    if (token.IsCancellationRequested) return;

                    Waiting parked;
                    try
                    {
                        parked = JsonSerializer.Deserialize<Waiting>(File.ReadAllText(file));
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    if (parked == null) continue;

                    if (!await PostAsync(parked.Peer, parked.Body, token, false).ConfigureAwait(false)) continue;

                    try
                    {
                        File.Delete(file);
                        Interlocked.Increment(ref _sent);
                        Interlocked.Increment(ref _rescued);
                        _say("[" + _whoAmI + "] " + Texts.T("transporte.bandeja-entrego", ("a", parked.Peer)));
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        private bool LooksLikeMe(string peer) =>
            peer.IndexOf(":" + _port, StringComparison.Ordinal) >= 0
            && (peer.IndexOf(_whoAmI, StringComparison.OrdinalIgnoreCase) >= 0
                || peer.IndexOf("localhost", StringComparison.OrdinalIgnoreCase) >= 0
                || peer.IndexOf("127.0.0.1", StringComparison.Ordinal) >= 0);

        private void Handle(HttpListenerContext call, Func<string, string> decide, string ifItFails)
        {
            string request;
            using (StreamReader reader = new StreamReader(call.Request.InputStream, Encoding.UTF8))
                request = reader.ReadToEnd();

            if (decide == null)
            {
                call.Response.StatusCode = 503;
                return;
            }

            try
            {
                string reply = decide(request);

                call.Response.Headers[SignatureHeader] = _whoAmI;

                if (string.IsNullOrEmpty(reply))
                {
                    call.Response.StatusCode = 204;
                    return;
                }

                byte[] bytes = Encoding.UTF8.GetBytes(reply);
                call.Response.StatusCode = 200;
                call.Response.ContentType = "application/json";
                call.Response.ContentLength64 = bytes.Length;
                call.Response.OutputStream.Write(bytes, 0, bytes.Length);
            }
            catch (Exception failure)
            {
                _say("[" + _whoAmI + "] " + ifItFails + ": " + failure.Message);

                // El "no" lleva razon aparte del texto, para que el otro telefono pueda
                // decidir solo si vuelve a presentarse o si borra el bingo de su lista.
                string because = failure is Refusal refusal ? refusal.Because : Refusal.Plain;

                byte[] why = Encoding.UTF8.GetBytes(
                    "{\"no\":" + JsonSerializer.Serialize(because)
                    + ",\"dice\":" + JsonSerializer.Serialize(failure.Message) + "}");

                call.Response.Headers[SignatureHeader] = _whoAmI;
                call.Response.StatusCode = 403;
                call.Response.ContentType = "application/json";
                call.Response.ContentLength64 = why.Length;

                try { call.Response.OutputStream.Write(why, 0, why.Length); } catch (Exception) { }
            }
        }

        private static string Address(string peer) =>
            peer.TrimEnd('/') + TellPath;

        /// <summary>
        /// Baja el certificado publico de un telefono, para verificar la firma de un
        /// codigo. Null si no contesta, si es una version que no sirve certificados
        /// (contesta 204 como a cualquier GET) o si lo que manda no se puede leer: quien
        /// llama decide que hacer con un codigo firmado cuyo firmante no aparece.
        /// </summary>
        public static async Task<System.Security.Cryptography.X509Certificates.X509Certificate2>
            FetchPeerCertAsync(string peer, CancellationToken token = default)
        {
            try
            {
                using HttpRequestMessage ask =
                    new HttpRequestMessage(HttpMethod.Get, peer.TrimEnd('/') + CertPath);

                using HttpResponseMessage answer =
                    await Wire.SendAsync(ask, token).ConfigureAwait(false);

                if ((int)answer.StatusCode != 200) return null;

                string der = (await answer.Content.ReadAsStringAsync(token)
                    .ConfigureAwait(false)).Trim();

                return System.Security.Cryptography.X509Certificates.X509CertificateLoader
                    .LoadCertificate(Convert.FromBase64String(der));
            }
            catch (Exception)
            {
                return null;
            }
        }

        public async Task<bool> EnlistAsync(string communityLeader, string whoISayIAm,
            CancellationToken token = default)
        {
            Reply reply = await AskAsync(
                communityLeader, EnlistPath, whoISayIAm, Texts.T("transporte.accion.alistarme"), 8, token).ConfigureAwait(false);

            return reply.Ok;
        }

        public Task<Reply> EnlistAndListenAsync(string communityLeader, string whoISayIAm,
            CancellationToken token = default) =>
            AskAsync(communityLeader, EnlistPath, whoISayIAm, Texts.T("transporte.accion.alistarme"), 8, token);

        public Task<Reply> ReportSaleAsync(string communityLeader, string sale,
            CancellationToken token = default) =>
            AskAsync(communityLeader, SalePath, sale, Texts.T("transporte.accion.avisar-venta"), 1, token);

        public Task<Reply> ReportCallAsync(string communityLeader, string call,
            CancellationToken token = default) =>
            AskAsync(communityLeader, CallPath, call, Texts.T("transporte.accion.avisar-canto"), 1, token);

        public Task<Reply> AskForExchangeAsync(string communityLeader, string exchange,
            CancellationToken token = default) =>
            AskAsync(communityLeader, ExchangePath, exchange, Texts.T("transporte.accion.pedir-cambio"), 1, token);

        public Task<Reply> AskForHandoverAsync(string communityLeader, string handover,
            CancellationToken token = default) =>
            AskAsync(communityLeader, HandoverPath, handover, Texts.T("transporte.accion.pasar-cartones"), 1, token);

        public Task<Reply> AskToGiveBackAsync(string communityLeader, string giveBack,
            CancellationToken token = default) =>
            AskAsync(communityLeader, GiveBackPath, giveBack, Texts.T("transporte.accion.devolver-cartones"), 1, token);

        public Task<Reply> AskToUndoAsync(string communityLeader, string plea,
            CancellationToken token = default) =>
            AskAsync(communityLeader, PleaPath, plea, Texts.T("transporte.accion.pedir-anulacion"), 1, token);

        /// <summary>Cantar BINGO. Tres intentos y no uno: la persona ya grito y esta
        /// parada esperando, y a diferencia de una venta aca no hay nadie haciendo fila
        /// atras -- vale la pena insistir un poco antes de decirle que no llego.</summary>
        public Task<Reply> ShoutBingoAsync(string communityLeader, string claim,
            CancellationToken token = default) =>
            AskAsync(communityLeader, BingoPath, claim, Texts.T("transporte.accion.cantar-bingo"), 3, token);

        /// <summary>Un solo intento: es una pregunta antes de cobrar, y si el organizador no
        /// contesta rapido es peor hacer esperar a la fila que quedarse sin la confirmacion.
        /// </summary>
        public Task<Reply> AskWhoIsAsync(string communityLeader, string question,
            CancellationToken token = default) =>
            AskAsync(communityLeader, WhoIsPath, question, Texts.T("transporte.accion.preguntar-quien"),
                1, token);

        public sealed class Reply
        {
            public bool Ok { get; init; }

            public string Body { get; init; }

            /// <summary>Lo que se le dice a la persona.</summary>
            public string Why { get; init; }

            /// <summary>La razon del "no", para que el telefono decida solo. Uno de los
            /// codigos de <see cref="Refusal"/>.</summary>
            public string Because { get; init; }
        }

        /// <summary>Lee el "no" del organizador. Un organizador viejo contesta texto pelado:
        /// entonces la razon es la generica y el texto es el cuerpo tal cual.</summary>
        private static (string Because, string Says) ReadRefusal(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return (Refusal.Plain, null);

            try
            {
                using JsonDocument doc = JsonDocument.Parse(body);

                if (!doc.RootElement.TryGetProperty("no", out JsonElement because))
                    return (Refusal.Plain, body);

                string says = doc.RootElement.TryGetProperty("dice", out JsonElement dice)
                    ? dice.GetString()
                    : body;

                return (because.GetString() ?? Refusal.Plain, says);
            }
            catch (Exception)
            {
                return (Refusal.Plain, body);
            }
        }

        private async Task<Reply> AskAsync(string peer, string path, string whatISay,
            string whatImDoing, int attempts, CancellationToken token)
        {
            string where = peer.TrimEnd('/') + path;
            string lastWhy = null;

            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                try
                {
                    using HttpRequestMessage knock = new HttpRequestMessage(HttpMethod.Post, where)
                    {
                        Content = new StringContent(whatISay, Encoding.UTF8, "application/json"),
                    };

                    using HttpResponseMessage answer =
                        await Wire.SendAsync(knock, token).ConfigureAwait(false);

                    string body = answer.Content == null
                        ? null
                        : await answer.Content.ReadAsStringAsync(token).ConfigureAwait(false);

                    if (answer.IsSuccessStatusCode && answer.Headers.Contains(SignatureHeader))
                    {
                        _say("[" + _whoAmI + "] " + whatImDoing + ": listo");
                        return new Reply { Ok = true, Body = body };
                    }

                    if (answer.StatusCode == HttpStatusCode.Forbidden)
                    {
                        (string because, string says) = ReadRefusal(body);

                        _say("[" + _whoAmI + "] " + Texts.T("transporte.me-dijeron-que-no", ("motivo", says)));
                        return new Reply { Ok = false, Why = says, Because = because };
                    }

                    lastWhy = "contesto " + (int)answer.StatusCode;
                    _say("[" + _whoAmI + "] " + peer + " " + lastWhy
                        + " al " + whatImDoing + " (intento " + attempt + ")");
                }
                catch (Exception failure)
                {
                    lastWhy = failure.Message;
                    if (attempt == 1)
                        _say("[" + _whoAmI + "] " + Texts.T("transporte.todavia-no-alcanzo", ("a", peer), ("motivo", failure.Message)));
                }

                if (attempt < attempts) await Task.Delay(1200, token).ConfigureAwait(false);
            }

            _say("[" + _whoAmI + "] " + Texts.T("transporte.no-pude-sin-respuesta", ("que", whatImDoing)));
            return new Reply { Ok = false, Why = lastWhy, Because = Refusal.Plain };
        }

        public void Dispose()
        {
            try { _life.Cancel(); } catch (Exception) { }
            try { _ears?.Close(); } catch (Exception) { }
            _life.Dispose();
        }

        private sealed class Unsubscribe : IDisposable
        {
            private readonly List<Action<BrokerRecord>> _handlers;

            private readonly Action<BrokerRecord> _mine;

            public Unsubscribe(List<Action<BrokerRecord>> handlers, Action<BrokerRecord> mine)
            {
                _handlers = handlers;
                _mine = mine;
            }

            public void Dispose()
            {
                lock (_handlers) _handlers.Remove(_mine);
            }
        }

        private sealed class Wired
        {
            public string Topic { get; set; }

            public string Key { get; set; }

            public Dictionary<string, string> Headers { get; set; }

            public string Value { get; set; }
        }

        private sealed class Waiting
        {
            public string Peer { get; set; }

            public string Body { get; set; }
        }
    }
}
