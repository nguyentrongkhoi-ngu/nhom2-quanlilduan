using System;

namespace SurveyWeb.Models;

public class Response
{
    public long ResponseId { get; set; }
    public int SurveyId { get; set; }
    public Guid RespondentId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }

    // computed column ở DB
    public bool IsCompleted { get; set; }
}
