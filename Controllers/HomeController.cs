using System.Diagnostics;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using OSO.Models;

namespace OSO.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly BD bd = new();

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Registro()
    {
        return View();
    }

    [HttpPost]
    public IActionResult Registrar(Usuario usuario, bool esPropietario)
    {
        bd.RegistrarUsuario(usuario, esPropietario);
        return RedirectToAction("Index");
    }

    public IActionResult Login()
    {
        return View();
    }

    public IActionResult IniciarSesion(string correo, string contrasena)
    {
        Usuario? usuario = bd.verificarUsuario(new Usuario { Correo = correo });
        if (usuario != null && usuario.Contrasena == contrasena)
        {
            HttpContext.Session.SetInt32("IdUsuario", usuario.Id);
            HttpContext.Session.SetInt32("IdPropietario", usuario.IdPropietario);
            return RedirectToAction("Bienvenida");
        }

        ViewBag.Error = "Nombre de usuario o contraseña incorrectos.";
        return View("Login");
    }

    public IActionResult Bienvenida()
    {
        return View();
    }

    public IActionResult Propiedades()
    {
        int? idActual = HttpContext.Session.GetInt32("IdPropietario");

        if (!idActual.HasValue)
        {
            return View(new List<Propiedades>());
        }

        return View(bd.ObtenerPropiedadesPorIdPropietario(idActual.Value));
    }

    public IActionResult AgregarPropiedad()
    {
        return View();
    }

    public IActionResult AgregarP(Propiedades propiedad, IFormFile foto)
    {
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");

        if (idPropietario.HasValue)
        {
            propiedad.IdPropietario = idPropietario.Value;
        }

        if (foto != null && foto.Length > 0)
        {
            string nombreArchivo = Path.GetFileName(foto.FileName);
            string ruta = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "imagenes", nombreArchivo);

            using (var stream = new FileStream(ruta, FileMode.Create))
            {
                foto.CopyTo(stream);
            }

            propiedad.Foto = nombreArchivo;
        }
        else
        {
            propiedad.Foto = "default.jpg";
        }

        propiedad.FechaDeAlta = DateTime.Today;
        propiedad.Estado = string.IsNullOrWhiteSpace(propiedad.Estado) ? "Disponible" : propiedad.Estado;

        bd.AgregarPropiedad(propiedad);
        return RedirectToAction("Propiedades");
    }

    [HttpGet]
    public IActionResult EditarPropiedad(int idPropiedad)
    {
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");

        if (!idPropietario.HasValue)
        {
            return RedirectToAction("Login");
        }

        Propiedades? propiedad = bd.ObtenerPropiedadPorId(idPropiedad);

        if (propiedad == null || propiedad.IdPropietario != idPropietario.Value)
        {
            return RedirectToAction("Propiedades");
        }

        return View(propiedad);
    }

    [HttpPost]
    public IActionResult GuardarEdicionPropiedad(Propiedades propiedad, IFormFile? foto)
    {
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");

        if (!idPropietario.HasValue)
        {
            return RedirectToAction("Login");
        }

        Propiedades? propiedadActual = bd.ObtenerPropiedadPorId(propiedad.IdPropiedad);

        if (propiedadActual == null || propiedadActual.IdPropietario != idPropietario.Value)
        {
            return RedirectToAction("Propiedades");
        }

        propiedad.IdPropietario = idPropietario.Value;
        propiedad.Foto = propiedadActual.Foto;

        if (foto != null && foto.Length > 0)
        {
            string nombreArchivo = Path.GetFileName(foto.FileName);
            string ruta = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "imagenes", nombreArchivo);

            using (var stream = new FileStream(ruta, FileMode.Create))
            {
                foto.CopyTo(stream);
            }

            propiedad.Foto = nombreArchivo;
        }

        bd.EditarPropiedad(propiedad);
        return RedirectToAction("Propiedades");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult InfoPropiedad(int idPropiedad)
    {
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");

        if (!idPropietario.HasValue)
        {
            return RedirectToAction("Login");
        }

        Propiedades? propiedad = bd.ObtenerPropiedadPorId(idPropiedad);

        if (propiedad == null || propiedad.IdPropietario != idPropietario.Value)
        {
            return RedirectToAction("Propiedades");
        }

        return View(propiedad);
    }

    [HttpGet]
    public IActionResult Servicios(int idPropiedad)
    {
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");

        if (!idPropietario.HasValue)
        {
            return RedirectToAction("Login");
        }

        Propiedades? propiedad = bd.ObtenerPropiedadPorId(idPropiedad);

        if (propiedad == null || propiedad.IdPropietario != idPropietario.Value)
        {
            return RedirectToAction("Propiedades");
        }

        List<Gastos> tipos = bd.ObtenerTiposGastoPorPropiedad(idPropiedad);
        ViewBag.Propiedad = propiedad;
        ViewBag.Servicios = tipos.Select(t => new
        {
            IdGasto = t.IdGasto,
            Nombre = t.TipoGasto,
            Estado = ObtenerEstadoServicio(idPropiedad, t.TipoGasto),
            UltimosGastos = ObtenerUltimosGastos(idPropiedad, t.TipoGasto)
        }).ToList();

        return View(propiedad);
    }

    [HttpGet]
    public IActionResult ServicioDetalle(int idPropiedad, string tipo)
    {
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");

        if (!idPropietario.HasValue)
        {
            return RedirectToAction("Login");
        }

        Propiedades? propiedad = bd.ObtenerPropiedadPorId(idPropiedad);

        if (propiedad == null || propiedad.IdPropietario != idPropietario.Value)
        {
            return RedirectToAction("Propiedades");
        }

        List<GastoXTipo> gastos = bd.ObtenerGastosPorServicioYTipo(idPropiedad, tipo);

        ViewBag.Tipo = tipo;
        ViewBag.IdPropiedad = idPropiedad;
        ViewBag.Propiedad = propiedad;
        ViewBag.Gastos = gastos;

        return View(propiedad);
    }

    [HttpGet]
    public IActionResult AgregarGasto(int idPropiedad, string tipo)
    {
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");

        if (!idPropietario.HasValue)
        {
            return RedirectToAction("Login");
        }

        Propiedades? propiedad = bd.ObtenerPropiedadPorId(idPropiedad);
        if (propiedad == null || propiedad.IdPropietario != idPropietario.Value)
        {
            return RedirectToAction("Propiedades");
        }

        ViewBag.Propiedad = propiedad;
        ViewBag.Tipo = tipo;
        ViewBag.Tipos = bd.ObtenerTiposGastoPorPropiedad(idPropiedad);
        return View(new GastoXTipo
        {
            FechaAlta = DateTime.Today,
            Estado = "A pagar",
            FechaVencimiento = DateTime.Today.AddDays(15)
        });
    }

    [HttpGet]
    public IActionResult AgregarTipoGasto(int idPropiedad)
    {
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");

        if (!idPropietario.HasValue)
        {
            return RedirectToAction("Login");
        }

        Propiedades? propiedad = bd.ObtenerPropiedadPorId(idPropiedad);
        if (propiedad == null || propiedad.IdPropietario != idPropietario.Value)
        {
            return RedirectToAction("Propiedades");
        }

        ViewBag.Propiedad = propiedad;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult GuardarGasto(int idPropiedad, int idGasto, DateTime mes, decimal monto, DateTime fechaVencimiento, string estado, string? comprobante)
    {
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");

        if (!idPropietario.HasValue)
        {
            return RedirectToAction("Login");
        }

        Propiedades? propiedad = bd.ObtenerPropiedadPorId(idPropiedad);
        if (propiedad == null || propiedad.IdPropietario != idPropietario.Value)
        {
            return RedirectToAction("Propiedades");
        }

        if (idGasto <= 0)
        {
            var tipoGasto = bd.ObtenerTiposGasto().FirstOrDefault(t => t.TipoGasto == Request.Form["tipo"]);
            idGasto = tipoGasto?.IdGasto ?? 0;
        }

        if (idGasto <= 0)
        {
            return RedirectToAction("Servicios", new { idPropiedad });
        }

        var gasto = new GastoXTipo
        {
            IdGasto = idGasto,
            Mes = mes,
            Monto = monto,
            FechaVencimiento = fechaVencimiento,
            FechaAlta = DateTime.Today,
            Estado = estado,
            Comprobante = comprobante,
            FechaPago = estado == "Pagado" ? DateTime.Today : null
        };

        bd.AgregarGasto(idPropiedad, gasto);
        string? tipo = bd.ObtenerTipoGastoPorId(idGasto)?.TipoGasto;
        return RedirectToAction("ServicioDetalle", new { idPropiedad, tipo });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult GuardarEstadoGasto(int idPropiedad, int idGastoXTipo, string tipo, string estado)
    {
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");

        if (!idPropietario.HasValue)
        {
            return RedirectToAction("Login");
        }

        Propiedades? propiedad = bd.ObtenerPropiedadPorId(idPropiedad);
        if (propiedad == null || propiedad.IdPropietario != idPropietario.Value)
        {
            return RedirectToAction("Propiedades");
        }

        bd.ActualizarEstadoGasto(idGastoXTipo, estado, estado == "Pagado" ? DateTime.Today : null);
        return RedirectToAction("ServicioDetalle", new { idPropiedad, tipo });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult GuardarTipoGasto(int idPropiedad, string nombreTipo)
    {
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");

        if (!idPropietario.HasValue)
        {
            return RedirectToAction("Login");
        }

        Propiedades? propiedad = bd.ObtenerPropiedadPorId(idPropiedad);
        if (propiedad == null || propiedad.IdPropietario != idPropietario.Value)
        {
            return RedirectToAction("Propiedades");
        }

        if (string.IsNullOrWhiteSpace(nombreTipo))
        {
            return RedirectToAction("Servicios", new { idPropiedad });
        }

        var nombreLimpio = nombreTipo.Trim();
        var tipoExistente = bd.ObtenerTiposGastoPorPropiedad(idPropiedad).FirstOrDefault(t => t.TipoGasto.Equals(nombreLimpio, StringComparison.OrdinalIgnoreCase));

        if (tipoExistente == null)
        {
            using var connection = new Microsoft.Data.SqlClient.SqlConnection("Server=localhost;Database=OSO;Integrated Security=true;TrustServerCertificate=True;");
            connection.Open();

            var nuevoId = connection.ExecuteScalar<int>(
                "INSERT INTO Gastos (TipoGasto, Descripcion) VALUES (@TipoGasto, @Descripcion); SELECT CAST(SCOPE_IDENTITY() AS int);",
                new
                {
                    TipoGasto = nombreLimpio,
                    Descripcion = "Gasto adicional"
                });

            bd.RegistrarTipoGastoEnPropiedad(idPropiedad, nuevoId);
        }

        return RedirectToAction("Servicios", new { idPropiedad });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult EliminarPropiedad(int idPropiedad)
    {
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");

        if (!idPropietario.HasValue)
        {
            return RedirectToAction("Login");
        }

        bd.EliminarPropiedad(idPropiedad, idPropietario.Value);
        return RedirectToAction("Propiedades");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult EliminarTipoGasto(int idPropiedad, int idGasto)
    {
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");

        if (!idPropietario.HasValue)
        {
            return RedirectToAction("Login");
        }

        Propiedades? propiedad = bd.ObtenerPropiedadPorId(idPropiedad);
        if (propiedad == null || propiedad.IdPropietario != idPropietario.Value)
        {
            return RedirectToAction("Propiedades");
        }

        bd.EliminarTipoGastoDePropiedad(idPropiedad, idGasto);
        return RedirectToAction("Servicios", new { idPropiedad });
    }

    private string ObtenerEstadoServicio(int idPropiedad, string tipo)
    {
        List<GastoXTipo> gastos = bd.ObtenerGastosPorServicioYTipo(idPropiedad, tipo);
        return gastos.Any(g => g.Estado == "A pagar") ? "Pendiente" : (gastos.Any() ? "Al día" : "Sin registros");
    }

    private List<GastoXTipo> ObtenerUltimosGastos(int idPropiedad, string tipo)
    {
        return bd.ObtenerGastosPorServicioYTipo(idPropiedad, tipo).Take(3).ToList();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
