namespace OSO.Models;

public class Contratos
{
    public int IdContrato { get; set; }
    public int IdPropiedad { get; set; }
    public int IdInquilino { get; set; }
    public DateTime FechaInicio { get; set; }
    public DateTime FechaFin { get; set; }
    public decimal MontoAlquiler { get; set; }

    // Devuelve el mensaje de error, o null si el contrato es válido.
    public string? Validar()
    {
        if (IdInquilino <= 0)
        {
            return "Debe seleccionar un inquilino para crear el contrato.";
        }

        if (MontoAlquiler <= 0)
        {
            return "El monto del alquiler debe ser mayor a cero.";
        }

        if (FechaInicio == default || FechaFin == default)
        {
            return "Debe completar las fechas del contrato.";
        }

        if (FechaFin < FechaInicio)
        {
            return "La fecha de fin no puede ser anterior a la de inicio.";
        }

        return null;
    }
}
