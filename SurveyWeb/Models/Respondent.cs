using System;

namespace SurveyWeb.Models;

public class Respondent
{
    public Guid RespondentId { get; set; } // default NEWID() ở DB
    public int? UserId { get; set; }
    public string? Email { get; set; }
    public DateTime CreatedAt { get; set; }
}
