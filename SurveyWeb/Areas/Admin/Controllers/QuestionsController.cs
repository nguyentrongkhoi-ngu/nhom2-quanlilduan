using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SurveyWeb.Data;
using SurveyWeb.Models;

namespace SurveyWeb.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
public class QuestionsController : Controller
{
    private readonly SurveyDbContext _db;
    private readonly IWebHostEnvironment _env;
    private const long MaxQuestionImageSizeBytes = 2 * 1024 * 1024;
    private const string QuestionImageFolder = "uploads/question-images";

    public QuestionsController(SurveyDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int surveyId)
    {
        var survey = await _db.Surveys
            .Include(s => s.Topic)
            .Include(s => s.Questions.OrderBy(q => q.OrderIndex))
                .ThenInclude(q => q.Choices.OrderBy(c => c.OrderIndex))
            .FirstOrDefaultAsync(s => s.SurveyId == surveyId);

        if (survey == null) return NotFound();

        ViewBag.SurveyId = surveyId;
        ViewBag.SurveyTitle = survey.Title;
        return View(survey);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddQuestion([FromForm] QuestionDto dto)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index), new { surveyId = dto.SurveyId });
        }

        var survey = await _db.Surveys.FindAsync(dto.SurveyId);
        if (survey == null) return NotFound();

        ValidateQuestionImage(dto.Image);

        if (dto.QuestionType is "single" or "multi")
        {
            var validChoices = dto.Choices?.Where(c => !string.IsNullOrWhiteSpace(c)).ToList() ?? new List<string>();
            if (validChoices.Count < 2)
            {
                TempData["Error"] = "Câu hỏi lựa chọn cần ít nhất 2 phương án.";
                return RedirectToAction(nameof(Index), new { surveyId = dto.SurveyId });
            }
        }

        if (dto.QuestionType is "rating" or "nps")
        {
            if (!dto.MinValue.HasValue || !dto.MaxValue.HasValue)
            {
                TempData["Error"] = "Vui lòng nhập giá trị Min/Max cho câu hỏi thang điểm.";
                return RedirectToAction(nameof(Index), new { surveyId = dto.SurveyId });
            }

            if (dto.MaxValue.Value <= dto.MinValue.Value)
            {
                TempData["Error"] = "Giá trị Max phải lớn hơn Min.";
                return RedirectToAction(nameof(Index), new { surveyId = dto.SurveyId });
            }
        }

        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index), new { surveyId = dto.SurveyId });
        }

        var nextOrder = await _db.Questions
            .Where(q => q.SurveyId == dto.SurveyId)
            .MaxAsync(q => (int?)q.OrderIndex) ?? 0;

        var question = new Question
        {
            SurveyId = dto.SurveyId,
            OrderIndex = nextOrder + 1,
            QuestionText = dto.QuestionText,
            QuestionType = dto.QuestionType,
            IsRequired = dto.IsRequired,
            MinValue = dto.MinValue,
            MaxValue = dto.MaxValue,
            MaxLength = dto.MaxLength
        };

        _db.Questions.Add(question);
        await _db.SaveChangesAsync();

        if (dto.Image != null && dto.Image.Length > 0)
        {
            question.ImagePath = await SaveQuestionImageAsync(dto.Image);
            _db.Update(question);
            await _db.SaveChangesAsync();
        }

        if (dto.Choices != null && dto.Choices.Any())
        {
            var choices = dto.Choices
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Select((text, index) => new Choice
                {
                    QuestionId = question.QuestionId,
                    OrderIndex = index + 1,
                    ChoiceText = text.Trim()
                }).ToList();

            if (choices.Any())
            {
                _db.Choices.AddRange(choices);
                await _db.SaveChangesAsync();
            }
        }

        TempData["Success"] = "Đã thêm câu hỏi mới.";
        return RedirectToAction(nameof(Index), new { surveyId = dto.SurveyId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteQuestion(int questionId, int surveyId)
    {
        var question = await _db.Questions
            .Include(q => q.Choices)
            .FirstOrDefaultAsync(q => q.QuestionId == questionId);

        if (question == null) return NotFound();

        _db.Choices.RemoveRange(question.Choices);
        DeleteQuestionImage(question.ImagePath);
        _db.Questions.Remove(question);
        await _db.SaveChangesAsync();

        TempData["Success"] = "Đã xóa câu hỏi.";
        return RedirectToAction(nameof(Index), new { surveyId });
    }

    [HttpPost]
    public async Task<IActionResult> UpdateOrder([FromBody] List<int> questionIds)
    {
        for (int i = 0; i < questionIds.Count; i++)
        {
            var question = await _db.Questions.FindAsync(questionIds[i]);
            if (question != null)
            {
                question.OrderIndex = i + 1;
            }
        }

        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var question = await _db.Questions
            .Include(q => q.Choices.OrderBy(c => c.OrderIndex))
            .Include(q => q.Survey)
            .FirstOrDefaultAsync(q => q.QuestionId == id);

        if (question == null) return NotFound();

        return View(question);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [FromForm] QuestionDto dto)
    {
        var question = await _db.Questions
            .Include(q => q.Choices)
            .FirstOrDefaultAsync(q => q.QuestionId == id);

        if (question == null) return NotFound();

        ValidateQuestionImage(dto.Image);

        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Edit), new { id });
        }

        question.QuestionText = dto.QuestionText;
        question.QuestionType = dto.QuestionType;
        question.IsRequired = dto.IsRequired;
        question.MinValue = dto.MinValue;
        question.MaxValue = dto.MaxValue;
        question.MaxLength = dto.MaxLength;

        if (dto.Image != null && dto.Image.Length > 0)
        {
            DeleteQuestionImage(question.ImagePath);
            question.ImagePath = await SaveQuestionImageAsync(dto.Image);
        }
        else if (dto.RemoveImage)
        {
            DeleteQuestionImage(question.ImagePath);
            question.ImagePath = null;
        }

        _db.Choices.RemoveRange(question.Choices);

        if (dto.Choices != null && dto.Choices.Any())
        {
            var choices = dto.Choices
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Select((text, index) => new Choice
                {
                    QuestionId = question.QuestionId,
                    OrderIndex = index + 1,
                    ChoiceText = text.Trim()
                }).ToList();

            if (choices.Any())
            {
                _db.Choices.AddRange(choices);
            }
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã cập nhật câu hỏi.";
        return RedirectToAction(nameof(Index), new { surveyId = question.SurveyId });
    }

    private void ValidateQuestionImage(IFormFile? file)
    {
        if (file == null || file.Length == 0) return;
        if (!file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError("Image", "Vui lòng chọn tệp hình ảnh hợp lệ.");
            return;
        }

        if (file.Length > MaxQuestionImageSizeBytes)
        {
            ModelState.AddModelError("Image", "Ảnh minh họa tối đa 2 MB.");
        }
    }

    private async Task<string?> SaveQuestionImageAsync(IFormFile file)
    {
        var uploadsRoot = Path.Combine(_env.WebRootPath, QuestionImageFolder.Replace("/", Path.DirectorySeparatorChar.ToString()));
        Directory.CreateDirectory(uploadsRoot);

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".jpg";
        }

        var fileName = $"{Guid.NewGuid():N}{extension}";
        var destination = Path.Combine(uploadsRoot, fileName);

        await using var stream = System.IO.File.Create(destination);
        await file.CopyToAsync(stream);

        return Path.Combine(QuestionImageFolder, fileName).Replace("\\", "/");
    }

    private void DeleteQuestionImage(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return;

        var fullPath = Path.Combine(_env.WebRootPath, relativePath.Replace("/", Path.DirectorySeparatorChar.ToString()));
        if (System.IO.File.Exists(fullPath))
        {
            System.IO.File.Delete(fullPath);
        }
    }

    public class QuestionDto
    {
        public int SurveyId { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public string QuestionType { get; set; } = "text";
        public bool IsRequired { get; set; }
        public decimal? MinValue { get; set; }
        public decimal? MaxValue { get; set; }
        public int? MaxLength { get; set; }
        public List<string>? Choices { get; set; }
        public IFormFile? Image { get; set; }
        public bool RemoveImage { get; set; }
    }
}
