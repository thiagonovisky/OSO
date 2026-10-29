namespace OSO.Models;

public class Gastos
{
    public int IdGasto { get; set; }
    public string TipoGasto { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
}
