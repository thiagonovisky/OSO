namespace OSO.Models;

public class Evento
{
    public int IdEvento { get; set; }
    public int IdPropietario { get; set; }
    public int? IdPropiedad { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public TimeSpan HoraInicio { get; set; }
    public TimeSpan HoraFin { get; set; }
    public string? Ubicacion { get; set; }
    public string Invitado { get; set; } = "No";
    public string? Descripcion { get; set; }
    public string? NombrePropiedad { get; set; }
}
