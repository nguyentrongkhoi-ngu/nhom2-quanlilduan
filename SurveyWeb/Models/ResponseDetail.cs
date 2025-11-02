namespace SurveyWeb.Models;

public class ResponseDetail
{
    public long ResponseDetailId { get; set; }
    public long ResponseId { get; set; }
    public int QuestionId { get; set; }
    public int? ChoiceId { get; set; }
    public string? AnswerText { get; set; }
    public decimal? AnswerNumber { get; set; }
}
