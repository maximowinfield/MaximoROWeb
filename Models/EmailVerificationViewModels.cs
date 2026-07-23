using System.ComponentModel.DataAnnotations;
using MaximoROWeb.Services;

namespace MaximoROWeb.Models;

public sealed class EmailVerificationViewModel
{
    public EmailVerificationOutcome Outcome { get; init; }
}

public sealed class ResendVerificationViewModel
{
    [Display(Name = "Website")]
    public string Website { get; set; } = string.Empty;

    [Required]
    [StringLength(39)]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}
