using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
namespace Paper.Api;

public static class CustomerUpgrade
{
    public static async Task Run(AppDbContext db)
    {
        var sqlite=db.Database.IsSqlite();var pg=db.Database.IsNpgsql();
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var tx=await db.Database.BeginTransactionAsync();
            var columns=new Dictionary<string,string>();
            foreach(var (name,length) in new[]{("Profession",256),("Address",512),("PostalCode",5),("City",128),("Phone",32),("TaxOffice",128),("CustomerNotes",2000)})
                columns[name]=sqlite?"TEXT NOT NULL DEFAULT ''":pg?$"varchar({length}) NOT NULL DEFAULT ''":$"nvarchar({length}) NOT NULL DEFAULT N''";
            columns["CreatedById"]=sqlite?"TEXT NULL":pg?"uuid NULL":"uniqueidentifier NULL";
            columns["CustomerCreatedAtUtc"]=columns["CustomerUpdatedAtUtc"]=sqlite?"TEXT NULL":pg?"timestamp with time zone NULL":"datetime2 NULL";
            columns["CustomerVersion"]="int NOT NULL DEFAULT 0";
            foreach(var (name,type) in columns)
            {
                await using var check=db.Database.GetDbConnection().CreateCommand();check.Transaction=tx.GetDbTransaction();
                check.CommandText=sqlite?"SELECT COUNT(*) FROM pragma_table_info('Branches') WHERE name=@name":pg?"SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='Branches' AND column_name=@name":"SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'Branches') AND name=@name";
                var parameter=check.CreateParameter();parameter.ParameterName="@name";parameter.Value=name;check.Parameters.Add(parameter);
                if(Convert.ToInt32(await check.ExecuteScalarAsync())==0)
                    await db.Database.ExecuteSqlRawAsync(pg?$"ALTER TABLE \"Branches\" ADD COLUMN \"{name}\" {type}":$"ALTER TABLE Branches ADD {name} {type}");
            }
            var sql=sqlite?"CREATE TABLE IF NOT EXISTS CustomerMutations (Id TEXT PRIMARY KEY, BodyHash TEXT NOT NULL, ResultJson TEXT NOT NULL)":pg?"CREATE TABLE IF NOT EXISTS \"CustomerMutations\" (\"Id\" varchar(80) PRIMARY KEY, \"BodyHash\" varchar(64) NOT NULL, \"ResultJson\" text NOT NULL)":"IF OBJECT_ID(N'CustomerMutations',N'U') IS NULL CREATE TABLE CustomerMutations (Id nvarchar(80) NOT NULL PRIMARY KEY, BodyHash nvarchar(64) NOT NULL, ResultJson nvarchar(max) NOT NULL)";
            await db.Database.ExecuteSqlRawAsync(sql);await tx.CommitAsync();
        }
        finally {await db.Database.CloseConnectionAsync();}
    }
}
