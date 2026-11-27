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

    // No es una columna: el contrato que el inquilino todavía tiene que aceptar, si el aviso es
    // "el propietario cargó el contrato de <dirección>" (el aviso lleva directo a ese contrato).
    public int? IdContratoParaAceptar { get; set; }

    public bool EsRecordatorioFacturas => Texto.StartsWith(BD.TextoRecordatorioFacturas);

    // Une cada aviso de contrato cargado con el contrato pendiente de esa dirección.
    public static void VincularContratosParaAceptar(List<Notificacion> notificaciones, List<Contratos> contratosPendientes)
    {
        foreach (Notificacion notificacion in notificaciones)
        {
            if (notificacion.Texto.StartsWith(BD.TextoContratoCargado))
            {
                notificacion.IdContratoParaAceptar = contratosPendientes
                    .FirstOrDefault(c => !string.IsNullOrEmpty(c.Direccion) && notificacion.Texto.Contains(c.Direccion))?.IdContrato;
            }
        }
    }
}
