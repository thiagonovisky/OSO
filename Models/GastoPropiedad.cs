namespace OSO.Models;

public class GastoPropiedad
{
    public int IdGastoPropiedad { get; set; }
    public int IdPropiedad { get; set; }
    public int IdGasto { get; set; }
    public DateTime Periodo { get; set; }
    public decimal Monto { get; set; }
    public DateTime FechaVencimiento { get; set; }
    public DateTime FechaAlta { get; set; }
    public string? Aclaracion { get; set; }

    // Datos de la parte del propietario (por ahora es el único responsable)
    public int IdGastoResponsable { get; set; }
    public decimal MontoResponsable { get; set; }
    public decimal MontoPagado { get; set; }
    public DateTime? FechaPago { get; set; }
    public string? Comprobante { get; set; }

    // Calculados en las consultas
    public string TipoGasto { get; set; } = string.Empty;
    public string NombrePropiedad { get; set; } = string.Empty;

    // El estado no se guarda: se deduce de los pagos registrados
    public string Estado => MontoPagado >= MontoResponsable ? "Pagado" : "A pagar";
}
