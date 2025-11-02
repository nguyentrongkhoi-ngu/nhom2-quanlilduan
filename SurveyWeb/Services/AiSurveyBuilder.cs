namespace SurveyWeb.Services;

public class AiSurveyBuilder
{
    private static readonly string[] TitlePatterns =
    [
        "{Topic} - Trải nghiệm của {Audience}",
        "Khảo sát {Topic} cho {Audience}",
        "Cải thiện {Topic}: Ý kiến từ {Audience}",
        "{Audience} nghĩ gì về {Topic}?"
    ];

    private static readonly string[] DescriptionPatterns =
    [
        "Khảo sát này giúp chúng tôi hiểu rõ hơn về nhu cầu và trải nghiệm liên quan đến {Topic} của {Audience}. {Goal}",
        "Hãy dành vài phút chia sẻ cảm nhận của bạn về {Topic}. Mục tiêu của chúng tôi là {Goal}.",
        "Chúng tôi muốn lắng nghe từ {Audience} để nâng cao {Topic}. {Goal}",
        "Ý kiến của bạn về {Topic} sẽ giúp chúng tôi {Goal}. Khảo sát chỉ mất vài phút!"
    ];

    private static readonly string[] SingleChoiceQuestions =
    [
        "Bạn đánh giá mức độ hài lòng chung với {Topic} như thế nào?",
        "Yếu tố nào khiến bạn cảm thấy {Topic} hữu ích nhất?",
        "Trong số các tiêu chí dưới đây, yếu tố nào quan trọng nhất khi bạn đánh giá {Topic}?",
        "Bạn sẽ mô tả trải nghiệm tổng thể của mình với {Topic} ra sao?"
    ];

    private static readonly string[][] SingleChoiceAnswerSets =
    [
        ["Rất hài lòng", "Khá hài lòng", "Bình thường", "Chưa hài lòng"],
        ["Chất lượng", "Tốc độ", "Giá trị nhận được", "Khác"],
        ["Rất quan trọng", "Quan trọng", "Trung lập", "Ít quan trọng"],
        ["Xuất sắc", "Tốt", "Trung bình", "Cần cải thiện"]
    ];

    private static readonly string[] MultiChoiceQuestions =
    [
        "Những yếu tố nào dưới đây khiến bạn chọn {Topic}? (Chọn tất cả đáp án phù hợp)",
        "Bạn thường sử dụng {Topic} trong những tình huống nào?",
        "Điều gì khiến bạn giới thiệu {Topic} cho người khác?",
        "Bạn mong muốn {Topic} cải thiện thêm những khía cạnh nào?"
    ];

    private static readonly string[][] MultiChoiceAnswerSets =
    [
        ["Tính năng phù hợp", "Chi phí hợp lý", "Dễ sử dụng", "Dịch vụ hỗ trợ tốt"],
        ["Công việc hàng ngày", "Học tập/nghiên cứu", "Giải trí", "Khác"],
        ["Mang lại hiệu quả", "Đáng tin cậy", "Thương hiệu uy tín", "Đã được giới thiệu"],
        ["Thêm tính năng mới", "Cải thiện hiệu suất", "Hỗ trợ nhanh hơn", "Tài liệu hướng dẫn chi tiết"]
    ];

    private static readonly string[] RatingQuestions =
    [
        "Bạn chấm điểm mức độ hữu ích của {Topic} như thế nào?",
        "Bạn hài lòng với chất lượng tổng thể của {Topic} ra sao?",
        "Bạn đánh giá mức độ dễ sử dụng của {Topic} ở mức nào?",
        "Bạn cảm thấy {Topic} đáp ứng kỳ vọng của mình đến đâu?"
    ];

    private static readonly string[] TextQuestions =
    [
        "Điều bạn thích nhất ở {Topic} là gì?",
        "Nếu có thể thay đổi một điều về {Topic}, bạn sẽ chọn điều gì?",
        "Bạn có góp ý hoặc đề xuất nào để {Topic} tốt hơn không?",
        "Hãy chia sẻ trải nghiệm đáng nhớ nhất của bạn với {Topic}."
    ];

    private static readonly string[] NpsQuestions =
    [
        "Bạn có sẵn sàng giới thiệu {Topic} cho bạn bè hoặc đồng nghiệp không?",
        "Khả năng bạn đề xuất {Topic} cho người thân là bao nhiêu?"
    ];

    public AiSurveyDraft Generate(AiSurveyRequest request)
    {
        var normalizedTopic = string.IsNullOrWhiteSpace(request.Topic)
            ? "sản phẩm/dịch vụ của chúng tôi"
            : request.Topic.Trim();
        var normalizedAudience = string.IsNullOrWhiteSpace(request.Audience)
            ? "khách hàng"
            : request.Audience.Trim();
        var normalizedGoal = string.IsNullOrWhiteSpace(request.Goal)
            ? "Nâng cao chất lượng phục vụ."
            : request.Goal.Trim().TrimEnd('.') + ".";

        var title = Interpolate(TitlePatterns, normalizedTopic, normalizedAudience, normalizedGoal);
        var description = Interpolate(DescriptionPatterns, normalizedTopic, normalizedAudience, normalizedGoal);

        var questions = new List<AiQuestionDraft>();
        var pool = BuildQuestionTypePool(request);

        for (var i = 0; i < request.QuestionCount; i++)
        {
            var type = pool[i % pool.Count];
            questions.Add(type switch
            {
                "single" => BuildSingleChoice(normalizedTopic),
                "multi" => BuildMultiChoice(normalizedTopic),
                "rating" => BuildRating(normalizedTopic),
                "nps" => BuildNps(normalizedTopic),
                _ => BuildText(normalizedTopic)
            });
        }

        return new AiSurveyDraft(title, description, questions);
    }

    private static List<string> BuildQuestionTypePool(AiSurveyRequest request)
    {
        var preferred = request.PreferredQuestionTypes
            ?.Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(IsSupportedType)
            .Distinct()
            .ToList() ?? [];

        if (preferred.Count == 0)
        {
            preferred = new List<string> { "single", "rating", "text" };
        }

        return preferred;
    }

    private static bool IsSupportedType(string type) =>
        type is "single" or "multi" or "rating" or "text" or "nps";

    private static AiQuestionDraft BuildSingleChoice(string topic)
    {
        var template = Pick(SingleChoiceQuestions);
        var answers = Pick(SingleChoiceAnswerSets);
        return new AiQuestionDraft(
            "single",
            ReplacePlaceholders(template, topic),
            true,
            answers.ToList());
    }

    private static AiQuestionDraft BuildMultiChoice(string topic)
    {
        var template = Pick(MultiChoiceQuestions);
        var answers = Pick(MultiChoiceAnswerSets);
        return new AiQuestionDraft(
            "multi",
            ReplacePlaceholders(template, topic),
            true,
            answers.ToList());
    }

    private static AiQuestionDraft BuildRating(string topic)
    {
        var template = Pick(RatingQuestions);
        return new AiQuestionDraft(
            "rating",
            ReplacePlaceholders(template, topic),
            true,
            null,
            1,
            5);
    }

    private static AiQuestionDraft BuildNps(string topic)
    {
        var template = Pick(NpsQuestions);
        return new AiQuestionDraft(
            "nps",
            ReplacePlaceholders(template, topic),
            true,
            null,
            0,
            10);
    }

    private static AiQuestionDraft BuildText(string topic)
    {
        var template = Pick(TextQuestions);
        return new AiQuestionDraft(
            "text",
            ReplacePlaceholders(template, topic),
            false);
    }

    private static string Interpolate(string[] patterns, string topic, string audience, string goal)
    {
        var pattern = Pick(patterns);
        return pattern
            .Replace("{Topic}", topic, StringComparison.OrdinalIgnoreCase)
            .Replace("{Audience}", audience, StringComparison.OrdinalIgnoreCase)
            .Replace("{Goal}", goal, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReplacePlaceholders(string template, string topic) =>
        template.Replace("{Topic}", topic, StringComparison.OrdinalIgnoreCase);

    private static T Pick<T>(IReadOnlyList<T> items)
    {
        if (items.Count == 0) throw new InvalidOperationException("Collection cannot be empty.");
        var index = Random.Shared.Next(items.Count);
        return items[index];
    }

    public record AiSurveyRequest(
        string Topic,
        string Audience,
        string Goal,
        int QuestionCount,
        IReadOnlyList<string> PreferredQuestionTypes);

    public record AiSurveyDraft(
        string Title,
        string Description,
        IReadOnlyList<AiQuestionDraft> Questions);

    public record AiQuestionDraft(
        string Type,
        string Text,
        bool Required,
        IReadOnlyList<string>? Choices = null,
        decimal? MinValue = null,
        decimal? MaxValue = null);
}
