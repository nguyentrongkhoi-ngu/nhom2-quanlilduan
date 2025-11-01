using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema; // <-- thêm dòng này

namespace SurveyWeb.Models;

public class Survey
{
    public int SurveyId { get; set; }
    public int OwnerUserId { get; set; }
    public int TopicId { get; set; }

    [Required, MaxLength(300)]
    public string Title { get; set; } = default!;

    public string? Description { get; set; }

    // map snake_case
    [Required, MaxLength(20)]
    [Column("status_code")]
    public string StatusCode { get; set; } = "draft";

    public bool IsAnonymous { get; set; }

    // nếu DB là created_at thì có thể cần map tiếp:
    // [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("start_at")]
    public DateTime? StartAt { get; set; }

    [Column("end_at")]
    public DateTime? EndAt { get; set; }

    [Column("cover_image_path"), MaxLength(512)]
    public string? CoverImagePath { get; set; }

    public Topic? Topic { get; set; }
    public User? Owner { get; set; }
    public List<Question> Questions { get; set; } = new();
}
