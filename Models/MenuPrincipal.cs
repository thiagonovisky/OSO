namespace OSO.Models;

// Un acceso del menú principal. Acciones: las pantallas que pertenecen a esa sección (para marcarla como activa).
public record ItemMenu(string Accion, string Texto, string TextoCorto, string[] Acciones);

// El menú principal en un solo lugar: lo usan el header, la pantalla de bienvenida y el menú lateral,
// así el orden y los nombres no se desincronizan.
public static class MenuPrincipal
{
    private static readonly ItemMenu Expensas = new("Expensas", "Expensas extraordinarias", "Expensas", new[] { "Expensas", "AgregarExpensa" });
    private static readonly ItemMenu MisGastos = new("MisGastos", "Mis gastos", "Mis gastos", new[] { "MisGastos" });
    private static readonly ItemMenu CargarMes = new("CargarMes", "Cargar servicios", "Servicios", new[] { "CargarMes", "GuardarMes" });
    private static readonly ItemMenu Propiedades = new("Propiedades", "Propiedades", "Propiedades", new[] { "Propiedades" });
    private static readonly ItemMenu Mensajeria = new("Mensajeria", "Mensajería", "Mensajería", new[] { "Mensajeria" });
    private static readonly ItemMenu Calendario = new("Calendario", "Calendario", "Calendario", new[] { "Calendario", "AgregarEvento", "GuardarEvento" });

    // Pantallas que no son de ninguna sección del menú (no se marca ninguna).
    private static readonly string[] SinSeccion = { "Bienvenida", "Perfil", "Notificaciones", "Index", "Login", "Registro", "Error" };

    // Las expensas extraordinarias las carga solo el propietario.
    public static List<ItemMenu> Para(bool esPropietario) => esPropietario
        ? new List<ItemMenu> { Expensas, MisGastos, CargarMes, Propiedades, Mensajeria, Calendario }
        : new List<ItemMenu> { MisGastos, Propiedades, Mensajeria, Calendario };

    // Acción del ítem activo para la pantalla actual, o null. Las pantallas de una propiedad (servicios, contrato, gastos...)
    // cuentan como parte de Propiedades.
    public static string? SeccionDe(string? accion)
    {
        if (string.IsNullOrEmpty(accion) || SinSeccion.Contains(accion))
        {
            return null;
        }

        ItemMenu? item = new[] { Expensas, MisGastos, CargarMes, Propiedades, Mensajeria, Calendario }.FirstOrDefault(i => i.Acciones.Contains(accion));
        return item?.Accion ?? Propiedades.Accion;
    }
}
