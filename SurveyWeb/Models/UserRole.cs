namespace SurveyWeb.Models;

public class UserRole
{
    // PK kép (user_id, role_id)
    public int UserId { get; set; }   // FK -> Users.user_id
    public int RoleId { get; set; }   // FK -> Roles.role_id
    public DateTime GrantedAt { get; set; }

    // (optional) nav props nếu bạn cần
    public User? User { get; set; }
    public Role? Role { get; set; }
}
