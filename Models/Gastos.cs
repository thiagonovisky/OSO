namespace OSO.Models;

public class Gastos
{
    public const int LargoMaximoNombre = 50;

    public int IdGasto { get; set; }
    public string TipoGasto { get; set; } = string.Empty;
    public string? Descripcion { get; set; }

    public static bool EsNombreValido(string? nombreTipo)
    {
        return !string.IsNullOrWhiteSpace(nombreTipo) && nombreTipo.Trim().Length <= LargoMaximoNombre;
    }
}
