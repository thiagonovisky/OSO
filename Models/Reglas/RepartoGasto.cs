namespace OSO.Models;

public record ParteGasto(string Responsable, decimal Monto, decimal Porcentaje);

// Reparto de un gasto entre inquilino y propietario (función pura).
public static class RepartoGasto
{
    // Siempre devuelve dos partes. La del inquilino se redondea a 2 decimales y la del propietario es
    // lo que falta para llegar al total, así las partes suman exactamente el total.
    public static List<ParteGasto> Repartir(decimal total, decimal porcentajeInquilino)
    {
        decimal porcentaje = Math.Clamp(porcentajeInquilino, 0m, 100m);
        decimal inquilino = Math.Round(total * porcentaje / 100m, 2, MidpointRounding.AwayFromZero);
        decimal propietario = total - inquilino;

        return new List<ParteGasto>
        {
            new("Inquilino", inquilino, porcentaje),
            new("Propietario", propietario, 100m - porcentaje)
        };
    }
}
