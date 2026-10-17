using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Paper.Api.Tests;
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string path;
    private readonly string? postgres;
    private readonly bool keepDatabase;
    public ApiFactory(string? storageId=null, bool keepDatabase=false)
    {
        var id=storageId??Guid.NewGuid().ToString("N");
        path=Path.Combine(Path.GetTempPath(),$"paper-tests-{id}.db");
        this.keepDatabase=keepDatabase;
        if(Environment.GetEnvironmentVariable("PAPER_TEST_POSTGRES") is {Length:>0} admin)
            postgres=new Npgsql.NpgsqlConnectionStringBuilder(admin){Database=$"paper_test_{id}"}.ConnectionString;
    }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?>{
            ["Database:Provider"]=postgres is null?"Sqlite":"Postgres",["ConnectionStrings:Database"]=postgres??$"Data Source={path}",
            ["DATABASE_URL"]=null,["DATABASE_URL_UNPOOLED"]=null,["Database:AllowEphemeralSqlite"]="true",
            ["Bootstrap:Email"]="admin@example.test",["Bootstrap:Password"]="Integration-2026-Only",["Import:Path"]="",["Survey:InstallDefault"]="false"
        }));
    }
    public async Task Seed()
    { using var scope=Services.CreateScope();using var file=WorkbookFixture.Create();await new ExcelImporter().ImportAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(),file,2026); }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if(!disposing||keepDatabase)return;
        if(postgres is not null)
        {
            var builder=new Npgsql.NpgsqlConnectionStringBuilder(postgres);
            var name=builder.Database;builder.Database="postgres";builder.Pooling=false;
            using var connection=new Npgsql.NpgsqlConnection(builder.ConnectionString);connection.Open();
            using var command=connection.CreateCommand();command.CommandText=$"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)";command.ExecuteNonQuery();
        }
        else {Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();foreach(var suffix in new[]{"","-wal","-shm"})if(File.Exists(path+suffix))File.Delete(path+suffix);}
    }
}
public sealed class ApiTests
{
    private static async Task<HttpResponseMessage> Post(HttpClient client,string path,object body)
    {
        var token=await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request=new HttpRequestMessage(HttpMethod.Post,path){Content=JsonContent.Create(body)};
        request.Headers.Add("X-CSRF-TOKEN",token.GetProperty("token").GetString());return await client.SendAsync(request);
    }
    private static async Task Login(HttpClient client,string email="admin@example.test")
    { var result=await Post(client,"/api/auth/login",new{email,password="Integration-2026-Only"});Assert.Equal(HttpStatusCode.OK,result.StatusCode); }
    private static object VisitBody(JsonElement survey,Guid submission,string code="BR-01",object[]? answers=null)=>new{branchCode=code,submissionId=submission,surveyVersion=survey.GetProperty("version").GetString(),notes="Επίσκεψη δοκιμής",answers=answers??[]};

    [Fact] public async Task HealthEndpointIsAvailableWithoutAuthentication()
    {
        using var factory=new ApiFactory();using var client=factory.CreateClient();
        var health=await client.GetFromJsonAsync<JsonElement>("/healthz");
        Assert.Equal("ok",health.GetProperty("status").GetString());
    }
    [Fact] public async Task RequiresAuthenticationAndAntiforgery()
    {
        using var factory=new ApiFactory();using var client=factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/customers?afm=012345678")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/auth/login",new{email="admin@example.test",password="Integration-2026-Only"})).StatusCode);
    }
    [Fact] public async Task SameTaxIdReturnsFourBranchesWithTheirOwnTurnover()
    {
        using var factory=new ApiFactory();await factory.Seed();using var client=factory.CreateClient();await Login(client);
        var branches=await client.GetFromJsonAsync<JsonElement>("/api/customers?afm=012345678");Assert.Equal(4,branches.GetArrayLength());
        var first=await client.GetFromJsonAsync<JsonElement>("/api/customers/BR-01");Assert.Equal(40m,first.GetProperty("total").GetDecimal());
        var zero=await client.GetFromJsonAsync<JsonElement>("/api/customers/BR-03");Assert.Equal(0m,zero.GetProperty("total").GetDecimal());Assert.Equal(2,zero.GetProperty("categories").GetArrayLength());
        var overview=await client.GetFromJsonAsync<JsonElement>("/api/overview");Assert.EndsWith("Z",overview.GetProperty("latestImport").GetProperty("createdAtUtc").GetString());
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync("/api/customers?afm=123")).StatusCode);
        Assert.Equal(0,(await client.GetFromJsonAsync<JsonElement>("/api/customers?afm=999999999")).GetArrayLength());
    }
    [Fact] public async Task VisitRetryDoesNotDuplicateAndSnapshotSurvivesQuestionArchival()
    {
        using var factory=new ApiFactory();await factory.Seed();using var client=factory.CreateClient();await Login(client);
        Assert.Equal(HttpStatusCode.OK,(await Post(client,"/api/admin/questions",new{title="Χώροι",required=true,options=new[]{"Κουζίνα","Τραπεζαρία"}})).StatusCode);
        var survey=await client.GetFromJsonAsync<JsonElement>("/api/survey");var question=survey.GetProperty("questions")[0];
        var id=Guid.NewGuid();var body=VisitBody(survey,id,answers:[new{questionId=question.GetProperty("id").GetGuid(),optionIds=new[]{question.GetProperty("options")[0].GetProperty("id").GetGuid()}}]);
        Assert.Equal(HttpStatusCode.OK,(await Post(client,"/api/visits",body)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await Post(client,"/api/visits",body)).StatusCode);
        var token=await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");using var delete=new HttpRequestMessage(HttpMethod.Delete,$"/api/admin/questions/{question.GetProperty("id").GetString()}");delete.Headers.Add("X-CSRF-TOKEN",token.GetProperty("token").GetString());Assert.Equal(HttpStatusCode.NoContent,(await client.SendAsync(delete)).StatusCode);
        var history=await client.GetFromJsonAsync<JsonElement>("/api/visits");Assert.Equal(1,history.GetProperty("total").GetInt32());
        Assert.Equal("Κουζίνα",history.GetProperty("items")[0].GetProperty("answers")[0].GetProperty("selectedOptions")[0].GetString());
        Assert.Equal(HttpStatusCode.Conflict,(await Post(client,"/api/visits",VisitBody(survey,Guid.NewGuid()))).StatusCode);
    }
    [Fact] public async Task SellerCannotManageUsersOrSeeOtherSellersVisits()
    {
        using var factory=new ApiFactory();await factory.Seed();using var admin=factory.CreateClient();await Login(admin);
        var survey=await admin.GetFromJsonAsync<JsonElement>("/api/survey");Assert.Equal(HttpStatusCode.OK,(await Post(admin,"/api/visits",VisitBody(survey,Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await Post(admin,"/api/admin/users",new{email="seller@example.test",name="Πωλητής",password="Integration-2026-Only",role="Seller"})).StatusCode);
        using var seller=factory.CreateClient();await Login(seller,"seller@example.test");
        Assert.Equal(HttpStatusCode.Forbidden,(await seller.GetAsync("/api/admin/users")).StatusCode);
        var own=await seller.GetFromJsonAsync<JsonElement>("/api/visits");Assert.Equal(0,own.GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.OK,(await Post(seller,"/api/visits",VisitBody(survey,Guid.NewGuid(),"BR-02"))).StatusCode);
        var all=await admin.GetFromJsonAsync<JsonElement>("/api/visits?afm=012345678");Assert.Equal(2,all.GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.GetAsync("/api/visits?from=2026-11-01&to=2026-10-01")).StatusCode);
    }
    [Fact] public async Task RequiredQuestionAndUnknownOptionAreRejected()
    {
        using var factory=new ApiFactory();await factory.Seed();using var client=factory.CreateClient();await Login(client);
        await Post(client,"/api/admin/questions",new{title="Υποχρεωτική",required=true,options=new[]{"Ναι"}});
        var survey=await client.GetFromJsonAsync<JsonElement>("/api/survey");
        Assert.Equal(HttpStatusCode.BadRequest,(await Post(client,"/api/visits",VisitBody(survey,Guid.NewGuid()))).StatusCode);
        var question=survey.GetProperty("questions")[0];
        Assert.Equal(HttpStatusCode.BadRequest,(await Post(client,"/api/visits",VisitBody(survey,Guid.NewGuid(),answers:[new{questionId=question.GetProperty("id").GetGuid(),optionIds=new[]{Guid.NewGuid()}}]))).StatusCode);
    }
    [Fact] public async Task DisablingSellerRevokesExistingSession()
    {
        using var factory=new ApiFactory();using var admin=factory.CreateClient();await Login(admin);
        var created=await Post(admin,"/api/admin/users",new{email="disabled@example.test",name="Πωλητής",password="Integration-2026-Only",role="Seller"});
        var account=await created.Content.ReadFromJsonAsync<JsonElement>();
        using var seller=factory.CreateClient();await Login(seller,"disabled@example.test");
        var token=await admin.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var patch=new HttpRequestMessage(HttpMethod.Patch,$"/api/admin/users/{account.GetProperty("id").GetGuid()}"){Content=JsonContent.Create(new{active=false})};patch.Headers.Add("X-CSRF-TOKEN",token.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.OK,(await admin.SendAsync(patch)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await seller.GetAsync("/api/overview")).StatusCode);
    }
}
