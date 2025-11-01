using System;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using ClosedXML.Excel;
using SurveyWeb.Data;
using SurveyWeb.Models;
using System.IO;

namespace SurveyWeb.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
public class SurveysController : Controller
{
    private readonly SurveyDbContext _db;
    private readonly IWebHostEnvironment _env;
    private const long MaxCoverSizeBytes = 2 * 1024 * 1024;
    private const string CoverUploadFolder = "uploads/covers";

    public SurveysController(SurveyDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    private bool IsAdmin => User.IsInRole("Admin");

    private int? CurrentUserId
    {
        get
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(claim, out var id) ? id : (int?)null;
        }
    }

    private bool CanAccessSurvey(Survey survey)
    {
        if (IsAdmin) return true;
        var userId = CurrentUserId;
        return userId.HasValue && survey.OwnerUserId == userId.Value;
    }

    private static readonly string[] StatusOptions = new[] { "draft", "active", "closed" };

    // GET: /Admin/Surveys
    [HttpGet]
    public async Task<IActionResult> Index(string? q, string? status)
    {
        var query = _db.Surveys.Include(s => s.Topic).AsQueryable();

        if (!IsAdmin)
        {
            var userId = CurrentUserId;
            if (!userId.HasValue)
                return Forbid();
            query = query.Where(s => s.OwnerUserId == userId.Value);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            q = q.Trim();
            query = query.Where(s => s.Title.Contains(q) || (s.Description != null && s.Description.Contains(q)));
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(s => s.StatusCode == status);
        }

        var items = await query
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new SurveyRowVM
            {
                SurveyId = s.SurveyId,
                Title = s.Title,
                TopicName = s.Topic!.Name,
                StatusCode = s.StatusCode,
                CoverImagePath = s.CoverImagePath,
                StartAt = s.StartAt,
                EndAt = s.EndAt,
                CreatedAt = s.CreatedAt
            }).ToListAsync();

        ViewBag.Q = q;
        ViewBag.Status = status;
        ViewBag.StatusOptions = StatusOptions;
        return View(items);
    }

    public class SurveyRowVM
    {
        public int SurveyId { get; set; }
        public string Title { get; set; } = "";
        public string TopicName { get; set; } = "";
        public string StatusCode { get; set; } = "";
        public string? CoverImagePath { get; set; }
        public DateTime? StartAt { get; set; }
        public DateTime? EndAt { get; set; }
        public DateTime CreatedAt { get; set; }
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
        public List<NumericBucket> NumericBuckets { get; set; } = new();
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

    public class NumericBucket
    {
        public decimal Value { get; set; }
        public int Count { get; set; }
        public decimal Percentage { get; set; }
    }

    public class SurveyReportVM
    {
        public int SurveyId { get; set; }
        public string Title { get; set; } = "";
        public string TopicName { get; set; } = "";
        public string? CoverImagePath { get; set; } = null;
        public string StatusCode { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public int TotalResponses { get; set; }
        public int CompletedResponses { get; set; }
        public int InProgress => Math.Max(0, TotalResponses - CompletedResponses);
        public DateTime? LastResponseAt { get; set; }
    }
    // GET: /Admin/Surveys/Stats/{id}
    [HttpGet]
    public async Task<IActionResult> Stats(int id)
    {
        var survey = await _db.Surveys
            .Include(s => s.Topic)
            .Include(s => s.Questions.OrderBy(q => q.OrderIndex))
                .ThenInclude(q => q.Choices.OrderBy(c => c.OrderIndex))
            .FirstOrDefaultAsync(s => s.SurveyId == id);

        if (survey == null) return NotFound();

        if (!CanAccessSurvey(survey))
            return Forbid();

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
                QuestionText = question.QuestionText,
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
                        ChoiceText = choice.ChoiceText,
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

                    var buckets = numbers
                        .GroupBy(x => x)
                        .OrderBy(g => g.Key)
                        .Select(g => new NumericBucket
                        {
                            Value = g.Key,
                            Count = g.Count(),
                            Percentage = Math.Round((decimal)g.Count() * 100m / numbers.Count, 2)
                        })
                        .ToList();

                    qVm.NumericBuckets = buckets;
                }
            }
            else if (type == "text")
            {
                var texts = qDetails
                    .Select(d => d.AnswerText)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => t!.Trim())
                    .ToList();
                qVm.TextAnswerCount = texts.Count;
                qVm.TextAnswers = texts.Distinct().Take(50).ToList();
            }

            vm.Questions.Add(qVm);
        }

        return View(vm);
    }

    // GET: /Admin/Surveys/Export/{id}?format=csv|xlsx
    [HttpGet]
    public async Task<IActionResult> Export(int id, string format = "csv")
    {
        var survey = await _db.Surveys
            .Include(s => s.Topic)
            .Include(s => s.Questions.OrderBy(q => q.OrderIndex))
                .ThenInclude(q => q.Choices.OrderBy(c => c.OrderIndex))
            .FirstOrDefaultAsync(s => s.SurveyId == id);

        if (survey == null) return NotFound();

        if (!CanAccessSurvey(survey))
            return Forbid();

        var responses = await _db.Responses
            .Where(r => r.SurveyId == id && r.SubmittedAt != null)
            .OrderBy(r => r.SubmittedAt)
            .ToListAsync();

        if (responses.Count == 0)
        {
            TempData["Error"] = "Chưa có phản hồi nào để xuất.";
            return RedirectToAction(nameof(Stats), new { id });
        }

        var responseIds = responses.Select(r => r.ResponseId).ToList();
        var details = responseIds.Count == 0
            ? new List<ResponseDetail>()
            : await _db.ResponseDetails
                .Where(rd => responseIds.Contains(rd.ResponseId))
                .ToListAsync();

        if (string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase))
            return ExportToXlsx(survey, responses, details);

        return ExportToCsv(survey, responses, details);
    }

    private FileResult ExportToCsv(Survey survey, List<Response> responses, List<ResponseDetail> details)
    {
        var questions = survey.Questions.OrderBy(q => q.OrderIndex).ToList();
        var lookup = BuildDetailLookup(details);

        var headers = new List<string>
        {
            "ResponseId",
            "RespondentId",
            "StartedAt",
            "SubmittedAt"
        };
        headers.AddRange(questions.Select(q => $"Q{q.OrderIndex}: {q.QuestionText}"));

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(EscapeForCsv)));

        foreach (var response in responses)
        {
            var row = new List<string>
            {
                EscapeForCsv(response.ResponseId.ToString(CultureInfo.InvariantCulture)),
                EscapeForCsv(response.RespondentId.ToString()),
                EscapeForCsv(response.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                EscapeForCsv(response.SubmittedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty)
            };

            foreach (var question in questions)
            {
                var answer = FormatAnswer(question, lookup, response.ResponseId);
                row.Add(EscapeForCsv(answer));
            }

            sb.AppendLine(string.Join(",", row));
        }

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var bytes = encoding.GetBytes(sb.ToString());
        var fileName = $"{ToFileSafeSlug(survey.Title)}_{survey.SurveyId}.csv";
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }

    private FileResult ExportToXlsx(Survey survey, List<Response> responses, List<ResponseDetail> details)
    {
        var questions = survey.Questions.OrderBy(q => q.OrderIndex).ToList();
        var lookup = BuildDetailLookup(details);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Responses");

        var col = 1;
        ws.Cell(1, col++).Value = "ResponseId";
        ws.Cell(1, col++).Value = "RespondentId";
        ws.Cell(1, col++).Value = "StartedAt";
        ws.Cell(1, col++).Value = "SubmittedAt";
        foreach (var question in questions)
        {
            ws.Cell(1, col++).Value = $"Q{question.OrderIndex}: {question.QuestionText}";
        }

        var rowIndex = 2;
        foreach (var response in responses)
        {
            col = 1;
            ws.Cell(rowIndex, col++).Value = response.ResponseId;
            ws.Cell(rowIndex, col++).Value = response.RespondentId.ToString();

            var startedCell = ws.Cell(rowIndex, col++);
            startedCell.Value = response.StartedAt.ToLocalTime();
            startedCell.Style.DateFormat.Format = "yyyy-MM-dd HH:mm:ss";

            var submittedCell = ws.Cell(rowIndex, col++);
            if (response.SubmittedAt.HasValue)
            {
                submittedCell.Value = response.SubmittedAt.Value.ToLocalTime();
                submittedCell.Style.DateFormat.Format = "yyyy-MM-dd HH:mm:ss";
            }
            else
            {
                submittedCell.SetValue(string.Empty);
            }

            foreach (var question in questions)
            {
                var answer = FormatAnswer(question, lookup, response.ResponseId);
                ws.Cell(rowIndex, col++).SetValue(answer);
            }

            rowIndex++;
        }

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var fileName = $"{ToFileSafeSlug(survey.Title)}_{survey.SurveyId}.xlsx";
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    private static Dictionary<long, Dictionary<int, List<ResponseDetail>>> BuildDetailLookup(IEnumerable<ResponseDetail> details)
    {
        return details
            .GroupBy(d => d.ResponseId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(d => d.QuestionId)
                      .ToDictionary(qg => qg.Key, qg => qg.ToList()));
    }

    private static string FormatAnswer(Question question, Dictionary<long, Dictionary<int, List<ResponseDetail>>> detailLookup, long responseId)
    {
        if (!detailLookup.TryGetValue(responseId, out var questionLookup) ||
            !questionLookup.TryGetValue(question.QuestionId, out var entries) ||
            entries.Count == 0)
        {
            return string.Empty;
        }

        var type = question.QuestionType?.Trim().ToLowerInvariant();
        var choiceLookup = question.Choices.ToDictionary(c => c.ChoiceId, c => c);

        switch (type)
        {
            case "single":
                return FormatSingle(entries, choiceLookup);
            case "multi":
                return FormatMulti(entries, choiceLookup);
            case "rating":
            case "nps":
                return FormatNumeric(entries);
            case "text":
                return FormatText(entries);
            default:
                return FormatFallback(entries, choiceLookup);
        }
    }

    private static string FormatSingle(List<ResponseDetail> entries, Dictionary<int, Choice> choiceLookup)
    {
        var choiceEntry = entries.FirstOrDefault(e => e.ChoiceId.HasValue);
        if (choiceEntry?.ChoiceId is int choiceId)
        {
            if (choiceLookup.TryGetValue(choiceId, out var choice))
                return choice.ChoiceText;
            return choiceId.ToString(CultureInfo.InvariantCulture);
        }

        var textEntry = entries.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.AnswerText));
        if (textEntry != null)
            return textEntry.AnswerText!.Trim();

        var numericEntry = entries.FirstOrDefault(e => e.AnswerNumber.HasValue);
        if (numericEntry != null)
            return numericEntry.AnswerNumber!.Value.ToString(CultureInfo.InvariantCulture);

        return string.Empty;
    }

    private static string FormatMulti(List<ResponseDetail> entries, Dictionary<int, Choice> choiceLookup)
    {
        var selections = entries
            .Where(e => e.ChoiceId.HasValue)
            .Select(e => e.ChoiceId!.Value)
            .Distinct()
            .Select(id => choiceLookup.TryGetValue(id, out var choice)
                ? choice.ChoiceText
                : id.ToString(CultureInfo.InvariantCulture))
            .ToList();

        var freeTexts = entries
            .Where(e => !string.IsNullOrWhiteSpace(e.AnswerText))
            .Select(e => e.AnswerText!.Trim())
            .ToList();

        selections.AddRange(freeTexts);

        return string.Join("; ", selections.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private static string FormatNumeric(List<ResponseDetail> entries)
    {
        var numeric = entries.FirstOrDefault(e => e.AnswerNumber.HasValue)?.AnswerNumber;
        if (numeric.HasValue)
            return numeric.Value.ToString(CultureInfo.InvariantCulture);

        var text = entries.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.AnswerText))?.AnswerText;
        return text?.Trim() ?? string.Empty;
    }

    private static string FormatText(List<ResponseDetail> entries)
    {
        var parts = entries
            .Where(e => !string.IsNullOrWhiteSpace(e.AnswerText))
            .Select(e => e.AnswerText!.Trim())
            .ToList();

        if (parts.Count == 0)
            return string.Empty;

        return string.Join("; ", parts);
    }

    private static string FormatFallback(List<ResponseDetail> entries, Dictionary<int, Choice> choiceLookup)
    {
        var single = FormatSingle(entries, choiceLookup);
        if (!string.IsNullOrEmpty(single))
            return single;

        var multi = FormatMulti(entries, choiceLookup);
        if (!string.IsNullOrEmpty(multi))
            return multi;

        return FormatText(entries);
    }

    private static string EscapeForCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var escaped = value.Replace("\"", "\"\"");
        if (escaped.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
            return $"\"{escaped}\"";
        return escaped;
    }

    private static string ToFileSafeSlug(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "survey";
        var sb = new StringBuilder(input.Length);
        foreach (var ch in input.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
            else if (char.IsWhiteSpace(ch) || ch == '-' || ch == '_')
            {
                if (sb.Length == 0 || sb[^1] != '-')
                    sb.Append('-');
            }
        }

        var slug = sb.ToString().Trim('-');
        return string.IsNullOrEmpty(slug) ? "survey" : slug;
    }

    // GET: /Admin/Surveys/Reports
    [HttpGet]
    public async Task<IActionResult> Reports()
    {
        var query = _db.Surveys
            .Include(s => s.Topic)
            .AsQueryable();

        if (!IsAdmin)
        {
            var userId = CurrentUserId;
            if (!userId.HasValue)
                return Forbid();
            query = query.Where(s => s.OwnerUserId == userId.Value);
        }

        var reports = await query
            .Select(s => new SurveyReportVM
            {
                SurveyId = s.SurveyId,
                Title = s.Title,
                TopicName = s.Topic != null ? s.Topic.Name : string.Empty,
                CoverImagePath = s.CoverImagePath,
                StatusCode = s.StatusCode,
                CreatedAt = s.CreatedAt,
                TotalResponses = _db.Responses.Count(r => r.SurveyId == s.SurveyId),
                CompletedResponses = _db.Responses.Count(r => r.SurveyId == s.SurveyId && r.SubmittedAt != null),
                LastResponseAt = _db.Responses
                    .Where(r => r.SurveyId == s.SurveyId && r.SubmittedAt != null)
                    .Max(r => (DateTime?)r.SubmittedAt)
            })
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        return View(reports);
    }
    // GET: /Admin/Surveys/Create
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await FillDropdowns();
        var model = new Survey
        {
            StatusCode = "active",
            IsAnonymous = true,
            CreatedAt = DateTime.UtcNow
        };
        return View(model);
    }

    // POST: /Admin/Surveys/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Survey model, IFormFile? coverImage)
    {
        if (!StatusOptions.Contains(model.StatusCode ?? ""))
            ModelState.AddModelError(nameof(model.StatusCode), "Invalid status");

        ValidateCoverImage(coverImage);

        if (!ModelState.IsValid)
        {
            await FillDropdowns();
            return View(model);
        }

        model.StartAt = NormalizeToUtc(model.StartAt);
        model.EndAt = NormalizeToUtc(model.EndAt);
        model.CreatedAt = DateTime.UtcNow;
        var uidStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(uidStr, out var uid))
            model.OwnerUserId = uid;

        if (coverImage != null && coverImage.Length > 0)
        {
            model.CoverImagePath = await SaveCoverImageAsync(coverImage);
        }

        _db.Surveys.Add(model);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Survey created successfully";
        return RedirectToAction(nameof(Index));
    }

    // GET: /Admin/Surveys/Edit/{id}
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var s = await _db.Surveys
            .Include(x => x.Questions.OrderBy(q => q.OrderIndex))
                .ThenInclude(q => q.Choices.OrderBy(c => c.OrderIndex))
            .FirstOrDefaultAsync(x => x.SurveyId == id);

        if (s == null) return NotFound();

        if (!CanAccessSurvey(s))
            return Forbid();
        await FillDropdowns();
        return View(s);
    }

    // POST: /Admin/Surveys/Edit/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Survey model, IFormFile? coverImage, bool removeCover = false)
    {
        if (id != model.SurveyId) return BadRequest();

        var existing = await _db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.SurveyId == id);
        if (existing == null) return NotFound();

        if (!CanAccessSurvey(existing))
            return Forbid();

        if (!StatusOptions.Contains(model.StatusCode ?? ""))
            ModelState.AddModelError(nameof(model.StatusCode), "Invalid status");

        ValidateCoverImage(coverImage);

        if (!ModelState.IsValid)
        {
            model.CoverImagePath = existing.CoverImagePath;
            await FillDropdowns();
            return View(model);
        }

        model.OwnerUserId = existing.OwnerUserId;
        model.StartAt = NormalizeToUtc(model.StartAt);
        model.EndAt = NormalizeToUtc(model.EndAt);
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
        TempData["Success"] = "Survey updated successfully";
        return RedirectToAction(nameof(Index));
    }

    private static DateTime? NormalizeToUtc(DateTime? value)
    {
        if (value == null) return null;
        var dt = value.Value;
        if (dt.Kind == DateTimeKind.Utc) return dt;
        if (dt.Kind == DateTimeKind.Unspecified)
            dt = DateTime.SpecifyKind(dt, DateTimeKind.Local);
        return dt.ToUniversalTime();
    }

    // POST: /Admin/Surveys/Delete/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var s = await _db.Surveys.FindAsync(id);
        if (s == null) return NotFound();

        if (!CanAccessSurvey(s))
            return Forbid();

        var hasResponses = await _db.Responses.AnyAsync(r => r.SurveyId == id);
        if (hasResponses)
        {
            TempData["Error"] = "Cannot delete: survey already has responses.";
            return RedirectToAction(nameof(Index));
        }

        DeleteCoverImage(s.CoverImagePath);
        _db.Surveys.Remove(s);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Survey deleted";
        return RedirectToAction(nameof(Index));
    }

    private void ValidateCoverImage(IFormFile? file)
    {
        if (file == null || file.Length == 0) return;
        if (!file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError("CoverImagePath", "Please select a valid image file.");
            return;
        }

        if (file.Length > MaxCoverSizeBytes)
        {
            ModelState.AddModelError("CoverImagePath", "Cover image must be 2MB or smaller.");
        }
    }

    private async Task<string?> SaveCoverImageAsync(IFormFile file)
    {
        var uploadsRoot = Path.Combine(_env.WebRootPath, CoverUploadFolder.Replace("/", Path.DirectorySeparatorChar.ToString()));
        Directory.CreateDirectory(uploadsRoot);

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension))
            extension = ".jpg";

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

    private async Task FillDropdowns()
    {
        var topics = await _db.Topics.OrderBy(t => t.Name).ToListAsync();
        ViewBag.Topics = new SelectList(topics, "TopicId", "Name");
        ViewBag.StatusOptions = StatusOptions.Select(s => new SelectListItem { Text = s, Value = s });
    }
}


