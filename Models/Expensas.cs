namespace OSO.Models;
public class Expensas
{
    public int IdExpensa { get; set; }
    public double Monto { get; set; }
    public DateTime FechaVencimiento { get; set; }
    public DateTime FechaAlta { get; set; }
    public string? Aclaracion { get; set; }
    public string Estado { get; set; } = string.Empty;
    public string Comprobante { get; set; } = string.Empty;
    public DateTime? FechaPago { get; set; }
    public string NombrePropiedad { get; set; } = string.Empty;
}