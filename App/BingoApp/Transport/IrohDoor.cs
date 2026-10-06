using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Choreography.Transport.Brokered;
using Choreography.Transport.Iroh;

namespace BingoApp.Transport;

// La puerta del bingo por iroh: el jugador que no alcanza la IP del organizador (otra red, datos
// moviles) se presenta por el bus de tells en vez de por HTTP. El codigo trae el ticket iroh del
// organizador; el nonce de cada puerta sale del id del bingo, asi que nadie tiene que mandarlo.
// El bus entrega lo que manda el jugador SOLO al organizador (no se reenvia a otros enlaces), y
// la respuesta sale a todos pero la oye unicamente quien escucha su tema.
public static class IrohDoor
{
    // Cada puerta espera a un telefono a la vez; varias para que entrar de a muchos no choque.
    public const int Doors = 8;

    public const string RequestHeader = "pedido";
    public const string OkHeader = "ok";
    public const string BecauseHeader = "no";

    public static Guid DoorNonce(string festivalId, int door)
    {
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes("bingo-door|" + festivalId + "|" + door));
        return new Guid(hash);
    }

    public static string EnlistTopic(string festivalId) => "funcion." + festivalId + ".alistarse";

    public static string ReplyTopic(string festivalId, string request) =>
        "funcion." + festivalId + ".alistado." + request;

    // Del lado del organizador: abre las puertas y atiende los alistamientos que llegan por el bus
    // con la MISMA funcion que atiende los que llegan por HTTP.
    public static void Serve(IrohMessageBroker bus, string festivalId, Func<string, string> enlist,
        CancellationToken life, Action<string> say)
    {
        for (int door = 0; door < Doors; door++)
        {
            int mine = door;
            _ = Task.Run(async () =>
            {
                Guid nonce = DoorNonce(festivalId, mine);
                while (!life.IsCancellationRequested)
                {
                    try
                    {
                        await bus.AcceptLinkAsync(nonce, life);
                        say?.Invoke($"entro alguien por la puerta iroh {mine} de {festivalId}; enlaces: {bus.LinkCount}");
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception failure)
                    {
                        say?.Invoke($"la puerta iroh {mine} de {festivalId} fallo: {failure.Message}");
                        try { await Task.Delay(1000, life); } catch (OperationCanceledException) { return; }
                    }
                }
            });
        }

        bus.Subscribe(EnlistTopic(festivalId), record => _ = Task.Run(async () =>
        {
            string request = record.Headers != null && record.Headers.TryGetValue(RequestHeader, out string r) ? r : null;
            if (string.IsNullOrWhiteSpace(request)) return;

            Dictionary<string, string> headers = new() { [RequestHeader] = request };
            string value;
            try
            {
                value = enlist(record.Value) ?? string.Empty;
                headers[OkHeader] = "1";
            }
            catch (Exception failure)
            {
                headers[OkHeader] = "0";
                headers[BecauseHeader] = failure is Refusal refusal ? refusal.Because : Refusal.Plain;
                value = failure.Message;
            }

            try
            {
                await bus.ProduceAsync(ReplyTopic(festivalId, request), request, headers, value, life);
                say?.Invoke($"atendi un alistamiento por iroh en {festivalId} ({(headers[OkHeader] == "1" ? "entro" : "no: " + value)})");
            }
            catch (Exception failure)
            {
                say?.Invoke("no pude contestar un alistamiento por iroh: " + failure.Message);
            }
        }));

        say?.Invoke($"puertas iroh abiertas en {festivalId}: {Doors}");
    }

    // Del lado del jugador: engancharse al bus del organizador por alguna de las puertas.
    public static async Task LinkAsync(IrohMessageBroker bus, string ticket, string festivalId,
        CancellationToken ct, Action<string> say)
    {
        Exception last = null;
        int start = Random.Shared.Next(Doors);
        int offline = 0;
        for (int attempt = 0; attempt < Doors * 2; attempt++)
        {
            int door = (start + attempt) % Doors;
            try
            {
                await bus.LinkAsync(IrohMessageBroker.LinkAddress(ticket, DoorNonce(festivalId, door)), ct);
                say?.Invoke($"entre por la puerta iroh {door} del organizador");
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception failure)
            {
                last = failure;
                // Puerta ocupada: probar la siguiente enseguida. Otra falla (sin red, sin relay): pocas veces,
                // porque quien llama lo vuelve a intentar mas tarde.
                bool busy = failure is InvalidOperationException && failure.Message.Contains("rejected", StringComparison.OrdinalIgnoreCase);
                if (!busy && ++offline >= 2) break;
                await Task.Delay(busy ? 150 + Random.Shared.Next(300) : 1000, ct);
            }
        }
        throw new InvalidOperationException(Texts.T("corrillo.sin-puerta-iroh", ("motivo", last?.Message ?? "")), last);
    }

    public sealed record Answer(bool Ok, string Body, string Why, string Because);

    // Pedir el alta por el bus y esperar la respuesta del organizador.
    // Null si el organizador no contesto a tiempo; un Answer si contesto, que si o que no.
    public static async Task<Answer> EnlistAsync(IrohMessageBroker bus, string festivalId, string joinText,
        TimeSpan patience, CancellationToken ct)
    {
        string request = Guid.NewGuid().ToString("N");
        TaskCompletionSource<BrokerRecord> heard = new(TaskCreationOptions.RunContinuationsAsynchronously);

        using IDisposable ear = bus.Subscribe(ReplyTopic(festivalId, request), record => heard.TrySetResult(record));

        await bus.ProduceAsync(EnlistTopic(festivalId), request,
            new Dictionary<string, string> { [RequestHeader] = request }, joinText, ct);

        Task timeout = Task.Delay(patience, ct);
        if (await Task.WhenAny(heard.Task, timeout) != heard.Task)
            return null;

        BrokerRecord reply = heard.Task.Result;
        bool ok = reply.Headers != null && reply.Headers.TryGetValue(OkHeader, out string said) && said == "1";
        if (ok) return new Answer(true, reply.Value, null, null);

        string because = reply.Headers != null && reply.Headers.TryGetValue(BecauseHeader, out string b) ? b : Refusal.Plain;
        return new Answer(false, null, reply.Value, because);
    }
}
