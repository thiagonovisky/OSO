namespace OSO.Models;

// Un mes de un tipo de gasto que el contrato pone a cargo del inquilino (todo o en parte) y que todavía no tiene gasto cargado.
// No se guarda: se calcula a partir del contrato. El gasto se crea cuando el inquilino paga y carga el monto de la factura.
public class PendienteContrato
{
    // Cuántos meses hacia atrás (contando el actual) se muestran como "a pagar"; los anteriores van al historial de pendientes.
    public const int MesesRecientes = 3;

    public int IdPropiedad { get; set; }
    public string NombrePropiedad { get; set; } = string.Empty;
    public int IdGasto { get; set; }
    public string TipoGasto { get; set; } = string.Empty;
    public DateTime Periodo { get; set; }
    public string Responsable { get; set; } = "Inquilino"; // Inquilino | Compartido
    public decimal PorcentajeInquilino { get; set; } = 100m;

    public bool EsCompartido => Responsable == "Compartido";

    public bool EsReciente => Periodo >= new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-(MesesRecientes - 1));

    // Qué parte del gasto es del inquilino, para mostrarle.
    public string TextoACargo => EsCompartido ? $"{Formato.Numero(PorcentajeInquilino, "0.##")}% a tu cargo" : "100% a tu cargo";

    public string TextoMonto => EsCompartido ? "Monto total de la factura $" : "Monto $";
}
