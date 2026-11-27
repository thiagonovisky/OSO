namespace OSO.Models;

// Búsquedas en listas fijas de opciones (monedas, índices, responsables...).
public static class Opciones
{
    // La opción de la lista que coincide con el valor, sin importar mayúsculas ni espacios. Null si no está.
    public static string? Buscar(IEnumerable<string> opciones, string? valor) =>
        opciones.FirstOrDefault(o => string.Equals(o, valor?.Trim(), StringComparison.OrdinalIgnoreCase));
}
