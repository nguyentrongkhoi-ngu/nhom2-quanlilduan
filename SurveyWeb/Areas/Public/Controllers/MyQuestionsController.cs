using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SurveyWeb.Data;
using SurveyWeb.Models;

namespace SurveyWeb.Areas.Public.Controllers;

[Area("Public")]
[Authorize]
[Route("Public/MyQuestions")]
public class MyQuestionsController : Controller
{
    private readonly SurveyDbContext _db;
    public MyQuestionsController(SurveyDbContext db) => _db = db;

    private int? CurrentUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : (int?)null;

    private async Task<ManageQuestionsVM> BuildVmAsync(int surveyId, CreateQuestionVM? createForm = null)
    {
        var survey = await _db.Surveys
            .Include(s => s.Questions.OrderBy(q => q.OrderIndex))
                .ThenInclude(q => q.Choices.OrderBy(c => c.OrderIndex))
            .FirstOrDefaultAsync(s => s.SurveyId == surveyId);
        var vm = new ManageQuestionsVM
        {
            SurveyId = survey.SurveyId,
            SurveyTitle = survey.Title,
            Questions = survey.Questions
                .OrderBy(q => q.OrderIndex)
                .Select(q => new QuestionRow
                {
                    QuestionId = q.QuestionId,
                    OrderIndex = q.OrderIndex,
                    QuestionText = q.QuestionText,
                    QuestionType = q.QuestionType,
                    IsRequired = q.IsRequired,
                    MaxLength = q.MaxLength,
                    MinValue = q.MinValue,
                    MaxValue = q.MaxValue,
                    Choices = q.Choices.OrderBy(c => c.OrderIndex).Select(c => new ChoiceRow
                    {
                        ChoiceId = c.ChoiceId,
                        OrderIndex = c.OrderIndex,
                        ChoiceText = c.ChoiceText
                    }).ToList()
                }).ToList(),
            CreateForm = createForm ?? new CreateQuestionVM { SurveyId = surveyId }
        };
        return vm;
    }

    [HttpGet("{surveyId:int}")]
    public async Task<IActionResult> Index(int surveyId)
    {
        var uid = CurrentUserId; if (uid == null) return RedirectToAction("Login", "Account");
        var survey = await _db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.SurveyId == surveyId);
        if (survey == null) return NotFound();
        if (survey.OwnerUserId != uid.Value) return Forbid();
        var vm = await BuildVmAsync(surveyId);
        return View(vm);
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateQuestionVM model)
    {
        var uid = CurrentUserId; if (uid == null) return RedirectToAction("Login", "Account");
        var survey = await _db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.SurveyId == model.SurveyId);
        if (survey == null) return NotFound();
        if (survey.OwnerUserId != uid.Value) return Forbid();

        NormalizeAndValidate(model);
        if (!ModelState.IsValid)
        {
            var vm = await BuildVmAsync(model.SurveyId, model);
            return View("Index", vm);
        }

        var maxOrder = await _db.Questions
            .Where(x => x.SurveyId == model.SurveyId)
            .Select(x => (int?)x.OrderIndex)
            .MaxAsync();
        var nextOrder = (maxOrder ?? 0) + 1;

        var q = new Question
        {
            SurveyId = model.SurveyId,
            QuestionText = model.QuestionText.Trim(),
            QuestionType = model.QuestionType.Trim().ToLowerInvariant(),
            OrderIndex = nextOrder + 1,
            IsRequired = model.IsRequired,
            MaxLength = model.MaxLength,
            MinValue = model.MinValue,
            MaxValue = model.MaxValue
        };

        if (q.QuestionType is "single" or "multi")
        {
            var choices = (model.ChoicesRaw ?? string.Empty)
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select((text, idx) => new Choice { ChoiceText = text.Trim(), OrderIndex = idx + 1 })
                .ToList();
            q.Choices = choices;
        }

        _db.Questions.Add(q);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã thêm câu hỏi.";
        return RedirectToAction(nameof(Index), new { surveyId = model.SurveyId });
    }

    [HttpPost("Update")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(UpdateQuestionVM model)
    {
        var uid = CurrentUserId; if (uid == null) return RedirectToAction("Login", "Account");
        var q = await _db.Questions.Include(x => x.Survey).Include(x => x.Choices)
            .FirstOrDefaultAsync(x => x.QuestionId == model.QuestionId);
        if (q == null) return NotFound();
        if (q.Survey == null || q.Survey.OwnerUserId != uid.Value) return Forbid();

        NormalizeAndValidate(model);
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Dữ liệu chưa hợp lệ. Kiểm tra lại các trường yêu cầu.";
            return RedirectToAction(nameof(Index), new { surveyId = q.SurveyId });
        }

        q.QuestionText = model.QuestionText.Trim();
        q.QuestionType = model.QuestionType.Trim().ToLowerInvariant();
        // giữ nguyên thứ tự hiện tại
        q.IsRequired = model.IsRequired;
        q.MaxLength = model.MaxLength;
        q.MinValue = model.MinValue;
        q.MaxValue = model.MaxValue;

        // overwrite choices for single/multi
        if (q.QuestionType is "single" or "multi")
        {
            var existing = await _db.Choices.Where(c => c.QuestionId == q.QuestionId).ToListAsync();
            if (existing.Count > 0) _db.Choices.RemoveRange(existing);
            var choices = (model.ChoicesRaw ?? string.Empty)
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select((text, idx) => new Choice { QuestionId = q.QuestionId, ChoiceText = text.Trim(), OrderIndex = idx + 1 })
                .ToList();
            if (choices.Count > 0) _db.Choices.AddRange(choices);
        }
        else
        {
            // non-choice types: remove any existing choices
            var existing = await _db.Choices.Where(c => c.QuestionId == q.QuestionId).ToListAsync();
            if (existing.Count > 0) _db.Choices.RemoveRange(existing);
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã cập nhật câu hỏi.";
        return RedirectToAction(nameof(Index), new { surveyId = q.SurveyId });
    }

    [HttpPost("Delete/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var uid = CurrentUserId; if (uid == null) return RedirectToAction("Login", "Account");
        var q = await _db.Questions.Include(x => x.Survey).FirstOrDefaultAsync(x => x.QuestionId == id);
        if (q == null) return NotFound();
        if (q.Survey == null || q.Survey.OwnerUserId != uid.Value) return Forbid();

        _db.Questions.Remove(q);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã xóa câu hỏi.";
        return RedirectToAction(nameof(Index), new { surveyId = q.SurveyId });
    }

    public class ManageQuestionsVM
    {
        public int SurveyId { get; set; }
        public string SurveyTitle { get; set; } = string.Empty;
        public List<QuestionRow> Questions { get; set; } = new();
        public CreateQuestionVM CreateForm { get; set; } = new();
    }

    public class QuestionRow
    {
        public int QuestionId { get; set; }
        public int OrderIndex { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public string QuestionType { get; set; } = string.Empty;
        public bool IsRequired { get; set; }
        public int? MaxLength { get; set; }
        public decimal? MinValue { get; set; }
        public decimal? MaxValue { get; set; }
        public List<ChoiceRow> Choices { get; set; } = new();
    }

    public class ChoiceRow
    {
        public int ChoiceId { get; set; }
        public int OrderIndex { get; set; }
        public string ChoiceText { get; set; } = string.Empty;
    }

    public class CreateQuestionVM
    {
        [Required]
        public int SurveyId { get; set; }
        [Required, StringLength(2000)]
        public string QuestionText { get; set; } = string.Empty;
        [Required]
        public string QuestionType { get; set; } = "text"; // text | single | multi | rating | nps
        public bool IsRequired { get; set; }
        public int? MaxLength { get; set; }
        public decimal? MinValue { get; set; }
        public decimal? MaxValue { get; set; }
        public string? ChoicesRaw { get; set; }
    }

    public class UpdateQuestionVM : CreateQuestionVM
    {
        [Required]
        public int QuestionId { get; set; }
    }

    private void NormalizeAndValidate(CreateQuestionVM model)
    {
        var t = (model.QuestionType ?? "text").Trim().ToLowerInvariant();
        model.QuestionType = t;
        if (t == "text")
        {
            model.MinValue = null; model.MaxValue = null;
        }
        else if (t == "rating")
        {
            model.MinValue ??= 1; model.MaxValue ??= 5;
            if (model.MinValue >= model.MaxValue)
                ModelState.AddModelError(nameof(model.MaxValue), "Max phải lớn hơn Min");
        }
        else if (t == "nps")
        {
            model.MinValue ??= 0; model.MaxValue ??= 10;
            if (model.MinValue >= model.MaxValue)
                ModelState.AddModelError(nameof(model.MaxValue), "Max phải lớn hơn Min");
        }
        else if (t == "single" || t == "multi")
        {
            if (string.IsNullOrWhiteSpace(model.ChoicesRaw))
                ModelState.AddModelError(nameof(model.ChoicesRaw), "Nhập ít nhất 2 phương án (mỗi dòng 1 phương án)");
            else
            {
                var lines = model.ChoicesRaw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).ToList();
                if (lines.Count < 2)
                    ModelState.AddModelError(nameof(model.ChoicesRaw), "Cần tối thiểu 2 phương án");
            }
            model.MinValue = null; model.MaxValue = null; model.MaxLength = null;
        }
    }
}
