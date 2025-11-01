using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SurveyWeb.Data;

namespace SurveyWeb.Areas.Public.Controllers;

[Area("Public")]
public class SurveysController : Controller
{
    private readonly SurveyDbContext _db;
    public SurveysController(SurveyDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var list = await _db.Surveys.Include(s => s.Topic).OrderByDescending(s => s.CreatedAt).ToListAsync();
        return View(list);
    }

    public async Task<IActionResult> Details(int id)
    {
        var s = await _db.Surveys
            .Include(x => x.Topic)
            .Include(x => x.Questions)
                .ThenInclude(q => q.Choices)
            .FirstOrDefaultAsync(x => x.SurveyId == id);

        if (s == null) return NotFound();
        return View(s);
    }
}

