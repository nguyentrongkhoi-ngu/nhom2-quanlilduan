using System.ComponentModel.DataAnnotations;

namespace SurveyWeb.Models;

public class Choice
{
    public int ChoiceId { get; set; }
    public int QuestionId { get; set; }
    public int OrderIndex { get; set; } = 1;

    [Required, MaxLength(500)]
    public string ChoiceText { get; set; } = default!;

    public decimal? NumericValue { get; set; }

    public Question? Question { get; set; }
}
