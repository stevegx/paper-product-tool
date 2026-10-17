using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace Paper.Api.Tests;
public sealed class ImporterTests
{
    [Fact] public void GroupsByCustomerCodeAndKeepsTaxIdLeadingZero()
    {
        using var file=WorkbookFixture.Create(); var parsed=new ExcelImporter().Parse(file,2026);
        Assert.Equal(4,parsed.Branches.Count);Assert.All(parsed.Branches,b=>Assert.Equal("012345678",b.TaxId));
        Assert.Equal(40m,parsed.Turnovers.Where(x=>x.BranchCode=="BR-01").Sum(x=>x.Amount));
        Assert.DoesNotContain(parsed.Turnovers,x=>x.BranchCode=="BR-03");
    }
    [Theory][InlineData(true,false)][InlineData(false,true)]
    public void RejectsAmbiguousSourceInsteadOfSilentlyMerging(bool duplicate,bool conflicting)
    { using var file=WorkbookFixture.Create(duplicate,conflicting);Assert.Throws<InputException>(()=>new ExcelImporter().Parse(file,2026)); }
    [Fact] public async Task ReimportReplacesYearAndPreservesVisitsAndOtherYears()
    {
        await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);await db.Database.EnsureCreatedAsync();
        var importer=new ExcelImporter();using(var first=WorkbookFixture.Create())await importer.ImportAsync(db,first,2025);
        using(var current=WorkbookFixture.Create())await importer.ImportAsync(db,current,2026);
        var user=new AppUser{Email="test@example.test",Name="Test"};db.Users.Add(user);
        db.Visits.Add(new Visit{BranchCode="BR-01",UserId=user.Id,SubmissionId=Guid.NewGuid()});await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        using(var repeat=WorkbookFixture.Create())await importer.ImportAsync(db,repeat,2026);
        Assert.Equal(4,await db.Branches.CountAsync());Assert.Equal(4,await db.Turnovers.CountAsync(x=>x.Year==2026));
        Assert.Equal(4,await db.Turnovers.CountAsync(x=>x.Year==2025));Assert.Equal(1,await db.Visits.CountAsync());
        db.ChangeTracker.Clear();
        using var invalid=WorkbookFixture.Create(duplicateCategory:true);await Assert.ThrowsAsync<InputException>(()=>importer.ImportAsync(db,invalid,2026));
        Assert.Equal(4,await db.Turnovers.CountAsync(x=>x.Year==2026));
    }
}
