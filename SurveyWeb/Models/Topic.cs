using System;
using System.ComponentModel.DataAnnotations;

namespace SurveyWeb.Models;

public class Topic
{
    public int TopicId { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = default!;

    [Required, MaxLength(200)]
    public string Slug { get; set; } = default!;

    public DateTime CreatedAt { get; set; }
}
