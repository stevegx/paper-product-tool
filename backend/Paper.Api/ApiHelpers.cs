using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Paper.Api;
public static class ApiHelpers
{
    public static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public static object PublicUser(AppUser user) => new { user.Id, user.Email, user.Name, user.Role, user.Active };
    public static object VisitDto(Visit visit) => new { visit.Id, visit.BranchCode, customerName = visit.Branch?.Name, taxId = visit.Branch?.TaxId, sellerName = visit.User.Name, visit.UserId, createdAtUtc = DateTime.SpecifyKind(visit.OccurredAtUtc ?? visit.CreatedAtUtc, DateTimeKind.Utc), receivedAtUtc = DateTime.SpecifyKind(visit.CreatedAtUtc, DateTimeKind.Utc), updatedAtUtc = visit.UpdatedAtUtc.HasValue ? DateTime.SpecifyKind(visit.UpdatedAtUtc.Value, DateTimeKind.Utc) : (DateTime?)null, visit.Version, visit.LastEditedById, visit.IsDeleted, visit.DeletedAtUtc, visit.DeletedById, visit.Notes, answers = JsonSerializer.Deserialize<SavedAnswer[]>(visit.AnswersJson) };
    public static async Task<List<SurveyQuestion>> Questions(AppDbContext db)
    {
        var questions = await db.Questions.AsNoTracking().Include(x => x.Options).Where(x => x.Active).OrderBy(x => x.Position).ThenBy(x => x.Id).ToListAsync();
        foreach (var question in questions) question.Options = question.Options.OrderBy(x => x.Position).ThenBy(x => x.Id).ToList();
        return questions;
    }
    public static string SurveyVersion(List<SurveyQuestion> questions) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(questions))));
    public static DateTime AthensDate(DateOnly date) => TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), TimeZoneInfo.FindSystemTimeZoneById("Europe/Athens"));
    public static AppUser MakeUser(CreateUserInput input, PasswordHasher<AppUser> hasher)
    {
        var email = (input.Email ?? "").Trim().ToLowerInvariant(); var name = (input.Name ?? "").Trim(); var password = input.Password ?? "";
        if (email.Length > 254 || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email || name.Length is 0 or > 128) throw new InputException("Συμπλήρωσε έγκυρο email και όνομα έως 128 χαρακτήρες.");
        if (password.Length is < 12 or > 128 || !password.Any(char.IsLetter) || !password.Any(char.IsDigit)) throw new InputException("Ο κωδικός χρειάζεται 12–128 χαρακτήρες, με γράμματα και αριθμούς.");
        if (input.Role is not ("Admin" or "Seller")) throw new InputException("Μη έγκυρος ρόλος.");
        var user = new AppUser { Email = email, Name = name, Role = input.Role };
        user.PasswordHash = hasher.HashPassword(user, password); return user;
    }
    public static Task SignIn(HttpContext context, AppUser user) => context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.Name), new Claim(ClaimTypes.Role, user.Role)], CookieAuthenticationDefaults.AuthenticationScheme)));
}
public sealed class WriteGate { public SemaphoreSlim Lock { get; } = new(1, 1); }
public sealed record LoginInput(string? Email, string? Password);
public sealed record CreateUserInput(string? Email, string? Name, string? Password, string Role);
public sealed record UserStatusInput(bool Active);
public sealed record QuestionInput(string? Title, bool Required, string[]? Options, string SelectionMode = "Multiple", string? SectionKey = null);
public sealed record AnswerInput(Guid QuestionId, Guid[]? OptionIds);
public sealed record VisitInput(string BranchCode, Guid SubmissionId, string? SurveyVersion, string? Notes, AnswerInput[]? Answers);
