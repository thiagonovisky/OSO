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

    // Los eventos que arma el contrato (y los vencimientos de gastos) tienen Tipo distinto de "Manual" y no se editan a mano.
    public int? IdContrato { get; set; }
    public int? IdGastoPropiedad { get; set; }
    public string Tipo { get; set; } = "Manual";

    // En el calendario del propietario los eventos manuales se identifican por la propiedad.
    public string TextoEnCalendario => Tipo == "Manual" ? NombrePropiedad ?? Titulo : Titulo;

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
