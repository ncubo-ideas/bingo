using System.Text.Json;
using System.Text.Json.Serialization;

namespace BingoApp.Contract;

/// <summary>Lo que el colaborador le avisa al organizador cuando cobra un carton.</summary>
public sealed class SaleNotice
{
    [JsonPropertyName("funcion")]
    public string FestivalId { get; set; }

    [JsonPropertyName("vendedor")]
    public string Seller { get; set; }

    [JsonPropertyName("comprador")]
    public string Buyer { get; set; }

    [JsonPropertyName("contacto")]
    public string Contact { get; set; }

    [JsonPropertyName("cartones")]
    public List<long> Cards { get; set; } = new();

    /// <summary>Con que papelito viene. Sin el, el organizador no le cree el nombre.</summary>
    [JsonPropertyName("marca")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Mark { get; set; }

    /// <summary>La marca de QUIEN COMPRA, no la de quien vende. Va vacia en la primera
    /// compra (el organizador la acuna) y viene llena cuando la persona vuelve a comprar,
    /// porque entonces la colaboradora la saco de sus vendidos o le escaneo el codigo.
    /// Es lo unico que distingue a dos compradoras que se llaman igual.</summary>
    [JsonPropertyName("marcaComprador")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string BuyerMark { get; set; }

    public string ToText() => JsonSerializer.Serialize(this);

    public static SaleNotice FromText(string json) =>
        JsonSerializer.Deserialize<SaleNotice>(json)
        ?? throw new ArgumentException(Texts.T("contrato.no-entendi.venta"));
}
