namespace OSO.Models;

// Guarda en wwwroot los archivos que suben los usuarios (fotos y comprobantes).
public static class GestorArchivos
{
    public const string CarpetaImagenes = "imagenes";
    public const string CarpetaComprobantes = "comprobantes";

    private static readonly string[] ExtensionesImagen = { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".jfif" };
    private static readonly string[] ExtensionesComprobante = { ".jpg", ".jpeg", ".png", ".webp", ".pdf" };

    public static bool TieneContenido(IFormFile? archivo)
    {
        return archivo != null && archivo.Length > 0;
    }

    public static bool EsImagen(IFormFile archivo)
    {
        return Array.IndexOf(ExtensionesImagen, ObtenerExtension(archivo)) >= 0;
    }

    public static bool EsComprobanteValido(IFormFile archivo)
    {
        return Array.IndexOf(ExtensionesComprobante, ObtenerExtension(archivo)) >= 0;
    }

    // La imagen es opcional: si no se subió nada no hay error.
    public static string? ValidarImagenOpcional(IFormFile? archivo)
    {
        return TieneContenido(archivo) && !EsImagen(archivo!) ? "El archivo debe ser una imagen (jpg, png, webp o gif)." : null;
    }

    public static string? ValidarComprobante(IFormFile? archivo)
    {
        if (!TieneContenido(archivo))
        {
            return "Debe adjuntar un comprobante para confirmar el pago.";
        }

        return EsComprobanteValido(archivo!) ? null : "El comprobante debe ser una imagen o un PDF.";
    }

    // Guarda el archivo con un nombre único (prefijo + fecha y hora) y devuelve ese nombre.
    public static string Guardar(IFormFile archivo, string carpeta, string prefijo)
    {
        string rutaCarpeta = ObtenerRutaCarpeta(carpeta);
        Directory.CreateDirectory(rutaCarpeta);

        string nombreArchivo = $"{prefijo}_{DateTime.Now:yyyyMMddHHmmssfff}{ObtenerExtension(archivo)}";

        using (var stream = new FileStream(Path.Combine(rutaCarpeta, nombreArchivo), FileMode.Create))
        {
            archivo.CopyTo(stream);
        }

        return nombreArchivo;
    }

    public static void Borrar(string carpeta, string nombreArchivo)
    {
        File.Delete(Path.Combine(ObtenerRutaCarpeta(carpeta), nombreArchivo));
    }

    private static string ObtenerExtension(IFormFile archivo)
    {
        return Path.GetExtension(archivo.FileName).ToLowerInvariant();
    }

    private static string ObtenerRutaCarpeta(string carpeta)
    {
        return Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", carpeta);
    }
}
