using System.Diagnostics;
using MaximoROWeb.Models;
using MaximoROWeb.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MaximoROWeb.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly AccountRegistrationService _accountRegistrationService;
    private readonly EmailVerificationService _emailVerificationService;
    private readonly IVerificationEmailSender _verificationEmailSender;

    public HomeController(
        ILogger<HomeController> logger,
        AccountRegistrationService accountRegistrationService,
        EmailVerificationService emailVerificationService,
        IVerificationEmailSender verificationEmailSender)
    {
        _logger = logger;
        _accountRegistrationService = accountRegistrationService;
        _emailVerificationService = emailVerificationService;
        _verificationEmailSender = verificationEmailSender;
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

    public IActionResult ProgressionPatch()
    {
        return View();
    }

    public IActionResult JourneyForwardUpdate()
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
        SetEmailVerificationAvailability();
        return View(new RegisterViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("registration")]
    public async Task<IActionResult> Register(
        RegisterViewModel model,
        CancellationToken cancellationToken)
    {
        SetEmailVerificationAvailability();

        if (!string.IsNullOrWhiteSpace(model.Website))
        {
            TempData["RegistrationSuccess"] =
                "Your registration request was received.";

            return RedirectToAction(nameof(Register));
        }

        if (!_verificationEmailSender.IsConfigured)
        {
            ModelState.AddModelError(
                string.Empty,
                "Registration is temporarily unavailable while email verification is being configured.");

            return View(model);
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

        if (!result.Succeeded
            || string.IsNullOrWhiteSpace(result.VerificationToken))
        {
            ModelState.AddModelError(
                string.Empty,
                result.Error ?? "Account registration failed.");

            return View(model);
        }

        var delivery =
            await _verificationEmailSender.SendVerificationAsync(
                email,
                username,
                result.VerificationToken,
                cancellationToken);

        if (delivery.Succeeded)
        {
            TempData["RegistrationSuccess"] =
                "Your account was created. Check your email and open the verification link before signing in to the game.";
        }
        else
        {
            TempData["RegistrationWarning"] =
                "Your account was created but the verification email could not be delivered. Use the resend page in a few minutes.";
        }

        return RedirectToAction(nameof(Register));
    }

    [HttpGet("/account/verify-email")]
    [EnableRateLimiting("verification-link")]
    [ResponseCache(
        Duration = 0,
        Location = ResponseCacheLocation.None,
        NoStore = true)]
    public async Task<IActionResult> VerifyEmail(
        [FromQuery] string? token,
        CancellationToken cancellationToken)
    {
        EmailVerificationOutcome outcome;

        try
        {
            outcome = await _emailVerificationService.VerifyAsync(
                token,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "An email verification request could not be completed.");

            outcome = EmailVerificationOutcome.AccountUnavailable;
        }

        return View(
            "EmailVerification",
            new EmailVerificationViewModel
            {
                Outcome = outcome
            });
    }

    [HttpGet("/account/resend-verification")]
    public IActionResult ResendVerification()
    {
        SetEmailVerificationAvailability();
        return View(new ResendVerificationViewModel());
    }

    [HttpPost("/account/resend-verification")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("verification-resend")]
    public async Task<IActionResult> ResendVerification(
        ResendVerificationViewModel model,
        CancellationToken cancellationToken)
    {
        SetEmailVerificationAvailability();

        if (!string.IsNullOrWhiteSpace(model.Website))
        {
            TempData["ResendVerificationMessage"] =
                GetGenericResendMessage();

            return RedirectToAction(nameof(ResendVerification));
        }

        if (!_verificationEmailSender.IsConfigured)
        {
            ModelState.AddModelError(
                string.Empty,
                "Verification email delivery is temporarily unavailable.");

            return View(model);
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var result =
                await _emailVerificationService
                    .CreateResendTokenAsync(
                        model.Email.Trim(),
                        cancellationToken);

            if (result.Outcome == ResendTokenOutcome.Created
                && result.Email is not null
                && result.Username is not null
                && result.Token is not null)
            {
                await _verificationEmailSender.SendVerificationAsync(
                    result.Email,
                    result.Username,
                    result.Token,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "A verification email resend request could not be completed.");
        }

        TempData["ResendVerificationMessage"] =
            GetGenericResendMessage();

        return RedirectToAction(nameof(ResendVerification));
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

    private void SetEmailVerificationAvailability()
    {
        ViewData["EmailVerificationReady"] =
            _verificationEmailSender.IsConfigured;
    }

    private static string GetGenericResendMessage() =>
        "If an unverified account matches that email, a new verification link will be sent. Please also check your spam folder.";
}
