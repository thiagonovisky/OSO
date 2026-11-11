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
}