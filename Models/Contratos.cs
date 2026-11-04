namespace OSO.Models;

public class Contratos
{
    public int IdContrato { get; set; }
    public int IdPropiedad { get; set; }
    public int IdInquilino { get; set; }
    public DateTime FechaInicio { get; set; }
    public DateTime FechaFin { get; set; }
    public decimal MontoAlquiler { get; set; }
}