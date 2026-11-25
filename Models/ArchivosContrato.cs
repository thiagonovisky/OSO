namespace OSO.Models;

// Archivos de contrato (PDF o foto). Se guardan en App_Data, fuera de wwwroot: no tienen URL pública
// y solo se entregan desde una acción que verifica que quien pide sea parte del contrato.
public static class ArchivosContrato
{
    public const string Carpeta = "App_Data/contratos";
    public const long TamanoMaximo = 10L * 1024 * 1024;

    private static readonly string[] Extensiones = { ".pdf", ".jpg", ".jpeg", ".png", ".webp" };

    // Devuelve el mensaje de error, o null si el archivo se puede usar.
    public static string? Validar(IFormFile? archivo)
    {
        if (archivo == null || archivo.Length == 0)
        {
            return "Elegí el archivo del contrato (PDF o foto).";
        }

        if (Array.IndexOf(Extensiones, ObtenerExtension(archivo.FileName)) < 0)
        {
            return "El contrato tiene que ser un PDF o una imagen (jpg, png o webp).";
        }

        if (archivo.Length > TamanoMaximo)
        {
            return "El archivo supera los 10 MB.";
        }

        if (!FirmaCoincide(archivo))
        {
            return "El archivo no parece ser un PDF o una imagen válida.";
        }

        return null;
    }

    // Guarda con nombre contrato_{id}_{guid}.ext (nunca el nombre original) y devuelve el nombre guardado.
    public static string Guardar(int idContrato, IFormFile archivo)
    {
        string carpeta = ObtenerRutaCarpeta();
        Directory.CreateDirectory(carpeta);

        string nombre = $"contrato_{idContrato}_{Guid.NewGuid():N}{ObtenerExtension(archivo.FileName)}";
        using var stream = new FileStream(Path.Combine(carpeta, nombre), FileMode.CreateNew);
        archivo.CopyTo(stream);
        return nombre;
    }

    // Ruta completa del archivo guardado, o null si el nombre no es válido o el archivo no existe.
    public static string? ObtenerRuta(string? nombreGuardado)
    {
        if (string.IsNullOrWhiteSpace(nombreGuardado) || nombreGuardado != Path.GetFileName(nombreGuardado))
        {
            return null;
        }

        string ruta = Path.Combine(ObtenerRutaCarpeta(), nombreGuardado);
        return File.Exists(ruta) ? ruta : null;
    }

    public static void Borrar(string? nombreGuardado)
    {
        string? ruta = ObtenerRuta(nombreGuardado);
        if (ruta != null)
        {
            File.Delete(ruta);
        }
    }

    public static string ObtenerTipoMime(string nombreArchivo) => ObtenerExtension(nombreArchivo) switch
    {
        ".pdf" => "application/pdf",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "application/octet-stream"
    };

    private static string ObtenerExtension(string nombreArchivo) => Path.GetExtension(nombreArchivo).ToLowerInvariant();

    private static string ObtenerRutaCarpeta() => Path.Combine(Directory.GetCurrentDirectory(), Carpeta);

    // Los primeros bytes tienen que corresponder a la extensión (no alcanza con renombrar un .exe a .pdf).
    private static bool FirmaCoincide(IFormFile archivo)
    {
        byte[] cabecera = new byte[12];
        using Stream stream = archivo.OpenReadStream();
        int leidos = stream.Read(cabecera, 0, cabecera.Length);
        if (leidos < 4)
        {
            return false;
        }

        return ObtenerExtension(archivo.FileName) switch
        {
            ".pdf" => cabecera[0] == 0x25 && cabecera[1] == 0x50 && cabecera[2] == 0x44 && cabecera[3] == 0x46,
            ".jpg" or ".jpeg" => cabecera[0] == 0xFF && cabecera[1] == 0xD8 && cabecera[2] == 0xFF,
            ".png" => cabecera[0] == 0x89 && cabecera[1] == 0x50 && cabecera[2] == 0x4E && cabecera[3] == 0x47,
            ".webp" => leidos >= 12 && cabecera[0] == 0x52 && cabecera[1] == 0x49 && cabecera[2] == 0x46 && cabecera[3] == 0x46
                       && cabecera[8] == 0x57 && cabecera[9] == 0x45 && cabecera[10] == 0x42 && cabecera[11] == 0x50,
            _ => false
        };
    }
}
