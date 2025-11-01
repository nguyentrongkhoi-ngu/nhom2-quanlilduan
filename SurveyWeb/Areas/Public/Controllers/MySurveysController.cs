using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SurveyWeb.Data;
using SurveyWeb.Models;

namespace SurveyWeb.Areas.Public.Controllers;

[Area("Public")]
[Authorize]
public class MySurveysController : Controller
{
    private readonly SurveyDbContext _db;
    private readonly IWebHostEnvironment _env;

    private const long MaxCoverSizeBytes = 2 * 1024 * 1024;
    private const string CoverUploadFolder = "uploads/covers";
    private static readonly string[] StatusOptions = new[] { "draft", "active", "closed" };

    public MySurveysController(SurveyDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null) return RedirectToAction("Login", "Account");

        var vm = new MySurveysViewModel
        {
            Surveys = await _db.Surveys
                .AsNoTracking()
                .Where(s => s.OwnerUserId == currentUserId.Value)
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => new SurveyRow
                {
                    SurveyId = s.SurveyId,
                    Title = s.Title,
                    StatusCode = s.StatusCode,
                    CreatedAt = s.CreatedAt,
                    StartAt = s.StartAt,
                    EndAt = s.EndAt,
                    TopicName = s.Topic != null ? s.Topic.Name : null,
                    QuestionCount = s.Questions.Count
                })
                .ToListAsync()
        };

        await PopulateFormLookups();
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null) return RedirectToAction("Login", "Account");
        await PopulateFormLookups();
        return View(new MySurveyCreateModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(MySurveyCreateModel model, IFormFile? coverImage, bool returnToCreatePage = false)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null) return RedirectToAction("Login", "Account");

        ValidateCoverImage(coverImage);

        if (!ModelState.IsValid)
        {
            await PopulateFormLookups();
            if (returnToCreatePage)
            {
                return View("Create", model);
            }
            var vm = await BuildViewModelWithErrors(currentUserId.Value);
            vm.CreateForm = model;
            return View("Index", vm);
        }

        if (model.StartAt.HasValue && model.EndAt.HasValue && model.EndAt < model.StartAt)
        {
            ModelState.AddModelError(nameof(model.EndAt), "Ngày kết thúc phải sau ngày bắt đầu.");
        }

        if (!await _db.Topics.AnyAsync(t => t.TopicId == model.TopicId))
        {
            ModelState.AddModelError(nameof(model.TopicId), "Danh mục không tồn tại.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateFormLookups();
            if (returnToCreatePage)
            {
                return View("Create", model);
            }
            var vm = await BuildViewModelWithErrors(currentUserId.Value);
            vm.CreateForm = model;
            return View("Index", vm);
        }

        var survey = new Survey
        {
            Title = model.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim(),
            TopicId = model.TopicId,
            StatusCode = model.StatusCode,
            OwnerUserId = currentUserId.Value,
            IsAnonymous = model.IsAnonymous,
            CreatedAt = DateTime.UtcNow,
            StartAt = model.StartAt,
            EndAt = model.EndAt
        };

        if (coverImage != null && coverImage.Length > 0)
        {
            survey.CoverImagePath = await SaveCoverImageAsync(coverImage);
        }

        _db.Surveys.Add(survey);
        await _db.SaveChangesAsync();

        TempData["Success"] = "Đã tạo khảo sát mới. Bạn có thể tiếp tục thêm câu hỏi trong khu vực quản lý.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null) return RedirectToAction("Login", "Account");

        var s = await _db.Surveys.FirstOrDefaultAsync(x => x.SurveyId == id);
        if (s == null) return NotFound();
        if (s.OwnerUserId != currentUserId.Value) return Forbid();

        await PopulateFormLookups();
        return View(s);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Survey model, IFormFile? coverImage, bool removeCover = false)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null) return RedirectToAction("Login", "Account");
        if (id != model.SurveyId) return BadRequest();

        var existing = await _db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.SurveyId == id);
        if (existing == null) return NotFound();
        if (existing.OwnerUserId != currentUserId.Value) return Forbid();

        if (!StatusOptions.Contains(model.StatusCode ?? string.Empty))
            ModelState.AddModelError(nameof(model.StatusCode), "Invalid status");

        ValidateCoverImage(coverImage);

        if (!ModelState.IsValid)
        {
            model.CoverImagePath = existing.CoverImagePath;
            await PopulateFormLookups();
            return View(model);
        }

        model.OwnerUserId = existing.OwnerUserId;
        string? coverPath = existing.CoverImagePath;

        if (coverImage != null && coverImage.Length > 0)
        {
            coverPath = await SaveCoverImageAsync(coverImage);
            DeleteCoverImage(existing.CoverImagePath);
        }
        else if (removeCover)
        {
            DeleteCoverImage(existing.CoverImagePath);
            coverPath = null;
        }

        model.CoverImagePath = coverPath;

        _db.Entry(model).Property(x => x.CreatedAt).IsModified = false;
        _db.Update(model);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Cập nhật khảo sát thành công";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null) return RedirectToAction("Login", "Account");

        var s = await _db.Surveys.FindAsync(id);
        if (s == null) return NotFound();
        if (s.OwnerUserId != currentUserId.Value) return Forbid();

        var hasResponses = await _db.Responses.AnyAsync(r => r.SurveyId == id);
        if (hasResponses)
        {
            TempData["Error"] = "Không thể xóa: Khảo sát đã có phản hồi.";
            return RedirectToAction(nameof(Index));
        }

        DeleteCoverImage(s.CoverImagePath);
        _db.Surveys.Remove(s);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã xóa khảo sát";
        return RedirectToAction(nameof(Index));
    }

    private void ValidateCoverImage(IFormFile? file)
    {
        if (file == null || file.Length == 0) return;
        if (!file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError("CoverImagePath", "Vui lòng chọn đúng định dạng ảnh.");
            return;
        }
        if (file.Length > MaxCoverSizeBytes)
        {
            ModelState.AddModelError("CoverImagePath", "Ảnh bìa tối đa 2MB.");
        }
    }

    private async Task<string?> SaveCoverImageAsync(IFormFile file)
    {
        var uploadsRoot = Path.Combine(_env.WebRootPath, CoverUploadFolder.Replace("/", Path.DirectorySeparatorChar.ToString()));
        Directory.CreateDirectory(uploadsRoot);

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension)) extension = ".jpg";
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var destination = Path.Combine(uploadsRoot, fileName);
        using (var stream = System.IO.File.Create(destination))
        {
            await file.CopyToAsync(stream);
        }
        return Path.Combine(CoverUploadFolder, fileName).Replace("\\", "/");
    }

    private void DeleteCoverImage(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return;
        var fullPath = Path.Combine(_env.WebRootPath, relativePath.Replace("/", Path.DirectorySeparatorChar.ToString()));
        if (System.IO.File.Exists(fullPath))
        {
            System.IO.File.Delete(fullPath);
        }
    }

    // GET: /Public/MySurveys/Stats/{id}
    [HttpGet]
    public async Task<IActionResult> Stats(int id)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null) return RedirectToAction("Login", "Account");

        var survey = await _db.Surveys
            .Include(s => s.Topic)
            .Include(s => s.Questions.OrderBy(q => q.OrderIndex))
                .ThenInclude(q => q.Choices.OrderBy(c => c.OrderIndex))
            .FirstOrDefaultAsync(s => s.SurveyId == id);

        if (survey == null) return NotFound();
        if (survey.OwnerUserId != currentUserId.Value) return Forbid();

        var responses = await _db.Responses
            .Where(r => r.SurveyId == id)
            .OrderBy(r => r.StartedAt)
            .ToListAsync();

        var totalResponses = responses.Count;
        var completedResponses = responses.Count(r => r.SubmittedAt != null);
        var inProgressResponses = totalResponses - completedResponses;
        var distinctRespondents = responses.Select(r => r.RespondentId).Distinct().Count();

        var responseIds = responses.Select(r => r.ResponseId).ToList();
        var details = responseIds.Count == 0
            ? new List<ResponseDetail>()
            : await _db.ResponseDetails
                .Where(rd => responseIds.Contains(rd.ResponseId))
                .ToListAsync();

        TimeSpan? avgCompletion = null;
        var completionDurations = responses
            .Where(r => r.SubmittedAt != null)
            .Select(r => r.SubmittedAt!.Value - r.StartedAt)
            .Where(ts => ts.TotalSeconds >= 0)
            .ToList();
        if (completionDurations.Count > 0)
        {
            avgCompletion = TimeSpan.FromSeconds(completionDurations.Average(ts => ts.TotalSeconds));
        }

        var vm = new SurveyStatsVM
        {
            SurveyId = survey.SurveyId,
            Title = survey.Title,
            CoverImagePath = survey.CoverImagePath,
            TopicName = survey.Topic?.Name,
            StatusCode = survey.StatusCode,
            CreatedAt = survey.CreatedAt,
            StartAt = survey.StartAt,
            EndAt = survey.EndAt,
            TotalResponses = totalResponses,
            CompletedResponses = completedResponses,
            InProgressResponses = inProgressResponses,
            DistinctRespondents = distinctRespondents,
            FirstResponseAt = responses.FirstOrDefault()?.StartedAt,
            LastResponseAt = responses.LastOrDefault()?.SubmittedAt ?? responses.LastOrDefault()?.StartedAt,
            AverageCompletionTime = avgCompletion
        };

        foreach (var question in survey.Questions.OrderBy(q => q.OrderIndex))
        {
            var qDetails = details.Where(d => d.QuestionId == question.QuestionId).ToList();
            var answeredCount = qDetails.Select(d => d.ResponseId).Distinct().Count();
            var qVm = new QuestionStatsVM
            {
                QuestionId = question.QuestionId,
                OrderIndex = question.OrderIndex,
                QuestionText = question.QuestionText ?? string.Empty,
                QuestionType = question.QuestionType ?? string.Empty,
                IsRequired = question.IsRequired,
                AnsweredCount = answeredCount,
                SkippedCount = Math.Max(0, totalResponses - answeredCount)
            };

            var type = question.QuestionType?.ToLowerInvariant();
            if (type is "single" or "multi")
            {
                foreach (var choice in question.Choices.OrderBy(c => c.OrderIndex))
                {
                    var count = qDetails.Count(d => d.ChoiceId == choice.ChoiceId);
                    var percentage = totalResponses > 0 ? Math.Round((decimal)count * 100m / totalResponses, 2) : 0m;
                    qVm.Choices.Add(new ChoiceStatsVM
                    {
                        ChoiceId = choice.ChoiceId,
                        ChoiceText = choice.ChoiceText ?? string.Empty,
                        Count = count,
                        Percentage = percentage,
                        OrderIndex = choice.OrderIndex
                    });
                }

                qVm.TotalSelections = qDetails.Count;
            }
            else if (type is "rating" or "nps")
            {
                var numbers = qDetails
                    .Where(d => d.AnswerNumber.HasValue)
                    .Select(d => d.AnswerNumber!.Value)
                    .OrderBy(x => x)
                    .ToList();

                if (numbers.Count > 0)
                {
                    decimal median;
                    int mid = numbers.Count / 2;
                    if (numbers.Count % 2 == 0)
                        median = (numbers[mid - 1] + numbers[mid]) / 2m;
                    else
                        median = numbers[mid];

                    qVm.Numeric = new NumericStats
                    {
                        Count = numbers.Count,
                        Average = Math.Round(numbers.Average(), 2),
                        Median = Math.Round(median, 2),
                        Min = numbers.Min(),
                        Max = numbers.Max()
                    };
                }
            }
            else if (type == "text")
            {
                qVm.TextAnswerCount = qDetails.Count(d => !string.IsNullOrWhiteSpace(d.AnswerText));
            }

            vm.Questions.Add(qVm);
        }

        return View(vm);
    }

    private int? GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(claim, out var id) ? id : null;
    }

    private async Task<MySurveysViewModel> BuildViewModelWithErrors(int userId)
    {
        var vm = new MySurveysViewModel
        {
            Surveys = await _db.Surveys
                .AsNoTracking()
                .Where(s => s.OwnerUserId == userId)
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => new SurveyRow
                {
                    SurveyId = s.SurveyId,
                    Title = s.Title,
                    StatusCode = s.StatusCode,
                    CreatedAt = s.CreatedAt,
                    StartAt = s.StartAt,
                    EndAt = s.EndAt,
                    TopicName = s.Topic != null ? s.Topic.Name : null,
                    QuestionCount = s.Questions.Count
                })
                .ToListAsync()
        };
        return vm;
    }

    private async Task PopulateFormLookups()
    {
        var topics = await _db.Topics.OrderBy(t => t.Name).ToListAsync();
        ViewBag.Topics = new SelectList(topics, "TopicId", "Name");
        ViewBag.StatusOptions = new List<SelectListItem>
        {
            new("Nháp", "draft"),
            new("Đang mở", "active"),
            new("Đã đóng", "closed")
        };
    }

    public class MySurveysViewModel
    {
        public List<SurveyRow> Surveys { get; set; } = new();
        public MySurveyCreateModel CreateForm { get; set; } = new();
    }

    public class SurveyRow
    {
        public int SurveyId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string StatusCode { get; set; } = string.Empty;
        public string? TopicName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? StartAt { get; set; }
        public DateTime? EndAt { get; set; }
        public int QuestionCount { get; set; }
    }

    public class SurveyStatsVM
    {
        public int SurveyId { get; set; }
        public string Title { get; set; } = "";
        public string? TopicName { get; set; }
        public string StatusCode { get; set; } = "";
        public string? CoverImagePath { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? StartAt { get; set; }
        public DateTime? EndAt { get; set; }
        public int TotalResponses { get; set; }
        public int CompletedResponses { get; set; }
        public int InProgressResponses { get; set; }
        public int DistinctRespondents { get; set; }
        public DateTime? FirstResponseAt { get; set; }
        public DateTime? LastResponseAt { get; set; }
        public TimeSpan? AverageCompletionTime { get; set; }
        public List<QuestionStatsVM> Questions { get; set; } = new();
    }

    public class QuestionStatsVM
    {
        public int QuestionId { get; set; }
        public int OrderIndex { get; set; }
        public string QuestionText { get; set; } = "";
        public string QuestionType { get; set; } = "";
        public bool IsRequired { get; set; }
        public int AnsweredCount { get; set; }
        public int SkippedCount { get; set; }
        public int TotalSelections { get; set; }
        public List<ChoiceStatsVM> Choices { get; set; } = new();
        public NumericStats? Numeric { get; set; }
        public List<string> TextAnswers { get; set; } = new();
        public int TextAnswerCount { get; set; }
    }

    public class ChoiceStatsVM
    {
        public int ChoiceId { get; set; }
        public string ChoiceText { get; set; } = "";
        public int Count { get; set; }
        public decimal Percentage { get; set; }
        public int OrderIndex { get; set; }
    }

    public class NumericStats
    {
        public decimal Average { get; set; }
        public decimal Median { get; set; }
        public decimal Min { get; set; }
        public decimal Max { get; set; }
        public int Count { get; set; }
    }

    public class MySurveyCreateModel
    {
        [Required(ErrorMessage = "Tiêu đề là bắt buộc.")]
        [StringLength(300)]
        public string Title { get; set; } = string.Empty;

        [StringLength(2000)]
        public string? Description { get; set; }

        [Required(ErrorMessage = "Vui lòng chọn danh mục.")]
        public int TopicId { get; set; }

        [Required]
        public string StatusCode { get; set; } = "draft";

        public bool IsAnonymous { get; set; } = true;

        [Display(Name = "Ngày bắt đầu")]
        public DateTime? StartAt { get; set; }

        [Display(Name = "Ngày kết thúc")]
        public DateTime? EndAt { get; set; }

        [Display(Name = "Ảnh bìa")]
        public string? CoverImagePath { get; set; }
    }
}
