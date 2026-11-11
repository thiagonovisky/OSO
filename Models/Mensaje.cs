namespace OSO.Models;

public class Mensaje
{
    public int IdMensaje { get; set; }
    public int IdEmisor { get; set; }
    public int IdReceptor { get; set; }
    public string Texto { get; set; } = string.Empty;
    public DateTime FechaEnvio { get; set; }
    public bool Leido { get; set; }
}

// Una persona con la que el usuario puede chatear, junto con el último mensaje intercambiado.
public class Contacto
{
    public int IdUsuario { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Apellido { get; set; } = string.Empty;
    public string? FotoPerfil { get; set; }
    public string? UltimoMensaje { get; set; }
    public DateTime? FechaUltimoMensaje { get; set; }
    public int? IdEmisorUltimo { get; set; }
    public bool UltimoLeido { get; set; }
    public int NoLeidos { get; set; }

    public string NombreCompleto => $"{Nombre} {Apellido}".Trim();
    public string Iniciales => $"{(Nombre.Length > 0 ? Nombre[0] : ' ')}{(Apellido.Length > 0 ? Apellido[0] : ' ')}".Trim().ToUpperInvariant();
}
