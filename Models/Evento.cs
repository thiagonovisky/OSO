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

    // Devuelve el mensaje de error, o null si el evento es válido.
    public string? Validar()
    {
        if (string.IsNullOrWhiteSpace(Titulo))
        {
            return "El título es obligatorio.";
        }

        if (Titulo.Length > 100)
        {
            return "El título no puede superar los 100 caracteres.";
        }

        if (Fecha == default)
        {
            return "La fecha del evento no es válida.";
        }

        if (HoraFin <= HoraInicio)
        {
            return "La hora de fin debe ser posterior a la de inicio.";
        }

        if (Invitado != "No" && Invitado != "Inquilino")
        {
            return "El invitado elegido no es válido.";
        }

        if ((Ubicacion?.Length ?? 0) > 150 || (Descripcion?.Length ?? 0) > 300)
        {
            return "La ubicación o la descripción son demasiado largas.";
        }

        return null;
    }
}
