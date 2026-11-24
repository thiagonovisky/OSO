using System.Globalization;
using System.Text.Json;

namespace OSO.Models;

// Lee las respuestas de las APIs de índices. Tolerante: si el JSON está vacío o roto devuelve una lista vacía.
public static class LectorIndices
{
    // apis.datos.gob.ar: { "data": [["2026-10-01", 12785.9321], ...] }
    public static List<(DateTime Periodo, decimal Valor)> LeerIpc(string? json)
    {
        var valores = new List<(DateTime, decimal)>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return valores;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("data", out JsonElement datos)
                || datos.ValueKind != JsonValueKind.Array)
            {
                return valores;
            }

            foreach (JsonElement fila in datos.EnumerateArray())
            {
                if (fila.ValueKind != JsonValueKind.Array || fila.GetArrayLength() < 2)
                {
                    continue;
                }

                if (TryFecha(fila[0], out DateTime fecha) && TryValor(fila[1], out decimal valor))
                {
                    valores.Add((new DateTime(fecha.Year, fecha.Month, 1), valor));
                }
            }
        }
        catch (JsonException)
        {
            return new List<(DateTime, decimal)>();
        }

        return valores;
    }

    // api.bcra.gob.ar v4: { "results": [ { "detalle": [ { "fecha": "2026-11-24", "valor": 37.73 } ] } ] }
    public static List<(DateTime Periodo, decimal Valor)> LeerIcl(string? json)
    {
        var valores = new List<(DateTime, decimal)>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return valores;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("results", out JsonElement resultados)
                || resultados.ValueKind != JsonValueKind.Array)
            {
                return valores;
            }

            foreach (JsonElement resultado in resultados.EnumerateArray())
            {
                if (resultado.ValueKind != JsonValueKind.Object
                    || !resultado.TryGetProperty("detalle", out JsonElement detalle)
                    || detalle.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (JsonElement fila in detalle.EnumerateArray())
                {
                    if (fila.ValueKind == JsonValueKind.Object
                        && fila.TryGetProperty("fecha", out JsonElement f) && TryFecha(f, out DateTime fecha)
                        && fila.TryGetProperty("valor", out JsonElement v) && TryValor(v, out decimal valor))
                    {
                        valores.Add((fecha.Date, valor));
                    }
                }
            }
        }
        catch (JsonException)
        {
            return new List<(DateTime, decimal)>();
        }

        return valores;
    }

    private static bool TryFecha(JsonElement e, out DateTime fecha)
    {
        fecha = default;
        return e.ValueKind == JsonValueKind.String
               && DateTime.TryParseExact(e.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha)
               && fecha.Year >= 2000;
    }

    private static bool TryValor(JsonElement e, out decimal valor)
    {
        valor = 0;
        bool leido = e.ValueKind switch
        {
            JsonValueKind.Number => e.TryGetDecimal(out valor),
            JsonValueKind.String => decimal.TryParse(e.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out valor),
            _ => false
        };

        return leido && valor > 0;
    }
}
