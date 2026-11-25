namespace OSO.Models;

// Un mes de un tipo de gasto que el contrato pone a cargo del inquilino y que todavía no tiene gasto cargado.
// No se guarda: se calcula a partir del contrato. El gasto se crea cuando el inquilino paga y carga el monto de la factura.
public class PendienteContrato
{
    public int IdPropiedad { get; set; }
    public string NombrePropiedad { get; set; } = string.Empty;
    public int IdGasto { get; set; }
    public string TipoGasto { get; set; } = string.Empty;
    public DateTime Periodo { get; set; }
}
