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
        // usar directamente el valor enviado por el formulario
        Console.WriteLine(esPropietario);

        bd.RegistrarUsuario(usuario, esPropietario);
        return View("Index");
    }

    public IActionResult Login()
    {
        return View();
    }

    public IActionResult IniciarSesion(string correo, string contrasena){
        Usuario usuario = bd.verificarUsuario(new Usuario { Correo = correo });
        if (usuario != null && usuario.Contrasena == contrasena)
        {
            //HttpContext.Session.SetString("Usuario", usuario.correo);
           // HttpContext.Session.SetString("ID", usuario.id.ToString());
            return View("Bienvenida");
        }
        else
        {
            ViewBag.Error= "Nombre de usuario o contraseña incorrectos.";
            return View("Login");
        }
    }

       public IActionResult Propiedades()
    {
        return View();
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
