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

    // Parte del inquilino (0 si no le corresponde nada) y lo que ya pagó
    public decimal MontoInquilino { get; set; }
    public decimal PagadoInquilino { get; set; }

    // El estado no se guarda: se deduce de los pagos registrados (de todas las partes)
    public string Estado => MontoPagado >= MontoResponsable && PagadoInquilino >= MontoInquilino ? "Pagado" : "A pagar";

    // Sin pagar y con la fecha de vencimiento ya pasada
    public bool Vencido => Estado != "Pagado" && FechaVencimiento.Date < DateTime.Today;

    // Devuelve el mensaje de error, o null si el gasto (o la expensa) es válido.
    public string? Validar()
    {
        if (Monto <= 0)
        {
            return "El monto debe ser mayor a cero.";
        }

        if (FechaVencimiento == default)
        {
            return "La fecha de vencimiento es obligatoria.";
        }

        if ((Aclaracion?.Length ?? 0) > 255)
        {
            return "La aclaración no puede superar los 255 caracteres.";
        }

        return null;
    }
}
