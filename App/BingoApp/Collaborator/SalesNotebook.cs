using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BingoApp.Collaborator;

/// <summary>
/// La libreta de la colaboradora: las ventas que hizo cuando el organizador no estaba, y
/// que todavia le debe contar.
///
/// Existe porque un Cast no puede asentar solo. Escribe REENVIANDOLE el comando al
/// Director, y si el Director no esta, no hay a quien reenviarle: el sustrato tira al
/// toque y no guarda nada. La copia del diario que ella tiene tampoco sirve de borrador --
/// escribir ahi partiria la historia en dos, y ademas al reengancharse esa copia se aparta
/// y se estrena una limpia.
///
/// Asi que la cola es nuestra, y va en un archivo APARTE del diario. No es un diario
/// chiquito ni pretende serlo: es la libreta donde ella anota lo que cobro, para leersela
/// al padre cuando aparezca.
///
/// Se escribe en dos tiempos --archivo temporal y despues rename-- porque un corte de luz
/// a la mitad de un archivo de plata cobrada no es una posibilidad teorica: la libreta se
/// guarda justo cuando el telefono esta peleando con la red.
/// </summary>
public sealed class SalesNotebook
{
    /// <summary>Una venta cobrada que el organizador todavia no sabe.</summary>
    public sealed class Sale
    {
        /// <summary>El renglon: distingue dos ventas que por lo demas son iguales, y es lo
        /// que permite tachar la correcta cuando entra.</summary>
        public string Id { get; set; } = string.Empty;

        public string Buyer { get; set; } = string.Empty;

        public string Contact { get; set; } = string.Empty;

        public List<long> Cards { get; set; } = new();

        /// <summary>El carnet que ella acuño en el momento. Viaja tal cual cuando la venta
        /// por fin se asienta: si se acuñara de nuevo al drenar, el comprador tendria un
        /// numero distinto del que se llevo escrito.</summary>
        public string BuyerMark { get; set; } = string.Empty;

        /// <summary>Cuando la cobro. Para poder decirle "la de las 8:14" en vez de "una de
        /// las que quedaron", que con la fila encima es la diferencia entre acordarse y no.</summary>
        public string When { get; set; } = string.Empty;

        /// <summary>Por que el organizador no la acepto, si es que llego a rebotar. Vacio
        /// mientras esta esperando su turno.</summary>
        public string Bounced { get; set; } = string.Empty;
    }

    /// <summary>Lo que la libreta guarda entre una apertura y otra.</summary>
    private sealed class Pages
    {
        public List<Sale> Waiting { get; set; } = new();

        public List<Sale> Bounced { get; set; } = new();
    }

    private readonly string _file;
    private readonly object _guard = new();
    private readonly List<Sale> _waiting = new();
    private readonly List<Sale> _bounced = new();
    private readonly Action<string> _say;

    public SalesNotebook(string file, Action<string> say)
    {
        _file = file;
        _say = say ?? (_ => { });

        Load();
    }

    public int Count
    {
        get { lock (_guard) return _waiting.Count; }
    }

    /// <summary>Las ventas pendientes, en el orden en que se cobraron. Se drena en ese
    /// orden y no en otro: si dos ventas se pisan, la que gana tiene que ser la primera que
    /// se cobro, que es la que la persona vivio primero.</summary>
    public IReadOnlyList<Sale> Waiting
    {
        get { lock (_guard) return _waiting.ToList(); }
    }

    /// <summary>Los cartones comprometidos en la libreta. La pantalla los tiene que
    /// descontar de su taco aunque el diario todavia los muestre en su mano: si no, vende
    /// el mismo carton dos veces y la segunda persona se queda sin nada.</summary>
    public IReadOnlyList<long> CardsSpokenFor
    {
        get
        {
            lock (_guard)
            {
                List<long> all = new();

                foreach (Sale sale in _waiting)
                    foreach (long card in sale.Cards)
                        if (!all.Contains(card)) all.Add(card);

                return all;
            }
        }
    }

    /// <summary>Lo que ese carnet lleva comprado segun la libreta. Hace falta para armarle
    /// el codigo a quien vuelve a comprar mientras el organizador sigue caido: sus compras
    /// anteriores tampoco estan en el diario todavia.</summary>
    public IReadOnlyList<long> CardsUnder(string buyerMark)
    {
        List<long> theirs = new();

        if (string.IsNullOrWhiteSpace(buyerMark)) return theirs;

        lock (_guard)
        {
            foreach (Sale sale in _waiting)
            {
                if (!string.Equals(sale.BuyerMark, buyerMark, StringComparison.Ordinal)) continue;

                foreach (long card in sale.Cards)
                    if (!theirs.Contains(card)) theirs.Add(card);
            }
        }

        return theirs;
    }

    /// <summary>El nombre de quien tiene ese carnet, si le vendio con el estando solos.</summary>
    public string WhoHasThisMark(string buyerMark)
    {
        if (string.IsNullOrWhiteSpace(buyerMark)) return null;

        lock (_guard)
        {
            foreach (Sale sale in _waiting)
                if (string.Equals(sale.BuyerMark, buyerMark, StringComparison.Ordinal))
                    return sale.Buyer;
        }

        return null;
    }

    /// <summary>Anotar una venta. Vuelve solo cuando quedo en disco: entre cobrar y
    /// guardar no puede haber un "despues", porque ese despues es donde se pierde la plata.</summary>
    public void Write(Sale sale)
    {
        lock (_guard)
        {
            _waiting.Add(sale);
            Save();
        }
    }

    /// <summary>Tacharla, porque el organizador ya la asento o porque ya estaba asentada.</summary>
    public void CrossOut(string id)
    {
        lock (_guard)
        {
            int removed = _waiting.RemoveAll(s => string.Equals(s.Id, id, StringComparison.Ordinal));

            if (removed > 0) Save();
        }
    }

    /// <summary>
    /// Pasarla al renglon de los problemas: el organizador la miro y la rechazo, y ella ya
    /// cobro.
    ///
    /// No se tacha, se MUEVE. Una venta rechazada es plata que alguien entrego y un carton
    /// que no existe, y eso no se resuelve solo: hay que ir a buscar a la persona. Si
    /// desapareciera de la libreta, el unico rastro seria un renglon del registro que nadie
    /// va a leer, y la diferencia aparece recien al cuadrar la caja -- cuando ya nadie se
    /// acuerda de quien era.
    /// </summary>
    public void Bounce(string id, string why)
    {
        lock (_guard)
        {
            Sale bad = _waiting.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal));

            if (bad is null) return;

            _waiting.Remove(bad);
            bad.Bounced = string.IsNullOrWhiteSpace(why) ? Texts.T("colaborador.log.organizador-no-la-acepto") : why;
            _bounced.Add(bad);

            Save();
        }
    }

    /// <summary>Las que rebotaron y siguen sin resolverse.</summary>
    public IReadOnlyList<Sale> Trouble
    {
        get { lock (_guard) return _bounced.ToList(); }
    }

    public int TroubleCount
    {
        get { lock (_guard) return _bounced.Count; }
    }

    /// <summary>Darla por resuelta. La resuelve una persona hablando con otra, no el codigo:
    /// esto solo la saca de la lista cuando alguien dice que ya esta.</summary>
    public void Settle(string id)
    {
        lock (_guard)
        {
            int removed = _bounced.RemoveAll(s => string.Equals(s.Id, id, StringComparison.Ordinal));

            if (removed > 0) Save();
        }
    }

    private void Save()
    {
        try
        {
            string folder = Path.GetDirectoryName(_file);

            if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder);

            string temp = _file + ".nuevo";

            File.WriteAllText(temp, JsonSerializer.Serialize(
                new Pages { Waiting = _waiting, Bounced = _bounced }));

            if (File.Exists(_file)) File.Delete(_file);

            File.Move(temp, _file);
        }
        catch (Exception failure)
        {
            // Aca no hay a donde escalar: la venta ya se cobro y la persona ya se fue con
            // su codigo. Lo unico honesto es decirlo fuerte, para que quede en el registro
            // y alguien pueda cuadrarlo a mano.
            _say(Texts.T("colaborador.log.no-pude-guardar-libreta", ("motivo", Texts.Explain(failure))));
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_file)) return;

            string written = File.ReadAllText(_file);

            // La libreta empezo siendo una lista pelada, antes de que hubiera renglon de
            // problemas. Una version nueva leyendo una vieja no puede tirar y quedarse
            // callada: eso serian ventas cobradas que desaparecen en una actualizacion.
            if (written.TrimStart().StartsWith("[", StringComparison.Ordinal))
            {
                List<Sale> onlyWaiting = JsonSerializer.Deserialize<List<Sale>>(written);

                if (onlyWaiting is not null) _waiting.AddRange(onlyWaiting);
            }
            else
            {
                Pages saved = JsonSerializer.Deserialize<Pages>(written);

                if (saved is null) return;

                if (saved.Waiting is not null) _waiting.AddRange(saved.Waiting);
                if (saved.Bounced is not null) _bounced.AddRange(saved.Bounced);
            }

            if (_waiting.Count > 0)
                _say(Texts.T("colaborador.log.traigo-ventas", ("n", _waiting.Count.ToString())));

            if (_bounced.Count > 0)
                                _say(Texts.T("colaborador.log.tengo-rebotadas", ("n", _bounced.Count.ToString())));
        }
        catch (Exception failure)
        {
            _say(Texts.T("colaborador.log.no-pude-leer-libreta", ("motivo", Texts.Explain(failure))));
        }
    }
}
