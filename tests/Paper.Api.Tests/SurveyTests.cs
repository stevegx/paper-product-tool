using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Paper.Api.Tests;
public sealed class SurveyTests
{
    private static List<SurveyQuestion> Sample() => [
        new() { Id=Guid.NewGuid(),Title="Πρώτη",SectionKey="kitchen",SectionTitle="Κουζίνα",SelectionMode="Multiple",Options=[new(){Label="Α"},new(){Label="Β"}] },
        new() { Id=Guid.NewGuid(),Title="Δεύτερη",SectionKey="kitchen",SectionTitle="Κουζίνα",SelectionMode="Single",Options=[new(){Label="Γ"},new(){Label="Δ"}] },
        new() { Id=Guid.NewGuid(),Title="Τρίτη",SectionKey="wc",SectionTitle="Τουαλέτες",SelectionMode="Single",Options=[new(){Label="Ναι"}] }
    ];
    [Fact] public void CompletelyUntouchedSectionsCanBeSkipped()
    { var saved=SurveyCatalog.ValidateAnswers(Sample(),[]);Assert.Equal(3,saved.Count);Assert.All(saved,a=>Assert.Empty(a.SelectedOptions)); }
    [Fact] public void OneAnswerRequiresRestOfThatSectionOnly()
    {
        var q=Sample();var partial=new[]{new AnswerInput(q[0].Id,[q[0].Options[0].Id])};
        var error=Assert.Throws<InputException>(()=>SurveyCatalog.ValidateAnswers(q,partial));Assert.Contains("Κουζίνα",error.Message);
        var complete=SurveyCatalog.ValidateAnswers(q,[partial[0],new AnswerInput(q[1].Id,[q[1].Options[0].Id])]);
        Assert.Empty(complete[2].SelectedOptions);Assert.Equal("Κουζίνα",complete[0].SectionTitle);
    }
    [Fact] public void SingleChoiceRejectsTwoOptionsAndMultipleChoiceKeepsBoth()
    {
        var q=Sample();
        Assert.Throws<InputException>(()=>SurveyCatalog.ValidateAnswers(q,[new(q[0].Id,[q[0].Options[0].Id]),new(q[1].Id,q[1].Options.Select(x=>x.Id).ToArray())]));
        var saved=SurveyCatalog.ValidateAnswers(q,[new(q[0].Id,q[0].Options.Select(x=>x.Id).ToArray()),new(q[1].Id,[q[1].Options[0].Id])]);
        Assert.Equal(2,saved[0].SelectedOptions.Length);
    }
    [Fact] public void HistoricalAnswersWithoutSectionMetadataStillDeserialize()
    {var id=Guid.NewGuid();var old=JsonSerializer.Serialize(new[]{new {QuestionId=id,Title="Παλιά ερώτηση",SelectedOptions=new[]{"Ναι"}}});var saved=JsonSerializer.Deserialize<SavedAnswer[]>(old)!;Assert.Equal("Ναι",saved[0].SelectedOptions[0]);Assert.Equal("",saved[0].SectionKey);}
    [Fact] public async Task UpgradePreservesOldQuestionsAndCanRunTwice()
    {
        await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE Questions (Id TEXT PRIMARY KEY, Title TEXT NOT NULL, Required INTEGER NOT NULL, Active INTEGER NOT NULL, Position INTEGER NOT NULL)");
        var id=Guid.NewGuid();await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Questions VALUES ({id}, {"Παλιά"}, {false}, {true}, {0})");
        await SurveyCatalog.UpgradeAsync(db);await SurveyCatalog.UpgradeAsync(db);
        var question=await db.Questions.SingleAsync();Assert.Equal(id,question.Id);Assert.Equal("Παλιά",question.Title);Assert.Equal("Multiple",question.SelectionMode);Assert.Equal("",question.SectionKey);
    }
    [Fact] public async Task CatalogHasExactContentAndRepeatedInstallPreservesArchival()
    {
        await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);await db.Database.EnsureCreatedAsync();
        using var factory=new ApiFactory();var environment=factory.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>();
        var path=Path.Combine(environment.ContentRootPath,"Surveys","store-visit-v1.json");
        await SurveyCatalog.InstallAsync(db,path);
        Assert.Equal(35,await db.Questions.CountAsync());Assert.Equal(8,await db.Questions.Select(q=>q.SectionKey).Distinct().CountAsync());
        Assert.Equal(116,await db.Set<SurveyOption>().CountAsync());Assert.Equal(1,await db.Questions.CountAsync(q=>q.SelectionMode=="Multiple"));
        Assert.Equal(34,await db.Questions.CountAsync(q=>q.SelectionMode=="Single"));
        var first=await db.Questions.OrderBy(q=>q.Position).FirstAsync();first.Active=false;await db.SaveChangesAsync();
        await SurveyCatalog.InstallAsync(db,path);Assert.Equal(35,await db.Questions.CountAsync());Assert.False(first.Active);
    }
}
