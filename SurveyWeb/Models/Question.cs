using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SurveyWeb.Models;

public class Question
{
    public int QuestionId { get; set; }
    public int SurveyId { get; set; }
    public int OrderIndex { get; set; } = 1;

    [Required]
    public string QuestionText { get; set; } = default!;

    // single | multi | text | rating | nps
    [Required, MaxLength(20)]
    public string QuestionType { get; set; } = default!;

    public bool IsRequired { get; set; } = false;

    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    public int? MaxLength { get; set; }
    [MaxLength(512)]
    public string? ImagePath { get; set; }

    public Survey? Survey { get; set; }
    public List<Choice> Choices { get; set; } = new();
}
