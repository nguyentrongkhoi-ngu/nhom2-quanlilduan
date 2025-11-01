using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SurveyWeb.Data;
using SurveyWeb.Models;

namespace SurveyWeb.Areas.Public.Controllers;

[Area("Public")]
[Authorize] // chưa đăng nhập sẽ bị chuyển tới /Account/Login?returnUrl=...
public class TakeController : Controller
{
    private readonly SurveyDbContext _db;
    public TakeController(SurveyDbContext db) => _db = db;

    // GET: /Public/Take/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Index(int id)
    {
        var s = await _db.Surveys
            .Include(x => x.Questions.OrderBy(q => q.OrderIndex))
                .ThenInclude(q => q.Choices.OrderBy(c => c.OrderIndex))
            .FirstOrDefaultAsync(x => x.SurveyId == id);

        if (s == null) return NotFound();

        // Kiểm tra thời gian hiệu lực
        var now = DateTime.UtcNow;
        if (s.StatusCode != "active" ||
            (s.StartAt.HasValue && s.StartAt.Value > now) ||
            (s.EndAt.HasValue && s.EndAt.Value < now))
        {
            return BadRequest("Survey không khả dụng.");
        }

        return View(s);
    }

    [HttpGet("Chatbot/{id:int}")]
    public async Task<IActionResult> Chatbot(int id)
    {
        var survey = await _db.Surveys
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.SurveyId == id);

        if (survey == null) return NotFound();

        var now = DateTime.UtcNow;
        if (survey.StatusCode != "active" ||
            (survey.StartAt.HasValue && survey.StartAt.Value > now) ||
            (survey.EndAt.HasValue && survey.EndAt.Value < now))
        {
            return BadRequest("Survey không khả dụng.");
        }

        var vm = new ChatbotViewModel
        {
            SurveyId = survey.SurveyId,
            SurveyTitle = string.IsNullOrWhiteSpace(survey.Title) ? "Khảo sát" : survey.Title.Trim()
        };

        return View(vm);
    }

    // POST: /Public/Take/{id}
    [HttpPost("{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(int id, IFormCollection form)
    {
        var survey = await _db.Surveys
            .Include(x => x.Questions)
                .ThenInclude(q => q.Choices)
            .FirstOrDefaultAsync(x => x.SurveyId == id);

        if (survey == null) return NotFound();

        // Tạo respondent (ẩn danh)
        var respondent = new Respondent
        {
            RespondentId = Guid.NewGuid(),
            Email = null,
            CreatedAt = DateTime.UtcNow
        };
        _db.Respondents.Add(respondent);
        await _db.SaveChangesAsync();

        // Tạo response
        var response = new Response
        {
            SurveyId = survey.SurveyId,
            RespondentId = respondent.RespondentId,
            StartedAt = DateTime.UtcNow
        };
        _db.Responses.Add(response);
        await _db.SaveChangesAsync(); // có ResponseId

        var details = new List<ResponseDetail>();

        foreach (var q in survey.Questions.OrderBy(x => x.OrderIndex))
        {
            var keySingle = $"q_{q.QuestionId}";
            if (q.QuestionType is "text" or "rating" or "nps" or "single")
            {
                var val = form[keySingle].ToString();
                if (string.IsNullOrWhiteSpace(val) && q.IsRequired)
                    return BadRequest($"Thiếu trả lời cho câu {q.OrderIndex}");

                var rd = new ResponseDetail
                {
                    ResponseId = response.ResponseId,
                    QuestionId = q.QuestionId
                };

                if (q.QuestionType == "text")
                {
                    rd.AnswerText = string.IsNullOrWhiteSpace(val) ? null : val;
                }
                else if (q.QuestionType is "rating" or "nps")
                {
                    if (!string.IsNullOrWhiteSpace(val) && decimal.TryParse(val, out var num))
                        rd.AnswerNumber = num;
                }
                else if (q.QuestionType == "single")
                {
                    if (int.TryParse(val, out var choiceId))
                        rd.ChoiceId = choiceId;
                }

                details.Add(rd);
            }
            else if (q.QuestionType == "multi")
            {
                foreach (var c in q.Choices)
                {
                    var ck = $"q_{q.QuestionId}_opt_{c.ChoiceId}";
                    if (form.ContainsKey(ck))
                    {
                        details.Add(new ResponseDetail
                        {
                            ResponseId = response.ResponseId,
                            QuestionId = q.QuestionId,
                            ChoiceId = c.ChoiceId
                        });
                    }
                }

                if (q.IsRequired && !details.Any(d => d.QuestionId == q.QuestionId))
                    return BadRequest($"Thiếu chọn cho câu {q.OrderIndex}");
            }
        }

        _db.ResponseDetails.AddRange(details);
        response.SubmittedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return RedirectToAction("Thanks", new { id = response.ResponseId });
    }

    public IActionResult Thanks(long id) => View(model: id);

    public class ChatbotViewModel
    {
        public int SurveyId { get; set; }
        public string SurveyTitle { get; set; } = string.Empty;
    }
}

