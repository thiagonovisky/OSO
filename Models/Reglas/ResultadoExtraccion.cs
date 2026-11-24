using System.Globalization;
using System.Text.Json;

namespace OSO.Models;

// Un dato leído del contrato y qué tan seguro está el modelo (0 a 1).
public class CampoExtraido<T>
{
    public T? Valor { get; set; }
    public decimal Confianza { get; set; }
    public bool TieneValor { get; set; }
}

public class ParteExtraida
{
    public string Rol { get; set; } = string.Empty; // Locador | Locatario | Garante
    public string? Nombre { get; set; }
    public string? Documento { get; set; }
    public string? Domicilio { get; set; }
    public string? Mail { get; set; }
}

public class GastoExtraido
{
    public string TipoGasto { get; set; } = string.Empty;
    public string? Responsable { get; set; }
    public decimal? PorcentajeInquilino { get; set; }
    public string? TextoClausula { get; set; }
    public decimal Confianza { get; set; }
}

// Lo que Gemini leyó del contrato. Todo es tolerante: un dato inválido queda sin valor y con confianza 0.
public class ResultadoExtraccion
{
    public CampoExtraido<DateTime> FechaFirma { get; set; } = new();
    public CampoExtraido<DateTime> FechaInicio { get; set; } = new();
    public CampoExtraido<DateTime> FechaFin { get; set; } = new();
    public CampoExtraido<DateTime> FechaPrimerAjuste { get; set; } = new();
    public CampoExtraido<decimal> MontoInicial { get; set; } = new();
    public CampoExtraido<decimal> PorcentajeFijo { get; set; } = new();
    public CampoExtraido<decimal> PunitorioDiario { get; set; } = new();
    public CampoExtraido<decimal> MontoDeposito { get; set; } = new();
    public CampoExtraido<string> Moneda { get; set; } = new();
    public CampoExtraido<string> IndiceAjuste { get; set; } = new();
    public CampoExtraido<string> Destino { get; set; } = new();
    public CampoExtraido<string> MedioPago { get; set; } = new();
    public CampoExtraido<string> MonedaDeposito { get; set; } = new();
    public CampoExtraido<string> TipoGarantia { get; set; } = new();
    public CampoExtraido<string> CondicionesRescision { get; set; } = new();
    public CampoExtraido<int> FrecuenciaAjusteMeses { get; set; } = new();
    public CampoExtraido<int> DiaVencimiento { get; set; } = new();
    public CampoExtraido<int> PreavisoRescisionDias { get; set; } = new();
    public List<ParteExtraida> Partes { get; set; } = new();
    public List<GastoExtraido> Gastos { get; set; } = new();

    private static readonly string[] Monedas = { "ARS", "USD" };
    private static readonly string[] Indices = { "IPC", "ICL", "CasaPropia", "Fijo", "Otro" };
    private static readonly string[] Destinos = { "Vivienda", "Comercial", "Temporario" };
    private static readonly string[] Garantias = { "Propietaria", "Caucion", "ReciboSueldo", "Otra" };
    private static readonly string[] Responsables = { "Inquilino", "Propietario", "Compartido" };

    // Nombre del campo -> confianza, para resaltar en la revisión lo que el modelo no tiene claro.
    public Dictionary<string, decimal> ConfianzaPorCampo() => new()
    {
        ["FechaFirma"] = FechaFirma.Confianza,
        ["FechaInicio"] = FechaInicio.Confianza,
        ["FechaFin"] = FechaFin.Confianza,
        ["FechaPrimerAjuste"] = FechaPrimerAjuste.Confianza,
        ["MontoAlquiler"] = MontoInicial.Confianza,
        ["PorcentajeFijo"] = PorcentajeFijo.Confianza,
        ["PunitorioDiario"] = PunitorioDiario.Confianza,
        ["MontoDeposito"] = MontoDeposito.Confianza,
        ["Moneda"] = Moneda.Confianza,
        ["IndiceAjuste"] = IndiceAjuste.Confianza,
        ["Destino"] = Destino.Confianza,
        ["MedioPago"] = MedioPago.Confianza,
        ["MonedaDeposito"] = MonedaDeposito.Confianza,
        ["TipoGarantia"] = TipoGarantia.Confianza,
        ["CondicionesRescision"] = CondicionesRescision.Confianza,
        ["FrecuenciaAjusteMeses"] = FrecuenciaAjusteMeses.Confianza,
        ["DiaVencimiento"] = DiaVencimiento.Confianza,
        ["PreavisoRescisionDias"] = PreavisoRescisionDias.Confianza
    };

    public static ResultadoExtraccion Parsear(string? json)
    {
        var r = new ResultadoExtraccion();
        if (string.IsNullOrWhiteSpace(json))
        {
            return r;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object)
            {
                return r;
            }

            r.FechaFirma = LeerFecha(raiz, "fechaFirma");
            r.FechaInicio = LeerFecha(raiz, "fechaInicio");
            r.FechaFin = LeerFecha(raiz, "fechaFin");
            r.FechaPrimerAjuste = LeerFecha(raiz, "fechaPrimerAjuste");
            r.MontoInicial = LeerDecimal(raiz, "montoInicial", 0.01m, 1_000_000_000m);
            r.PorcentajeFijo = LeerDecimal(raiz, "porcentajeFijo", 0m, 100m);
            r.PunitorioDiario = LeerDecimal(raiz, "punitorioDiario", 0m, 100m);
            r.MontoDeposito = LeerDecimal(raiz, "montoDeposito", 0m, 1_000_000_000m);
            r.Moneda = LeerOpcion(raiz, "moneda", Monedas);
            r.IndiceAjuste = LeerOpcion(raiz, "indiceAjuste", Indices);
            r.Destino = LeerOpcion(raiz, "destino", Destinos);
            r.MonedaDeposito = LeerOpcion(raiz, "monedaDeposito", Monedas);
            r.TipoGarantia = LeerOpcion(raiz, "tipoGarantia", Garantias);
            r.MedioPago = LeerTexto(raiz, "medioPago", 200);
            r.CondicionesRescision = LeerTexto(raiz, "condicionesRescision", 1000);
            r.FrecuenciaAjusteMeses = LeerEntero(raiz, "frecuenciaAjusteMeses", 1, 60);
            r.DiaVencimiento = LeerEntero(raiz, "diaVencimiento", 1, 31);
            r.PreavisoRescisionDias = LeerEntero(raiz, "preavisoRescisionDias", 0, 365);
            r.Partes = LeerPartes(raiz);
            r.Gastos = LeerGastos(raiz);
        }
        catch (JsonException)
        {
            return new ResultadoExtraccion();
        }

        return r;
    }

    // Por cada tipo del catálogo: lo que dijo el contrato (Origen "IA") o el responsable por defecto (Origen "Default").
    public static List<ReglaGasto> ReglasDesde(ResultadoExtraccion r, IEnumerable<Gastos> catalogo)
    {
        var reglas = new List<ReglaGasto>();

        foreach (Gastos tipo in catalogo)
        {
            GastoExtraido? extraido = r.Gastos
                .Where(g => string.Equals(g.TipoGasto.Trim(), tipo.TipoGasto, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(g => g.Confianza)
                .FirstOrDefault();

            string porDefecto = tipo.ResponsableDefault == "Inquilino" ? "Inquilino" : "Propietario";
            var regla = new ReglaGasto
            {
                IdGasto = tipo.IdGasto,
                TipoGasto = tipo.TipoGasto,
                Responsable = porDefecto,
                PorcentajeInquilino = porDefecto == "Inquilino" ? 100m : 0m,
                Origen = "Default"
            };

            string? responsable = Responsables.FirstOrDefault(x => string.Equals(x, extraido?.Responsable?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (extraido != null && responsable != null && (responsable != "Compartido" || extraido.PorcentajeInquilino.HasValue))
            {
                regla.Responsable = responsable;
                regla.PorcentajeInquilino = responsable switch
                {
                    "Inquilino" => 100m,
                    "Propietario" => 0m,
                    _ => extraido.PorcentajeInquilino!.Value
                };
                regla.Origen = "IA";
                regla.TextoClausula = Recortar(extraido.TextoClausula, 1000);
                regla.Confianza = extraido.Confianza;
            }

            reglas.Add(regla);
        }

        return reglas;
    }

    // Copia al contrato los datos que el modelo encontró y calcula el régimen legal (y el ajuste que la ley impone).
    public static void AplicarA(ResultadoExtraccion r, Contratos c)
    {
        if (r.FechaFirma.TieneValor) c.FechaFirma = r.FechaFirma.Valor;
        if (r.FechaInicio.TieneValor) c.FechaInicio = r.FechaInicio.Valor;
        if (r.FechaFin.TieneValor) c.FechaFin = r.FechaFin.Valor;
        if (r.FechaPrimerAjuste.TieneValor) c.FechaPrimerAjuste = r.FechaPrimerAjuste.Valor;
        if (r.MontoInicial.TieneValor) c.MontoAlquiler = r.MontoInicial.Valor;
        if (r.PorcentajeFijo.TieneValor) c.PorcentajeFijo = r.PorcentajeFijo.Valor;
        if (r.PunitorioDiario.TieneValor) c.PunitorioDiario = r.PunitorioDiario.Valor;
        if (r.MontoDeposito.TieneValor) c.MontoDeposito = r.MontoDeposito.Valor;
        if (r.Moneda.TieneValor) c.Moneda = r.Moneda.Valor!;
        if (r.IndiceAjuste.TieneValor) c.IndiceAjuste = r.IndiceAjuste.Valor;
        if (r.Destino.TieneValor) c.Destino = r.Destino.Valor;
        if (r.MedioPago.TieneValor) c.MedioPago = r.MedioPago.Valor;
        if (r.MonedaDeposito.TieneValor) c.MonedaDeposito = r.MonedaDeposito.Valor;
        if (r.TipoGarantia.TieneValor) c.TipoGarantia = r.TipoGarantia.Valor;
        if (r.CondicionesRescision.TieneValor) c.CondicionesRescision = r.CondicionesRescision.Valor;
        if (r.FrecuenciaAjusteMeses.TieneValor) c.FrecuenciaAjusteMeses = r.FrecuenciaAjusteMeses.Valor;
        if (r.DiaVencimiento.TieneValor) c.DiaVencimiento = (byte)r.DiaVencimiento.Valor;
        if (r.PreavisoRescisionDias.TieneValor) c.PreavisoRescisionDias = r.PreavisoRescisionDias.Valor;

        ReglasContrato.AplicarRegimen(c);
    }

    // ---- Lectura tolerante del JSON ----

    private static string? Recortar(string? texto, int largo)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }

        texto = texto.Trim();
        return texto.Length > largo ? texto[..largo] : texto;
    }

    private static decimal LeerConfianza(JsonElement campo)
    {
        if (campo.ValueKind == JsonValueKind.Object && campo.TryGetProperty("confianza", out JsonElement c) && TryDecimal(c, out decimal valor))
        {
            return Math.Round(Math.Clamp(valor, 0m, 1m), 2);
        }

        return 0m;
    }

    private static bool TryDecimal(JsonElement e, out decimal valor)
    {
        valor = 0;
        if (e.ValueKind == JsonValueKind.Number)
        {
            return e.TryGetDecimal(out valor);
        }

        if (e.ValueKind == JsonValueKind.String)
        {
            string? texto = e.GetString();
            return decimal.TryParse(texto, NumberStyles.Number, CultureInfo.InvariantCulture, out valor);
        }

        return false;
    }

    // Devuelve el elemento "valor" del campo, o null si no está o es null.
    private static JsonElement? Valor(JsonElement raiz, string nombre, out JsonElement campo)
    {
        campo = default;
        foreach (JsonProperty p in raiz.EnumerateObject())
        {
            if (string.Equals(p.Name, nombre, StringComparison.OrdinalIgnoreCase))
            {
                campo = p.Value;
                if (campo.ValueKind == JsonValueKind.Object && campo.TryGetProperty("valor", out JsonElement v)
                    && v.ValueKind != JsonValueKind.Null && v.ValueKind != JsonValueKind.Undefined)
                {
                    return v;
                }

                return null;
            }
        }

        return null;
    }

    private static CampoExtraido<DateTime> LeerFecha(JsonElement raiz, string nombre)
    {
        var resultado = new CampoExtraido<DateTime>();
        JsonElement? v = Valor(raiz, nombre, out JsonElement campo);
        if (v is { ValueKind: JsonValueKind.String }
            && DateTime.TryParseExact(v.Value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fecha)
            && fecha.Year is >= 1990 and <= 2100)
        {
            resultado.Valor = fecha;
            resultado.TieneValor = true;
            resultado.Confianza = LeerConfianza(campo);
        }

        return resultado;
    }

    private static CampoExtraido<decimal> LeerDecimal(JsonElement raiz, string nombre, decimal minimo, decimal maximo)
    {
        var resultado = new CampoExtraido<decimal>();
        JsonElement? v = Valor(raiz, nombre, out JsonElement campo);
        if (v != null && TryDecimal(v.Value, out decimal valor) && valor >= minimo && valor <= maximo)
        {
            resultado.Valor = Math.Round(valor, 3);
            resultado.TieneValor = true;
            resultado.Confianza = LeerConfianza(campo);
        }

        return resultado;
    }

    private static CampoExtraido<int> LeerEntero(JsonElement raiz, string nombre, int minimo, int maximo)
    {
        var resultado = new CampoExtraido<int>();
        JsonElement? v = Valor(raiz, nombre, out JsonElement campo);
        if (v != null && TryDecimal(v.Value, out decimal valor) && valor == Math.Truncate(valor) && valor >= minimo && valor <= maximo)
        {
            resultado.Valor = (int)valor;
            resultado.TieneValor = true;
            resultado.Confianza = LeerConfianza(campo);
        }

        return resultado;
    }

    private static CampoExtraido<string> LeerTexto(JsonElement raiz, string nombre, int largoMaximo)
    {
        var resultado = new CampoExtraido<string>();
        JsonElement? v = Valor(raiz, nombre, out JsonElement campo);
        if (v is { ValueKind: JsonValueKind.String })
        {
            string? texto = Recortar(v.Value.GetString(), largoMaximo);
            if (texto != null)
            {
                resultado.Valor = texto;
                resultado.TieneValor = true;
                resultado.Confianza = LeerConfianza(campo);
            }
        }

        return resultado;
    }

    // Texto que tiene que ser uno de los valores permitidos (sin importar mayúsculas).
    private static CampoExtraido<string> LeerOpcion(JsonElement raiz, string nombre, string[] permitidos)
    {
        var resultado = new CampoExtraido<string>();
        JsonElement? v = Valor(raiz, nombre, out JsonElement campo);
        if (v is { ValueKind: JsonValueKind.String })
        {
            string? canonico = permitidos.FirstOrDefault(p => string.Equals(p, v.Value.GetString()?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (canonico != null)
            {
                resultado.Valor = canonico;
                resultado.TieneValor = true;
                resultado.Confianza = LeerConfianza(campo);
            }
        }

        return resultado;
    }

    private static string? TextoDe(JsonElement objeto, string nombre, int largo)
    {
        foreach (JsonProperty p in objeto.EnumerateObject())
        {
            if (string.Equals(p.Name, nombre, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
            {
                return Recortar(p.Value.GetString(), largo);
            }
        }

        return null;
    }

    private static List<ParteExtraida> LeerPartes(JsonElement raiz)
    {
        var partes = new List<ParteExtraida>();
        if (!raiz.TryGetProperty("partes", out JsonElement lista) || lista.ValueKind != JsonValueKind.Array)
        {
            return partes;
        }

        foreach (JsonElement item in lista.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            partes.Add(new ParteExtraida
            {
                Rol = TextoDe(item, "rol", 30) ?? string.Empty,
                Nombre = TextoDe(item, "nombre", 150),
                Documento = TextoDe(item, "documento", 30),
                Domicilio = TextoDe(item, "domicilio", 200),
                Mail = TextoDe(item, "mail", 100)
            });
        }

        return partes;
    }

    private static List<GastoExtraido> LeerGastos(JsonElement raiz)
    {
        var gastos = new List<GastoExtraido>();
        if (!raiz.TryGetProperty("gastos", out JsonElement lista) || lista.ValueKind != JsonValueKind.Array)
        {
            return gastos;
        }

        foreach (JsonElement item in lista.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string? tipo = TextoDe(item, "tipoGasto", 50);
            if (tipo == null)
            {
                continue;
            }

            var gasto = new GastoExtraido
            {
                TipoGasto = tipo,
                Responsable = TextoDe(item, "responsable", 20),
                TextoClausula = TextoDe(item, "textoClausula", 1000)
            };

            decimal confianza = LeerConfianza(item);
            foreach (JsonProperty p in item.EnumerateObject())
            {
                if (string.Equals(p.Name, "porcentajeInquilino", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind != JsonValueKind.Null)
                {
                    if (TryDecimal(p.Value, out decimal porcentaje) && porcentaje >= 0 && porcentaje <= 100)
                    {
                        gasto.PorcentajeInquilino = Math.Round(porcentaje, 2);
                    }
                    else
                    {
                        // Porcentaje imposible: se descarta el dato y se marca la fila para revisar.
                        confianza = 0m;
                    }
                }
            }

            gasto.Confianza = confianza;
            gastos.Add(gasto);
        }

        return gastos;
    }
}
