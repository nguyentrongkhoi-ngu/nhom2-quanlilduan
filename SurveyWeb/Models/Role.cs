namespace SurveyWeb.Models;

public class Role
{
    public int RoleId { get; set; }          // role_id
    public string Name { get; set; } = "";   // name (UNIQUE)
    public string? Description { get; set; } // description
    public DateTime CreatedAt { get; set; }  // created_at
}
