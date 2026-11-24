using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using OSO.Models;

namespace OSO.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly IDataProtector _protectorInvitaciones;
    private readonly BD bd = new();
    private readonly ExtractorContratoGemini _extractor;

    public HomeController(ILogger<HomeController> logger, IDataProtectionProvider dataProtectionProvider, ExtractorContratoGemini extractor)
    {
        _logger = logger;
        _extractor = extractor;
        _protectorInvitaciones = dataProtectionProvider.CreateProtector("OSO.InvitacionPropiedad");
    }

    // En cada pedido de un usuario logueado: si es inquilino, se generan los avisos de gastos por vencer.
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        int? idUsuario = HttpContext.Session.GetInt32("IdUsuario");
        int? idInquilino = HttpContext.Session.GetInt32("IdInquilino");

        if (idUsuario.HasValue && idInquilino.HasValue && HttpMethods.IsGet(Request.Method))
        {
            try
            {
                bd.GenerarNotificacionesVencimiento(idUsuario.Value, idInquilino.Value);
            }
            catch (Exception ex)
            {
                // Las notificaciones nunca deben romper la navegación.
                _logger.LogError(ex, "No se pudieron generar las notificaciones de vencimiento");
            }
        }

        base.OnActionExecuting(context);
    }

    // Después de la acción (ya marcó lo leído, ej. al abrir un chat) se cuenta lo no leído para la campanita.
    public override void OnActionExecuted(ActionExecutedContext context)
    {
        int? idUsuario = HttpContext.Session.GetInt32("IdUsuario");

        if (idUsuario.HasValue && context.Result is ViewResult)
        {
            try
            {
                ViewBag.NotificacionesNoLeidas = bd.ContarNotificacionesNoLeidas(idUsuario.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "No se pudieron contar las notificaciones");
            }
        }

        base.OnActionExecuted(context);
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
        HttpContext.Session.SetString("NombreUsuario", usuario.Nombre ?? string.Empty);

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
    [ValidateAntiForgeryToken]
    public IActionResult Registrar(Usuario usuario, bool esPropietario, string? returnUrl, IFormFile? fotoPerfil,
                                   string? confirmarContrasena, string? codigoPais, string? telefono)
    {
        ViewBag.ReturnUrl = returnUrl;

        // Quien viene desde un link de invitación siempre se registra como inquilino.
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            esPropietario = false;
        }

        string? error = usuario.ValidarYPrepararRegistro(confirmarContrasena, codigoPais, telefono)
                        ?? GestorArchivos.ValidarImagenOpcional(fotoPerfil);

        if (error == null && bd.ObtenerUsuarioPorCorreo(usuario.Correo) != null)
        {
            error = "Ya existe una cuenta con ese correo. Iniciá sesión.";
        }

        if (error != null)
        {
            ViewBag.Error = error;
            return View("Registro");
        }

        usuario.FotoPerfil = GestorArchivos.TieneContenido(fotoPerfil)
            ? GestorArchivos.Guardar(fotoPerfil!, GestorArchivos.CarpetaImagenes, $"perfil_{usuario.Correo}")
            : null;
        usuario.Contrasena = PasswordHashService.HashPassword(usuario.Contrasena);
        bd.RegistrarUsuario(usuario, esPropietario);

        Usuario? usuarioCreado = bd.ObtenerUsuarioPorCorreo(usuario.Correo);
        if (usuarioCreado == null)
        {
            ViewBag.Error = "No se pudo crear la cuenta. Intentá de nuevo.";
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
    [ValidateAntiForgeryToken]
    public IActionResult IniciarSesion(string correo, string contrasena, string? returnUrl)
    {
        ViewBag.ReturnUrl = returnUrl;

        if (string.IsNullOrWhiteSpace(correo) || string.IsNullOrWhiteSpace(contrasena))
        {
            ViewBag.Error = "Debe completar correo y contraseña.";
            return View("Login");
        }

        Usuario? usuario = bd.ObtenerUsuarioPorCorreo(correo.Trim());
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

        if (bd.ObtenerPropiedadDePropietario(idPropiedad, idPropietario.Value) != null)
        {
            bd.QuitarInquilinoDePropiedad(idPropiedad);
        }

        return RedirectToAction("Propiedades");
    }

    public IActionResult Propiedades()
    {
        int? idActual = HttpContext.Session.GetInt32("IdPropietario");

        if (!idActual.HasValue)
        {
            int? idInquilino = HttpContext.Session.GetInt32("IdInquilino");
            ViewBag.EsInquilino = idInquilino.HasValue;
            return View(idInquilino.HasValue ? bd.ObtenerPropiedadesPorIdInquilino(idInquilino.Value) : new List<Propiedades>());
        }

        List<Propiedades> propiedades = bd.ObtenerPropiedadesPorIdPropietario(idActual.Value);

        // Las propiedades con inquilino muestran sus datos; las libres, el link para invitar a uno.
        var links = new Dictionary<int, string>();
        var inquilinos = new Dictionary<int, Usuario?>();
        foreach (Propiedades propiedad in propiedades)
        {
            if (propiedad.IdInquilino.HasValue)
            {
                inquilinos[propiedad.IdPropiedad] = bd.ObtenerUsuarioPorIdInquilino(propiedad.IdInquilino.Value);
            }
            else
            {
                links[propiedad.IdPropiedad] = Url.Action("Invitacion", "Home", new { token = GenerarTokenInvitacion(propiedad.IdPropiedad) }, Request.Scheme)!;
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
        int? idInquilino = HttpContext.Session.GetInt32("IdInquilino");
        if (!idPropietario.HasValue && !idInquilino.HasValue)
        {
            return RedirectToAction("Login");
        }

        DateTime primerDia = CalendarioMes.ObtenerPrimerDia(anio, mes);
        DateTime hoy = DateTime.Today;
        const int cantidadProximos = 5;

        // El inquilino ve el calendario en modo lectura: sin agregar ni modificar eventos.
        if (!idPropietario.HasValue)
        {
            return View(new CalendarioMes(
                primerDia,
                bd.ObtenerEventosPorInquilino(idInquilino!.Value, primerDia, primerDia.AddMonths(1)),
                bd.ObtenerProximosEventosInquilino(idInquilino.Value, hoy, cantidadProximos),
                soloLectura: true));
        }

        return View(new CalendarioMes(
            primerDia,
            bd.ObtenerEventosPorPropietario(idPropietario.Value, primerDia, primerDia.AddMonths(1)),
            bd.ObtenerProximosEventos(idPropietario.Value, hoy, cantidadProximos),
            soloLectura: false));
    }

    [HttpGet]
    public IActionResult AgregarEvento(DateTime? fecha)
    {
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");
        if (!idPropietario.HasValue)
        {
            return RedirectToAction("Calendario");
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
            return RedirectToAction("Calendario");
        }

        string? error = evento.Validar();
        if (error != null)
        {
            TempData["Error"] = error;
            return RedirectToAction("AgregarEvento", new { fecha = evento.Fecha.ToString("yyyy-MM-dd") });
        }

        // Sólo se puede asociar el evento a una propiedad propia.
        if (evento.IdPropiedad.HasValue && bd.ObtenerPropiedadDePropietario(evento.IdPropiedad.Value, idPropietario.Value) == null)
        {
            evento.IdPropiedad = null;
        }

        evento.IdPropietario = idPropietario.Value;
        bd.AgregarEvento(evento);
        return RedirectToAction("Calendario", new { anio = evento.Fecha.Year, mes = evento.Fecha.Month });
    }

    [HttpGet]
    public IActionResult Mensajeria(int? con)
    {
        int? idUsuario = HttpContext.Session.GetInt32("IdUsuario");
        if (!idUsuario.HasValue)
        {
            return RedirectToAction("Login");
        }

        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");
        int? idInquilino = HttpContext.Session.GetInt32("IdInquilino");

        // Sólo se puede abrir el chat con alguien que sea contacto del usuario.
        Contacto? activo = con.HasValue ? bd.ObtenerContacto(idUsuario.Value, idPropietario, idInquilino, con.Value) : null;
        if (activo != null)
        {
            bd.MarcarMensajesComoLeidos(idUsuario.Value, activo.IdUsuario);
            activo.NoLeidos = 0;
            ViewBag.Mensajes = bd.ObtenerConversacion(idUsuario.Value, activo.IdUsuario);
        }

        ViewBag.Activo = activo;
        ViewBag.IdUsuario = idUsuario.Value;
        ViewBag.EsPropietario = idPropietario.HasValue;
        return View(bd.ObtenerContactos(idUsuario.Value, idPropietario, idInquilino));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult EnviarMensaje(int idReceptor, string? texto)
    {
        int? idUsuario = HttpContext.Session.GetInt32("IdUsuario");
        if (!idUsuario.HasValue)
        {
            return RedirectToAction("Login");
        }

        Contacto? receptor = bd.ObtenerContacto(
            idUsuario.Value,
            HttpContext.Session.GetInt32("IdPropietario"),
            HttpContext.Session.GetInt32("IdInquilino"),
            idReceptor);

        if (receptor != null && Mensaje.EsTextoValido(texto))
        {
            bd.EnviarMensaje(idUsuario.Value, idReceptor, texto!.Trim());
        }

        return RedirectToAction("Mensajeria", new { con = idReceptor });
    }

    public IActionResult Expensas()
    {
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");

        if (!idPropietario.HasValue)
        {
            return RedirectToAction(HttpContext.Session.GetInt32("IdInquilino").HasValue ? "Propiedades" : "Login");
        }

        return View(bd.ObtenerExpensasPorIdPropietario(idPropietario.Value));
    }

    [HttpGet]
    public IActionResult AgregarExpensa()
    {
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");

        if (!idPropietario.HasValue)
        {
            return RedirectToAction(HttpContext.Session.GetInt32("IdInquilino").HasValue ? "Propiedades" : "Login");
        }

        ViewBag.Propiedades = bd.ObtenerPropiedadesPorIdPropietario(idPropietario.Value);
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult AgregarExpensa(GastoPropiedad expensa, int idPropiedad)
    {
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");

        if (!idPropietario.HasValue)
        {
            return RedirectToAction(HttpContext.Session.GetInt32("IdInquilino").HasValue ? "Propiedades" : "Login");
        }

        if (bd.ObtenerPropiedadDePropietario(idPropiedad, idPropietario.Value) == null)
        {
            return RedirectToAction("Propiedades");
        }

        string? error = expensa.Validar();
        if (error != null)
        {
            TempData["Error"] = error;
            return RedirectToAction("AgregarExpensa");
        }

        expensa.FechaAlta = expensa.FechaAlta == default ? DateTime.Today : expensa.FechaAlta.Date;
        expensa.FechaVencimiento = expensa.FechaVencimiento.Date;

        bd.AgregarExpensa(expensa, idPropiedad);
        return RedirectToAction("Expensas");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult PagarExpensa(int idGastoResponsable, IFormFile? comprobanteArchivo)
    {
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");

        if (!idPropietario.HasValue)
        {
            return RedirectToAction(HttpContext.Session.GetInt32("IdInquilino").HasValue ? "Propiedades" : "Login");
        }

        string? error = GestorArchivos.ValidarComprobante(comprobanteArchivo);
        if (error != null)
        {
            TempData["Mensaje"] = error;
            return RedirectToAction("Expensas");
        }

        string comprobante = GestorArchivos.Guardar(comprobanteArchivo!, GestorArchivos.CarpetaComprobantes, $"pago_expensa_{idGastoResponsable}");

        if (bd.MarcarExpensaComoPagada(idGastoResponsable, idPropietario.Value, comprobante))
        {
            TempData["Mensaje"] = "Pago confirmado correctamente.";
        }
        else
        {
            GestorArchivos.Borrar(GestorArchivos.CarpetaComprobantes, comprobante);
            TempData["Mensaje"] = "No se pudo registrar el pago de esa expensa.";
        }

        return RedirectToAction("Expensas");
    }

    public IActionResult MisGastos()
    {
        int? idInquilino = HttpContext.Session.GetInt32("IdInquilino");

        if (!idInquilino.HasValue)
        {
            return RedirectToAction(HttpContext.Session.GetInt32("IdPropietario").HasValue ? "Propiedades" : "Login");
        }

        return View(bd.ObtenerGastosPorIdInquilino(idInquilino.Value));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult PagarGasto(int idGastoResponsable, IFormFile? comprobanteArchivo)
    {
        int? idInquilino = HttpContext.Session.GetInt32("IdInquilino");

        if (!idInquilino.HasValue)
        {
            return RedirectToAction("Login");
        }

        string? error = GestorArchivos.ValidarComprobante(comprobanteArchivo);
        if (error != null)
        {
            TempData["Mensaje"] = error;
            return RedirectToAction("MisGastos");
        }

        string comprobante = GestorArchivos.Guardar(comprobanteArchivo!, GestorArchivos.CarpetaComprobantes, $"pago_gasto_{idGastoResponsable}");

        if (bd.PagarGastoInquilino(idGastoResponsable, idInquilino.Value, comprobante))
        {
            TempData["Mensaje"] = "Pago registrado. El comprobante quedó pendiente de verificación.";
        }
        else
        {
            GestorArchivos.Borrar(GestorArchivos.CarpetaComprobantes, comprobante);
            TempData["Mensaje"] = "No se pudo registrar el pago de ese gasto.";
        }

        return RedirectToAction("MisGastos");
    }

    public IActionResult Notificaciones()
    {
        int? idUsuario = HttpContext.Session.GetInt32("IdUsuario");

        if (!idUsuario.HasValue)
        {
            return RedirectToAction("Login");
        }

        List<Notificacion> notificaciones = bd.ObtenerNotificaciones(idUsuario.Value);
        bd.MarcarNotificacionesComoLeidas(idUsuario.Value);
        return View(notificaciones);
    }

    public IActionResult AgregarPropiedad()
    {
        if (!HttpContext.Session.GetInt32("IdPropietario").HasValue)
        {
            return RedirectToAction("Login");
        }

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult AgregarP(Propiedades propiedad, IFormFile? foto)
    {
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");

        if (!idPropietario.HasValue)
        {
            return RedirectToAction("Login");
        }

        string? error = propiedad.Validar() ?? GestorArchivos.ValidarImagenOpcional(foto);
        if (error != null)
        {
            TempData["Error"] = error;
            return RedirectToAction("AgregarPropiedad");
        }

        propiedad.IdPropietario = idPropietario.Value;
        propiedad.Direccion = propiedad.Direccion.Trim();
        propiedad.Foto = GestorArchivos.TieneContenido(foto)
            ? GestorArchivos.Guardar(foto!, GestorArchivos.CarpetaImagenes, $"propiedad_{idPropietario.Value}")
            : "default.jpg";
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

        Propiedades? propiedad = bd.ObtenerPropiedadDePropietario(idPropiedad, idPropietario.Value);

        if (propiedad == null)
        {
            return RedirectToAction("Propiedades");
        }

        return View(propiedad);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult GuardarEdicionPropiedad(Propiedades propiedad, IFormFile? foto)
    {
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");

        if (!idPropietario.HasValue)
        {
            return RedirectToAction("Login");
        }

        Propiedades? propiedadActual = bd.ObtenerPropiedadDePropietario(propiedad.IdPropiedad, idPropietario.Value);

        if (propiedadActual == null)
        {
            return RedirectToAction("Propiedades");
        }

        string? error = propiedad.Validar() ?? GestorArchivos.ValidarImagenOpcional(foto);
        if (error != null)
        {
            TempData["Error"] = error;
            return RedirectToAction("EditarPropiedad", new { idPropiedad = propiedad.IdPropiedad });
        }

        propiedad.IdPropietario = idPropietario.Value;
        propiedad.Direccion = propiedad.Direccion.Trim();
        propiedad.Foto = GestorArchivos.TieneContenido(foto)
            ? GestorArchivos.Guardar(foto!, GestorArchivos.CarpetaImagenes, $"propiedad_{idPropietario.Value}")
            : propiedadActual.Foto;

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

        Propiedades? propiedad = bd.ObtenerPropiedadDePropietario(idPropiedad, idPropietario.Value);

        if (propiedad == null)
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

        Propiedades? propiedad = bd.ObtenerPropiedadDePropietario(idPropiedad, idPropietario.Value);

        if (propiedad == null)
        {
            return RedirectToAction("Propiedades");
        }

        ViewBag.Propiedad = propiedad;
        ViewBag.Contrato = bd.ObtenerContratoActivoPorPropiedad(idPropiedad);
        ViewBag.ContratoEnCurso = bd.ObtenerContratoEnCursoPorPropiedad(idPropiedad);
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

        Propiedades? propiedad = bd.ObtenerPropiedadDePropietario(idPropiedad, idPropietario.Value);

        if (propiedad == null)
        {
            return RedirectToAction("Propiedades");
        }

        if (bd.ObtenerContratoActivoPorPropiedad(idPropiedad) != null || bd.ObtenerContratoEnCursoPorPropiedad(idPropiedad) != null)
        {
            return RedirectToAction("Contrato", new { idPropiedad });
        }

        ViewBag.Propiedad = propiedad;
        ViewBag.Inquilinos = ObtenerInquilinosParaContrato(propiedad);
        return View(propiedad);
    }

    // Si ya hay un inquilino vinculado, el contrato sólo puede ser con él.
    private List<Usuario> ObtenerInquilinosParaContrato(Propiedades propiedad)
    {
        if (propiedad.IdInquilino.HasValue)
        {
            Usuario? inquilinoActual = bd.ObtenerUsuarioPorIdInquilino(propiedad.IdInquilino.Value);
            return inquilinoActual != null ? new List<Usuario> { inquilinoActual } : new List<Usuario>();
        }

        return bd.ObtenerInquilinosDisponibles();
    }

    // Devuelve el inquilino con el que se puede hacer el contrato, o null si el elegido no corresponde.
    private int? ElegirInquilinoParaContrato(Propiedades propiedad, int idInquilino)
    {
        int idElegido = propiedad.IdInquilino ?? idInquilino;
        return ObtenerInquilinosParaContrato(propiedad).Any(i => i.IdInquilino == idElegido) ? idElegido : null;
    }

    [HttpGet]
    public IActionResult SubirContrato(int idPropiedad)
    {
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");

        if (!idPropietario.HasValue)
        {
            return RedirectToAction("Login");
        }

        Propiedades? propiedad = bd.ObtenerPropiedadDePropietario(idPropiedad, idPropietario.Value);

        if (propiedad == null)
        {
            return RedirectToAction("Propiedades");
        }

        if (bd.ObtenerContratoActivoPorPropiedad(idPropiedad) != null || bd.ObtenerContratoEnCursoPorPropiedad(idPropiedad) != null)
        {
            return RedirectToAction("Contrato", new { idPropiedad });
        }

        ViewBag.Inquilinos = ObtenerInquilinosParaContrato(propiedad);
        return View(propiedad);
    }

    // Sube el contrato, lo lee con Gemini y deja un Borrador listo para revisar.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(11_534_336)] // 10 MB del archivo + lo que ocupa el resto del formulario
    public async Task<IActionResult> SubirContrato(int idPropiedad, int idInquilino, IFormFile? archivo)
    {
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");

        if (!idPropietario.HasValue)
        {
            return RedirectToAction("Login");
        }

        Propiedades? propiedad = bd.ObtenerPropiedadDePropietario(idPropiedad, idPropietario.Value);

        if (propiedad == null)
        {
            return RedirectToAction("Propiedades");
        }

        string? error = ArchivosContrato.Validar(archivo);
        int? idInquilinoContrato = ElegirInquilinoParaContrato(propiedad, idInquilino);
        if (error == null && idInquilinoContrato == null)
        {
            error = "Debe seleccionar un inquilino para crear el contrato.";
        }

        if (error != null)
        {
            TempData["Error"] = error;
            return RedirectToAction("SubirContrato", new { idPropiedad });
        }

        int idContrato;
        try
        {
            idContrato = bd.CrearContratoBorrador(idPropiedad, idInquilinoContrato!.Value);
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction("Contrato", new { idPropiedad });
        }

        bd.GuardarNombreArchivoContrato(idContrato, ArchivosContrato.Guardar(idContrato, archivo!));

        List<Gastos> catalogo = bd.ObtenerCatalogoGastos();
        using var memoria = new MemoryStream();
        await archivo!.CopyToAsync(memoria);

        var (json, errorIa) = await _extractor.ExtraerAsync(memoria.ToArray(), ArchivosContrato.ObtenerTipoMime(archivo.FileName), catalogo.Select(g => g.TipoGasto));
        ResultadoExtraccion resultado = ResultadoExtraccion.Parsear(json);
        bool leyoAlgo = json != null && (resultado.Gastos.Count > 0 || resultado.ConfianzaPorCampo().Values.Any(c => c > 0));

        if (!leyoAlgo)
        {
            bd.GuardarExtraccion(idContrato, _extractor.Modelo, null, "Error", errorIa ?? "La respuesta de la IA no tenía datos del contrato.");
            TempData["Error"] = "No pudimos leer el contrato; completalo a mano.";
            return RedirectToAction("RevisarContrato", new { idContrato });
        }

        bd.GuardarExtraccion(idContrato, _extractor.Modelo, json, "Ok", null);

        Contratos contrato = bd.ObtenerContratoPorId(idContrato)!;
        ResultadoExtraccion.AplicarA(resultado, contrato);
        bd.GuardarDatosContrato(contrato);
        bd.GuardarReglasGasto(idContrato, ResultadoExtraccion.ReglasDesde(resultado, catalogo));

        TempData["Success"] = "Leímos el contrato. Revisá los datos marcados antes de confirmar.";
        return RedirectToAction("RevisarContrato", new { idContrato });
    }

    // El archivo solo lo bajan el propietario de la propiedad o el inquilino del contrato.
    [HttpGet]
    public IActionResult DescargarContrato(int idContrato)
    {
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");
        int? idInquilino = HttpContext.Session.GetInt32("IdInquilino");

        if (!idPropietario.HasValue && !idInquilino.HasValue)
        {
            return RedirectToAction("Login");
        }

        if (!bd.EsParteDelContrato(idContrato, idPropietario, idInquilino))
        {
            return RedirectToAction("Propiedades");
        }

        Contratos? contrato = bd.ObtenerContratoPorId(idContrato);
        string? ruta = ArchivosContrato.ObtenerRuta(contrato?.ArchivoContrato);
        if (contrato == null || ruta == null)
        {
            TempData["Error"] = "Este contrato no tiene un archivo cargado.";
            return RedirectToAction("Propiedades");
        }

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return PhysicalFile(ruta, ArchivosContrato.ObtenerTipoMime(contrato.ArchivoContrato!));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult GuardarContrato(int idPropiedad, int idInquilino)
    {
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");

        if (!idPropietario.HasValue)
        {
            return RedirectToAction("Login");
        }

        Propiedades? propiedad = bd.ObtenerPropiedadDePropietario(idPropiedad, idPropietario.Value);

        if (propiedad == null)
        {
            return RedirectToAction("Propiedades");
        }

        // Una propiedad tiene un único inquilino: si ya hay uno, se usa siempre ése.
        int? idInquilinoContrato = ElegirInquilinoParaContrato(propiedad, idInquilino);
        if (idInquilinoContrato == null)
        {
            TempData["Error"] = "Debe seleccionar un inquilino para crear el contrato.";
            return RedirectToAction("CrearContrato", new { idPropiedad });
        }

        // El contrato nace en Borrador: se completa y se confirma en la pantalla de revisión.
        try
        {
            int idContrato = bd.CrearContratoBorrador(idPropiedad, idInquilinoContrato.Value);
            return RedirectToAction("RevisarContrato", new { idContrato });
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction("Contrato", new { idPropiedad });
        }
    }

    [HttpGet]
    public IActionResult Servicios(int idPropiedad)
    {
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");

        if (!idPropietario.HasValue)
        {
            return RedirectToAction("Login");
        }

        Propiedades? propiedad = bd.ObtenerPropiedadDePropietario(idPropiedad, idPropietario.Value);

        if (propiedad == null)
        {
            return RedirectToAction("Propiedades");
        }

        ViewBag.Propiedad = propiedad;
        ViewBag.Servicios = bd.ObtenerServiciosDePropiedad(idPropiedad);
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

        Propiedades? propiedad = bd.ObtenerPropiedadDePropietario(idPropiedad, idPropietario.Value);

        if (propiedad == null)
        {
            return RedirectToAction("Propiedades");
        }

        ViewBag.Tipo = tipo;
        ViewBag.IdPropiedad = idPropiedad;
        ViewBag.Propiedad = propiedad;
        ViewBag.Gastos = bd.ObtenerGastosPorServicioYTipo(idPropiedad, tipo);
        return View(propiedad);
    }

    [HttpGet]
    public IActionResult AgregarGasto(int idPropiedad, string? tipo)
    {
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");

        if (!idPropietario.HasValue)
        {
            return RedirectToAction("Login");
        }

        Propiedades? propiedad = bd.ObtenerPropiedadDePropietario(idPropiedad, idPropietario.Value);
        if (propiedad == null)
        {
            return RedirectToAction("Propiedades");
        }

        Gastos? tipoGasto = string.IsNullOrWhiteSpace(tipo) ? null : bd.ObtenerTipoGastoDePropiedad(idPropiedad, tipo);
        if (tipoGasto == null)
        {
            return RedirectToAction("Servicios", new { idPropiedad });
        }

        ViewBag.Propiedad = propiedad;
        ViewBag.TipoGasto = tipoGasto;
        return View(new GastoPropiedad
        {
            FechaAlta = DateTime.Today,
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

        Propiedades? propiedad = bd.ObtenerPropiedadDePropietario(idPropiedad, idPropietario.Value);
        if (propiedad == null)
        {
            return RedirectToAction("Propiedades");
        }

        ViewBag.Propiedad = propiedad;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult GuardarGasto(int idPropiedad, int idGasto, DateTime mes, decimal monto, DateTime fechaVencimiento, string estado, IFormFile? comprobanteArchivo, string? responsable)
    {
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");

        if (!idPropietario.HasValue)
        {
            return RedirectToAction("Login");
        }

        Propiedades? propiedad = bd.ObtenerPropiedadDePropietario(idPropiedad, idPropietario.Value);
        if (propiedad == null)
        {
            return RedirectToAction("Propiedades");
        }

        Gastos? tipoGasto = bd.PropiedadTieneTipoGasto(idPropiedad, idGasto) ? bd.ObtenerTipoGastoPorId(idGasto) : null;
        if (tipoGasto == null)
        {
            return RedirectToAction("Servicios", new { idPropiedad });
        }

        var gasto = new GastoPropiedad
        {
            IdGasto = idGasto,
            Periodo = mes,
            Monto = monto,
            FechaVencimiento = fechaVencimiento,
            FechaAlta = DateTime.Today
        };

        string? error = mes == default ? "Debe indicar el mes del gasto." : gasto.Validar();
        if (error == null && GestorArchivos.TieneContenido(comprobanteArchivo))
        {
            error = GestorArchivos.ValidarComprobante(comprobanteArchivo);
        }

        if (error != null)
        {
            TempData["Error"] = error;
            return RedirectToAction("AgregarGasto", new { idPropiedad, tipo = tipoGasto.TipoGasto });
        }

        string? comprobante = GestorArchivos.TieneContenido(comprobanteArchivo)
            ? GestorArchivos.Guardar(comprobanteArchivo!, GestorArchivos.CarpetaComprobantes, $"gasto_{idPropiedad}_{idGasto}")
            : null;

        // Sólo se le puede asignar el gasto al inquilino si la propiedad tiene uno.
        bool loPagaElInquilino = responsable == "Inquilino" && propiedad.IdInquilino.HasValue;

        bd.AgregarGasto(idPropiedad, idGasto, gasto, estado == "Pagado", comprobante, loPagaElInquilino);
        return RedirectToAction("ServicioDetalle", new { idPropiedad, tipo = tipoGasto.TipoGasto });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult GuardarEstadoGasto(int idPropiedad, int idGastoPropiedad, string tipo, string estado)
    {
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");

        if (!idPropietario.HasValue)
        {
            return RedirectToAction("Login");
        }

        if (bd.ObtenerPropiedadDePropietario(idPropiedad, idPropietario.Value) == null)
        {
            return RedirectToAction("Propiedades");
        }

        bd.ActualizarEstadoGasto(idPropiedad, idGastoPropiedad, estado == "Pagado");
        return RedirectToAction("ServicioDetalle", new { idPropiedad, tipo });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult GuardarTipoGasto(int idPropiedad, string? nombreTipo)
    {
        int? idPropietario = HttpContext.Session.GetInt32("IdPropietario");

        if (!idPropietario.HasValue)
        {
            return RedirectToAction("Login");
        }

        if (bd.ObtenerPropiedadDePropietario(idPropiedad, idPropietario.Value) == null)
        {
            return RedirectToAction("Propiedades");
        }

        if (!Gastos.EsNombreValido(nombreTipo))
        {
            TempData["Error"] = $"El nombre del tipo de gasto es obligatorio y no puede superar los {Gastos.LargoMaximoNombre} caracteres.";
            return RedirectToAction("AgregarTipoGasto", new { idPropiedad });
        }

        if (bd.ObtenerTipoGastoDePropiedad(idPropiedad, nombreTipo!) == null)
        {
            int nuevoId = bd.CrearTipoGasto(nombreTipo!);
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

        if (bd.ObtenerPropiedadDePropietario(idPropiedad, idPropietario.Value) != null)
        {
            bd.EliminarTipoGastoDePropiedad(idPropiedad, idGasto);
        }

        return RedirectToAction("Servicios", new { idPropiedad });
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
