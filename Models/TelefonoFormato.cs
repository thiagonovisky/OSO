using System.Text.RegularExpressions;

namespace OSO.Models;

public record PaisTelefono(string Nombre, string Codigo, int Digitos, string Ejemplo);

public static class TelefonoFormato
{
    // Digitos = cantidad de dígitos del número nacional (código de área + número), sin el código de país.
    public static readonly List<PaisTelefono> Paises = new()
    {
        new("Argentina", "54", 10, "11-2345-6789"),
        new("Uruguay", "598", 8, "9234-5678"),
        new("Chile", "56", 9, "9-2345-6789"),
        new("Brasil", "55", 11, "119-2345-6789"),
        new("Paraguay", "595", 9, "9-8123-4567"),
        new("Bolivia", "591", 8, "7123-4567"),
        new("Perú", "51", 9, "9-1234-5678"),
        new("México", "52", 10, "55-1234-5678"),
        new("España", "34", 9, "6-1234-5678"),
        new("Estados Unidos", "1", 10, "20-1555-0123"),
    };

    public static PaisTelefono? BuscarPais(string? codigo)
    {
        string codigoBuscado = (codigo ?? "").Trim().TrimStart('+');
        foreach (PaisTelefono pais in Paises)
        {
            if (pais.Codigo == codigoBuscado)
            {
                return pais;
            }
        }

        return null;
    }

    public static string SoloDigitos(string texto) => Regex.Replace(texto, @"\D", "");

    // Arma el valor que se guarda en la base: "+54 1180425589"
    public static string Componer(string codigoPais, string digitosNacionales) =>
        $"+{codigoPais} {digitosNacionales}";

    // Formato de un número nacional: área-xxxx-xxxx (el área son los dígitos que sobran antes de los últimos 8).
    public static string FormatearNacional(string digitos)
    {
        if (digitos.Length <= 4) return digitos;
        if (digitos.Length <= 8) return digitos.Substring(0, 4) + "-" + digitos.Substring(4);
        int area = digitos.Length - 8;
        return digitos.Substring(0, area) + "-" + digitos.Substring(area, 4) + "-" + digitos.Substring(area + 4);
    }

    // "+54 1180425589" -> "+54 11-8042-5589". Números viejos sin código de país se formatean solo con guiones.
    public static string Formatear(string? telefono)
    {
        string valor = (telefono ?? "").Trim();
        if (valor.StartsWith("+"))
        {
            int espacio = valor.IndexOf(' ');
            if (espacio > 0)
            {
                string codigo = valor.Substring(0, espacio);
                string nacional = SoloDigitos(valor.Substring(espacio));
                return codigo + " " + FormatearNacional(nacional);
            }
        }
        return FormatearNacional(SoloDigitos(valor));
    }
}
