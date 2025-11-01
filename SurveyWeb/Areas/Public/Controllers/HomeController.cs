using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SurveyWeb.Data;
using SurveyWeb.Models.ViewModels;

namespace SurveyWeb.Areas.Public.Controllers;

[Area("Public")]
public class HomeController : Controller
{
    private readonly SurveyDbContext _db;
    public HomeController(SurveyDbContext db) => _db = db;

    // GET /
    public async Task<IActionResult> Index(string? q, int? topicId, string? sort, int page = 1, int pageSize = 9)
    {
        var now = DateTime.UtcNow;
        var baseQuery = _db.Surveys.AsNoTracking()
            .Include(s => s.Topic)
            .Where(s => s.StatusCode == "active" &&
                        (s.StartAt == null || s.StartAt <= now) &&
                        (s.EndAt == null || s.EndAt >= now));

        if (!string.IsNullOrWhiteSpace(q))
        {
            var k = q.Trim();
            baseQuery = baseQuery.Where(s =>
                s.Title.Contains(k) || (s.Description != null && s.Description.Contains(k)) || s.Topic!.Name.Contains(k));
        }

        if (topicId.HasValue)
            baseQuery = baseQuery.Where(s => s.TopicId == topicId.Value);

        sort = string.IsNullOrWhiteSpace(sort) ? "new" : sort.ToLowerInvariant();
        baseQuery = sort switch
        {
            "title"   => baseQuery.OrderBy(s => s.Title),
            "endsoon" => baseQuery.OrderBy(s => s.EndAt ?? DateTime.MaxValue),
            _         => baseQuery.OrderByDescending(s => s.CreatedAt),
        };

        var total = await baseQuery.CountAsync();
        var items = await baseQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new SurveyListItemVM
            {
                SurveyId = s.SurveyId,
                Title = s.Title,
                Description = s.Description,
                TopicName = s.Topic!.Name,
                StatusCode = s.StatusCode,
                CoverImagePath = s.CoverImagePath,
                StartAt = s.StartAt,
                EndAt = s.EndAt,
                CreatedAt = s.CreatedAt,
                IsActiveNow = s.StatusCode == "active" && 
                              (s.StartAt == null || s.StartAt <= now) && 
                              (s.EndAt == null || s.EndAt >= now),
                DaysLeft = s.EndAt == null ? null : (int?)Math.Max(0, (s.EndAt.Value - now).TotalDays)
            })
            .ToListAsync();

        // topics + count (cho chip filter)
        var topics = await _db.Surveys.AsNoTracking()
            .Include(s => s.Topic)
            .Where(s => s.StatusCode == "active" && (s.StartAt == null || s.StartAt <= now) && (s.EndAt == null || s.EndAt >= now))
            .GroupBy(s => new { s.TopicId, s.Topic!.Name })
            .Select(g => new { g.Key.TopicId, g.Key.Name, C = g.Count() })
            .OrderBy(o => o.Name)
            .ToListAsync();

        var vm = new HomeIndexVM
        {
            Q = q,
            TopicId = topicId,
            Sort = sort,
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            Items = items,
            Topics = topics.Select(t => (t.TopicId, t.Name, t.C))
        };

        return View(vm);
    }
}

