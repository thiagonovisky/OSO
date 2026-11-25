namespace OSO.Models;

// Un mes de un servicio mensual que el contrato pone a cargo de alguien (todo o en parte) y que todavía no tiene gasto cargado.
// No se guarda: se calcula a partir del contrato. El gasto se crea cuando quien paga carga el monto de la factura.
public class PendienteContrato
{
    // Cuántos meses hacia atrás (contando el actual) se muestran como "a pagar"; los anteriores van a "meses anteriores".
    public const int MesesRecientes = 3;

    public int IdPropiedad { get; set; }
    public string NombrePropiedad { get; set; } = string.Empty;
    public int IdGasto { get; set; }
    public string TipoGasto { get; set; } = string.Empty;
    public DateTime Periodo { get; set; }
    public string Responsable { get; set; } = "Inquilino"; // Inquilino | Propietario | Compartido (según el contrato)
    public decimal PorcentajeInquilino { get; set; } = 100m;
    public string Lado { get; set; } = "Inquilino"; // de quién es este pendiente: Inquilino | Propietario

    public bool EsCompartido => Responsable == "Compartido";

    // Qué porcentaje le toca a quien mira el pendiente.
    public decimal PorcentajePropio => Lado == "Propietario" ? 100m - PorcentajeInquilino : PorcentajeInquilino;

    public bool EsReciente => Periodo >= new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-(MesesRecientes - 1));

    public string TextoACargo => $"{Formato.Numero(PorcentajePropio, "0.##")}% a tu cargo";

    public string TextoMonto => EsCompartido ? "Monto total de la factura $" : "Monto $";

    // Quién paga, contado desde el lado del propietario (pantalla de servicios).
    public string QuienPaga => Responsable switch
    {
        "Inquilino" => "Lo paga el inquilino",
        "Propietario" => "Lo pagás vos",
        _ => $"Compartido ({Formato.Numero(PorcentajeInquilino, "0.##")}% inquilino)"
    };
}
