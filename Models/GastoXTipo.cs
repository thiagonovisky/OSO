namespace OSO.Models;

public class GastoXTipo
{
    public int IdGastoXTipo { get; set; }
    public int IdGasto { get; set; }
    public int IdServicios { get; set; }
    public DateTime Mes { get; set; }
    public decimal Monto { get; set; }
    public DateTime FechaVencimiento { get; set; }
    public DateTime FechaAlta { get; set; }
    public string Estado { get; set; } = string.Empty;
    public string? Comprobante { get; set; }
    public DateTime? FechaPago { get; set; }

    public string TipoGasto { get; set; } = string.Empty;
    public string NombreServicio { get; set; } = string.Empty;
}
