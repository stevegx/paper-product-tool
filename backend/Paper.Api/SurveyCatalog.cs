using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Paper.Api;
public static class SurveyCatalog
{
    // Additive upgrade for existing first-release databases. Preserve all records.
    public static async Task UpgradeAsync(AppDbContext db)
    {
        var columns = new[] { ("SectionKey", 64, ""), ("SectionTitle", 256, ""), ("SelectionMode", 16, "Multiple") };
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            foreach (var (name, length, fallback) in columns)
            {
                await using var check = db.Database.GetDbConnection().CreateCommand();
                check.Transaction = transaction.GetDbTransaction();
                check.CommandText = db.Database.IsSqlite() ? "SELECT COUNT(*) FROM pragma_table_info('Questions') WHERE name = @column" : db.Database.IsNpgsql() ? "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='Questions' AND column_name=@column" : "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'Questions') AND name=@column";
                var parameter = check.CreateParameter(); parameter.ParameterName = "@column"; parameter.Value = name; check.Parameters.Add(parameter);
                if (Convert.ToInt32(await check.ExecuteScalarAsync()) > 0) continue;
                // These identifiers/defaults are fixed literals above, never user input.
                var sql = db.Database.IsSqlite()
                    ? $"ALTER TABLE Questions ADD COLUMN {name} TEXT NOT NULL DEFAULT '{fallback}'"
                    : db.Database.IsNpgsql() ? $"ALTER TABLE \"Questions\" ADD COLUMN \"{name}\" varchar({length}) NOT NULL DEFAULT '{fallback}'"
                    : $"ALTER TABLE Questions ADD {name} nvarchar({length}) NOT NULL CONSTRAINT DF_Questions_{name} DEFAULT N'{fallback}'";
                await db.Database.ExecuteSqlRawAsync(sql);
            }
            await transaction.CommitAsync();
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
    public static async Task InstallAsync(AppDbContext db, string path)
    {
        var catalog = JsonSerializer.Deserialize<Catalog>(await File.ReadAllTextAsync(path), new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidDataException("Invalid survey catalog.");
        var ids = (await db.Questions.Select(x => x.Id).ToListAsync()).ToHashSet();
        foreach (var section in catalog.Sections)
        foreach (var item in section.Questions)
        {
            if (ids.Contains(item.Id)) continue; // Repeated startup preserves archived/edited questions.
            db.Questions.Add(new SurveyQuestion
            {
                Id = item.Id, Title = item.Title, SectionKey = section.Key, SectionTitle = section.Title,
                Position = (section.Position + 1) * 100 + item.Position,
                SelectionMode = item.SelectionMode, Required = false,
                Options = item.Options.Select(option => new SurveyOption { Id = option.Id, Label = option.Label, Position = option.Position }).ToList()
            });
        }
        await db.SaveChangesAsync();
    }
    public static List<SavedAnswer> ValidateAnswers(List<SurveyQuestion> questions, AnswerInput[] answers)
    {
        if (answers.Length > questions.Count || answers.Any(x => x is null) || answers.Select(x => x.QuestionId).Distinct().Count() != answers.Length || answers.Any(x => questions.All(q => q.Id != x.QuestionId)))
            throw new InputException("Οι απαντήσεις περιέχουν άγνωστες ή διπλές ερωτήσεις.");
        var selection = answers.ToDictionary(x => x.QuestionId, x => x.OptionIds ?? []);
        var startedSections = questions.Where(q => q.SectionKey.Length > 0 && selection.GetValueOrDefault(q.Id, []).Length > 0).Select(q => q.SectionKey).ToHashSet();
        var saved = new List<SavedAnswer>();
        foreach (var question in questions)
        {
            var selected = selection.GetValueOrDefault(question.Id, []);
            if (selected.Distinct().Count() != selected.Length || selected.Any(id => question.Options.All(x => x.Id != id))) throw new InputException("Μη έγκυρη επιλογή στο ερωτηματολόγιο.");
            if (question.SelectionMode == "Single" && selected.Length > 1) throw new InputException($"Επίλεξε μόνο μία απάντηση στην ερώτηση «{question.Title}».");
            if (selected.Length == 0 && (question.Required || startedSections.Contains(question.SectionKey)))
                throw new InputException(question.SectionKey.Length > 0 ? $"Ολοκλήρωσε την ενότητα «{question.SectionTitle}»: λείπει η απάντηση «{question.Title}»." : $"Απάντησε στην ερώτηση «{question.Title}».");
            saved.Add(new SavedAnswer(question.Id, question.Title, question.Options.Where(x => selected.Contains(x.Id)).Select(x => x.Label).ToArray(), question.SectionKey, question.SectionTitle, selected));
        }
        return saved;
    }
    private sealed record Catalog(List<CatalogSection> Sections);
    private sealed record CatalogSection(string Key, string Title, int Position, List<CatalogQuestion> Questions);
    private sealed record CatalogQuestion(Guid Id, string Title, string SelectionMode, int Position, List<CatalogOption> Options);
    private sealed record CatalogOption(Guid Id, string Label, int Position);
}
