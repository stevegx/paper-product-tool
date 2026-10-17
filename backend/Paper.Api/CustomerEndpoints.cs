using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using static Paper.Api.ApiHelpers;

namespace Paper.Api;
public static class CustomerEndpoints
{
    public static void MapCustomerEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();
        api.MapGet("/customers", async (string? afm, AppDbContext db) =>
        {
            if (afm is null || !Regex.IsMatch(afm, "^[0-9]{9}$")) throw new InputException("Συμπλήρωσε ένα ΑΦΜ με ακριβώς 9 ψηφία.");
            return Results.Ok(await db.Branches.AsNoTracking().Where(x => x.TaxId == afm).OrderBy(x => x.Code).ToListAsync());
        });
        api.MapGet("/customers/{code}", async (string code, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var branch = await db.Branches.AsNoTracking().SingleOrDefaultAsync(x => x.Code == code);
            if (branch is null) return Results.NotFound();
            var latestImport = await db.Imports.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync();
            var year = latestImport?.Year ?? 2026;
            var sales = await db.Turnovers.AsNoTracking().Where(x => x.BranchCode == code && x.Year == year).ToListAsync();
            var categories = await db.Categories.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
            var visits = db.Visits.AsNoTracking().Where(x => x.BranchCode == code);
            if (!principal.IsInRole("Admin")) { var id = UserId(principal); visits = visits.Where(x => x.UserId == id); }
            var history = await visits.Include(x => x.User).Include(x => x.Branch).OrderByDescending(x => x.CreatedAtUtc).Take(10).ToListAsync();
            return Results.Ok(new { branch, year, total = sales.Sum(x => x.Amount), categories = categories.Select(x => new { x.Id, x.Name, amount = sales.FirstOrDefault(s => s.CategoryId == x.Id)?.Amount ?? 0m }), history = history.Select(VisitDto) });
        });
        api.MapGet("/survey", async (AppDbContext db) => Results.Ok(await VisitEndpoints.PublishedSurvey(db)));
        api.MapPost("/visits", async (VisitInput input, ClaimsPrincipal principal, AppDbContext db, WriteGate gate, CancellationToken ct) =>
        {
            await gate.Lock.WaitAsync(ct);
            try
            {
                var id = UserId(principal);
                if (input.SubmissionId == Guid.Empty) throw new InputException("Λείπει το αναγνωριστικό αποστολής.");
                var prior = await db.Visits.Include(x => x.User).Include(x => x.Branch).SingleOrDefaultAsync(x => x.UserId == id && x.SubmissionId == input.SubmissionId, ct);
                if (prior is not null) return Results.Ok(VisitDto(prior));
                if (!await db.Branches.AnyAsync(x => x.Code == input.BranchCode, ct)) return Results.NotFound();
                var questions = await Questions(db);
                if (input.SurveyVersion != SurveyVersion(questions)) return Results.Conflict(new { message = "Το ερωτηματολόγιο άλλαξε. Ανανέωσε την καρτέλα πριν την αποστολή." });
                if ((input.Notes ?? "").Length > 2000) throw new InputException("Οι σημειώσεις δέχονται έως 2.000 χαρακτήρες.");
                var answers = input.Answers ?? [];
                var saved = SurveyCatalog.ValidateAnswers(questions, answers);
                if(!saved.Any(a=>a.SelectedOptions.Length>0)&&string.IsNullOrWhiteSpace(input.Notes))throw new InputException("Συμπλήρωσε τουλάχιστον μία ενότητα ή γράψε σημειώσεις. Δεν αποστέλλεται κενή επίσκεψη.");
                var visit = new Visit { BranchCode = input.BranchCode, UserId = id, Notes = (input.Notes ?? "").Trim(), AnswersJson = JsonSerializer.Serialize(saved), SubmissionId = input.SubmissionId };
                db.Visits.Add(visit); await db.SaveChangesAsync(ct);
                await db.Entry(visit).Reference(x => x.User).LoadAsync(ct); await db.Entry(visit).Reference(x => x.Branch).LoadAsync(ct);
                return Results.Ok(VisitDto(visit));
            }
            finally { gate.Lock.Release(); }
        });
        api.MapGet("/visits", async (ClaimsPrincipal principal, AppDbContext db, string? afm, Guid? sellerId, DateOnly? from, DateOnly? to, int page = 1) =>
        {
            if (page < 1 || page > 100000 || (afm is not null && !Regex.IsMatch(afm, "^[0-9]{9}$"))) throw new InputException("Έλεγξε τα φίλτρα αναζήτησης.");
            if (from > to) throw new InputException("Η ημερομηνία έναρξης πρέπει να προηγείται της λήξης.");
            var query = VisitEndpoints.Filter(db, principal, afm, sellerId, from, to);
            var total = await query.CountAsync();
            var customerCount = await query.Select(x => x.BranchCode).Distinct().CountAsync();
            var sellerCount = await query.Select(x => x.UserId).Distinct().CountAsync();
            var items = await query.Include(x => x.Branch).Include(x => x.User).OrderByDescending(x => x.OccurredAtUtc ?? x.CreatedAtUtc).ThenBy(x => x.Id).Skip((page - 1) * 25).Take(25).ToListAsync();
            return Results.Ok(new { total, customerCount, sellerCount, page, pageSize = 25, items = items.Select(VisitDto) });
        });
        api.MapGet("/overview", async (AppDbContext db) => new { customers = await db.Branches.CountAsync(), latestImport = await db.Imports.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync() });
    }
}
