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

    public IActionResult Registro(string? returnUrl)
    {
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [HttpPost]
    public IActionResult Registrar(Usuario usuario, bool esPropietario, string? returnUrl, IFormFile? fotoPerfil)
    {
        if (fotoPerfil != null && fotoPerfil.Length > 0)
        {
            string fotosPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "imagenes");
            Directory.CreateDirectory(fotosPath);

            string extension = Path.GetExtension(fotoPerfil.FileName);
            string nombreArchivo = $"perfil_{usuario.Correo}_{DateTime.Now:yyyyMMddHHmmssfff}{extension}";
            string rutaArchivo = Path.Combine(fotosPath, nombreArchivo);

            using (var stream = new FileStream(rutaArchivo, FileMode.Create))
            {
                fotoPerfil.CopyTo(stream);
            }

            usuario.FotoPerfil = nombreArchivo;
        }
        else
        {
            usuario.FotoPerfil = null;
        }

        string contrasenaPlana = usuario.Contrasena;
        usuario.Contrasena = PasswordHashService.HashPassword(contrasenaPlana);
        bd.RegistrarUsuario(usuario, esPropietario);

        Usuario? usuarioCreado = bd.verificarUsuario(new Usuario { Correo = usuario.Correo });
        if (usuarioCreado != null && PasswordHashService.VerifyPassword(contrasenaPlana, usuarioCreado.Contrasena))
        {
            HttpContext.Session.SetInt32("IdUsuario", usuarioCreado.Id);
            HttpContext.Session.SetString("FotoPerfil", usuarioCreado.FotoPerfil ?? string.Empty);

            if (usuarioCreado.IdPropietario > 0)
            {
                HttpContext.Session.SetInt32("IdPropietario", usuarioCreado.IdPropietario);
                HttpContext.Session.SetString("EsPropietario", "true");
            }
            else
            {
                HttpContext.Session.Remove("IdPropietario");
                HttpContext.Session.SetString("EsPropietario", "false");
            }

            if (usuarioCreado.IdInquilino > 0)
            {
                HttpContext.Session.SetInt32("IdInquilino", usuarioCreado.IdInquilino);
            }
            else
            {
                HttpContext.Session.Remove("IdInquilino");
            }

            if (!string.IsNullOrWhiteSpace(returnUrl) && !esPropietario && usuarioCreado.IdInquilino > 0)
            {
                return Redirect(returnUrl);
            }
        }

        return RedirectToAction("Bienvenida");
    }

    public IActionResult Login(string? returnUrl)
    {
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [HttpPost]
    public IActionResult IniciarSesion(string correo, string contrasena, string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(correo) || string.IsNullOrWhiteSpace(contrasena))
        {
            ViewBag.Error = "Debe completar correo y contraseña.";
            return View("Login");
        }

        Usuario? usuario = bd.verificarUsuario(new Usuario { Correo = correo.Trim() });
        if (usuario == null)
        {
            ViewBag.Error = "Nombre de usuario o contraseña incorrectos.";
            return View("Login");
        }

        if (!PasswordHashService.VerifyPassword(contrasena, usuario.Contrasena))
        {
            ViewBag.Error = "Nombre de usuario o contraseña incorrectos.";
            return View("Login");
        }

        HttpContext.Session.Clear();
        HttpContext.Session.SetInt32("IdUsuario", usuario.Id);
        HttpContext.Session.SetString("FotoPerfil", usuario.FotoPerfil ?? string.Empty);

        if (usuario.IdPropietario > 0)
        {
            HttpContext.Session.SetInt32("IdPropietario", usuario.IdPropietario);
            HttpContext.Session.SetString("EsPropietario", "true");
        }
        else
        {
            HttpContext.Session.Remove("IdPropietario");
            HttpContext.Session.SetString("EsPropietario", "false");
        }

        if (usuario.IdInquilino > 0)
        {
            HttpContext.Session.SetInt32("IdInquilino", usuario.IdInquilino);
        }
        else
        {
            HttpContext.Session.Remove("IdInquilino");
        }

        if (!string.IsNullOrWhiteSpace(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction("Bienvenida");
    }

    public IActionResult Bienvenida()
    {
        return View();
    }

    [HttpGet]
    public IActionResult Perfil()
    {
        int? idUsuario = HttpContext.Session.GetInt32("IdUsuario");

        if (!idUsuario.HasValue)
        {
            return RedirectToAction("Login");
        }

        Usuario? usuario = bd.ObtenerUsuarioPorId(idUsuario.Value);

        if (usuario == null)
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }

        return View(usuario);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult CerrarSesion()
    {
        HttpContext.Session.Clear();
        return RedirectToAction("Login");
    }

    [HttpGet]
    public IActionResult Invitacion(int idPropiedad)
    {
        Propiedades? propiedad = bd.ObtenerPropiedadPorId(idPropiedad);
        if (propiedad == null)
        {
            return RedirectToAction("Bienvenida");
        }

        if (propiedad.IdInquilino.HasValue)
        {
            TempData["Error"] = "Esta propiedad ya tiene un inquilino asignado y el link ya no está disponible.";
            return RedirectToAction("Login");
        }

        int? idInquilino = HttpContext.Session.GetInt32("IdInquilino");
        if (idInquilino.HasValue)
        {
            bool vinculada = bd.VincularInquilinoAPropiedad(idPropiedad, idInquilino.Value);
            if (!vinculada)
            {
                TempData["Error"] = "La propiedad ya tiene un inquilino asociado.";
                return RedirectToAction("Bienvenida");
            }

            ViewBag.Propiedad = propiedad;
            return View(propiedad);
        }

        ViewBag.Propiedad = propiedad;
        ViewBag.ReturnUrl = Url.Action("Invitacion", "Home", new { idPropiedad }, Request.Scheme, Request.Host.ToString());
        return View(propiedad);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult QuitarInquilino(int idPropiedad)
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

        bd.QuitarInquilinoDePropiedad(idPropiedad);
        return RedirectToAction("Propiedades");
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

    public IActionResult Expensas(){
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");

        if (!idPropietario.HasValue)
        {
            return RedirectToAction("Login");
        }

        List<Expensas> expensas = bd.ObtenerExpensasPorIdPropietario(idPropietario.Value);
        return View(expensas);
    }

    [HttpGet]
    public IActionResult AgregarExpensa(){
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");

        if (!idPropietario.HasValue)
        {
            return RedirectToAction("Login");
        }

        ViewBag.Propiedades = bd.ObtenerPropiedadesPorIdPropietario(idPropietario.Value);
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult AgregarExpensa(Expensas expensa, int idPropiedad)
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

        expensa.Comprobante = string.Empty;
        expensa.Estado = "Pendiente";
        expensa.FechaPago = null;
        expensa.FechaAlta = expensa.FechaAlta == default ? DateTime.Today : expensa.FechaAlta.Date;
        expensa.FechaVencimiento = expensa.FechaVencimiento.Date;

        bd.AgregarExpensa(expensa, idPropiedad);
        return RedirectToAction("Expensas");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult PagarExpensa(int idExpensa, IFormFile? comprobanteArchivo)
    {
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");

        if (!idPropietario.HasValue)
        {
            return RedirectToAction("Login");
        }

        if (comprobanteArchivo == null || comprobanteArchivo.Length == 0)
        {
            TempData["Mensaje"] = "Debe adjuntar un comprobante para confirmar el pago.";
            return RedirectToAction("Expensas");
        }

        string comprobantesPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "comprobantes");
        Directory.CreateDirectory(comprobantesPath);

        string extension = Path.GetExtension(comprobanteArchivo.FileName);
        string nombreArchivo = $"pago_expensa_{idExpensa}_{DateTime.Now:yyyyMMddHHmmssfff}{extension}";
        string rutaArchivo = Path.Combine(comprobantesPath, nombreArchivo);

        using (var stream = new FileStream(rutaArchivo, FileMode.Create))
        {
            comprobanteArchivo.CopyTo(stream);
        }

        bd.MarcarExpensaComoPagada(idExpensa, idPropietario.Value, nombreArchivo);
        TempData["Mensaje"] = "Pago confirmado correctamente.";
        return RedirectToAction("Expensas");
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
    public IActionResult Contrato(int idPropiedad)
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

        Contratos? contrato = bd.ObtenerContratoActivoPorPropiedad(idPropiedad);
        ViewBag.Propiedad = propiedad;
        ViewBag.Contrato = contrato;

        return View(propiedad);
    }

    [HttpGet]
    public IActionResult CrearContrato(int idPropiedad)
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

        if (bd.ObtenerContratoActivoPorPropiedad(idPropiedad) != null)
        {
            return RedirectToAction("Contrato", new { idPropiedad });
        }

        ViewBag.Propiedad = propiedad;
        ViewBag.Inquilinos = bd.ObtenerInquilinosDisponibles();
        return View(propiedad);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult GuardarContrato(int idPropiedad, int idInquilino, DateTime fechaInicio, DateTime fechaFin, decimal montoAlquiler)
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

        if (propiedad.IdInquilino.HasValue && idInquilino <= 0)
        {
            idInquilino = propiedad.IdInquilino.Value;
        }

        if (idInquilino <= 0)
        {
            TempData["Error"] = "Debe seleccionar un inquilino para crear el contrato.";
            return RedirectToAction("CrearContrato", new { idPropiedad });
        }

        if (fechaFin < fechaInicio)
        {
            TempData["Error"] = "La fecha de fin no puede ser anterior a la de inicio.";
            return RedirectToAction("CrearContrato", new { idPropiedad });
        }

        var contrato = new Contratos
        {
            IdPropiedad = idPropiedad,
            IdInquilino = idInquilino,
            FechaInicio = fechaInicio,
            FechaFin = fechaFin,
            MontoAlquiler = montoAlquiler <= 0 ? propiedad.Alquiler : montoAlquiler
        };

        try
        {
            bd.CrearContrato(contrato);
            TempData["Success"] = "Contrato creado con éxito.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction("CrearContrato", new { idPropiedad });
        }

        return RedirectToAction("Contrato", new { idPropiedad });
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
            int nuevoId = bd.CrearTipoGasto(nombreLimpio);
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
