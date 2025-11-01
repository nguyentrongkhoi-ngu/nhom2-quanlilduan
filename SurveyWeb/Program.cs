using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using SurveyWeb.Data;
using SurveyWeb.Services;

var builder = WebApplication.CreateBuilder(args);

// MVC + DbContext
builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<SurveyDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("SurveyDb")));
builder.Services.AddSingleton<AiSurveyBuilder>();
builder.Services.AddScoped<SurveyChatbotEngine>();
builder.Services.AddMemoryCache();
builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");

// Cookie Auth
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Account/Login";
        o.LogoutPath = "/Account/Logout";
        o.AccessDeniedPath = "/Account/Login";
        o.ReturnUrlParameter = "returnUrl";
        o.SlidingExpiration = true;
        o.ExpireTimeSpan = TimeSpan.FromDays(30);
    });
builder.Services.AddAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SurveyDbContext>();
    await DbSeeder.SeedAsync(db);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Area routes (Admin)
app.MapControllerRoute(
    name: "admin",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}",
    defaults: new { area = "Admin" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}",
    defaults: new { area = "Public" });

app.Run();

