using System.Globalization;
using System.Text.Json;

namespace OSO.Models;

// Lo que se leyó de una factura de servicio (luz, gas, agua, expensas...). Todo es opcional: lo que no se leyó queda en null.
// Servicio y Domicilio sirven para ubicar la factura en la pantalla "Cargar el mes"; PeriodoDesde es el primer mes que cubre
// (en las bimestrales, el anterior a Periodo).
public record LecturaFactura(decimal? Monto, DateTime? Vencimiento, DateTime? Periodo, string? Proveedor, decimal Confianza,
    string? Servicio = null, string? Domicilio = null, DateTime? PeriodoDesde = null)
{
    // Con poca seguridad se le avisa a la persona que revise bien los datos antes de usarlos.
    public bool EsDudosa => Confianza < ReglasContrato.UmbralBajaConfianza;

    public bool LeyoAlgo => Monto.HasValue || Vencimiento.HasValue || Periodo.HasValue;
}

// Lee el monto, el vencimiento y el mes de una factura (PDF o foto) con Gemini. No guarda la factura ni su contenido:
// el resultado solo completa el formulario y la persona lo revisa antes de guardar.
public class ExtractorFacturaGemini(ExtractorContratoGemini gemini)
{
    public async Task<(LecturaFactura? Lectura, string? Error)> LeerAsync(byte[] archivo, string mimeType, string? tipoGasto)
    {
        var (json, error) = await gemini.EnviarAsync(ArmarPedido(archivo, mimeType, tipoGasto), "la factura");
        if (json == null)
        {
            return (null, error);
        }

        LecturaFactura? lectura = Parsear(json);
        return lectura is { LeyoAlgo: true } ? (lectura, null) : (null, "No pudimos leer datos de la factura; cargalos a mano.");
    }

    // Tolerante: si falta un campo o viene con otro formato, ese dato queda en null en vez de romper.
    public static LecturaFactura? Parsear(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            decimal? monto = Numero(raiz, "monto");
            if (monto is <= 0)
            {
                monto = null;
            }

            decimal confianza = Math.Clamp(Numero(raiz, "confianza") ?? 0m, 0m, 1m);
            DateTime? periodo = Fecha(raiz, "periodo");
            return new LecturaFactura(monto, Fecha(raiz, "fechaVencimiento"), periodo.HasValue ? new DateTime(periodo.Value.Year, periodo.Value.Month, 1) : null, Texto(raiz, "proveedor"), confianza);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static decimal? Numero(JsonElement raiz, string nombre)
    {
        if (!raiz.TryGetProperty(nombre, out JsonElement e))
        {
            return null;
        }

        if (e.ValueKind == JsonValueKind.Number && e.TryGetDecimal(out decimal n))
        {
            return n;
        }

        return e.ValueKind == JsonValueKind.String && decimal.TryParse(e.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal s) ? s : null;
    }

    private static DateTime? Fecha(JsonElement raiz, string nombre) =>
        raiz.TryGetProperty(nombre, out JsonElement e) && e.ValueKind == JsonValueKind.String
            && DateTime.TryParseExact(e.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime f)
            ? f : null;

    private static string? Texto(JsonElement raiz, string nombre)
    {
        if (!raiz.TryGetProperty(nombre, out JsonElement e) || e.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? t = e.GetString()?.Trim();
        return string.IsNullOrEmpty(t) ? null : (t.Length > 80 ? t[..80] : t);
    }

    private static object ArmarPedido(byte[] archivo, string mimeType, string? tipoGasto)
    {
        string esperada = string.IsNullOrWhiteSpace(tipoGasto) ? "una factura de un servicio o de expensas" : $"una factura de {tipoGasto}";
        string instrucciones = $@"Sos un asistente que lee facturas argentinas de servicios (luz, gas, agua, internet, expensas, impuestos) y extrae tres datos.
Se espera {esperada}. Leé el documento adjunto y completá el JSON pedido.
Reglas:
- monto: el TOTAL A PAGAR de la factura (con impuestos y recargos), como número sin símbolos ni separadores de miles y con punto decimal. No tomes subtotales ni saldos anteriores.
- fechaVencimiento: el primer vencimiento para pagar, formato YYYY-MM-DD.
- periodo: el mes que se está facturando, formato YYYY-MM-01 (si la factura es de varios meses, el último).
- proveedor: el nombre de la empresa o administración que emite la factura.
- confianza: entre 0 y 1, qué tan seguro estás de haber leído bien el monto y las fechas (0 si el documento no es una factura legible).
- Si un dato no figura, devolvé null. No inventes ni supongas.";

        return new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { inline_data = new { mime_type = mimeType, data = Convert.ToBase64String(archivo) } },
                        new { text = instrucciones }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0,
                responseMimeType = "application/json",
                responseSchema = ArmarEsquema()
            }
        };
    }

    private static Dictionary<string, object> ArmarEsquema()
    {
        static Dictionary<string, object> Campo(string tipo, bool puedeSerNull) =>
            puedeSerNull ? new() { ["type"] = tipo, ["nullable"] = true } : new() { ["type"] = tipo };

        var propiedades = new Dictionary<string, object>
        {
            ["monto"] = Campo("NUMBER", true),
            ["fechaVencimiento"] = Campo("STRING", true),
            ["periodo"] = Campo("STRING", true),
            ["proveedor"] = Campo("STRING", true),
            ["confianza"] = Campo("NUMBER", false)
        };

        return new Dictionary<string, object>
        {
            ["type"] = "OBJECT",
            ["properties"] = propiedades,
            ["required"] = propiedades.Keys.ToArray()
        };
    }
}
