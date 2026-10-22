namespace OSO.Models;
public class Propiedades
{
    public int IdPropiedad { get; set; }
    public string Direccion { get; set; }
    public int Superficie { get; set; }
    public string Ambientes { get; set; }
    public string Estado { get; set; }
    public DateTime FechaDeAlta { get; set; }
    public string Foto { get; set; }
    public int IdPropietario { get; set; }
    public int IdInquilino { get; set; }
    public int ResumenPropiedad { get; set; }
}