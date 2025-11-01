using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SurveyWeb.Data;
using SurveyWeb.Services;

namespace SurveyWeb.Areas.Public.Controllers;

[Area("Public")]
[Route("api/public/chatbot")]
[ApiController]
[AllowAnonymous]
[AutoValidateAntiforgeryToken]
public class ChatbotController : ControllerBase
{
    private readonly SurveyChatbotEngine _engine;
    private readonly SurveyDbContext _db;

    public ChatbotController(SurveyChatbotEngine engine, SurveyDbContext db)
    {
        _engine = engine;
        _db = db;
    }

    [HttpGet("surveys")]
    public async Task<IActionResult> GetSurveys()
    {
        var now = DateTime.UtcNow;
        var surveys = await _db.Surveys
            .AsNoTracking()
            .Where(s =>
                s.StatusCode == "active" &&
                (!s.StartAt.HasValue || s.StartAt.Value <= now) &&
                (!s.EndAt.HasValue || s.EndAt.Value >= now))
            .OrderByDescending(s => s.CreatedAt)
            .Take(25)
            .Select(s => new ChatbotSurvey
            {
                SurveyId = s.SurveyId,
                Title = s.Title ?? "Khảo sát"
            })
            .ToListAsync();

        return Ok(surveys);
    }

    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] StartRequest request)
    {
        if (request == null || request.SurveyId <= 0)
            return BadRequest(new { message = "surveyId không hợp lệ." });

        var participant = User.Identity?.Name ?? "bạn";
        var result = await _engine.StartAsync(request.SurveyId, participant);
        if (result == null)
            return NotFound(new { message = "Khảo sát không khả dụng hoặc không tìm thấy." });

        return Ok(new StartResponse
        {
            ConversationId = result.ConversationId,
            SurveyTitle = result.SurveyTitle,
            Messages = result.Messages.Select(m => new ChatMessageDto { Sender = m.Sender, Text = m.Text }).ToList()
        });
    }

    [HttpPost("message")]
    public async Task<IActionResult> Message([FromBody] MessageRequest request)
    {
        if (request == null || request.ConversationId == Guid.Empty || string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { message = "Thiếu dữ liệu bắt buộc." });

        var participant = User.Identity?.Name ?? "bạn";
        var reply = await _engine.SendAsync(request.ConversationId, request.Message, participant);

        var response = new MessageResponse
        {
            ConversationId = request.ConversationId,
            Completed = reply.Completed,
            SessionExpired = reply.SessionExpired,
            Messages = reply.Messages.Select(m => new ChatMessageDto { Sender = m.Sender, Text = m.Text }).ToList()
        };

        if (reply.SessionExpired)
            return StatusCode(410, response);

        return Ok(response);
    }

    public class StartRequest
    {
        [Required]
        public int SurveyId { get; set; }
    }

    public class MessageRequest
    {
        [Required]
        public Guid ConversationId { get; set; }

        [Required]
        [StringLength(4000)]
        public string Message { get; set; } = string.Empty;
    }

    public class StartResponse
    {
        public Guid ConversationId { get; set; }
        public string SurveyTitle { get; set; } = string.Empty;
        public List<ChatMessageDto> Messages { get; set; } = new();
    }

    public class MessageResponse
    {
        public Guid ConversationId { get; set; }
        public bool Completed { get; set; }
        public bool SessionExpired { get; set; }
        public List<ChatMessageDto> Messages { get; set; } = new();
    }

    public class ChatMessageDto
    {
        public string Sender { get; set; } = "bot";
        public string Text { get; set; } = "";
    }

    public class ChatbotSurvey
    {
        public int SurveyId { get; set; }
        public string Title { get; set; } = string.Empty;
    }
}
