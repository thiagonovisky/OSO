using System.Text.RegularExpressions;

namespace OSO.Models;
public class Usuario
{
    public const int LargoMinimoContrasena = 8;

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
    public string Telefono { get; set; }
    public DateTime FechaAlta { get; set; } = DateTime.Now;
    public string? FotoPerfil { get; set; } = null;

    // Valida los datos del registro. Si son correctos, deja el correo sin espacios y el teléfono
    // con el formato que se guarda en la base. Devuelve el mensaje de error, o null si está todo bien.
    public string? ValidarYPrepararRegistro(string? confirmarContrasena, string? codigoPais, string? telefono)
    {
        if (string.IsNullOrWhiteSpace(Nombre) || string.IsNullOrWhiteSpace(Apellido))
        {
            return "Debe completar nombre y apellido.";
        }

        if (Nombre.Trim().Length > 50 || Apellido.Trim().Length > 50)
        {
            return "El nombre y el apellido no pueden superar los 50 caracteres.";
        }

        if (string.IsNullOrWhiteSpace(Correo) || string.IsNullOrWhiteSpace(Contrasena))
        {
            return "Debe completar correo y contraseña.";
        }

        if (Correo.Trim().Length > 100)
        {
            return "El correo no puede superar los 100 caracteres.";
        }

        if (!Regex.IsMatch(Correo.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
        {
            return "El correo electrónico no es válido.";
        }

        PaisTelefono? pais = TelefonoFormato.BuscarPais(codigoPais);
        if (pais == null)
        {
            return "Elegí un país para el teléfono.";
        }

        string digitosTelefono = (telefono ?? "").Replace("-", "").Replace(" ", "").Trim();
        if (!Regex.IsMatch(digitosTelefono, @"^\d+$") || digitosTelefono.Length != pais.Digitos)
        {
            return $"El teléfono de {pais.Nombre} debe tener {pais.Digitos} dígitos, incluido el código de área.";
        }

        if (!EsContrasenaSegura(Contrasena))
        {
            return $"La contraseña debe tener al menos {LargoMinimoContrasena} caracteres y 1 carácter especial.";
        }

        if (Contrasena != confirmarContrasena)
        {
            return "Las contraseñas no coinciden.";
        }

        Nombre = Nombre.Trim();
        Apellido = Apellido.Trim();
        Correo = Correo.Trim();
        Telefono = TelefonoFormato.Componer(pais.Codigo, digitosTelefono);
        return null;
    }

    // Mínimo 8 caracteres y al menos uno que no sea letra ni número.
    private static bool EsContrasenaSegura(string contrasena)
    {
        return contrasena.Length >= LargoMinimoContrasena && Regex.IsMatch(contrasena, @"[^\p{L}\p{N}]");
    }
}
