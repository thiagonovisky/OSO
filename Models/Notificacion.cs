namespace OSO.Models;

public class Notificacion
{
    public int IdNotificacion { get; set; }
    public int IdUsuario { get; set; }
    public int? IdGastoResponsable { get; set; }
    public int? IdEmisor { get; set; } // Si es aviso de un mensaje: quién lo mandó
    public string Texto { get; set; } = string.Empty;
    public DateTime FechaCreacion { get; set; }
    public bool Leida { get; set; }
}
