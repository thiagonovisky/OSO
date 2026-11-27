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

    public string NombreConCorreo => $"{Nombre} {Apellido} ({Correo})";

    public string NombreCompleto => $"{Nombre} {Apellido}".Trim();

    // Valida los datos del registro. Si son correctos, deja el correo sin espacios y el teléfono
    // con el formato que se guarda en la base. Devuelve el mensaje de error, o null si está todo bien.
    public string? ValidarYPrepararRegistro(string? confirmarContrasena, string? codigoPais, string? telefono)
    {
        string? error = ValidarNombre(Nombre, Apellido);
        if (error != null)
        {
            return error;
        }

        if (string.IsNullOrWhiteSpace(Correo) || string.IsNullOrWhiteSpace(Contrasena))
        {
            return "Debe completar correo y contraseña.";
        }

        error = ValidarCorreo(Correo);
        if (error != null)
        {
            return error;
        }

        error = ValidarTelefono(codigoPais, telefono, out string telefonoCompuesto);
        if (error != null)
        {
            return error;
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
        Telefono = telefonoCompuesto;
        return null;
    }

    // Validaciones de cada dato: las usa el registro y la edición del perfil. Devuelven el mensaje de error o null.
    public static string? ValidarNombre(string? nombre, string? apellido)
    {
        if (string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(apellido))
        {
            return "Debe completar nombre y apellido.";
        }

        return nombre.Trim().Length > 50 || apellido.Trim().Length > 50
            ? "El nombre y el apellido no pueden superar los 50 caracteres."
            : null;
    }

    public static string? ValidarCorreo(string? correo)
    {
        if (string.IsNullOrWhiteSpace(correo))
        {
            return "Debe completar el correo.";
        }

        if (correo.Trim().Length > 100)
        {
            return "El correo no puede superar los 100 caracteres.";
        }

        return Regex.IsMatch(correo.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$") ? null : "El correo electrónico no es válido.";
    }

    // Si es válido, devuelve en telefonoCompuesto el valor que se guarda en la base ("+54 1180425589").
    public static string? ValidarTelefono(string? codigoPais, string? telefono, out string telefonoCompuesto)
    {
        telefonoCompuesto = string.Empty;
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

        telefonoCompuesto = TelefonoFormato.Componer(pais.Codigo, digitosTelefono);
        return null;
    }

    // Mínimo 8 caracteres y al menos uno que no sea letra ni número.
    private static bool EsContrasenaSegura(string contrasena)
    {
        return contrasena.Length >= LargoMinimoContrasena && Regex.IsMatch(contrasena, @"[^\p{L}\p{N}]");
    }
}
