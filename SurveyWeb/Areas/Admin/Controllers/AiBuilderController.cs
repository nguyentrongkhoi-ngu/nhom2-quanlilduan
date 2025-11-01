using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SurveyWeb.Data;
using SurveyWeb.Models;
using SurveyWeb.Services;

namespace SurveyWeb.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
public class AiBuilderController : Controller
{
    private static readonly string[] StatusOptions = ["draft", "active", "closed"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly AiSurveyBuilder _surveyBuilder;
    private readonly SurveyDbContext _db;

    public AiBuilderController(AiSurveyBuilder surveyBuilder, SurveyDbContext db)
    {
        _surveyBuilder = surveyBuilder;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var vm = new BuilderViewModel
        {
            QuestionCount = 5,
            PreferredTypes = new List<string> { "single", "rating", "text" },
            StatusCode = "draft",
            IsAnonymous = true
        };

        await PopulateLookups();
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(BuilderViewModel vm)
    {
        vm.PreferredTypes ??= new List<string>();

        if (!ModelState.IsValid)
        {
            await PopulateLookups();
            return View("Index", vm);
        }

        if (vm.QuestionCount < 1)
        {
            ModelState.AddModelError(nameof(vm.QuestionCount), "Số câu hỏi phải lớn hơn 0.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateLookups();
            return View("Index", vm);
        }

        var request = new AiSurveyBuilder.AiSurveyRequest(
            vm.Topic ?? string.Empty,
            vm.Audience ?? string.Empty,
            vm.Goal ?? string.Empty,
            vm.QuestionCount,
            vm.PreferredTypes);

        var draft = _surveyBuilder.Generate(request);
        vm.Generated = draft;
        vm.GeneratedJson = JsonSerializer.Serialize(draft, JsonOptions);

        await PopulateLookups();
        TempData["Success"] = "Đã tạo bản nháp khảo sát. Kiểm tra nội dung bên dưới trước khi lưu.";
        return View("Index", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CommitDto dto)
    {
        if (!StatusOptions.Contains(dto.StatusCode))
        {
            ModelState.AddModelError(nameof(dto.StatusCode), "Trạng thái không hợp lệ.");
        }

        if (!await _db.Topics.AnyAsync(t => t.TopicId == dto.TopicId))
        {
            ModelState.AddModelError(nameof(dto.TopicId), "Danh mục không tồn tại.");
        }

        AiSurveyBuilder.AiSurveyDraft? draft = null;
        if (!string.IsNullOrWhiteSpace(dto.GeneratedJson))
        {
            try
            {
                draft = JsonSerializer.Deserialize<AiSurveyBuilder.AiSurveyDraft>(dto.GeneratedJson, JsonOptions);
            }
            catch (JsonException)
            {
                ModelState.AddModelError(string.Empty, "Không đọc được dữ liệu khảo sát đã tạo. Vui lòng tạo lại.");
            }
        }
        else
        {
            ModelState.AddModelError(string.Empty, "Thiếu dữ liệu bản nháp khảo sát.");
        }

        if (!ModelState.IsValid || draft == null)
        {
            TempData["Error"] = "Không thể lưu khảo sát từ bản nháp. Vui lòng tạo lại.";
            return RedirectToAction(nameof(Index));
        }

        var status = StatusOptions.Contains(dto.StatusCode) ? dto.StatusCode : "draft";
        var survey = new Survey
        {
            Title = dto.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
            TopicId = dto.TopicId,
            StatusCode = status,
            IsAnonymous = dto.IsAnonymous,
            CreatedAt = DateTime.UtcNow,
            StartAt = null,
            EndAt = null
        };

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(userIdClaim, out var uid))
        {
            survey.OwnerUserId = uid;
        }

        _db.Surveys.Add(survey);
        await _db.SaveChangesAsync();

        var order = 1;
        var questions = new List<Question>();
        foreach (var questionDraft in draft.Questions ?? Array.Empty<AiSurveyBuilder.AiQuestionDraft>())
        {
            var question = new Question
            {
                SurveyId = survey.SurveyId,
                OrderIndex = order++,
                QuestionText = questionDraft.Text,
                QuestionType = NormalizeType(questionDraft.Type),
                IsRequired = questionDraft.Required,
                MinValue = questionDraft.MinValue,
                MaxValue = questionDraft.MaxValue
            };

            var choiceItems = questionDraft.Choices?
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select((choiceText, index) => new Choice
                {
                    OrderIndex = index + 1,
                    ChoiceText = choiceText.Trim()
                })
                .ToList();

            if (choiceItems is { Count: > 0 })
            {
                question.Choices = choiceItems;
            }

            questions.Add(question);
        }

        if (questions.Count > 0)
        {
            _db.Questions.AddRange(questions);
            await _db.SaveChangesAsync();
        }

        TempData["Success"] = $"Đã tạo khảo sát \"{survey.Title}\" với {questions.Count} câu hỏi.";
        return RedirectToAction("Index", "Questions", new { area = "Admin", surveyId = survey.SurveyId });
    }

    private static string NormalizeType(string? type) =>
        type?.Trim().ToLowerInvariant() switch
        {
            "single" => "single",
            "multi" => "multi",
            "rating" => "rating",
            "nps" => "nps",
            _ => "text"
        };

    private async Task PopulateLookups()
    {
        var topics = await _db.Topics.OrderBy(t => t.Name).ToListAsync();
        ViewBag.Topics = new SelectList(topics, "TopicId", "Name");
        ViewBag.StatusOptions = StatusOptions.Select(s => new SelectListItem
        {
            Text = s switch
            {
                "draft" => "Nháp",
                "active" => "Đang mở",
                "closed" => "Đã đóng",
                _ => s.ToUpperInvariant()
            },
            Value = s
        }).ToList();
    }

    public class BuilderViewModel
    {
        [Display(Name = "Chủ đề chính *")]
        [Required(ErrorMessage = "Vui lòng nhập chủ đề.")]
        [StringLength(120)]
        public string Topic { get; set; } = string.Empty;

        [Display(Name = "Đối tượng chính")]
        [StringLength(120)]
        public string? Audience { get; set; }

        [Display(Name = "Mục tiêu khảo sát")]
        [StringLength(240)]
        public string? Goal { get; set; }

        [Display(Name = "Số câu hỏi dự kiến")]
        [Range(1, 20, ErrorMessage = "Số câu hỏi nằm trong khoảng 1 - 20.")]
        public int QuestionCount { get; set; } = 5;

        [Display(Name = "Kiểu câu hỏi ưu tiên")]
        public List<string> PreferredTypes { get; set; } = new();

        [Display(Name = "Danh mục")]
        public int? TopicId { get; set; }

        [Display(Name = "Trạng thái mặc định")]
        public string StatusCode { get; set; } = "draft";

        [Display(Name = "Cho phép trả lời ẩn danh")]
        public bool IsAnonymous { get; set; } = true;

        public AiSurveyBuilder.AiSurveyDraft? Generated { get; set; }
        public string? GeneratedJson { get; set; }
    }

    public class CommitDto
    {
        [Required(ErrorMessage = "Tiêu đề khảo sát bắt buộc.")]
        [StringLength(300)]
        public string Title { get; set; } = string.Empty;

        [StringLength(2000)]
        public string? Description { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn danh mục hợp lệ.")]
        public int TopicId { get; set; }

        [Required]
        public string StatusCode { get; set; } = "draft";

        public bool IsAnonymous { get; set; }

        [Required]
        public string GeneratedJson { get; set; } = string.Empty;
    }
}
