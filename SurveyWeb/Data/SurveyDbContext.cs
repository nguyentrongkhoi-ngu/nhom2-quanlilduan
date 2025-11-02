using Microsoft.EntityFrameworkCore;
using SurveyWeb.Models;

namespace SurveyWeb.Data;

public class SurveyDbContext : DbContext
{
    public SurveyDbContext(DbContextOptions<SurveyDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Topic> Topics => Set<Topic>();
    public DbSet<Survey> Surveys => Set<Survey>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<Choice> Choices => Set<Choice>();
    public DbSet<Respondent> Respondents => Set<Respondent>();
    public DbSet<Response> Responses => Set<Response>();
    public DbSet<ResponseDetail> ResponseDetails => Set<ResponseDetail>();
    public DbSet<Role> Roles { get; set; } = null!;
    public DbSet<UserRole> UserRoles { get; set; } = null!;
   protected override void OnModelCreating(ModelBuilder mb)
    {
        // USERS
        mb.Entity<User>(e =>
        {
            e.ToTable("Users");
            e.HasKey(x => x.UserId);
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.Email).HasColumnName("email").HasMaxLength(255).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.PasswordHash).HasColumnName("password_hash").HasMaxLength(255).IsRequired();
            e.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(100);
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

       // === Roles ===
        mb.Entity<Role>(e =>
        {
            e.ToTable("Roles");
            e.HasKey(x => x.RoleId);
            e.Property(x => x.RoleId).HasColumnName("role_id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        // === UserRoles (PK kép) ===
        mb.Entity<UserRole>(e =>
        {
            e.ToTable("UserRoles");
            e.HasKey(x => new { x.UserId, x.RoleId });

            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.RoleId).HasColumnName("role_id");
            e.Property(x => x.GrantedAt).HasColumnName("granted_at");

            // Quan hệ
            e.HasOne(x => x.User)
                .WithMany()                // hoặc .WithMany(u => u.UserRoles) nếu bạn có nav trong User
                .HasForeignKey(x => x.UserId);

            e.HasOne(x => x.Role)
                .WithMany()
                .HasForeignKey(x => x.RoleId);
        });

        // TOPICS
        mb.Entity<Topic>(e =>
        {
            e.ToTable("Topics");
            e.HasKey(x => x.TopicId);
            e.Property(x => x.TopicId).HasColumnName("topic_id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            e.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.Slug).IsUnique();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        // SURVEYS  (map snake_case cột chính → fix lỗi Query)
        mb.Entity<Survey>(e =>
        {
            e.ToTable("Surveys");
            e.HasKey(x => x.SurveyId);
            e.Property(x => x.SurveyId).HasColumnName("survey_id");
            e.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
            e.Property(x => x.TopicId).HasColumnName("topic_id");
            e.Property(x => x.Title).HasColumnName("title").HasMaxLength(300).IsRequired();
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.StatusCode).HasColumnName("status_code").HasMaxLength(20).IsRequired();
            e.Property(x => x.IsAnonymous).HasColumnName("is_anonymous");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.StartAt).HasColumnName("start_at");
            e.Property(x => x.EndAt).HasColumnName("end_at");
            e.Property(x => x.CoverImagePath).HasColumnName("cover_image_path").HasMaxLength(512);

            e.HasOne(x => x.Topic).WithMany().HasForeignKey(x => x.TopicId);
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId);
            e.HasMany(x => x.Questions).WithOne(q => q.Survey).HasForeignKey(q => q.SurveyId);
        });

        // QUESTIONS
        mb.Entity<Question>(e =>
        {
            e.ToTable("Questions");
            e.HasKey(x => x.QuestionId);
            e.Property(x => x.QuestionId).HasColumnName("question_id");
            e.Property(x => x.SurveyId).HasColumnName("survey_id");
            e.Property(x => x.OrderIndex).HasColumnName("order_index").HasDefaultValue(1);
            e.Property(x => x.QuestionText).HasColumnName("question_text").IsRequired();
            e.Property(x => x.QuestionType).HasColumnName("question_type").HasMaxLength(20).IsRequired();
            e.Property(x => x.IsRequired).HasColumnName("is_required").HasDefaultValue(false);
            e.Property(x => x.MinValue).HasColumnName("min_value").HasPrecision(10, 2);
            e.Property(x => x.MaxValue).HasColumnName("max_value").HasPrecision(10, 2);
            e.Property(x => x.MaxLength).HasColumnName("max_length");
            e.Property(x => x.ImagePath).HasColumnName("image_path").HasMaxLength(512);
        });

        // CHOICES
        mb.Entity<Choice>(e =>
        {
            e.ToTable("Choices");
            e.HasKey(x => x.ChoiceId);
            e.Property(x => x.ChoiceId).HasColumnName("choice_id");
            e.Property(x => x.QuestionId).HasColumnName("question_id");
            e.Property(x => x.OrderIndex).HasColumnName("order_index").HasDefaultValue(1);
            e.Property(x => x.ChoiceText).HasColumnName("choice_text").HasMaxLength(500).IsRequired();
            e.Property(x => x.NumericValue).HasColumnName("numeric_value").HasPrecision(10, 2);
            e.HasOne(x => x.Question).WithMany(q => q.Choices).HasForeignKey(x => x.QuestionId);
        });

        // RESPONDENTS
        mb.Entity<Respondent>(e =>
        {
            e.ToTable("Respondents");
            e.HasKey(x => x.RespondentId);
            e.Property(x => x.RespondentId).HasColumnName("respondent_id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.Email).HasColumnName("email").HasMaxLength(255);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        // RESPONSES (is_completed computed)
        mb.Entity<Response>(e =>
        {
            e.ToTable("Responses");
            e.HasKey(x => x.ResponseId);
            e.Property(x => x.ResponseId).HasColumnName("response_id");
            e.Property(x => x.SurveyId).HasColumnName("survey_id");
            e.Property(x => x.RespondentId).HasColumnName("respondent_id");
            e.Property(x => x.StartedAt).HasColumnName("started_at");
            e.Property(x => x.SubmittedAt).HasColumnName("submitted_at");
            e.Property(x => x.IsCompleted).HasColumnName("is_completed")
                .ValueGeneratedOnAddOrUpdate(); // computed column
        });

        // RESPONSE DETAILS (unique composite)
        mb.Entity<ResponseDetail>(e =>
        {
            e.ToTable("ResponseDetails");
            e.HasKey(x => x.ResponseDetailId);
            e.Property(x => x.ResponseDetailId).HasColumnName("response_detail_id");
            e.Property(x => x.ResponseId).HasColumnName("response_id");
            e.Property(x => x.QuestionId).HasColumnName("question_id");
            e.Property(x => x.ChoiceId).HasColumnName("choice_id");
            e.Property(x => x.AnswerText).HasColumnName("answer_text");
            e.Property(x => x.AnswerNumber).HasColumnName("answer_number").HasPrecision(10, 2);
            e.HasIndex(x => new { x.ResponseId, x.QuestionId, x.ChoiceId }).IsUnique();
        });
    }
}
