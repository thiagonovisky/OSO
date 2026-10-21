namespace OSO.Models;
public class Usuario
{
    public int IdUsuario { get; set; }
    public int Id
    {
        get => IdUsuario;
        set => IdUsuario = value;
    }

    public int IdPropietario { get; set; }
    public int IdInquilino { get; set; }
    public string Nombre { get; set; }
    public string Apellido { get; set; }
    public string Correo { get; set; }
    public string Contrasena { get; set; }
    public int Telefono { get; set; }
}