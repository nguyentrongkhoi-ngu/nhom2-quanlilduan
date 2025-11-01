using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SurveyWeb.Data;

namespace SurveyWeb.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class DashboardController : Controller
    {
        private readonly SurveyDbContext _db;
        public DashboardController(SurveyDbContext db) => _db = db;

        public async Task<IActionResult> Index()
        {
            var totalUsers = await _db.Users.CountAsync();
            var totalSurveys = await _db.Surveys.CountAsync();
            var totalResponses = await _db.Responses.CountAsync();
            var totalTopics = await _db.Topics.CountAsync();

            ViewBag.TotalUsers = totalUsers;
            ViewBag.TotalSurveys = totalSurveys;
            ViewBag.TotalResponses = totalResponses;
            ViewBag.TotalTopics = totalTopics;
            return View();
        }
    }
}


