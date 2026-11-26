using System.Globalization;
using System.Text;

namespace OSO.Models;

// Compara la dirección de una propiedad con el domicilio que figura en una factura, tolerando mayúsculas, acentos,
// abreviaturas ("Av.", "Avda.", "Gral.") y lo que la factura agrega (piso, depto, localidad).
public static class ComparadorDomicilios
{
    private static readonly Dictionary<string, string> Sinonimos = new()
    {
        ["avenida"] = "av", ["avda"] = "av",
        ["general"] = "gral"
    };

    private static readonly HashSet<string> Ignoradas = ["av", "calle", "piso", "dto", "depto", "p", "de"];

    // Coinciden si la factura tiene la misma altura, todas las palabras del nombre de la calle y, si la propiedad indica
    // una unidad ("3B", "piso 3 dto B"), también esa unidad: en un mismo edificio, la factura de otra unidad no coincide,
    // y una factura sin unidad no alcanza para saber cuál es.
    public static bool Coinciden(string direccionPropiedad, string domicilioFactura)
    {
        List<string> propiedad = Palabras(direccionPropiedad);

        // La altura es el primer número que tiene antes alguna palabra de la calle: así "25 de Mayo 300" o
        // "Av. 9 de Julio 1000" toman 300 y 1000, no el número del nombre.
        int posAltura = -1;
        for (int i = 0; i < propiedad.Count && posAltura < 0; i++)
        {
            if (EsNumero(propiedad[i]) && propiedad.Take(i).Any(p => !Ignoradas.Contains(p)))
            {
                posAltura = i;
            }
        }

        if (posAltura < 0)
        {
            return false;
        }

        List<string> palabrasFactura = Palabras(domicilioFactura);
        HashSet<string> factura = palabrasFactura.ToHashSet();
        HashSet<string> facturaSeparada = palabrasFactura.SelectMany(SepararLetrasYNumeros).ToHashSet();

        List<string> calle = propiedad.Take(posAltura).Where(p => !Ignoradas.Contains(p)).ToList();
        List<string> unidad = propiedad.Skip(posAltura + 1).SelectMany(SepararLetrasYNumeros).Where(p => !Ignoradas.Contains(p)).ToList();

        return factura.Contains(propiedad[posAltura])
            && calle.All(factura.Contains)
            && unidad.All(facturaSeparada.Contains);
    }

    private static bool EsNumero(string palabra) => palabra.All(char.IsDigit);

    // "3b" -> "3", "b"; "p3" -> "p", "3". Así "3B" coincide con "PISO 3 DTO B" o con "P3 DTO B".
    private static IEnumerable<string> SepararLetrasYNumeros(string palabra)
    {
        int inicio = 0;
        for (int i = 1; i <= palabra.Length; i++)
        {
            if (i == palabra.Length || char.IsDigit(palabra[i]) != char.IsDigit(palabra[i - 1]))
            {
                yield return palabra[inicio..i];
                inicio = i;
            }
        }
    }

    private static List<string> Palabras(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return [];
        }

        var sb = new StringBuilder();
        foreach (char c in texto.ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }

        return sb.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => Sinonimos.GetValueOrDefault(p, p))
            .ToList();
    }
}

public enum EstadoUbicacion { Leida, Revisar, ParaUbicar }

// Dónde va una factura leída: la fila que completa (Clave) y los meses anteriores que también cubre (ClavesIncluidas).
public record Ubicacion(EstadoUbicacion Estado, string? Clave, IReadOnlyList<string> ClavesIncluidas);

public static class UbicadorFacturas
{
    private static readonly Ubicacion ParaUbicar = new(EstadoUbicacion.ParaUbicar, null, []);

    public static Ubicacion Ubicar(LecturaFactura lectura, IReadOnlyList<FilaCargarMes> filas)
    {
        if (string.IsNullOrWhiteSpace(lectura.Servicio) || !lectura.Periodo.HasValue)
        {
            return ParaUbicar;
        }

        DateTime periodo = lectura.Periodo.Value;
        List<FilaCargarMes> candidatos = filas
            .Where(f => f.Periodo == periodo && string.Equals(f.TipoGasto, lectura.Servicio.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        FilaCargarMes? elegida;
        bool segura;
        if (!string.IsNullOrWhiteSpace(lectura.Domicilio))
        {
            // Con domicilio leído, solo vale la propiedad que coincide: nunca se completa la fila de otra.
            List<FilaCargarMes> coinciden = candidatos.Where(f => ComparadorDomicilios.Coinciden(f.NombrePropiedad, lectura.Domicilio)).ToList();
            elegida = coinciden.Count == 1 ? coinciden[0] : null;
            segura = true;
        }
        else
        {
            elegida = candidatos.Count == 1 ? candidatos[0] : null;
            segura = false;
        }

        if (elegida == null)
        {
            return ParaUbicar;
        }

        List<string> incluidas = lectura.PeriodoDesde is DateTime desde
            ? filas.Where(f => f.IdPropiedad == elegida.IdPropiedad && f.IdGasto == elegida.IdGasto && f.Periodo >= desde && f.Periodo < periodo)
                .Select(f => f.Clave)
                .ToList()
            : [];

        EstadoUbicacion estado = segura && !lectura.EsDudosa ? EstadoUbicacion.Leida : EstadoUbicacion.Revisar;
        return new Ubicacion(estado, elegida.Clave, incluidas);
    }
}
