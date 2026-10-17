using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using static Paper.Api.ApiHelpers;

namespace Paper.Api;
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization("Admin");
        admin.MapGet("/users", async (AppDbContext db) => (await db.Users.AsNoTracking().OrderBy(x => x.Name).ToListAsync()).Select(PublicUser));
        admin.MapPost("/users", async (CreateUserInput input, AppDbContext db, PasswordHasher<AppUser> hasher) =>
        {
            var user = MakeUser(input, hasher);
            if (await db.Users.AnyAsync(x => x.Email == user.Email)) throw new InputException("Το email χρησιμοποιείται ήδη.");
            db.Users.Add(user); await db.SaveChangesAsync(); return Results.Ok(PublicUser(user));
        });
        admin.MapPatch("/users/{id:guid}", async (Guid id, UserStatusInput input, ClaimsPrincipal principal, AppDbContext db) =>
        {
            if (id == UserId(principal) && !input.Active) throw new InputException("Δεν μπορείς να απενεργοποιήσεις τον δικό σου λογαριασμό.");
            var user = await db.Users.FindAsync(id); if (user is null) return Results.NotFound();
            user.Active = input.Active; await db.SaveChangesAsync(); return Results.Ok(PublicUser(user));
        });
        admin.MapPost("/questions", async (QuestionInput input, AppDbContext db, WriteGate gate, CancellationToken ct) =>
        {
            await gate.Lock.WaitAsync(ct);
            try
            {
                var title = (input.Title ?? "").Trim(); var labels = (input.Options ?? []).Select(x => (x ?? "").Trim()).ToArray();
                if (input.SelectionMode is not ("Single" or "Multiple")) throw new InputException("Μη έγκυρος τύπος επιλογής.");
                var sectionKey = (input.SectionKey ?? "").Trim();
                var section = sectionKey.Length > 0 ? await db.Questions.FirstOrDefaultAsync(x => x.SectionKey == sectionKey, ct) : null;
                if (sectionKey.Length > 0 && section is null) throw new InputException("Η ενότητα δεν βρέθηκε.");
                if (title.Length is 0 or > 256 || labels.Length is < 1 or > 20 || labels.Any(x => x.Length is 0 or > 128) || labels.Distinct(StringComparer.OrdinalIgnoreCase).Count() != labels.Length) throw new InputException("Γράψε τίτλο έως 256 χαρακτήρες και 1–20 διαφορετικές επιλογές έως 128 χαρακτήρες.");
                if (await db.Questions.CountAsync(x => x.Active, ct) >= 50) throw new InputException("Επιτρέπονται έως 50 ενεργές ερωτήσεις.");
                var question = new SurveyQuestion { Title = title, Required = section is null && input.Required, SectionKey = sectionKey, SectionTitle = section?.SectionTitle ?? "", SelectionMode = input.SelectionMode, Position = section is null ? 10000 + await db.Questions.CountAsync(ct) : 1 + await db.Questions.Where(x => x.SectionKey == sectionKey).MaxAsync(x => x.Position, ct), Options = labels.Select((label,index) => new SurveyOption { Label = label, Position = index }).ToList() };
                db.Questions.Add(question); await db.SaveChangesAsync(ct); return Results.Ok(question.Id);
            }
            finally { gate.Lock.Release(); }
        });
        admin.MapDelete("/questions/{id:guid}", async (Guid id, AppDbContext db, WriteGate gate, CancellationToken ct) =>
        {
            await gate.Lock.WaitAsync(ct);
            try { var question = await db.Questions.FindAsync([id], ct); if (question is null) return Results.NotFound(); question.Active = false; await db.SaveChangesAsync(ct); return Results.NoContent(); }
            finally { gate.Lock.Release(); }
        });
        admin.MapPost("/import", async (HttpContext context, AppDbContext db, ExcelImporter importer, WriteGate gate, CancellationToken ct) =>
        {
            var form = await context.Request.ReadFormAsync(ct); var file = form.Files.GetFile("file");
            if (file is null || file.Length is 0 or > 8 * 1024 * 1024 || !Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)) throw new InputException("Επίλεξε αρχείο .xlsx έως 8 MB.");
            if (!int.TryParse(form["year"], out var year)) throw new InputException("Συμπλήρωσε το έτος του τζίρου.");
            await gate.Lock.WaitAsync(ct);
            try
            {
                using var stream = file.OpenReadStream();
                try { return Results.Ok(await importer.ImportAsync(db, stream, year, ct)); }
                catch (Exception exception) when (exception is not InputException and not DbUpdateException and not OperationCanceledException)
                { app.Logger.LogWarning(exception, "Invalid Excel import"); throw new InputException("Το αρχείο δεν είναι έγκυρο Excel ή δεν μπορεί να διαβαστεί."); }
            }
            finally { gate.Lock.Release(); }
        });
    }
}
