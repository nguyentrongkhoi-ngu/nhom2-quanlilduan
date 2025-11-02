using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SurveyWeb.Data;
using SurveyWeb.Models;

namespace SurveyWeb.Services;

public class SurveyChatbotEngine
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(45);
    private const string CachePrefix = "chatbot:";

    private static readonly string[] HelpKeywords = { "help", "gợi ý", "goi y", "hint", "giải thích", "giai thich", "hướng dẫn", "huong dan" };
    private static readonly string[] RepeatKeywords = { "repeat", "nhắc lại", "nhac lai", "again", "lặp lại", "lap lai" };
    private static readonly string[] SkipKeywords = { "skip", "bỏ qua", "bo qua", "next", "sang câu khác" };
    private static readonly string[] CancelKeywords = { "stop", "cancel", "thoát", "thoat", "quit", "kết thúc", "ket thuc" };
    private static readonly string[] SuggestKeywords = { "goi y", "suggest", "goi y tra loi", "goi y cau tra loi" };

    private readonly IMemoryCache _cache;
    private readonly SurveyDbContext _db;

    public SurveyChatbotEngine(IMemoryCache cache, SurveyDbContext db)
    {
        _cache = cache;
        _db = db;
    }

    public async Task<ChatbotStartResult?> StartAsync(int surveyId, string participantName)
    {
        var survey = await _db.Surveys
            .AsNoTracking()
            .Include(s => s.Questions.OrderBy(q => q.OrderIndex))
                .ThenInclude(q => q.Choices.OrderBy(c => c.OrderIndex))
            .FirstOrDefaultAsync(s => s.SurveyId == surveyId);

        if (survey == null) return null;
        if (!IsSurveyAvailable(survey)) return null;

        var questionDtos = survey.Questions
            .OrderBy(q => q.OrderIndex)
            .Select(q => new ChatQuestion
            {
                QuestionId = q.QuestionId,
                OrderIndex = q.OrderIndex,
                QuestionType = (q.QuestionType ?? "text").Trim().ToLowerInvariant(),
                QuestionText = q.QuestionText ?? string.Empty,
                IsRequired = q.IsRequired,
                MinValue = q.MinValue,
                MaxValue = q.MaxValue,
                Choices = q.Choices
                    .OrderBy(c => c.OrderIndex)
                    .Select(c => new ChatChoice
                    {
                        ChoiceId = c.ChoiceId,
                        OrderIndex = c.OrderIndex,
                        ChoiceText = c.ChoiceText ?? string.Empty
                    })
                    .ToList()
            })
            .ToList();

        if (questionDtos.Count == 0) return null;

        var session = new ChatSession
        {
            ConversationId = Guid.NewGuid(),
            SurveyId = survey.SurveyId,
            SurveyTitle = survey.Title ?? "Khảo sát",
            Questions = questionDtos
        };

        var cacheKey = BuildCacheKey(session.ConversationId);
        _cache.Set(cacheKey, session, new MemoryCacheEntryOptions
        {
            SlidingExpiration = SessionLifetime
        });

        var greeting = BuildGreeting(participantName, session.SurveyTitle);
        var firstPrompt = BuildQuestionPrompt(session.Questions[0]);

        return new ChatbotStartResult
        {
            ConversationId = session.ConversationId,
            SurveyTitle = session.SurveyTitle,
            Messages =
            [
                new ChatMessage("bot", greeting),
                new ChatMessage("bot", firstPrompt)
            ]
        };
    }

    public async Task<ChatbotReply> SendAsync(Guid conversationId, string input, string participantName)
    {
        var cacheKey = BuildCacheKey(conversationId);
        if (!_cache.TryGetValue(cacheKey, out ChatSession? session) || session == null)
        {
            return new ChatbotReply
            {
                SessionExpired = true,
                Messages =
                [
                    new ChatMessage("bot", "Phiên trò chuyện đã kết thúc hoặc không tồn tại. Vui lòng bắt đầu lại khảo sát.")
                ]
            };
        }

        var reply = new ChatbotReply();

        if (string.IsNullOrWhiteSpace(input))
        {
            reply.Messages.Add(new ChatMessage("bot", "Xin vui lòng nhập câu trả lời hoặc gõ \"gợi ý\" để được hỗ trợ."));
            return reply;
        }

        var normalized = input.Trim().ToLowerInvariant();

        if (ContainsKeyword(normalized, CancelKeywords))
        {
            _cache.Remove(cacheKey);
            reply.SessionExpired = true;
            reply.Messages.Add(new ChatMessage("bot", "Đã kết thúc phiên trò chuyện theo yêu cầu. Khi muốn tham gia lại, hãy bắt đầu khảo sát một lần nữa."));
            return reply;
        }

        if (ContainsKeyword(normalized, SuggestKeywords))
        {
            var currentQuestion = session.Questions[Math.Min(Math.Max(session.CurrentIndex, 0), session.Questions.Count - 1)];
            var suggestion = BuildSuggestionMessage(currentQuestion);
            reply.Messages.Add(new ChatMessage("bot", suggestion));
            return reply;
        }

        if (session.Completed)
        {
            reply.Completed = true;
            reply.Messages.Add(new ChatMessage("bot", "Chúng ta đã hoàn tất khảo sát trước đó. Cảm ơn đã trò chuyện!"));
            return reply;
        }

        if (session.CurrentIndex >= session.Questions.Count)
        {
            reply.Completed = true;
            reply.Messages.Add(new ChatMessage("bot", "Cảm ơn bạn, khảo sát này đã được hoàn thành."));
            return reply;
        }

        var question = session.Questions[session.CurrentIndex];

        if (ContainsKeyword(normalized, HelpKeywords))
        {
            reply.Messages.Add(new ChatMessage("bot", BuildHelpMessage(question)));
            return reply;
        }

        if (ContainsKeyword(normalized, RepeatKeywords))
        {
            reply.Messages.Add(new ChatMessage("bot", BuildQuestionPrompt(question)));
            return reply;
        }

        if (ContainsKeyword(normalized, SkipKeywords))
        {
            if (question.IsRequired)
            {
                reply.Messages.Add(new ChatMessage("bot", "Câu hỏi này là bắt buộc. Bạn hãy cho mình câu trả lời phù hợp nhé."));
                return reply;
            }

            session.CurrentIndex++;
            reply.Messages.Add(new ChatMessage("bot", "Đã bỏ qua câu hỏi này."));
            if (session.CurrentIndex < session.Questions.Count)
            {
                reply.Messages.Add(new ChatMessage("bot", BuildQuestionPrompt(session.Questions[session.CurrentIndex])));
            }
            else
            {
                await PersistResponsesAsync(session);
                _cache.Remove(cacheKey);
                session.Completed = true;
                reply.Completed = true;
                reply.Messages.Add(new ChatMessage("bot", BuildCompletionMessage(participantName)));
            }
            return reply;
        }

        var parseResult = ParseAnswer(question, input);
        if (!parseResult.IsValid)
        {
            reply.Messages.Add(new ChatMessage("bot", parseResult.ErrorMessage ?? "Mình chưa hiểu câu trả lời, bạn thử diễn đạt lại nhé."));
            return reply;
        }

        session.Answers[question.QuestionId] = parseResult.Answer!;
        session.CurrentIndex++;
        reply.Messages.Add(new ChatMessage("bot", parseResult.Acknowledgement ?? "Đã ghi nhận câu trả lời."));

        if (session.CurrentIndex < session.Questions.Count)
        {
            reply.Messages.Add(new ChatMessage("bot", BuildQuestionPrompt(session.Questions[session.CurrentIndex])));
            return reply;
        }

        await PersistResponsesAsync(session);
        _cache.Remove(cacheKey);
        session.Completed = true;
        reply.Completed = true;
        reply.Messages.Add(new ChatMessage("bot", BuildCompletionMessage(participantName)));
        return reply;
    }

    private static bool IsSurveyAvailable(Survey survey)
    {
        if (!string.Equals(survey.StatusCode, "active", StringComparison.OrdinalIgnoreCase))
            return false;

        var now = DateTime.UtcNow;
        if (survey.StartAt.HasValue && survey.StartAt.Value > now)
            return false;
        if (survey.EndAt.HasValue && survey.EndAt.Value < now)
            return false;
        if (survey.Questions == null || survey.Questions.Count == 0)
            return false;
        return true;
    }

    private static string BuildQuestionPrompt(ChatQuestion question)
    {
        var sb = new StringBuilder();
        sb.Append("Câu ").Append(question.OrderIndex).Append(": ").Append(question.QuestionText);

        if (question.Choices.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Bạn có thể chọn một trong các phương án sau:");
            foreach (var choice in question.Choices)
            {
                sb.Append("  ").Append(choice.OrderIndex).Append(". ").Append(choice.ChoiceText).AppendLine();
            }
            if (question.QuestionType == "multi")
            {
                sb.Append("Bạn có thể chọn nhiều đáp án, ví dụ: 1,3");
            }
            else
            {
                sb.Append("Trả lời bằng số thứ tự hoặc nội dung của phương án.");
            }
        }
        else if (question.QuestionType is "rating" or "nps")
        {
            var min = question.MinValue ?? 1;
            var max = question.MaxValue ?? 5;
            sb.AppendLine();
            sb.Append("Hãy nhập một số từ ").Append(min).Append(" đến ").Append(max).Append(".");
        }
        else
        {
            sb.AppendLine();
            sb.Append("Bạn có thể trả lời bằng văn bản.");
        }

        sb.AppendLine();
        sb.Append("Nếu cần trợ giúp, hãy gõ \"gợi ý\". Để nghe lại câu hỏi, gõ \"nhắc lại\".");
        return sb.ToString().TrimEnd();
    }

    private static string BuildHelpMessage(ChatQuestion question)
    {
        if (question.Choices.Count > 0)
        {
            var choiceText = string.Join(", ", question.Choices.Select(c => $"{c.OrderIndex}. {c.ChoiceText}"));
            if (question.QuestionType == "multi")
            {
                return $"Câu này cho phép chọn nhiều đáp án. Bạn có thể nhập số thứ tự hoặc tên đáp án, ví dụ: 1,3. Danh sách: {choiceText}.";
            }
            return $"Hãy chọn một đáp án phù hợp. Bạn có thể nhập số thứ tự hoặc tên đáp án. Danh sách: {choiceText}.";
        }

        if (question.QuestionType is "rating" or "nps")
        {
            var min = question.MinValue ?? 1;
            var max = question.MaxValue ?? 5;
            return $"Đây là câu thang điểm. Nhập một con số từ {min} đến {max} để thể hiện mức độ của bạn.";
        }

        return "Bạn có thể trả lời một cách tự do bằng văn bản. Chia sẻ cảm nhận của bạn nhé!";
    }

    private static string BuildGreeting(string participantName, string surveyTitle)
    {
        if (string.IsNullOrWhiteSpace(participantName))
            return $"Xin chào! Mình là trợ lý chatbot của khảo sát \"{surveyTitle}\". Chúng ta cùng bắt đầu nhé?";
        return $"Xin chào {participantName}! Mình là trợ lý chatbot của khảo sát \"{surveyTitle}\". Mình sẽ lần lượt gửi câu hỏi, bạn cứ trả lời thoải mái nhé.";
    }

    private static string BuildCompletionMessage(string participantName)
    {
        if (string.IsNullOrWhiteSpace(participantName))
            return "Cảm ơn bạn đã hoàn thành khảo sát. Những chia sẻ của bạn rất quý giá!";
        return $"Cảm ơn {participantName} đã hoàn thành khảo sát. Những chia sẻ của bạn rất quý giá!";
    }

    private static bool ContainsKeyword(string normalizedMessage, IEnumerable<string> keywords) =>
        keywords.Any(k => normalizedMessage.Contains(k, StringComparison.Ordinal));

    private static ParseResult ParseAnswer(ChatQuestion question, string input)
    {
        var trimmed = input.Trim();
        var lower = trimmed.ToLowerInvariant();

        if (question.Choices.Count > 0)
        {
            var numbers = ExtractNumbers(trimmed);
            if (question.QuestionType == "single")
            {
                var choice = ResolveSingleChoice(question, numbers, trimmed);
                if (choice == null)
                {
                    return ParseResult.Invalid("Mình chưa nhận ra lựa chọn của bạn. Hãy nhập số thứ tự hoặc tên đáp án nhé.");
                }

                var singleAnswer = new ChatAnswer
                {
                    QuestionId = question.QuestionId,
                    QuestionType = question.QuestionType,
                    ChoiceIds = new List<int> { choice.ChoiceId }
                };

                return ParseResult.Valid(singleAnswer, $"Đã chọn: {choice.ChoiceText}.");
            }

            var choices = ResolveMultipleChoices(question, numbers, trimmed);
            if (choices.Count == 0)
            {
                return ParseResult.Invalid("Mình chưa nhận ra các lựa chọn. Bạn có thể nhập dạng \"1,3\" hoặc tên các đáp án.");
            }

            var multiAnswer = new ChatAnswer
            {
                QuestionId = question.QuestionId,
                QuestionType = question.QuestionType,
                ChoiceIds = choices.Select(c => c.ChoiceId).ToList()
            };

            var listText = string.Join(", ", choices.Select(c => c.ChoiceText));
            return ParseResult.Valid(multiAnswer, $"Đã ghi nhận các lựa chọn: {listText}.");
        }

        if (question.QuestionType is "rating" or "nps")
        {
            if (!decimal.TryParse(trimmed.Replace(',', '.'), out var number))
            {
                return ParseResult.Invalid("Vui lòng nhập một con số hợp lệ.");
            }

            var min = question.MinValue ?? (question.QuestionType == "nps" ? 0 : 1);
            var max = question.MaxValue ?? (question.QuestionType == "nps" ? 10 : 5);
            if (number < min || number > max)
            {
                return ParseResult.Invalid($"Giá trị hợp lệ nằm trong khoảng {min} - {max}.");
            }

            var answer = new ChatAnswer
            {
                QuestionId = question.QuestionId,
                QuestionType = question.QuestionType,
                NumberValue = number
            };

            return ParseResult.Valid(answer, $"Bạn chấm mức {number}. Đã ghi nhận!");
        }

        var textAnswer = trimmed;
        if (string.IsNullOrWhiteSpace(textAnswer))
        {
            return ParseResult.Invalid("Bạn có thể chia sẻ thêm vài dòng để mình ghi nhận nhé.");
        }

        var text = textAnswer.Length > 1000 ? textAnswer[..1000] : textAnswer;
        var freeAnswer = new ChatAnswer
        {
            QuestionId = question.QuestionId,
            QuestionType = question.QuestionType,
            TextValue = text
        };

        return ParseResult.Valid(freeAnswer, "Cảm ơn bạn đã chia sẻ!");
    }

    private static ChatChoice? ResolveSingleChoice(ChatQuestion question, List<int> numbers, string originalInput)
    {
        if (numbers.Count > 0)
        {
            var idx = numbers[0];
            var matchByIndex = question.Choices.FirstOrDefault(c => c.OrderIndex == idx);
            if (matchByIndex != null) return matchByIndex;
        }

        var trimmed = originalInput.Trim();
        return question.Choices.FirstOrDefault(c =>
            string.Equals(c.ChoiceText.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private static List<ChatChoice> ResolveMultipleChoices(ChatQuestion question, List<int> numbers, string originalInput)
    {
        var selections = new List<ChatChoice>();
        foreach (var number in numbers)
        {
            var choice = question.Choices.FirstOrDefault(c => c.OrderIndex == number);
            if (choice != null && selections.All(s => s.ChoiceId != choice.ChoiceId))
            {
                selections.Add(choice);
            }
        }

        if (selections.Count > 0)
            return selections;

        var tokens = originalInput.Split(new[] { ',', ';', '/', '|' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            var trimmed = token.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            var choice = question.Choices.FirstOrDefault(c =>
                string.Equals(c.ChoiceText.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
            if (choice != null && selections.All(s => s.ChoiceId != choice.ChoiceId))
            {
                selections.Add(choice);
            }
        }

        return selections;
    }

    private static List<int> ExtractNumbers(string text)
    {
        var matches = Regex.Matches(text, @"\d+");
        return matches
            .Select(m => int.TryParse(m.Value, out var value) ? value : (int?)null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .Distinct()
            .ToList();
    }

    private async Task PersistResponsesAsync(ChatSession session)
    {
        var respondent = new Respondent
        {
            RespondentId = Guid.NewGuid(),
            Email = null,
            CreatedAt = DateTime.UtcNow
        };
        _db.Respondents.Add(respondent);
        await _db.SaveChangesAsync();

        var response = new Response
        {
            SurveyId = session.SurveyId,
            RespondentId = respondent.RespondentId,
            StartedAt = session.StartedAt,
            SubmittedAt = DateTime.UtcNow
        };
        _db.Responses.Add(response);
        await _db.SaveChangesAsync();

        var details = new List<ResponseDetail>();
        foreach (var question in session.Questions)
        {
            if (!session.Answers.TryGetValue(question.QuestionId, out var answer))
                continue;

            switch (question.QuestionType)
            {
                case "single":
                case "multi":
                    if (answer.ChoiceIds != null)
                    {
                        foreach (var choiceId in answer.ChoiceIds)
                        {
                            details.Add(new ResponseDetail
                            {
                                ResponseId = response.ResponseId,
                                QuestionId = question.QuestionId,
                                ChoiceId = choiceId
                            });
                        }
                    }
                    break;
                case "rating":
                case "nps":
                    details.Add(new ResponseDetail
                    {
                        ResponseId = response.ResponseId,
                        QuestionId = question.QuestionId,
                        AnswerNumber = answer.NumberValue
                    });
                    break;
                default:
                    details.Add(new ResponseDetail
                    {
                        ResponseId = response.ResponseId,
                        QuestionId = question.QuestionId,
                        AnswerText = answer.TextValue
                    });
                    break;
            }
        }

        if (details.Count > 0)
        {
            _db.ResponseDetails.AddRange(details);
        }

        await _db.SaveChangesAsync();
    }

    private static string BuildSuggestionMessage(ChatQuestion q)
    {
        var title = !string.IsNullOrWhiteSpace(q.QuestionText) ? q.QuestionText.Trim() : "câu hỏi";

        switch (q.QuestionType)
        {
            case "single":
            {
                var top = q.Choices.OrderBy(c => c.OrderIndex).FirstOrDefault();
                if (top != null)
                {
                    var idx = top.OrderIndex;
                    return $"Gợi ý cho '{title}': Chọn phương án số {idx} - \"{top.ChoiceText}\".\nBạn có thể gõ: '{idx}' hoặc gõ đúng nội dung phương án.";
                }
                return $"Gợi ý cho '{title}': Chọn phương án phù hợp nhất với bạn.";
            }
            case "multi":
            {
                var picks = q.Choices.OrderBy(c => c.OrderIndex).Take(2).ToList();
                if (picks.Count > 0)
                {
                    var nums = string.Join(", ", picks.Select(p => p.OrderIndex));
                    var labels = string.Join(", ", picks.Select(p => p.ChoiceText));
                    return $"Gợi ý cho '{title}': Chọn {nums} ({labels}).\nGõ: '{nums}' hoặc gõ tên phương án, phân cách bởi dấu phẩy.";
                }
                return $"Gợi ý cho '{title}': Chọn 1–3 phương án phù hợp.";
            }
            case "rating":
            case "nps":
            {
                var min = q.MinValue ?? 0;
                var max = q.MaxValue ?? (q.QuestionType == "nps" ? 10 : 5);
                var mid = Math.Round((min + max) / 2m, 0);
                var label = q.QuestionType == "nps" ? "mức (0–10)" : $"thang điểm ({min}–{max})";
                return $"Gợi ý cho '{title}': {mid} trên {label}.\nGõ: '{mid}'.";
            }
            default:
            {
                var example = "Tôi hài lòng với trải nghiệm hiện tại, nhưng mong muốn cải thiện tốc độ phản hồi và tài liệu hướng dẫn.";
                return $"Gợi ý cho '{title}':\n• Ví dụ: \"{example}\"\nBạn có thể sửa lại để phù hợp.";
            }
        }
    }

    private static string BuildCacheKey(Guid conversationId) => $"{CachePrefix}{conversationId:N}";

    private class ChatSession
    {
        public Guid ConversationId { get; init; }
        public int SurveyId { get; init; }
        public string SurveyTitle { get; init; } = "";
        public List<ChatQuestion> Questions { get; init; } = new();
        public Dictionary<int, ChatAnswer> Answers { get; } = new();
        public int CurrentIndex { get; set; }
        public DateTime StartedAt { get; } = DateTime.UtcNow;
        public bool Completed { get; set; }
    }

    private class ChatQuestion
    {
        public int QuestionId { get; init; }
        public int OrderIndex { get; init; }
        public string QuestionType { get; init; } = "text";
        public string QuestionText { get; init; } = "";
        public bool IsRequired { get; init; }
        public decimal? MinValue { get; init; }
        public decimal? MaxValue { get; init; }
        public List<ChatChoice> Choices { get; init; } = new();
    }

    private class ChatChoice
    {
        public int ChoiceId { get; init; }
        public int OrderIndex { get; init; }
        public string ChoiceText { get; init; } = "";
    }

    private class ChatAnswer
    {
        public int QuestionId { get; init; }
        public string QuestionType { get; init; } = "text";
        public List<int>? ChoiceIds { get; set; }
        public string? TextValue { get; set; }
        public decimal? NumberValue { get; set; }
    }

    private record ParseResult(bool IsValid, ChatAnswer? Answer, string? ErrorMessage, string? Acknowledgement)
    {
        public static ParseResult Invalid(string error) => new(false, null, error, null);
        public static ParseResult Valid(ChatAnswer answer, string acknowledgement) => new(true, answer, null, acknowledgement);
    }

    public class ChatMessage
    {
        public ChatMessage() { }

        public ChatMessage(string sender, string text)
        {
            Sender = sender;
            Text = text;
        }

        public string Sender { get; set; } = "bot";
        public string Text { get; set; } = "";
    }

    public class ChatbotStartResult
    {
        public Guid ConversationId { get; set; }
        public string SurveyTitle { get; set; } = "";
        public List<ChatMessage> Messages { get; set; } = new();
    }

    public class ChatbotReply
    {
        public bool SessionExpired { get; set; }
        public bool Completed { get; set; }
        public List<ChatMessage> Messages { get; set; } = new();
    }
}
