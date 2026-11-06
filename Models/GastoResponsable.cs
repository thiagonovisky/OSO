namespace OSO.Models;

public class GastoResponsable
{
    public int IdGastoResponsable { get; set; }
    public int IdGastoPropiedad { get; set; }
    public string Responsable { get; set; } = "Propietario"; // Propietario | Inquilino
    public decimal Monto { get; set; }
    public decimal? Porcentaje { get; set; }
    public string Origen { get; set; } = "Manual"; // Manual | IA | Regla
    public string? Justificacion { get; set; }
}
