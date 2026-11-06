using System.Diagnostics;
using System.Security.Cryptography;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using OSO.Models;

namespace OSO.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly IDataProtector _protectorInvitaciones;
    private readonly BD bd = new();

    public HomeController(ILogger<HomeController> logger, IDataProtectionProvider dataProtectionProvider)
    {
        _logger = logger;
        _protectorInvitaciones = dataProtectionProvider.CreateProtector("OSO.InvitacionPropiedad");
    }

    // El token de invitación es el id de la propiedad cifrado/firmado: no se puede adivinar ni modificar.
    private string GenerarTokenInvitacion(int idPropiedad)
    {
        return _protectorInvitaciones.Protect(idPropiedad.ToString());
    }

    private int? LeerTokenInvitacion(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            return int.TryParse(_protectorInvitaciones.Unprotect(token), out int id) ? id : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private IActionResult RedirigirLocalOBienvenida(string? returnUrl)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        return RedirectToAction("Bienvenida");
    }

    private void GuardarSesion(Usuario usuario)
    {
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
            HttpContext.Session.SetString("EsPropietario", "false");
        }

        if (usuario.IdInquilino > 0)
        {
            HttpContext.Session.SetInt32("IdInquilino", usuario.IdInquilino);
        }
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
        // Quien viene desde un link de invitación siempre se registra como inquilino.
        bool desdeInvitacion = !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl);
        if (desdeInvitacion)
        {
            esPropietario = false;
        }

        if (string.IsNullOrWhiteSpace(usuario.Correo) || string.IsNullOrWhiteSpace(usuario.Contrasena))
        {
            ViewBag.Error = "Debe completar correo y contraseña.";
            ViewBag.ReturnUrl = returnUrl;
            return View("Registro");
        }

        usuario.Correo = usuario.Correo.Trim();
        if (bd.verificarUsuario(new Usuario { Correo = usuario.Correo }) != null)
        {
            ViewBag.Error = "Ya existe una cuenta con ese correo. Iniciá sesión.";
            ViewBag.ReturnUrl = returnUrl;
            return View("Registro");
        }

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
        if (usuarioCreado == null)
        {
            ViewBag.Error = "No se pudo crear la cuenta. Intentá de nuevo.";
            ViewBag.ReturnUrl = returnUrl;
            return View("Registro");
        }

        GuardarSesion(usuarioCreado);
        return RedirigirLocalOBienvenida(returnUrl);
    }

    public IActionResult Login(string? returnUrl)
    {
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [HttpPost]
    public IActionResult IniciarSesion(string correo, string contrasena, string? returnUrl)
    {
        ViewBag.ReturnUrl = returnUrl;

        if (string.IsNullOrWhiteSpace(correo) || string.IsNullOrWhiteSpace(contrasena))
        {
            ViewBag.Error = "Debe completar correo y contraseña.";
            return View("Login");
        }

        Usuario? usuario = bd.verificarUsuario(new Usuario { Correo = correo.Trim() });
        if (usuario == null || !PasswordHashService.VerifyPassword(contrasena, usuario.Contrasena))
        {
            ViewBag.Error = "Nombre de usuario o contraseña incorrectos.";
            return View("Login");
        }

        GuardarSesion(usuario);
        return RedirigirLocalOBienvenida(returnUrl);
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
    public IActionResult Invitacion(string? token)
    {
        int? idPropiedad = LeerTokenInvitacion(token);
        Propiedades? propiedad = idPropiedad.HasValue ? bd.ObtenerPropiedadPorId(idPropiedad.Value) : null;

        if (propiedad == null)
        {
            ViewBag.Estado = "invalido";
            return View((Propiedades?)null);
        }

        int? idInquilino = HttpContext.Session.GetInt32("IdInquilino");
        ViewBag.ReturnUrl = Url.Action("Invitacion", "Home", new { token });

        // La propiedad ya tiene inquilino: el link deja de servir (salvo para mostrarle al propio inquilino que ya está vinculado).
        if (propiedad.IdInquilino.HasValue)
        {
            ViewBag.Estado = idInquilino == propiedad.IdInquilino ? "yaVinculado" : "ocupada";
            return View(propiedad);
        }

        if (!HttpContext.Session.GetInt32("IdUsuario").HasValue)
        {
            ViewBag.Estado = "sinSesion";
            return View(propiedad);
        }

        if (!idInquilino.HasValue)
        {
            ViewBag.Estado = "noEsInquilino";
            return View(propiedad);
        }

        // El UPDATE sólo afecta la fila si IdInquilino sigue en NULL, así nunca quedan dos inquilinos.
        ViewBag.Estado = bd.VincularInquilinoAPropiedad(propiedad.IdPropiedad, idInquilino.Value) ? "vinculado" : "ocupada";
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

        List<Propiedades> propiedades = bd.ObtenerPropiedadesPorIdPropietario(idActual.Value);

        var links = new Dictionary<int, string>();
        var inquilinos = new Dictionary<int, Usuario?>();
        foreach (Propiedades p in propiedades)
        {
            if (p.IdInquilino.HasValue)
            {
                inquilinos[p.IdPropiedad] = bd.ObtenerUsuarioPorIdInquilino(p.IdInquilino.Value);
            }
            else
            {
                links[p.IdPropiedad] = Url.Action("Invitacion", "Home", new { token = GenerarTokenInvitacion(p.IdPropiedad) }, Request.Scheme)!;
            }
        }

        ViewBag.LinksInvitacion = links;
        ViewBag.InquilinosPorPropiedad = inquilinos;
        return View(propiedades);
    }

    [HttpGet]
    public IActionResult Calendario(int? anio, int? mes)
    {
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");
        if (!idPropietario.HasValue)
        {
            return RedirectToAction("Login");
        }

        DateTime hoy = DateTime.Today;
        DateTime primerDia = new(anio ?? hoy.Year, mes ?? hoy.Month, 1);

        ViewBag.Mes = primerDia;
        ViewBag.Proximos = bd.ObtenerProximosEventos(idPropietario.Value, hoy, 5);
        return View(bd.ObtenerEventosPorPropietario(idPropietario.Value, primerDia, primerDia.AddMonths(1)));
    }

    [HttpGet]
    public IActionResult AgregarEvento(DateTime? fecha)
    {
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");
        if (!idPropietario.HasValue)
        {
            return RedirectToAction("Login");
        }

        ViewBag.Propiedades = bd.ObtenerPropiedadesPorIdPropietario(idPropietario.Value);
        return View(new Evento { Fecha = (fecha ?? DateTime.Today).Date, HoraInicio = new TimeSpan(9, 0, 0), HoraFin = new TimeSpan(10, 0, 0) });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult GuardarEvento(Evento evento)
    {
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");
        if (!idPropietario.HasValue)
        {
            return RedirectToAction("Login");
        }

        if (string.IsNullOrWhiteSpace(evento.Titulo) || evento.HoraFin <= evento.HoraInicio)
        {
            TempData["Error"] = string.IsNullOrWhiteSpace(evento.Titulo)
                ? "El título es obligatorio."
                : "La hora de fin debe ser posterior a la de inicio.";
            return RedirectToAction("AgregarEvento", new { fecha = evento.Fecha.ToString("yyyy-MM-dd") });
        }

        if (evento.IdPropiedad.HasValue &&
            !bd.ObtenerPropiedadesPorIdPropietario(idPropietario.Value).Any(p => p.IdPropiedad == evento.IdPropiedad.Value))
        {
            evento.IdPropiedad = null;
        }

        evento.IdPropietario = idPropietario.Value;
        bd.AgregarEvento(evento);
        return RedirectToAction("Calendario", new { anio = evento.Fecha.Year, mes = evento.Fecha.Month });
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

        // Si ya hay un inquilino vinculado, el contrato sólo puede ser con él.
        if (propiedad.IdInquilino.HasValue)
        {
            Usuario? inquilinoActual = bd.ObtenerUsuarioPorIdInquilino(propiedad.IdInquilino.Value);
            ViewBag.Inquilinos = inquilinoActual != null ? new List<Usuario> { inquilinoActual } : new List<Usuario>();
        }
        else
        {
            ViewBag.Inquilinos = bd.ObtenerInquilinosDisponibles();
        }

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

        // Una propiedad tiene un único inquilino: si ya hay uno, se usa siempre ése.
        if (propiedad.IdInquilino.HasValue)
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
