using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using SurveyWeb.Data;
using SurveyWeb.Models;

namespace SurveyWeb.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
public class TopicsController : Controller
{
    private readonly SurveyDbContext _db;
    public TopicsController(SurveyDbContext db) => _db = db;

    // GET: /Admin/Topics
    [HttpGet]
    public async Task<IActionResult> Index(string? q)
    {
        var query = _db.Topics.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            q = q.Trim();
            query = query.Where(x => x.Name.Contains(q) || x.Slug.Contains(q));
        }
        var items = await query.OrderByDescending(x => x.CreatedAt).ToListAsync();
        ViewBag.Q = q;
        return View(items);
    }

    // GET: /Admin/Topics/Create
    [HttpGet]
    public IActionResult Create() => View(new Topic { CreatedAt = DateTime.UtcNow });

    // POST: /Admin/Topics/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Topic model)
    {
        if (!ModelState.IsValid) return View(model);

        // unique slug check
        var exists = await _db.Topics.AnyAsync(t => t.Slug == model.Slug);
        if (exists)
        {
            ModelState.AddModelError(nameof(model.Slug), "Slug đã tồn tại");
            return View(model);
        }

        model.CreatedAt = DateTime.UtcNow;
        _db.Topics.Add(model);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Tạo danh mục thành công";
        return RedirectToAction(nameof(Index));
    }

    // GET: /Admin/Topics/Edit/{id}
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var topic = await _db.Topics.FindAsync(id);
        if (topic == null) return NotFound();
        return View(topic);
    }

    // POST: /Admin/Topics/Edit/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Topic model)
    {
        if (id != model.TopicId) return BadRequest();
        if (!ModelState.IsValid) return View(model);

        var dup = await _db.Topics.AnyAsync(t => t.Slug == model.Slug && t.TopicId != id);
        if (dup)
        {
            ModelState.AddModelError(nameof(model.Slug), "Slug đã tồn tại");
            return View(model);
        }

        _db.Entry(model).Property(x => x.CreatedAt).IsModified = false; // giữ nguyên
        _db.Update(model);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Cập nhật danh mục thành công";
        return RedirectToAction(nameof(Index));
    }

    // POST: /Admin/Topics/Delete/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var topic = await _db.Topics.FindAsync(id);
        if (topic == null) return NotFound();

        var used = await _db.Surveys.AnyAsync(s => s.TopicId == id);
        if (used)
        {
            TempData["Error"] = "Không thể xóa: danh mục đang được dùng.";
            return RedirectToAction(nameof(Index));
        }

        _db.Topics.Remove(topic);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã xóa danh mục";
        return RedirectToAction(nameof(Index));
    }
}

