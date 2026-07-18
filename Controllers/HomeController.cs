using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MaximoROWeb.Models;
using MaximoROWeb.Services;
using Microsoft.AspNetCore.RateLimiting;

namespace MaximoROWeb.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly AccountRegistrationService _accountRegistrationService;

    public HomeController(
        ILogger<HomeController> logger,
        AccountRegistrationService accountRegistrationService)
    {
        _logger = logger;
        _accountRegistrationService = accountRegistrationService;
    }

    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Features()
    {
        return View();
    }

    public IActionResult ServerInfo()
    {
        return View();
    }

    public IActionResult News()
    {
        return View();
    }

    public IActionResult Downloads()
    {
        return View();
    }

    public IActionResult Rules()
    {
        return View();
    }

    public IActionResult Discord()
    {
        return View();
    }

    [HttpGet]
    public IActionResult Register()
    {
        return View(new RegisterViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("registration")]
    public async Task<IActionResult> Register(
        RegisterViewModel model,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(model.Website))
        {
            TempData["RegistrationSuccess"] =
                "Your registration request was received.";

            return RedirectToAction(nameof(Register));
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var username = model.Username.Trim();
        var email = model.Email.Trim();
        var sex = model.Sex.ToUpperInvariant();

        var result = await _accountRegistrationService.RegisterAsync(
            username,
            model.Password,
            email,
            sex,
            cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(
                string.Empty,
                result.Error ?? "Account registration failed.");

            return View(model);
        }

        TempData["RegistrationSuccess"] =
            "Your MaximoRO account was created successfully. You can now sign in through the game client.";

        return RedirectToAction(nameof(Register));
    }

    [ResponseCache(
        Duration = 0,
        Location = ResponseCacheLocation.None,
        NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel
        {
            RequestId = Activity.Current?.Id
                ?? HttpContext.TraceIdentifier
        });
    }
}