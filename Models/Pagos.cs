namespace OSO.Models;

public class Pagos
{
    public int IdPago { get; set; }
    public int? IdContrato { get; set; }
    public int? IdGastoResponsable { get; set; }
    public string? Comprobante { get; set; }
    public DateTime Fecha { get; set; }
    public decimal Monto { get; set; }
    public string Concepto { get; set; } = string.Empty;
}