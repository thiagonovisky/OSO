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
        int posAltura = PosicionAltura(propiedad);
        if (posAltura < 0)
        {
            return false;
        }

        List<string> palabrasFactura = Palabras(domicilioFactura);
        var factura = new HashSet<string>(palabrasFactura);
        var facturaSeparada = new HashSet<string>();
        foreach (string palabra in palabrasFactura)
        {
            facturaSeparada.UnionWith(SepararLetrasYNumeros(palabra));
        }

        if (!factura.Contains(propiedad[posAltura]))
        {
            return false;
        }

        // Todas las palabras de la calle (las de antes de la altura) tienen que estar en la factura.
        for (int i = 0; i < posAltura; i++)
        {
            if (!Ignoradas.Contains(propiedad[i]) && !factura.Contains(propiedad[i]))
            {
                return false;
            }
        }

        // Y también la unidad (lo que viene después de la altura), separada en letras y números.
        for (int i = posAltura + 1; i < propiedad.Count; i++)
        {
            foreach (string parte in SepararLetrasYNumeros(propiedad[i]))
            {
                if (!Ignoradas.Contains(parte) && !facturaSeparada.Contains(parte))
                {
                    return false;
                }
            }
        }

        return true;
    }

    // La altura es el primer número que tiene antes alguna palabra de la calle: así "25 de Mayo 300" o
    // "Av. 9 de Julio 1000" toman 300 y 1000, no el número del nombre. -1 si no hay altura.
    private static int PosicionAltura(List<string> palabras)
    {
        bool hayPalabraDeCalle = false;
        for (int i = 0; i < palabras.Count; i++)
        {
            if (EsNumero(palabras[i]) && hayPalabraDeCalle)
            {
                return i;
            }

            if (!Ignoradas.Contains(palabras[i]))
            {
                hayPalabraDeCalle = true;
            }
        }

        return -1;
    }

    private static bool EsNumero(string palabra)
    {
        foreach (char c in palabra)
        {
            if (!char.IsDigit(c))
            {
                return false;
            }
        }

        return true;
    }

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

        var palabras = new List<string>();
        foreach (string palabra in sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            palabras.Add(Sinonimos.GetValueOrDefault(palabra, palabra));
        }

        return palabras;
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

        // Candidatas: las filas del mismo mes y servicio. Con domicilio leído, solo vale la propiedad que coincide:
        // nunca se completa la fila de otra.
        bool segura = !string.IsNullOrWhiteSpace(lectura.Domicilio);
        var candidatos = new List<FilaCargarMes>();
        foreach (FilaCargarMes fila in filas)
        {
            bool mismoServicio = fila.Periodo == periodo && string.Equals(fila.TipoGasto, lectura.Servicio.Trim(), StringComparison.OrdinalIgnoreCase);
            if (mismoServicio && (!segura || ComparadorDomicilios.Coinciden(fila.NombrePropiedad, lectura.Domicilio!)))
            {
                candidatos.Add(fila);
            }
        }

        FilaCargarMes? elegida = candidatos.Count == 1 ? candidatos[0] : null;

        if (elegida == null)
        {
            return ParaUbicar;
        }

        // Los meses anteriores del mismo servicio que la factura también cubre (si dice "desde").
        var incluidas = new List<string>();
        if (lectura.PeriodoDesde is DateTime desde)
        {
            foreach (FilaCargarMes fila in filas)
            {
                if (fila.IdPropiedad == elegida.IdPropiedad && fila.IdGasto == elegida.IdGasto && fila.Periodo >= desde && fila.Periodo < periodo)
                {
                    incluidas.Add(fila.Clave);
                }
            }
        }

        EstadoUbicacion estado = segura && !lectura.EsDudosa ? EstadoUbicacion.Leida : EstadoUbicacion.Revisar;
        return new Ubicacion(estado, elegida.Clave, incluidas);
    }
}
