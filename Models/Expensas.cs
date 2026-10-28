namespace OSO.Models;
public class Expensas
{
    public int IdExpensa { get; set; }
    public double Monto { get; set; }
    public DateTime FechaVencimiento { get; set; }
    public DateTime FechaAlta { get; set; }
    public string Estado { get; set; }
    public string Comprobante { get; set; }
    public DateTime FechaPago { get; set; }
    public string NombrePropiedad { get; set; }
}