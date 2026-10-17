using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
namespace Paper.Api;
public static class VisitUpgrade
{
    public static async Task Run(AppDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var sqlite = db.Database.IsSqlite();
            var columns = new Dictionary<string,string>
            {
                ["Version"] = "int NOT NULL DEFAULT 1", ["OccurredAtUtc"] = sqlite ? "TEXT NULL" : "datetime2 NULL",
                ["UpdatedAtUtc"] = sqlite ? "TEXT NULL" : "datetime2 NULL", ["LastEditedById"] = sqlite ? "TEXT NULL" : "uniqueidentifier NULL",
                ["SurveyJson"] = sqlite ? "TEXT NOT NULL DEFAULT ''" : "nvarchar(max) NOT NULL DEFAULT N''",
                ["IsDeleted"] = sqlite ? "INTEGER NOT NULL DEFAULT 0" : "bit NOT NULL DEFAULT 0",
                ["DeletedAtUtc"] = sqlite ? "TEXT NULL" : "datetime2 NULL",
                ["DeletedById"] = sqlite ? "TEXT NULL" : "uniqueidentifier NULL"
            };
            foreach (var (name,type) in columns)
            {
                await using var check = db.Database.GetDbConnection().CreateCommand(); check.Transaction = tx.GetDbTransaction();
                check.CommandText = sqlite ? "SELECT COUNT(*) FROM pragma_table_info('Visits') WHERE name=@name" : "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'Visits') AND name=@name";
                var parameter=check.CreateParameter();parameter.ParameterName="@name";parameter.Value=name;check.Parameters.Add(parameter);
                if (Convert.ToInt32(await check.ExecuteScalarAsync()) == 0)
                {
                    // Identifiers and SQL types come exclusively from the fixed dictionary above.
                    var statement = $"ALTER TABLE Visits ADD {name} {type}";
                    await db.Database.ExecuteSqlRawAsync(statement);
                }
            }
            var tables = new Dictionary<string,string>
            {
                ["SurveyReleases"] = sqlite ? "Id TEXT PRIMARY KEY, QuestionsJson TEXT NOT NULL" : "Id nvarchar(64) NOT NULL PRIMARY KEY, QuestionsJson nvarchar(max) NOT NULL",
                ["VisitMutations"] = sqlite ? "Id TEXT PRIMARY KEY, BodyHash TEXT NOT NULL, ResultJson TEXT NOT NULL" : "Id nvarchar(80) NOT NULL PRIMARY KEY, BodyHash nvarchar(64) NOT NULL, ResultJson nvarchar(max) NOT NULL",
                ["VisitRevisions"] = sqlite ? "Id TEXT PRIMARY KEY, VisitId TEXT NOT NULL, EditorId TEXT NOT NULL, EditorName TEXT NOT NULL, ChangedAtUtc TEXT NOT NULL, BeforeJson TEXT NOT NULL, AfterJson TEXT NOT NULL" : "Id uniqueidentifier NOT NULL PRIMARY KEY, VisitId uniqueidentifier NOT NULL, EditorId uniqueidentifier NOT NULL, EditorName nvarchar(128) NOT NULL, ChangedAtUtc datetime2 NOT NULL, BeforeJson nvarchar(max) NOT NULL, AfterJson nvarchar(max) NOT NULL"
            };
            foreach (var (name,definition) in tables)
                await db.Database.ExecuteSqlRawAsync(sqlite ? $"CREATE TABLE IF NOT EXISTS {name} ({definition})" : $"IF OBJECT_ID(N'{name}', N'U') IS NULL CREATE TABLE {name} ({definition})");
            await tx.CommitAsync();
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}
