using System;
using System.ComponentModel.DataAnnotations;

namespace SurveyWeb.Models;

public class User
{
    public int UserId { get; set; }

    [Required, MaxLength(255)]
    public string Email { get; set; } = default!;

    [Required, MaxLength(255)]
    public string PasswordHash { get; set; } = default!;

    [MaxLength(100)]
    public string? DisplayName { get; set; }

    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}
