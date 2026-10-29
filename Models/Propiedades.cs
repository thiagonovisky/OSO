namespace OSO.Models;

public class Propiedades
{
    public int IdPropiedad { get; set; }
    public string Direccion { get; set; } = string.Empty;
    public int Superficie { get; set; }
    public int Ambientes { get; set; }
    public string Estado { get; set; } = string.Empty;
    public DateTime FechaDeAlta { get; set; }
    public string Foto { get; set; } = string.Empty;
    public int IdPropietario { get; set; }
    public int? IdInquilino { get; set; }
    public int? ResumenPropiedad { get; set; }
    public int? IdExpensas { get; set; }
}