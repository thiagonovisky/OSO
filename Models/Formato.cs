using System.Globalization;

namespace OSO.Models;

// Formatos de presentación que comparten las vistas (montos en pesos, fechas y datos opcionales).
public static class Formato
{
    private static readonly CultureInfo Argentina = new("es-AR");

    public static string Pesos(decimal? monto) => monto.HasValue ? monto.Value.ToString("C", Argentina) : "-";

    public static string Numero(decimal? valor, string formato = "N2") => valor.HasValue ? valor.Value.ToString(formato, Argentina) : "-";

    // "noviembre 2026"
    public static string Mes(DateTime fecha) => fecha.ToString("MMMM yyyy", Argentina);

    public static string Fecha(DateTime? fecha) => fecha.HasValue && fecha.Value != default ? fecha.Value.ToString("dd/MM/yyyy") : "-";

    // Un dato opcional con su unidad (ej. "30 días"), o "-" si no está cargado.
    public static string Dato(object? valor, string sufijo = "") => string.IsNullOrWhiteSpace(valor?.ToString()) ? "-" : $"{valor}{sufijo}";

    // Valor para un <input type="number">: siempre con punto decimal, sin importar la cultura.
    public static string ParaInput(decimal? valor, string formato = "0.00") => valor?.ToString(formato, CultureInfo.InvariantCulture) ?? string.Empty;
}
