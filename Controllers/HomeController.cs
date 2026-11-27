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
    private readonly ExtractorFacturaGemini _extractorFactura;
    private readonly LectorContratos _lectorContratos;

    public HomeController(ILogger<HomeController> logger, IDataProtectionProvider dataProtectionProvider, ExtractorFacturaGemini extractorFactura, LectorContratos lectorContratos)
    {
        _logger = logger;
        _extractorFactura = extractorFactura;
        _lectorContratos = lectorContratos;
        _protectorInvitaciones = dataProtectionProvider.CreateProtector("OSO.InvitacionPropiedad");
    }

    // En cada pedido de un usuario logueado: si es inquilino, se generan los avisos de gastos por vencer.
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        int? idInquilino = IdInquilinoSesion;
        int? idPropietario = IdPropietarioSesion;

        if (UsuarioLogueado(out int idUsuario) && HttpMethods.IsGet(Request.Method))
        {
            // Las notificaciones nunca deben romper la navegación.
            try
            {
                if (idInquilino.HasValue)
                {
                    bd.GenerarNotificacionesVencimiento(idUsuario, idInquilino.Value);
                }

                if (idInquilino.HasValue || idPropietario.HasValue)
                {
                    bd.GenerarNotificacionesEventos(idUsuario, idPropietario, idInquilino);
                    bd.GenerarRecordatorioFacturas(idUsuario, idInquilino, idPropietario);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "No se pudieron generar las notificaciones de vencimiento");
            }
        }

        base.OnActionExecuting(context);
    }

    // Antes de mostrar cualquier pantalla: el menú principal (header, menú lateral y bienvenida) y,
    // ya marcado lo leído por la acción (ej. al abrir un chat), lo no leído para la campanita.
    public override void OnActionExecuted(ActionExecutedContext context)
    {
        if (context.Result is ViewResult)
        {
            bool usuarioEsPropietario = HttpContext.Session.GetString("EsPropietario") == "true";
            ViewBag.UsuarioEsPropietario = usuarioEsPropietario;
            ViewBag.NombreUsuario = HttpContext.Session.GetString("NombreUsuario");
            ViewBag.FotoPerfil = HttpContext.Session.GetString("FotoPerfil");
            ViewBag.Menu = MenuPrincipal.Para(usuarioEsPropietario);
            ViewBag.SeccionMenu = MenuPrincipal.SeccionDe(context.RouteData.Values["action"]?.ToString());

            if (UsuarioLogueado(out int idUsuario))
            {
                try
                {
                    ViewBag.NotificacionesNoLeidas = bd.ContarNotificacionesNoLeidas(idUsuario);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "No se pudieron contar las notificaciones");
                }
            }
        }

        base.OnActionExecuted(context);
    }

    // ---- Sesión ----

    private int? IdPropietarioSesion => HttpContext.Session.GetInt32("IdPropietario");
    private int? IdInquilinoSesion => HttpContext.Session.GetInt32("IdInquilino");

    // Cada uno devuelve true si hay sesión de ese tipo, y en el out el id de quien la inició.
    private bool PropietarioLogueado(out int idPropietario) => LeerIdDeSesion("IdPropietario", out idPropietario);
    private bool InquilinoLogueado(out int idInquilino) => LeerIdDeSesion("IdInquilino", out idInquilino);
    private bool UsuarioLogueado(out int idUsuario) => LeerIdDeSesion("IdUsuario", out idUsuario);

    private bool LeerIdDeSesion(string clave, out int id)
    {
        int? valor = HttpContext.Session.GetInt32(clave);
        id = valor ?? 0;
        return valor.HasValue;
    }

    // Para las pantallas de un solo rol: quien inició sesión con el otro vuelve a sus propiedades; quien no, al login.
    private IActionResult RedirigirSinPermiso() => RedirectToAction(UsuarioLogueado(out _) ? "Propiedades" : "Login");

    // Quién está cargando facturas: el propietario (sus propiedades) o el inquilino (sus alquileres). false si no hay sesión.
    private bool QuienCarga(out string lado, out int idPersona)
    {
        if (PropietarioLogueado(out idPersona))
        {
            lado = BD.LadoPropietario;
            return true;
        }

        lado = BD.LadoInquilino;
        return InquilinoLogueado(out idPersona);
    }

    private void GuardarSesion(Usuario usuario)
    {
        HttpContext.Session.Clear();
        HttpContext.Session.SetInt32("IdUsuario", usuario.Id);
        HttpContext.Session.SetString("FotoPerfil", usuario.FotoPerfil ?? string.Empty);
        HttpContext.Session.SetString("NombreUsuario", usuario.Nombre ?? string.Empty);
        HttpContext.Session.SetString("EsPropietario", usuario.IdPropietario > 0 ? "true" : "false");

        if (usuario.IdPropietario > 0)
        {
            HttpContext.Session.SetInt32("IdPropietario", usuario.IdPropietario);
        }

        if (usuario.IdInquilino > 0)
        {
            HttpContext.Session.SetInt32("IdInquilino", usuario.IdInquilino);
        }
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult CerrarSesion()
    {
        HttpContext.Session.Clear();
        return RedirectToAction("Login");
    }

    // ---- Perfil: cada dato se guarda por separado desde su lápiz ----

    [HttpGet]
    public IActionResult Perfil()
    {
        Usuario? usuario = UsuarioDeLaSesion();
        if (usuario == null)
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }

        var (codigoPais, digitosTelefono) = TelefonoFormato.Separar(usuario.Telefono);
        ViewBag.CodigoPais = codigoPais;
        ViewBag.DigitosTelefono = digitosTelefono;
        return View(usuario);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ActualizarFotoPerfil(IFormFile? foto)
    {
        Usuario? usuario = UsuarioDeLaSesion();
        if (usuario == null)
        {
            return RedirectToAction("Login");
        }

        string? error = GestorArchivos.TieneContenido(foto) ? GestorArchivos.ValidarImagenOpcional(foto) : "Elegí una imagen para la foto de perfil.";
        if (error != null)
        {
            return VolverAlPerfil(error, null);
        }

        string nueva = GestorArchivos.Guardar(foto!, GestorArchivos.CarpetaImagenes, $"perfil_{usuario.Correo}");
        bd.ActualizarFotoPerfil(usuario.IdUsuario, nueva);
        GestorArchivos.BorrarFotoPerfil(usuario.FotoPerfil);
        HttpContext.Session.SetString("FotoPerfil", nueva);
        return VolverAlPerfil(null, "Actualizaste tu foto de perfil.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ActualizarNombre(string? nombre, string? apellido)
    {
        Usuario? usuario = UsuarioDeLaSesion();
        if (usuario == null)
        {
            return RedirectToAction("Login");
        }

        string? error = Usuario.ValidarNombre(nombre, apellido);
        if (error != null)
        {
            return VolverAlPerfil(error, null);
        }

        bd.ActualizarNombre(usuario.IdUsuario, nombre!.Trim(), apellido!.Trim());
        HttpContext.Session.SetString("NombreUsuario", nombre.Trim());
        return VolverAlPerfil(null, "Actualizaste tu nombre.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ActualizarCorreo(string? correo)
    {
        Usuario? usuario = UsuarioDeLaSesion();
        if (usuario == null)
        {
            return RedirectToAction("Login");
        }

        string? error = Usuario.ValidarCorreo(correo);
        string nuevo = correo?.Trim() ?? string.Empty;
        if (error == null && string.Equals(nuevo, usuario.Correo, StringComparison.OrdinalIgnoreCase))
        {
            return VolverAlPerfil(null, null);
        }

        if (error == null && bd.ObtenerUsuarioPorCorreo(nuevo) != null)
        {
            error = "Ya existe otra cuenta con ese mail.";
        }

        if (error != null)
        {
            return VolverAlPerfil(error, null);
        }

        bd.ActualizarCorreo(usuario.IdUsuario, nuevo);
        return VolverAlPerfil(null, "Actualizaste tu mail: desde ahora iniciás sesión con él. Le avisamos a la otra parte de tus alquileres.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ActualizarTelefono(string? codigoPais, string? telefono)
    {
        Usuario? usuario = UsuarioDeLaSesion();
        if (usuario == null)
        {
            return RedirectToAction("Login");
        }

        string? error = Usuario.ValidarTelefono(codigoPais, telefono, out string nuevo);
        if (error != null)
        {
            return VolverAlPerfil(error, null);
        }

        if (nuevo == usuario.Telefono)
        {
            return VolverAlPerfil(null, null);
        }

        bd.ActualizarTelefono(usuario.IdUsuario, nuevo);
        return VolverAlPerfil(null, "Actualizaste tu teléfono. Le avisamos a la otra parte de tus alquileres.");
    }

    private Usuario? UsuarioDeLaSesion()
    {
        return UsuarioLogueado(out int idUsuario) ? bd.ObtenerUsuarioPorId(idUsuario) : null;
    }

    private IActionResult VolverAlPerfil(string? error, string? exito)
    {
        if (error != null)
        {
            TempData["Error"] = error;
        }

        if (exito != null)
        {
            TempData["Success"] = exito;
        }

        return RedirectToAction("Perfil");
    }

    // ---- Propiedades e inquilinos ----

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

        int? idInquilino = IdInquilinoSesion;
        ViewBag.ReturnUrl = Url.Action("Invitacion", "Home", new { token });

        // La propiedad ya tiene inquilino: el link deja de servir (salvo para mostrarle al propio inquilino que ya está vinculado).
        if (propiedad.IdInquilino.HasValue)
        {
            ViewBag.Estado = idInquilino == propiedad.IdInquilino ? "yaVinculado" : "ocupada";
        }
        else if (!UsuarioLogueado(out _))
        {
            ViewBag.Estado = "sinSesion";
        }
        else if (!idInquilino.HasValue)
        {
            ViewBag.Estado = "noEsInquilino";
        }
        else
        {
            // El UPDATE sólo afecta la fila si IdInquilino sigue en NULL, así nunca quedan dos inquilinos.
            ViewBag.Estado = bd.VincularInquilinoAPropiedad(propiedad.IdPropiedad, idInquilino.Value) ? "vinculado" : "ocupada";
        }

        return View(propiedad);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult QuitarInquilino(int idPropiedad)
    {
        if (!PropietarioLogueado(out int idPropietario))
        {
            return RedirectToAction("Login");
        }

        if (bd.ObtenerPropiedadDePropietario(idPropiedad, idPropietario) != null)
        {
            // Con un contrato confirmado no se puede desvincular a secas: hay que terminarlo con su motivo.
            Contratos? vigente = bd.ObtenerContratoConfirmadoDePropiedad(idPropiedad);
            if (vigente != null)
            {
                return RedirectToAction("TerminarContrato", new { idContrato = vigente.IdContrato });
            }

            bd.QuitarInquilinoDePropiedad(idPropiedad);
        }

        return RedirectToAction("Propiedades");
    }

    public IActionResult Propiedades()
    {
        if (!PropietarioLogueado(out int idPropietario))
        {
            int? idInquilino = IdInquilinoSesion;
            ViewBag.EsInquilino = idInquilino.HasValue;
            ViewBag.ContratosPendientes = idInquilino.HasValue ? bd.ObtenerContratosPendientesDeInquilino(idInquilino.Value) : new List<Contratos>();
            ViewBag.ContratosFinalizados = idInquilino.HasValue ? bd.ObtenerContratosFinalizadosDeInquilino(idInquilino.Value) : new List<Contratos>();
            return View(idInquilino.HasValue ? bd.ObtenerPropiedadesPorIdInquilino(idInquilino.Value) : new List<Propiedades>());
        }

        List<Propiedades> propiedades = bd.ObtenerPropiedadesPorIdPropietario(idPropietario);

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

        ViewBag.ContratosConfirmados = bd.ObtenerContratosConfirmadosPorPropietario(idPropietario);
        ViewBag.LinksInvitacion = links;
        ViewBag.InquilinosPorPropiedad = inquilinos;
        return View(propiedades);
    }

    public IActionResult AgregarPropiedad()
    {
        if (!PropietarioLogueado(out _))
        {
            return RedirectToAction("Login");
        }

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult AgregarP(Propiedades propiedad, IFormFile? foto)
    {
        if (!PropietarioLogueado(out int idPropietario))
        {
            return RedirectToAction("Login");
        }

        string? error = propiedad.Validar() ?? GestorArchivos.ValidarImagenOpcional(foto);
        if (error != null)
        {
            TempData["Error"] = error;
            return RedirectToAction("AgregarPropiedad");
        }

        propiedad.IdPropietario = idPropietario;
        propiedad.Direccion = propiedad.Direccion.Trim();
        propiedad.Foto = GestorArchivos.TieneContenido(foto)
            ? GestorArchivos.Guardar(foto!, GestorArchivos.CarpetaImagenes, $"propiedad_{idPropietario}")
            : "default.jpg";
        propiedad.FechaDeAlta = DateTime.Today;
        propiedad.Estado = OSO.Models.Propiedades.EstadoDisponible;

        bd.AgregarPropiedad(propiedad);
        return RedirectToAction("Propiedades");
    }

    [HttpGet]
    public IActionResult EditarPropiedad(int idPropiedad)
    {
        if (!PropietarioLogueado(out int idPropietario))
        {
            return RedirectToAction("Login");
        }

        Propiedades? propiedad = bd.ObtenerPropiedadDePropietario(idPropiedad, idPropietario);
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
        if (!PropietarioLogueado(out int idPropietario))
        {
            return RedirectToAction("Login");
        }

        Propiedades? propiedadActual = bd.ObtenerPropiedadDePropietario(propiedad.IdPropiedad, idPropietario);
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

        propiedad.IdPropietario = idPropietario;
        propiedad.Direccion = propiedad.Direccion.Trim();
        propiedad.Foto = GestorArchivos.TieneContenido(foto)
            ? GestorArchivos.Guardar(foto!, GestorArchivos.CarpetaImagenes, $"propiedad_{idPropietario}")
            : propiedadActual.Foto;

        bd.EditarPropiedad(propiedad);
        return RedirectToAction("Propiedades");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult InfoPropiedad(int idPropiedad)
    {
        if (!PropietarioLogueado(out int idPropietario))
        {
            return RedirectToAction("Login");
        }

        Propiedades? propiedad = bd.ObtenerPropiedadDePropietario(idPropiedad, idPropietario);
        if (propiedad == null)
        {
            return RedirectToAction("Propiedades");
        }

        return View(propiedad);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult EliminarPropiedad(int idPropiedad)
    {
        if (!PropietarioLogueado(out int idPropietario))
        {
            return RedirectToAction("Login");
        }

        var (error, archivos) = bd.EliminarPropiedad(idPropiedad, idPropietario);
        if (error != null)
        {
            TempData["Error"] = error;
        }
        else
        {
            foreach (var archivo in archivos)
            {
                ArchivosContrato.Borrar(archivo);
            }
        }

        return RedirectToAction("Propiedades");
    }

    // ---- Calendario ----

    [HttpGet]
    public IActionResult Calendario(int? anio, int? mes)
    {
        int? idPropietario = IdPropietarioSesion;
        int? idInquilino = IdInquilinoSesion;
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
        if (!PropietarioLogueado(out int idPropietario))
        {
            return RedirectToAction("Calendario");
        }

        List<Propiedades> propiedades = bd.ObtenerPropiedadesPorIdPropietario(idPropietario);
        ViewBag.Propiedades = propiedades;
        Propiedades? unica = propiedades.Count == 1 ? propiedades[0] : null;
        return View(new Evento
        {
            Fecha = (fecha ?? DateTime.Today).Date,
            HoraInicio = new TimeSpan(9, 0, 0),
            HoraFin = new TimeSpan(9, 0, 0).Add(Evento.DuracionPropuesta),
            IdPropiedad = unica?.IdPropiedad,
            Ubicacion = unica?.Direccion
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult GuardarEvento(Evento evento)
    {
        if (!PropietarioLogueado(out int idPropietario))
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
        if (evento.IdPropiedad.HasValue && bd.ObtenerPropiedadDePropietario(evento.IdPropiedad.Value, idPropietario) == null)
        {
            evento.IdPropiedad = null;
        }

        evento.IdPropietario = idPropietario;

        // Los eventos del contrato y de los gastos los arma el sistema: desde acá solo se crean manuales.
        evento.Tipo = "Manual";
        evento.IdContrato = null;
        evento.IdGastoPropiedad = null;
        bd.AgregarEvento(evento);
        return RedirectToAction("Calendario", new { anio = evento.Fecha.Year, mes = evento.Fecha.Month });
    }

    // ---- Mensajería y notificaciones ----

    [HttpGet]
    public IActionResult Mensajeria(int? con)
    {
        if (!UsuarioLogueado(out int idUsuario))
        {
            return RedirectToAction("Login");
        }

        int? idPropietario = IdPropietarioSesion;

        // Sólo se puede abrir el chat con alguien que sea contacto del usuario.
        Contacto? activo = con.HasValue ? bd.ObtenerContacto(idUsuario, idPropietario, IdInquilinoSesion, con.Value) : null;
        if (activo != null)
        {
            bd.MarcarMensajesComoLeidos(idUsuario, activo.IdUsuario);
            activo.NoLeidos = 0;
            ViewBag.Mensajes = bd.ObtenerConversacion(idUsuario, activo.IdUsuario);
        }

        ViewBag.Activo = activo;
        ViewBag.EsPropietario = idPropietario.HasValue;
        return View(bd.ObtenerContactos(idUsuario, idPropietario, IdInquilinoSesion));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult EnviarMensaje(int idReceptor, string? texto)
    {
        if (!UsuarioLogueado(out int idUsuario))
        {
            return RedirectToAction("Login");
        }

        Contacto? receptor = bd.ObtenerContacto(idUsuario, IdPropietarioSesion, IdInquilinoSesion, idReceptor);
        if (receptor != null && Mensaje.EsTextoValido(texto))
        {
            bd.EnviarMensaje(idUsuario, idReceptor, texto!.Trim());
        }

        return RedirectToAction("Mensajeria", new { con = idReceptor });
    }

    public IActionResult Notificaciones()
    {
        if (!UsuarioLogueado(out int idUsuario))
        {
            return RedirectToAction("Login");
        }

        List<Notificacion> notificaciones = bd.ObtenerNotificaciones(idUsuario);
        bd.MarcarNotificacionesComoLeidas(idUsuario);

        // El aviso de "el propietario cargó el contrato" lleva directo al contrato que el inquilino todavía tiene que aceptar.
        bool esInquilino = InquilinoLogueado(out int idInquilino);
        if (esInquilino)
        {
            Notificacion.VincularContratosParaAceptar(notificaciones, bd.ObtenerContratosPendientesDeInquilino(idInquilino));
        }

        ViewBag.EsInquilino = esInquilino;
        ViewBag.SinLeer = notificaciones.Count(n => !n.Leida);
        return View(notificaciones);
    }

    // ---- Expensas extraordinarias ----

    public IActionResult Expensas()
    {
        if (!PropietarioLogueado(out int idPropietario))
        {
            return RedirigirSinPermiso();
        }

        return View(bd.ObtenerExpensasPorIdPropietario(idPropietario));
    }

    [HttpGet]
    public IActionResult AgregarExpensa()
    {
        if (!PropietarioLogueado(out int idPropietario))
        {
            return RedirigirSinPermiso();
        }

        List<Propiedades> propiedades = bd.ObtenerPropiedadesPorIdPropietario(idPropietario);
        ViewBag.Propiedades = propiedades;
        // Con una sola propiedad ya viene elegida; el vencimiento se propone y se pisa si se lee la liquidación.
        ViewBag.IdPropiedadElegida = propiedades.Count == 1 ? propiedades[0].IdPropiedad : (int?)null;
        return View(new GastoPropiedad { FechaVencimiento = DateTime.Today.AddDays(GastoPropiedad.DiasVencimientoPropuesto) });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult AgregarExpensa(GastoPropiedad expensa, int idPropiedad, decimal montoExtraordinarias)
    {
        if (!PropietarioLogueado(out int idPropietario))
        {
            return RedirigirSinPermiso();
        }

        if (bd.ObtenerPropiedadDePropietario(idPropiedad, idPropietario) == null)
        {
            return RedirectToAction("Propiedades");
        }

        // Las ordinarias son un servicio mensual: las carga y paga quien corresponde desde Mis gastos.
        // Acá solo se cargan las extraordinarias, que llegan cuando el consorcio las decide.
        // Validar() controla el resto (vencimiento, aclaración).
        expensa.Monto = montoExtraordinarias;
        string? error = montoExtraordinarias <= 0 ? "Ingresá el monto de la expensa extraordinaria (mayor a cero)." : expensa.Validar();
        if (error != null)
        {
            TempData["Error"] = error;
            return RedirectToAction("AgregarExpensa");
        }

        // La fecha de alta es la de carga: no la elige el propietario.
        expensa.FechaAlta = DateTime.Today;
        expensa.FechaVencimiento = expensa.FechaVencimiento.Date;

        bd.AgregarExpensa(expensa, idPropiedad, 0, montoExtraordinarias);
        TempData["Mensaje"] = "Expensa extraordinaria cargada. Cada uno ve su parte en Mis gastos (según el contrato) y la paga con el comprobante.";
        return RedirectToAction("Expensas");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult PagarExpensa(int idGastoResponsable, IFormFile? comprobanteArchivo)
    {
        if (!PropietarioLogueado(out int idPropietario))
        {
            return RedirigirSinPermiso();
        }

        string? error = GestorArchivos.ValidarComprobante(comprobanteArchivo);
        if (error != null)
        {
            TempData["Mensaje"] = error;
            return RedirectToAction("Expensas");
        }

        string comprobante = GestorArchivos.Guardar(comprobanteArchivo!, GestorArchivos.CarpetaComprobantes, $"pago_expensa_{idGastoResponsable}");
        if (bd.PagarParteDelPropietario(idGastoResponsable, idPropietario, comprobante))
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

    // ---- Mis gastos ----

    // Lo que le toca pagar a quien entra: al inquilino, su parte; al propietario, la suya (de todas sus propiedades).
    // Incluye los servicios del mes que el contrato le pone a cargo y que todavía nadie cargó.
    public IActionResult MisGastos()
    {
        List<GastoPropiedad> gastos;
        List<PendienteContrato> pendientes;
        if (PropietarioLogueado(out int idPropietario))
        {
            ViewBag.EsPropietario = true;
            ViewBag.TieneContrato = true;
            pendientes = bd.ObtenerPendientesContratoPropietario(idPropietario);
            gastos = bd.ObtenerGastosPorIdPropietario(idPropietario);
        }
        else if (InquilinoLogueado(out int idInquilino))
        {
            ViewBag.EsPropietario = false;
            ViewBag.TieneContrato = bd.InquilinoTieneContratoConfirmado(idInquilino);
            pendientes = bd.ObtenerPendientesContratoInquilino(idInquilino);
            gastos = bd.ObtenerGastosPorIdInquilino(idInquilino);
        }
        else
        {
            return RedirectToAction("Login");
        }

        ViewBag.PendientesRecientes = PendienteContrato.Recientes(pendientes, true);
        ViewBag.PendientesAnteriores = PendienteContrato.Recientes(pendientes, false);
        ViewBag.GastosAPagar = GastoPropiedad.PorPago(gastos, false);
        ViewBag.GastosPagados = GastoPropiedad.PorPago(gastos, true);
        ViewBag.HayGastos = gastos.Count > 0 || pendientes.Count > 0;
        return View(gastos);
    }

    // Paga un pendiente que sale del contrato (sin gasto cargado): quien paga (inquilino o propietario) pone el monto
    // de la factura y el comprobante. Si es compartido, al otro le queda su parte para pagar.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult PagarPendienteContrato(int idPropiedad, int idGasto, DateTime periodo, decimal monto, IFormFile? comprobanteArchivo, IFormFile? facturaArchivo)
    {
        if (!QuienCarga(out string lado, out int idPersona))
        {
            return RedirectToAction("Login");
        }

        // La factura es opcional; si viene, se guarda con el gasto para que la otra parte vea de dónde sale el monto.
        bool conFactura = GestorArchivos.TieneContenido(facturaArchivo);
        string? error = monto <= 0 ? "Ingresá el monto de la factura (mayor a cero)." : GestorArchivos.ValidarComprobante(comprobanteArchivo);
        error ??= conFactura ? ArchivosContrato.ValidarFactura(facturaArchivo) : null;
        if (error != null)
        {
            TempData["Mensaje"] = error;
            return RedirectToAction("MisGastos");
        }

        string comprobante = GestorArchivos.Guardar(comprobanteArchivo!, GestorArchivos.CarpetaComprobantes, $"pago_contrato_{idPropiedad}_{idGasto}_{periodo:yyyyMM}");
        string? factura = conFactura ? ArchivosContrato.GuardarFactura(idPropiedad, facturaArchivo!) : null;

        if (bd.PagarPendienteContrato(lado, idPersona, idPropiedad, idGasto, periodo, monto, comprobante, factura))
        {
            TempData["Mensaje"] = lado == BD.LadoPropietario
                ? "Pago registrado. Si el gasto es compartido, al inquilino le queda su parte para pagar."
                : "Pago registrado. El propietario ya puede ver el comprobante.";
        }
        else
        {
            ArchivosContrato.BorrarFactura(factura);
            GestorArchivos.Borrar(GestorArchivos.CarpetaComprobantes, comprobante);
            TempData["Mensaje"] = "No se pudo registrar el pago: ese gasto ya no está pendiente.";
        }

        return RedirectToAction("MisGastos");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult PagarGasto(int idGastoResponsable, IFormFile? comprobanteArchivo)
    {
        if (!QuienCarga(out string lado, out int idPersona))
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
        bool esPropietario = lado == BD.LadoPropietario;
        bool pagado = esPropietario
            ? bd.PagarParteDelPropietario(idGastoResponsable, idPersona, comprobante)
            : bd.PagarGastoInquilino(idGastoResponsable, idPersona, comprobante);

        if (pagado)
        {
            TempData["Mensaje"] = esPropietario ? "Pago registrado." : "Pago registrado. El comprobante quedó pendiente de verificación.";
        }
        else
        {
            GestorArchivos.Borrar(GestorArchivos.CarpetaComprobantes, comprobante);
            TempData["Mensaje"] = "No se pudo registrar el pago de ese gasto.";
        }

        return RedirectToAction("MisGastos");
    }

    // Factura guardada con un gasto: solo la ven el propietario de la propiedad y el inquilino del contrato de ese mes.
    [HttpGet]
    public IActionResult VerFactura(int idGastoPropiedad)
    {
        int? idPropietario = IdPropietarioSesion;
        int? idInquilino = IdInquilinoSesion;
        if (!idPropietario.HasValue && !idInquilino.HasValue)
        {
            return RedirectToAction("Login");
        }

        string? factura = bd.ObtenerFacturaDeGasto(idGastoPropiedad, idPropietario, idInquilino);
        string? ruta = ArchivosContrato.ObtenerRutaFactura(factura);
        if (ruta == null)
        {
            TempData["Mensaje"] = "Ese gasto no tiene una factura guardada.";
            return RedirectToAction("MisGastos");
        }

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return PhysicalFile(ruta, ArchivosContrato.ObtenerTipoMime(factura!));
    }

    // ---- Cargar el mes ----

    // Todas las facturas que faltan cargar, en una sola pantalla. La carga quien recibe la factura (propietario o inquilino),
    // aunque le toque pagarla al otro: cargar no es pagar. Cada uno paga después su parte desde Mis gastos.
    [HttpGet]
    public IActionResult CargarMes()
    {
        if (!QuienCarga(out string lado, out int idPersona))
        {
            return RedirectToAction("Login");
        }

        return MostrarCargarMes(lado, idPersona, null, new Dictionary<string, string>());
    }

    private const long LimiteGuardarMes = 200L * 1024 * 1024;

    // Guarda las filas completas. Las vacías siguen pendientes; las que tienen error vuelven a la pantalla con lo escrito;
    // las que la otra parte cargó mientras tanto no se pisan.
    [HttpPost]
    [ValidateAntiForgeryToken]
    // Las filas vacías no se envían, pero alguien atrasado puede tener cientos de filas completas, y cada una puede llevar su factura.
    [RequestFormLimits(ValueCountLimit = 20_000, MultipartBodyLengthLimit = LimiteGuardarMes)]
    [RequestSizeLimit(LimiteGuardarMes)]
    public IActionResult GuardarMes(List<FilaEnviada>? filas)
    {
        if (!QuienCarga(out string lado, out int idPersona))
        {
            return RedirectToAction("Login");
        }

        ResultadoCargaMes resultado = FilasCargarMes.Guardar(lado, idPersona, filas ?? new List<FilaEnviada>());
        if (resultado.Errores.Count == 0)
        {
            TempData["Success"] = resultado.Resumen.Length > 0 ? resultado.Resumen : "No completaste ningún monto.";
            return RedirectToAction("CargarMes");
        }

        // Con errores, la pantalla vuelve con lo que se escribió en las filas que no se guardaron.
        // Las facturas adjuntas no vuelven (un navegador no deja reponer archivos): la pantalla avisa que hay que adjuntarlas de nuevo.
        ViewBag.Resumen = resultado.Resumen;
        return MostrarCargarMes(lado, idPersona, filas, resultado.Errores);
    }

    private IActionResult MostrarCargarMes(string lado, int idPersona, List<FilaEnviada>? enviadas, Dictionary<string, string> errores)
    {
        List<FilaCargarMes> filas = bd.ObtenerFilasCargarMes(lado, idPersona);
        PantallaCargarMes pantalla = FilasCargarMes.ArmarPantalla(filas, enviadas, errores);
        ViewBag.EsPropietario = lado == BD.LadoPropietario;
        ViewBag.HayErrores = errores.Count > 0;
        ViewBag.MesesRecientes = pantalla.Recientes;
        ViewBag.MesesAnteriores = pantalla.Anteriores;
        ViewBag.CantidadAnteriores = pantalla.CantidadAnteriores;
        return View("CargarMes", filas);
    }

    // Lee una factura (PDF o foto) con la IA y devuelve el monto, el vencimiento y el mes para completar el formulario.
    // La factura no se guarda: solo se lee. Pueden usarla el propietario y el inquilino.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(11_534_336)]
    public async Task<IActionResult> LeerFactura(IFormFile? factura, string? tipo)
    {
        if (!QuienCarga(out _, out _))
        {
            return Unauthorized(new { ok = false, mensaje = "Iniciá sesión para leer la factura." });
        }

        string? error = ArchivosContrato.ValidarFactura(factura);
        if (error != null)
        {
            return Json(new { ok = false, mensaje = error });
        }

        using var memoria = new MemoryStream();
        await factura!.CopyToAsync(memoria);

        string? tipoGasto = tipo?.Trim();
        if (tipoGasto != null && tipoGasto.Length > 50)
        {
            tipoGasto = tipoGasto[..50];
        }

        var (lectura, errorIa, _) = await _extractorFactura.LeerAsync(memoria.ToArray(), ArchivosContrato.ObtenerTipoMime(factura.FileName), tipoGasto);
        if (lectura == null)
        {
            return Json(new { ok = false, mensaje = errorIa ?? "No pudimos leer la factura; cargá los datos a mano." });
        }

        return Json(new
        {
            ok = true,
            monto = lectura.Monto,
            vencimiento = lectura.Vencimiento?.ToString("yyyy-MM-dd"),
            periodo = lectura.Periodo?.ToString("yyyy-MM-dd"),
            proveedor = lectura.Proveedor,
            dudosa = lectura.EsDudosa
        });
    }

    // Lee una factura de la tanda de "Cargar el mes" y dice en qué fila va. No guarda la factura ni el gasto:
    // la pantalla completa la fila y el propietario confirma con "Guardar los completos".
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(11_534_336)]
    public async Task<IActionResult> LeerFacturaParaMes(IFormFile? factura)
    {
        if (!QuienCarga(out string lado, out int idPersona))
        {
            return Unauthorized(new { ok = false, tipoError = "NoDisponible", mensaje = "Iniciá sesión para leer facturas." });
        }

        string? error = ArchivosContrato.ValidarFactura(factura);
        if (error != null)
        {
            return Json(new { ok = false, tipoError = nameof(TipoErrorIa.NoSeLeyo), mensaje = error });
        }

        List<FilaCargarMes> filas = bd.ObtenerFilasCargarMes(lado, idPersona);
        using var memoria = new MemoryStream();
        await factura!.CopyToAsync(memoria);
        var (lectura, errorIa, tipo) = await _extractorFactura.LeerAsync(memoria.ToArray(), ArchivosContrato.ObtenerTipoMime(factura.FileName), null, FilasCargarMes.Servicios(filas));
        if (lectura == null)
        {
            return Json(new { ok = false, tipoError = tipo.ToString(), mensaje = errorIa ?? "No pudimos leer la factura." });
        }

        Ubicacion ubicacion = UbicadorFacturas.Ubicar(lectura, filas);
        return Json(new
        {
            ok = true,
            monto = lectura.Monto,
            vencimiento = lectura.Vencimiento?.ToString("yyyy-MM-dd"),
            periodo = lectura.Periodo?.ToString("yyyy-MM-dd"),
            proveedor = lectura.Proveedor,
            servicio = lectura.Servicio,
            domicilio = lectura.Domicilio,
            estado = ubicacion.Estado.ToString(),
            clave = ubicacion.Clave,
            clavesIncluidas = ubicacion.ClavesIncluidas
        });
    }

    // ---- Contrato: alta, ajustes y terminación (propietario) ----

    [HttpGet]
    public IActionResult Contrato(int idPropiedad)
    {
        if (!PropietarioLogueado(out int idPropietario))
        {
            return RedirectToAction("Login");
        }

        Propiedades? propiedad = bd.ObtenerPropiedadDePropietario(idPropiedad, idPropietario);
        if (propiedad == null)
        {
            return RedirectToAction("Propiedades");
        }

        Contratos? vigente = bd.ObtenerContratoActivoPorPropiedad(idPropiedad);
        ViewBag.Propiedad = propiedad;
        ViewBag.Contrato = vigente;
        ViewBag.ContratoEnCurso = bd.ObtenerContratoEnCursoPorPropiedad(idPropiedad);
        ViewBag.UltimoFinalizado = bd.ObtenerUltimoContratoFinalizado(idPropiedad);

        if (vigente != null)
        {
            Usuario? inquilino = bd.ObtenerUsuarioPorIdInquilino(vigente.IdInquilino);
            List<AjusteContrato> ajustes = bd.ObtenerAjustes(vigente.IdContrato);
            ProximoAjuste? proximo = AjustesDeContrato.Proximo(vigente, ajustes);
            ViewBag.NombreInquilino = inquilino?.NombreCompleto ?? $"Inquilino {vigente.IdInquilino}";
            ViewBag.Ajustes = ajustes;
            ViewBag.ProximoAjuste = proximo?.Fecha;
            ViewBag.PuedeAplicarAjuste = proximo?.YaCorresponde == true;
            ViewBag.CalculoAjuste = proximo?.Calculo;
        }

        return View(propiedad);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult AplicarAjuste(int idContrato, decimal? montoManual)
    {
        if (!PropietarioLogueado(out int idPropietario))
        {
            return RedirectToAction("Login");
        }

        var (contrato, propiedad) = ObtenerConfirmadoDelPropietario(idContrato, idPropietario);
        if (contrato == null || propiedad == null)
        {
            return RedirectToAction("Propiedades");
        }

        var (aplicado, mensaje) = AjustesDeContrato.Aplicar(contrato, montoManual);
        TempData[aplicado ? "Success" : "Error"] = mensaje;
        return RedirectToAction("Contrato", new { idPropiedad = propiedad.IdPropiedad });
    }

    [HttpGet]
    public IActionResult CrearContrato(int idPropiedad)
    {
        if (!PropietarioLogueado(out int idPropietario))
        {
            return RedirectToAction("Login");
        }

        Propiedades? propiedad = bd.ObtenerPropiedadDePropietario(idPropiedad, idPropietario);
        if (propiedad == null)
        {
            return RedirectToAction("Propiedades");
        }

        if (bd.TieneContratoActivoOEnCurso(idPropiedad))
        {
            return RedirectToAction("Contrato", new { idPropiedad });
        }

        ViewBag.Propiedad = propiedad;
        ViewBag.Inquilinos = bd.ObtenerInquilinosParaContrato(propiedad);
        return View(propiedad);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult GuardarContrato(int idPropiedad, int idInquilino)
    {
        if (!PropietarioLogueado(out int idPropietario))
        {
            return RedirectToAction("Login");
        }

        Propiedades? propiedad = bd.ObtenerPropiedadDePropietario(idPropiedad, idPropietario);
        if (propiedad == null)
        {
            return RedirectToAction("Propiedades");
        }

        // Una propiedad tiene un único inquilino: si ya hay uno, se usa siempre ése.
        int? idInquilinoContrato = ReglasContrato.ElegirInquilino(propiedad, idInquilino, bd.ObtenerInquilinosParaContrato(propiedad));
        if (idInquilinoContrato == null)
        {
            TempData["Error"] = "Debe seleccionar un inquilino para crear el contrato.";
            return RedirectToAction("CrearContrato", new { idPropiedad });
        }

        // El contrato nace en Borrador: se completa y se confirma en la pantalla de revisión.
        int? idContrato = CrearBorrador(idPropiedad, idInquilinoContrato.Value);
        return idContrato.HasValue
            ? RedirectToAction("RevisarContrato", new { idContrato = idContrato.Value })
            : RedirectToAction("Contrato", new { idPropiedad });
    }

    [HttpGet]
    public IActionResult SubirContrato(int idPropiedad)
    {
        if (!PropietarioLogueado(out int idPropietario))
        {
            return RedirectToAction("Login");
        }

        Propiedades? propiedad = bd.ObtenerPropiedadDePropietario(idPropiedad, idPropietario);
        if (propiedad == null)
        {
            return RedirectToAction("Propiedades");
        }

        if (bd.TieneContratoActivoOEnCurso(idPropiedad))
        {
            return RedirectToAction("Contrato", new { idPropiedad });
        }

        ViewBag.Inquilinos = bd.ObtenerInquilinosParaContrato(propiedad);
        return View(propiedad);
    }

    // Sube el contrato, lo lee con Gemini y deja un Borrador listo para revisar.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(11_534_336)] // 10 MB del archivo + lo que ocupa el resto del formulario
    public async Task<IActionResult> SubirContrato(int idPropiedad, int idInquilino, IFormFile? archivo)
    {
        if (!PropietarioLogueado(out int idPropietario))
        {
            return RedirectToAction("Login");
        }

        Propiedades? propiedad = bd.ObtenerPropiedadDePropietario(idPropiedad, idPropietario);
        if (propiedad == null)
        {
            return RedirectToAction("Propiedades");
        }

        int? idInquilinoContrato = ReglasContrato.ElegirInquilino(propiedad, idInquilino, bd.ObtenerInquilinosParaContrato(propiedad));
        string? error = ArchivosContrato.Validar(archivo)
                        ?? (idInquilinoContrato == null ? "Debe seleccionar un inquilino para crear el contrato." : null);
        if (error != null)
        {
            TempData["Error"] = error;
            return RedirectToAction("SubirContrato", new { idPropiedad });
        }

        int? idContrato = CrearBorrador(idPropiedad, idInquilinoContrato!.Value);
        if (idContrato == null)
        {
            return RedirectToAction("Contrato", new { idPropiedad });
        }

        bd.GuardarNombreArchivoContrato(idContrato.Value, ArchivosContrato.Guardar(idContrato.Value, archivo!));
        using var memoria = new MemoryStream();
        await archivo!.CopyToAsync(memoria);

        // La lectura sigue en segundo plano: la pantalla de revisión muestra "Leyendo..." hasta que termine.
        _lectorContratos.Empezar(idContrato.Value, memoria.ToArray(), ArchivosContrato.ObtenerTipoMime(archivo.FileName));
        return RedirectToAction("RevisarContrato", new { idContrato = idContrato.Value, recienSubido = true });
    }

    // Crea el contrato en Borrador. Si no se puede (por ejemplo, la propiedad ya tiene otro en curso),
    // deja el motivo para mostrar y devuelve null.
    private int? CrearBorrador(int idPropiedad, int idInquilino)
    {
        try
        {
            return bd.CrearContratoBorrador(idPropiedad, idInquilino);
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return null;
        }
    }

    // El archivo solo lo bajan el propietario de la propiedad o el inquilino del contrato.
    [HttpGet]
    public IActionResult DescargarContrato(int idContrato)
    {
        int? idPropietario = IdPropietarioSesion;
        int? idInquilino = IdInquilinoSesion;
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

    // El contrato Confirmado de una propiedad del propietario logueado; null si no corresponde.
    private (Contratos? Contrato, Propiedades? Propiedad) ObtenerConfirmadoDelPropietario(int idContrato, int idPropietario)
    {
        Contratos? contrato = bd.ObtenerContratoPorId(idContrato);
        Propiedades? propiedad = contrato == null ? null : bd.ObtenerPropiedadDePropietario(contrato.IdPropiedad, idPropietario);
        return propiedad != null && contrato!.Estado == "Confirmado" ? (contrato, propiedad) : (null, null);
    }

    [HttpGet]
    public IActionResult TerminarContrato(int idContrato)
    {
        if (!PropietarioLogueado(out int idPropietario))
        {
            return RedirectToAction("Login");
        }

        var (contrato, propiedad) = ObtenerConfirmadoDelPropietario(idContrato, idPropietario);
        if (contrato == null || propiedad == null)
        {
            return RedirectToAction("Propiedades");
        }

        Usuario? inquilino = bd.ObtenerUsuarioPorIdInquilino(contrato.IdInquilino);
        ViewBag.Propiedad = propiedad;
        ViewBag.NombreInquilino = inquilino?.NombreCompleto ?? "Inquilino";
        return View(contrato);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult TerminarContrato(int idContrato, string? motivo, DateTime? fechaFinReal, string? descripcion)
    {
        if (!PropietarioLogueado(out int idPropietario))
        {
            return RedirectToAction("Login");
        }

        var (contrato, propiedad) = ObtenerConfirmadoDelPropietario(idContrato, idPropietario);
        if (contrato == null || propiedad == null)
        {
            return RedirectToAction("Propiedades");
        }

        string? error = ReglasContrato.ValidarTerminacion(contrato, motivo, fechaFinReal, descripcion, DateTime.Today);
        if (error != null)
        {
            TempData["Error"] = error;
            return RedirectToAction("TerminarContrato", new { idContrato });
        }

        DateTime fechaReal = ReglasContrato.FechaFinReal(contrato, motivo!, fechaFinReal, DateTime.Today);
        if (bd.TerminarContrato(idContrato, motivo!, fechaReal, descripcion))
        {
            TempData["Success"] = "Terminaste el contrato. Le avisamos al inquilino y la propiedad quedó disponible.";
        }
        else
        {
            TempData["Error"] = "No se pudo terminar el contrato: puede que ya esté terminado.";
        }

        return RedirectToAction("Contrato", new { idPropiedad = propiedad.IdPropiedad });
    }

    // ---- Revisión del contrato (propietario) y aceptación (inquilino) ----

    // El contrato en Borrador de una propiedad del propietario logueado; null si no corresponde.
    private (Contratos? Contrato, Propiedades? Propiedad) ObtenerBorradorDelPropietario(int idContrato, int idPropietario)
    {
        Contratos? contrato = bd.ObtenerContratoPorId(idContrato);
        Propiedades? propiedad = contrato == null ? null : bd.ObtenerPropiedadDePropietario(contrato.IdPropiedad, idPropietario);
        return propiedad != null && contrato!.Estado == "Borrador" ? (contrato, propiedad) : (null, null);
    }

    [HttpGet]
    public IActionResult RevisarContrato(int idContrato, bool recienSubido = false)
    {
        if (!PropietarioLogueado(out int idPropietario))
        {
            return RedirectToAction("Login");
        }

        var (contrato, propiedad) = ObtenerBorradorDelPropietario(idContrato, idPropietario);
        if (contrato == null || propiedad == null)
        {
            return RedirectToAction("Propiedades");
        }

        // Mientras la IA lo está leyendo, el borrador todavía está vacío: se muestra una espera que se recarga sola.
        if (_lectorContratos.EstaLeyendo(idContrato))
        {
            ViewBag.IdContrato = idContrato;
            return View("LeyendoContrato", propiedad);
        }

        if (recienSubido && bd.ObtenerUltimaExtraccion(idContrato) != null)
        {
            TempData["Success"] = "Leímos el contrato. Revisá los datos marcados antes de confirmar.";
            // Sin el parámetro, para que recargar la página no repita el aviso.
            return RedirectToAction("RevisarContrato", new { idContrato });
        }

        return View(RevisionContrato.ParaRevisar(contrato, propiedad));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult GuardarRevision(Contratos contrato, List<ReglaGasto>? reglas, string accion)
    {
        if (!PropietarioLogueado(out int idPropietario))
        {
            return RedirectToAction("Login");
        }

        // Todo sale del contrato guardado; del formulario solo se toman los datos editables.
        var (guardado, propiedad) = ObtenerBorradorDelPropietario(contrato.IdContrato, idPropietario);
        if (guardado == null || propiedad == null)
        {
            return RedirectToAction("Propiedades");
        }

        string? error = guardado.CopiarDatosRevision(contrato);
        if (error != null)
        {
            TempData["Error"] = error;
            return RedirectToAction("RevisarContrato", new { idContrato = guardado.IdContrato });
        }

        // Reglas de gasto: solo se acepta responsable y porcentaje de tipos que existen en el contrato.
        List<ReglaGasto> reglasGuardadas = bd.ObtenerReglasGasto(guardado.IdContrato);
        ReglasContrato.CopiarCambiosDeReparto(reglasGuardadas, reglas);
        bd.GuardarDatosContrato(guardado);
        bd.GuardarReglasGasto(guardado.IdContrato, reglasGuardadas);

        if (accion != "confirmar")
        {
            TempData["Success"] = "Guardamos el borrador.";
            return RedirectToAction("RevisarContrato", new { idContrato = guardado.IdContrato });
        }

        if (ReglasContrato.Errores(guardado, reglasGuardadas).Count > 0)
        {
            TempData["Error"] = "Todavía no se puede confirmar: corregí los puntos marcados.";
            return RedirectToAction("RevisarContrato", new { idContrato = guardado.IdContrato });
        }

        if (bd.ConfirmarPorPropietario(guardado.IdContrato))
        {
            TempData["Success"] = "Contrato confirmado. Le avisamos al inquilino para que lo acepte.";
        }

        return RedirectToAction("Contrato", new { idPropiedad = propiedad.IdPropiedad });
    }

    // Cancela el borrador sin guardar nada: se borra el contrato, lo que leyó la IA y el archivo subido.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult DescartarBorrador(int idContrato)
    {
        if (!PropietarioLogueado(out int idPropietario))
        {
            return RedirectToAction("Login");
        }

        var (contrato, propiedad) = ObtenerBorradorDelPropietario(idContrato, idPropietario);
        if (contrato == null || propiedad == null)
        {
            return RedirectToAction("Propiedades");
        }

        var (descartado, archivo) = bd.DescartarBorrador(idContrato);
        if (descartado)
        {
            ArchivosContrato.Borrar(archivo);
            TempData["Success"] = "Cancelaste el borrador del contrato.";
        }

        return RedirectToAction("Contrato", new { idPropiedad = propiedad.IdPropiedad });
    }

    // Contrato en solo lectura. El inquilino lo ve y, si está pendiente, lo acepta.
    // El propietario ve así el contrato que ya envió; si sigue en borrador, va a la revisión.
    [HttpGet]
    public IActionResult VerContrato(int idContrato, string? desde)
    {
        int? idInquilino = IdInquilinoSesion;
        int? idPropietario = IdPropietarioSesion;
        if (!idInquilino.HasValue && !idPropietario.HasValue)
        {
            return RedirectToAction("Login");
        }

        Contratos? contrato = bd.ObtenerContratoPorId(idContrato);
        if (contrato == null)
        {
            return RedirectToAction("Propiedades");
        }

        bool comoPropietario = !idInquilino.HasValue;
        Propiedades? propiedad = comoPropietario
            ? bd.ObtenerPropiedadDePropietario(contrato.IdPropiedad, idPropietario!.Value)
            : contrato.IdInquilino == idInquilino!.Value ? bd.ObtenerPropiedadPorId(contrato.IdPropiedad) : null;

        if (propiedad == null || (contrato.EsBorrador && !comoPropietario))
        {
            return RedirectToAction("Propiedades");
        }

        if (contrato.EsBorrador)
        {
            return RedirectToAction("RevisarContrato", new { idContrato });
        }

        ViewBag.VolverANotificaciones = desde == "notificaciones";
        ViewBag.ComoPropietario = comoPropietario;
        return View(RevisionContrato.ParaLeer(contrato, propiedad));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult AceptarContrato(int idContrato)
    {
        if (!InquilinoLogueado(out int idInquilino))
        {
            return RedirigirSinPermiso();
        }

        if (bd.AceptarPorInquilino(idContrato, idInquilino))
        {
            TempData["Success"] = "Aceptaste el contrato. Ya está vigente.";
        }
        else
        {
            TempData["Error"] = "No se pudo aceptar el contrato. Puede que ya esté aceptado o que la propiedad tenga otro inquilino.";
        }

        return RedirectToAction("Propiedades");
    }

    // El inquilino no puede vetar la terminación, pero sí dejar por escrito que no está de acuerdo (una sola vez).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult DejarDisconformidad(int idContrato, string? texto)
    {
        if (!InquilinoLogueado(out int idInquilino))
        {
            return RedirigirSinPermiso();
        }

        if (string.IsNullOrWhiteSpace(texto) || texto.Trim().Length > ReglasContrato.LargoMaximoDisconformidad)
        {
            TempData["Error"] = $"Escribí tu disconformidad (hasta {ReglasContrato.LargoMaximoDisconformidad} caracteres).";
        }
        else if (!bd.DejarDisconformidad(idContrato, idInquilino, texto))
        {
            TempData["Error"] = "No se pudo registrar: el contrato no es tuyo, no terminó por ese motivo o ya dejaste una constancia.";
        }
        else
        {
            TempData["Success"] = "Quedó registrada tu disconformidad. La ven los dos.";
        }

        return RedirectToAction("VerContrato", new { idContrato });
    }

    // ---- Servicios y gastos de una propiedad (propietario) ----

    [HttpGet]
    public IActionResult Servicios(int idPropiedad)
    {
        if (!PropietarioLogueado(out int idPropietario))
        {
            return RedirectToAction("Login");
        }

        Propiedades? propiedad = bd.ObtenerPropiedadDePropietario(idPropiedad, idPropietario);
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
        if (!PropietarioLogueado(out int idPropietario))
        {
            return RedirectToAction("Login");
        }

        Propiedades? propiedad = bd.ObtenerPropiedadDePropietario(idPropiedad, idPropietario);
        if (propiedad == null)
        {
            return RedirectToAction("Propiedades");
        }

        ViewBag.Tipo = tipo;
        ViewBag.IdPropiedad = idPropiedad;
        ViewBag.Propiedad = propiedad;
        ViewBag.Gastos = bd.ObtenerGastosPorServicioYTipo(idPropiedad, tipo);
        ViewBag.PendientesInquilino = bd.ObtenerPendientesContratoPropiedad(idPropiedad, tipo);
        return View(propiedad);
    }

    [HttpGet]
    public IActionResult AgregarGasto(int idPropiedad, string? tipo, DateTime? periodo)
    {
        if (!PropietarioLogueado(out int idPropietario))
        {
            return RedirectToAction("Login");
        }

        Propiedades? propiedad = bd.ObtenerPropiedadDePropietario(idPropiedad, idPropietario);
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

        // Con un mes indicado, el formulario abre con el gasto ya cargado para corregirlo (por ejemplo el monto que declaró el inquilino).
        GastoPropiedad? existente = periodo.HasValue ? bd.ObtenerGastoDePropiedadPorPeriodo(idPropiedad, tipoGasto.IdGasto, periodo.Value) : null;
        if (existente != null)
        {
            ViewBag.Corrigiendo = true;
            ViewBag.DeclaradoPorInquilino = existente.CargadoPorInquilino;
            ViewBag.PartePropietarioPagada = existente.MiPartePagada;
            return View(existente);
        }

        return View(new GastoPropiedad
        {
            Periodo = DateTime.Today,
            FechaAlta = DateTime.Today,
            FechaVencimiento = DateTime.Today.AddDays(GastoPropiedad.DiasVencimientoPropuesto)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult GuardarGasto(int idPropiedad, int idGasto, DateTime mes, decimal monto, DateTime fechaVencimiento, string estado, IFormFile? comprobanteArchivo, string? responsable)
    {
        if (!PropietarioLogueado(out int idPropietario))
        {
            return RedirectToAction("Login");
        }

        Propiedades? propiedad = bd.ObtenerPropiedadDePropietario(idPropiedad, idPropietario);
        if (propiedad == null)
        {
            return RedirectToAction("Propiedades");
        }

        Gastos? tipoGasto = bd.PropiedadTieneTipoGasto(idPropiedad, idGasto) ? bd.ObtenerTipoGastoPorId(idGasto) : null;
        if (tipoGasto == null)
        {
            return RedirectToAction("Servicios", new { idPropiedad });
        }

        var gasto = new GastoPropiedad { IdGasto = idGasto, Periodo = mes, Monto = monto, FechaVencimiento = fechaVencimiento, FechaAlta = DateTime.Today };
        bool conComprobante = GestorArchivos.TieneContenido(comprobanteArchivo);
        string? error = mes == default ? "Debe indicar el mes del gasto." : gasto.Validar();
        error ??= conComprobante ? GestorArchivos.ValidarComprobante(comprobanteArchivo) : null;
        if (error != null)
        {
            TempData["Error"] = error;
            return RedirectToAction("AgregarGasto", new { idPropiedad, tipo = tipoGasto.TipoGasto });
        }

        string? comprobante = conComprobante
            ? GestorArchivos.Guardar(comprobanteArchivo!, GestorArchivos.CarpetaComprobantes, $"gasto_{idPropiedad}_{idGasto}")
            : null;

        // Si no se elige a nadie, el gasto se reparte según el contrato.
        string? responsableManual = GastoPropiedad.ResponsableElegido(responsable, propiedad.IdInquilino.HasValue);
        bd.AgregarGasto(idPropiedad, idGasto, gasto, estado == "Pagado", comprobante, responsableManual);
        return RedirectToAction("ServicioDetalle", new { idPropiedad, tipo = tipoGasto.TipoGasto });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult GuardarEstadoGasto(int idPropiedad, int idGastoPropiedad, string tipo, string estado)
    {
        if (!PropietarioLogueado(out int idPropietario))
        {
            return RedirectToAction("Login");
        }

        if (bd.ObtenerPropiedadDePropietario(idPropiedad, idPropietario) == null)
        {
            return RedirectToAction("Propiedades");
        }

        bd.ActualizarEstadoGasto(idPropiedad, idGastoPropiedad, estado == "Pagado");
        return RedirectToAction("ServicioDetalle", new { idPropiedad, tipo });
    }

    [HttpGet]
    public IActionResult AgregarTipoGasto(int idPropiedad)
    {
        if (!PropietarioLogueado(out int idPropietario))
        {
            return RedirectToAction("Login");
        }

        Propiedades? propiedad = bd.ObtenerPropiedadDePropietario(idPropiedad, idPropietario);
        if (propiedad == null)
        {
            return RedirectToAction("Propiedades");
        }

        ViewBag.Propiedad = propiedad;
        ViewBag.TiposSugeridos = bd.ObtenerTiposSugeridosParaPropiedad(idPropiedad);
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult GuardarTipoGasto(int idPropiedad, string? nombreTipo)
    {
        if (!PropietarioLogueado(out int idPropietario))
        {
            return RedirectToAction("Login");
        }

        if (bd.ObtenerPropiedadDePropietario(idPropiedad, idPropietario) == null)
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
    public IActionResult EliminarTipoGasto(int idPropiedad, int idGasto)
    {
        if (!PropietarioLogueado(out int idPropietario))
        {
            return RedirectToAction("Login");
        }

        if (bd.ObtenerPropiedadDePropietario(idPropiedad, idPropietario) != null)
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
