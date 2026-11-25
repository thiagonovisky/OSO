namespace OSO.Models;

// Un tipo de gasto de una propiedad (luz, agua, expensas...) con el estado de sus pagos.
public class ServicioPropiedad
{
    public int IdGasto { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public int CantidadGastos { get; set; }
    public int GastosPendientes { get; set; }
    public string? QuienPaga { get; set; } // según el contrato vigente; null si no hay

    public string Estado => GastosPendientes > 0 ? "Pendiente" : (CantidadGastos > 0 ? "Al día" : "Sin registros");

    public string EstadoCss => GastosPendientes > 0 ? "warn" : (CantidadGastos > 0 ? "ok" : "muted");
}
