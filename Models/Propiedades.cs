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

    // No se guarda en Propiedades: es el MontoAlquiler del contrato activo (se carga en las consultas)
    public decimal? AlquilerActual { get; set; }

    // Datos del dueño: sólo se cargan en la vista del inquilino.
    public string? NombrePropietario { get; set; }
    public string? CorreoPropietario { get; set; }
    public string? TelefonoPropietario { get; set; }
    public DateTime? FinContrato { get; set; }

    public static readonly string[] EstadosValidos = { "Disponible", "Desocupado", "Ocupado", "Ocupada" };

    // Devuelve el mensaje de error, o null si la propiedad es válida.
    public string? Validar()
    {
        if (string.IsNullOrWhiteSpace(Direccion))
        {
            return "La dirección es obligatoria.";
        }

        if (Direccion.Trim().Length > 50)
        {
            return "La dirección no puede superar los 50 caracteres.";
        }

        if (Superficie <= 0)
        {
            return "La superficie debe ser mayor a cero.";
        }

        if (Ambientes <= 0)
        {
            return "La cantidad de ambientes debe ser mayor a cero.";
        }

        if (!string.IsNullOrWhiteSpace(Estado) && Array.IndexOf(EstadosValidos, Estado) < 0)
        {
            return "El estado elegido no es válido.";
        }

        return null;
    }
}