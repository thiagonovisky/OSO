using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OSO.Models;

namespace OSO.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    BD bd = new BD();

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
        Console.WriteLine(esPropietario);
        bd.RegistrarUsuario(usuario, esPropietario);
        return View("Index");
    }

    public IActionResult Login()
    {
        return View();
    }

    public IActionResult IniciarSesion(string correo, string contrasena)
    {
        Usuario usuario = bd.verificarUsuario(new Usuario { Correo = correo });
        if (usuario != null && usuario.Contrasena == contrasena)
        {
            HttpContext.Session.SetInt32("IdUsuario", usuario.Id);
            HttpContext.Session.SetInt32("IdPropietario", usuario.IdPropietario);
            return RedirectToAction("Bienvenida");
        }
        else
        {
            ViewBag.Error = "Nombre de usuario o contraseña incorrectos.";
            return View("Login");
        }
    }

    public IActionResult Bienvenida()
    {
        return View();
    }

    public IActionResult Propiedades(int? idPropietario)
    {
        int? idActual = idPropietario ?? HttpContext.Session.GetInt32("IdPropietario");

        if (!idActual.HasValue)
        {
            return View(new List<Propiedades>());
        }

        List<Propiedades> propiedades = bd.ObtenerPropiedadesPorIdPropietario(idActual.Value);
        return View(propiedades);
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
