using System.ComponentModel.DataAnnotations;

namespace MaximoROWeb.Models;

public sealed class RegisterViewModel
{

    [Display(Name = "Website")]
    public string Website { get; set; } = string.Empty;

    [Required]
    [StringLength(23, MinimumLength = 4)]
    [RegularExpression(
        @"^[A-Za-z0-9_]+$",
        ErrorMessage = "Username may contain only letters, numbers, and underscores."
    )]
    [Display(Name = "Username")]
    public string Username { get; set; } = string.Empty;

    [Required]
    [StringLength(23, MinimumLength = 6)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    [Display(Name = "Confirm Password")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Required]
    [StringLength(39)]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^[MF]$", ErrorMessage = "Select a valid sex.")]
    public string Sex { get; set; } = "M";
}