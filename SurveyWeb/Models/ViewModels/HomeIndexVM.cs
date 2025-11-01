namespace SurveyWeb.Models.ViewModels;

public class SurveyListItemVM
{
    public int SurveyId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string TopicName { get; set; } = "";
    public string StatusCode { get; set; } = "draft";
    public string? CoverImagePath { get; set; }
    public DateTime? StartAt { get; set; }
    public DateTime? EndAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsActiveNow { get; set; }
    public int? DaysLeft { get; set; }
}

public class HomeIndexVM
{
    public string? Q { get; set; }
    public int? TopicId { get; set; }
    public string Sort { get; set; } = "new"; // new | endSoon | title
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 9;

    public int TotalItems { get; set; }
    public IEnumerable<SurveyListItemVM> Items { get; set; } = Enumerable.Empty<SurveyListItemVM>();
    public IEnumerable<(int Id, string Name, int Count)> Topics { get; set; } = Enumerable.Empty<(int, string, int)>();
}
