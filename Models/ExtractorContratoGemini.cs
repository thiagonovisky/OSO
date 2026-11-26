using System.Net;
using System.Text;
using System.Text.Json;

namespace OSO.Models;

// Lee un contrato (PDF o foto) con la API de Gemini y devuelve el JSON con los datos y su confianza.
// Nunca se loguea el contenido del contrato ni la respuesta: solo códigos de estado.
public class ExtractorContratoGemini(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<ExtractorContratoGemini> logger)
{
    private const string UrlBase = "https://generativelanguage.googleapis.com/v1beta/models/";
    private static readonly TimeSpan TiempoMaximo = TimeSpan.FromSeconds(150);
    private const int SegundosPorIntento = 60;

    // El modelo con el que se leyó el último pedido (se guarda junto a la extracción). Antes de leer, es el principal.
    public string Modelo { get; private set; } = configuration["Gemini:Modelo"] ?? ModeloPorDefecto;

    private const string ModeloPorDefecto = "gemini-3.5-flash";
    private string ModeloPrincipal => configuration["Gemini:Modelo"] ?? ModeloPorDefecto;

    // Modelos a probar si el principal está saturado. Se configuran en appsettings.json (Gemini:ModelosRespaldo).
    private string[] ModelosRespaldo => configuration.GetSection("Gemini:ModelosRespaldo").Get<string[]>() ?? [];

    public Task<(string? Json, string? Error)> ExtraerAsync(byte[] archivo, string mimeType, IEnumerable<string> tiposGasto) =>
        EnviarAsync(ArmarPedido(archivo, mimeType, tiposGasto.ToList()), "el contrato");

    // Manda un pedido ya armado a Gemini y devuelve el texto JSON de la respuesta. queLee: "el contrato", "la factura"...
    // Lo usa también el lector de facturas, para no repetir la conexión, el tiempo máximo ni el manejo de errores.
    public async Task<(string? Json, string? Error)> EnviarAsync(object pedidoGemini, string queLee)
    {
        string? key = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            return (null, "Falta configurar GEMINI_API_KEY");
        }

        string cuerpo = JsonSerializer.Serialize(pedidoGemini);
        string[] modelos = [ModeloPrincipal, .. ModelosRespaldo.Where(m => !string.IsNullOrWhiteSpace(m) && m != ModeloPrincipal)];

        try
        {
            using var cts = new CancellationTokenSource(TiempoMaximo);
            using HttpClient cliente = httpClientFactory.CreateClient();

            // Por cada modelo (el principal y después los de respaldo):
            // - si está saturado (500/502/503/504), se reintenta una vez y después se pasa al siguiente;
            // - si llegó a su límite de uso (429) o no respondió a tiempo, se pasa directo al siguiente.
            HttpResponseMessage? respuesta = null;
            bool algunoSeColgo = false;
            foreach (string modelo in modelos)
            {
                for (int intento = 0; intento < 2; intento++)
                {
                    if (intento > 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);
                    }

                    respuesta?.Dispose();
                    Modelo = modelo;
                    respuesta = await EnviarUnaVezAsync(cliente, modelo, cuerpo, key, cts.Token);
                    if (respuesta == null)
                    {
                        algunoSeColgo = true;
                        logger.LogWarning("Gemini ({Modelo}) no respondió en {Segundos} segundos", modelo, SegundosPorIntento);
                        break;
                    }

                    if (!EsErrorTemporal(respuesta.StatusCode))
                    {
                        break;
                    }

                    logger.LogWarning("Gemini ({Modelo}) respondió con el código {Codigo}", modelo, (int)respuesta.StatusCode);
                }

                if (respuesta != null && !EsErrorTemporal(respuesta.StatusCode) && respuesta.StatusCode != HttpStatusCode.TooManyRequests)
                {
                    break;
                }

                if (respuesta?.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    logger.LogWarning("Gemini ({Modelo}) llegó a su límite de uso", modelo);
                }
            }

            if (respuesta == null)
            {
                return (null, "Gemini tardó demasiado en responder.");
            }

            using HttpResponseMessage final = respuesta;
            if (!final.IsSuccessStatusCode)
            {
                logger.LogWarning("Gemini ({Modelo}) respondió con el código {Codigo}", Modelo, (int)final.StatusCode);
                return (null, final.StatusCode switch
                {
                    _ when algunoSeColgo => "Gemini tardó demasiado en responder.",
                    HttpStatusCode.TooManyRequests => "Se alcanzó el límite de uso gratuito de Gemini. Probá de nuevo más tarde.",
                    var c when EsErrorTemporal(c) => $"Gemini está saturado en este momento y no pudo leer {queLee}. Probá de nuevo en unos minutos.",
                    var c => $"Gemini no pudo procesar {queLee} (código {(int)c})."
                });
            }

            string texto = await final.Content.ReadAsStringAsync(cts.Token);
            string? json = ExtraerTextoDeRespuesta(texto);
            return json == null ? (null, $"Gemini no devolvió datos para {queLee}.") : (json, null);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Gemini tardó más de {Segundos} segundos", TiempoMaximo.TotalSeconds);
            return (null, "Gemini tardó demasiado en responder.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("No se pudo conectar con Gemini: {Tipo}", ex.GetType().Name);
            return (null, "No se pudo conectar con Gemini.");
        }
    }

    private static bool EsErrorTemporal(HttpStatusCode codigo) => codigo is HttpStatusCode.InternalServerError
        or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    // Cada intento necesita un HttpRequestMessage nuevo: no se puede reenviar el mismo.
    // Devuelve null si el modelo no respondió en SegundosPorIntento segundos (el tiempo total lo corta el token de afuera).
    private static async Task<HttpResponseMessage?> EnviarUnaVezAsync(HttpClient cliente, string modelo, string cuerpo, string key, CancellationToken tokenTotal)
    {
        using var porIntento = CancellationTokenSource.CreateLinkedTokenSource(tokenTotal);
        porIntento.CancelAfter(TimeSpan.FromSeconds(SegundosPorIntento));

        using var pedido = new HttpRequestMessage(HttpMethod.Post, $"{UrlBase}{modelo}:generateContent")
        {
            Content = new StringContent(cuerpo, Encoding.UTF8, "application/json")
        };
        pedido.Headers.Add("x-goog-api-key", key);
        try
        {
            // SendAsync espera a tener la respuesta completa, así que el tiempo por intento cubre también la descarga.
            return await cliente.SendAsync(pedido, porIntento.Token);
        }
        catch (OperationCanceledException) when (!tokenTotal.IsCancellationRequested)
        {
            return null;
        }
    }

    // candidates[0].content.parts[*].text
    private static string? ExtraerTextoDeRespuesta(string respuesta)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(respuesta);
            if (!doc.RootElement.TryGetProperty("candidates", out JsonElement candidatos) || candidatos.GetArrayLength() == 0)
            {
                return null;
            }

            if (!candidatos[0].TryGetProperty("content", out JsonElement contenido) || !contenido.TryGetProperty("parts", out JsonElement partes))
            {
                return null;
            }

            var sb = new StringBuilder();
            foreach (JsonElement parte in partes.EnumerateArray())
            {
                if (parte.TryGetProperty("text", out JsonElement t) && t.ValueKind == JsonValueKind.String)
                {
                    sb.Append(t.GetString());
                }
            }

            return sb.Length == 0 ? null : sb.ToString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static object ArmarPedido(byte[] archivo, string mimeType, List<string> tiposGasto)
    {
        string instrucciones = $@"Sos un asistente que lee contratos de alquiler argentinos y extrae sus datos.
Leé el documento adjunto y completá el JSON pedido.
Reglas:
- Si el contrato no dice un dato, devolvé null en ""valor"". No inventes ni supongas.
- Cada dato lleva una ""confianza"" entre 0 y 1: qué tan seguro estás de haberlo leído bien (0 si no lo encontraste).
- Las fechas van en formato YYYY-MM-DD. Los montos son números sin símbolos ni separadores de miles.
- montoInicial es el alquiler mensual del primer período.
- indiceAjuste: IPC, ICL, CasaPropia, Fijo u Otro. frecuenciaAjusteMeses: cada cuántos meses se actualiza el alquiler.
- diaVencimiento: día del mes (1 a 31) hasta el que se paga el alquiler.
- punitorioDiario: porcentaje por cada día de mora (por ejemplo 0.5).
- tipoGarantia: Propietaria, Caucion, ReciboSueldo u Otra. destino: Vivienda, Comercial o Temporario. Monedas: ARS o USD.
- partes: locador, locatario y garantes (rol: Locador, Locatario o Garante) con nombre, documento (DNI o CUIT), domicilio y mail si figuran.
- gastos: una fila por cada tipo de gasto de esta lista, solo si el contrato dice algo sobre quién lo paga: {string.Join("; ", tiposGasto)}.
  Para cada uno: responsable (Inquilino, Propietario o Compartido), porcentajeInquilino (0 a 100; solo importa si es Compartido),
  textoClausula (la frase literal del contrato de donde lo sacaste) y confianza. Si el contrato no menciona un tipo de gasto, no lo incluyas.";

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

    private static Dictionary<string, object> Campo(string tipo, string descripcion) => new()
    {
        ["type"] = "OBJECT",
        ["description"] = descripcion,
        ["properties"] = new Dictionary<string, object>
        {
            ["valor"] = new Dictionary<string, object> { ["type"] = tipo, ["nullable"] = true },
            ["confianza"] = new Dictionary<string, object> { ["type"] = "NUMBER" }
        },
        ["required"] = new[] { "confianza" }
    };

    private static Dictionary<string, object> ArmarEsquema()
    {
        var propiedades = new Dictionary<string, object>
        {
            ["fechaFirma"] = Campo("STRING", "Fecha de firma del contrato, YYYY-MM-DD"),
            ["fechaInicio"] = Campo("STRING", "Fecha de inicio del contrato, YYYY-MM-DD"),
            ["fechaFin"] = Campo("STRING", "Fecha de fin del contrato, YYYY-MM-DD"),
            ["montoInicial"] = Campo("NUMBER", "Alquiler mensual inicial"),
            ["moneda"] = Campo("STRING", "ARS o USD"),
            ["indiceAjuste"] = Campo("STRING", "IPC, ICL, CasaPropia, Fijo u Otro"),
            ["frecuenciaAjusteMeses"] = Campo("INTEGER", "Cada cuántos meses se ajusta"),
            ["fechaPrimerAjuste"] = Campo("STRING", "Fecha del primer ajuste, YYYY-MM-DD"),
            ["porcentajeFijo"] = Campo("NUMBER", "Porcentaje de ajuste si es fijo"),
            ["diaVencimiento"] = Campo("INTEGER", "Día del mes de vencimiento del pago"),
            ["medioPago"] = Campo("STRING", "CBU, alias o domicilio de pago"),
            ["punitorioDiario"] = Campo("NUMBER", "Porcentaje diario de punitorio por mora"),
            ["montoDeposito"] = Campo("NUMBER", "Monto del depósito en garantía"),
            ["monedaDeposito"] = Campo("STRING", "ARS o USD"),
            ["tipoGarantia"] = Campo("STRING", "Propietaria, Caucion, ReciboSueldo u Otra"),
            ["preavisoRescisionDias"] = Campo("INTEGER", "Días de preaviso para rescindir"),
            ["condicionesRescision"] = Campo("STRING", "Resumen de las condiciones de rescisión"),
            ["destino"] = Campo("STRING", "Vivienda, Comercial o Temporario"),
            ["partes"] = new Dictionary<string, object>
            {
                ["type"] = "ARRAY",
                ["items"] = new Dictionary<string, object>
                {
                    ["type"] = "OBJECT",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["rol"] = new Dictionary<string, object> { ["type"] = "STRING" },
                        ["nombre"] = new Dictionary<string, object> { ["type"] = "STRING", ["nullable"] = true },
                        ["documento"] = new Dictionary<string, object> { ["type"] = "STRING", ["nullable"] = true },
                        ["domicilio"] = new Dictionary<string, object> { ["type"] = "STRING", ["nullable"] = true },
                        ["mail"] = new Dictionary<string, object> { ["type"] = "STRING", ["nullable"] = true }
                    },
                    ["required"] = new[] { "rol" }
                }
            },
            ["gastos"] = new Dictionary<string, object>
            {
                ["type"] = "ARRAY",
                ["items"] = new Dictionary<string, object>
                {
                    ["type"] = "OBJECT",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["tipoGasto"] = new Dictionary<string, object> { ["type"] = "STRING" },
                        ["responsable"] = new Dictionary<string, object> { ["type"] = "STRING" },
                        ["porcentajeInquilino"] = new Dictionary<string, object> { ["type"] = "NUMBER", ["nullable"] = true },
                        ["textoClausula"] = new Dictionary<string, object> { ["type"] = "STRING", ["nullable"] = true },
                        ["confianza"] = new Dictionary<string, object> { ["type"] = "NUMBER" }
                    },
                    ["required"] = new[] { "tipoGasto", "responsable", "confianza" }
                }
            }
        };

        return new Dictionary<string, object>
        {
            ["type"] = "OBJECT",
            ["properties"] = propiedades,
            // Todos los campos son obligatorios en el esquema (el "valor" puede ser null): así el modelo no se saltea ninguno.
            ["required"] = propiedades.Keys.ToArray()
        };
    }
}
