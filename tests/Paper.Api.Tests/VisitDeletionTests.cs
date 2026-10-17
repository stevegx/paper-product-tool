using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Xunit;

namespace Paper.Api.Tests;
public sealed class VisitDeletionTests
{
    static async Task<HttpResponseMessage> Send(HttpClient client,string path,object body,HttpMethod? method=null)
    {
        var token=await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request=new HttpRequestMessage(method??HttpMethod.Post,path){Content=JsonContent.Create(body)};
        request.Headers.Add("X-CSRF-TOKEN",token.GetProperty("token").GetString());return await client.SendAsync(request);
    }
    static async Task Login(HttpClient client,string email="admin@example.test")=>
        Assert.Equal(HttpStatusCode.OK,(await Send(client,"/api/auth/login",new{email,password="Integration-2026-Only"})).StatusCode);
    static object Body(JsonElement survey,Guid mutation,string notes,int version=0)=>new{
        mutationId=mutation,expectedVersion=version,branchCode="BR-01",surveyVersion=survey.GetProperty("version").GetString(),
        occurredAtUtc=DateTime.UtcNow,notes,answers=Array.Empty<object>()};

    [Fact] public async Task EmptyVisitIsRejectedByBothApisButNotesOnlyIsAccepted()
    {
        using var f=new ApiFactory();await f.Seed();using var c=f.CreateClient();await Login(c);
        var survey=await c.GetFromJsonAsync<JsonElement>("/api/survey");
        Assert.Equal(HttpStatusCode.BadRequest,(await Send(c,$"/api/visits/{Guid.NewGuid()}",Body(survey,Guid.NewGuid()," \n "),HttpMethod.Put)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Send(c,"/api/visits",new{branchCode="BR-01",submissionId=Guid.NewGuid(),surveyVersion=survey.GetProperty("version").GetString(),notes="  ",answers=Array.Empty<object>()})).StatusCode);
        Assert.Equal(0,(await c.GetFromJsonAsync<JsonElement>("/api/visits")).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.OK,(await Send(c,$"/api/visits/{Guid.NewGuid()}",Body(survey,Guid.NewGuid(),"Επίσκεψη μόνο με σημειώσεις"),HttpMethod.Put)).StatusCode);
    }

    [Fact] public async Task DeletionIsVersionedReversibleAndOldOfflineDeliveryCannotResurrectIt()
    {
        using var f=new ApiFactory();await f.Seed();using var c=f.CreateClient();await Login(c);
        var survey=await c.GetFromJsonAsync<JsonElement>("/api/survey");var id=Guid.NewGuid();var original=Body(survey,Guid.NewGuid(),"Σημείωση");
        Assert.Equal(HttpStatusCode.OK,(await Send(c,$"/api/visits/{id}",original,HttpMethod.Put)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Send(c,$"/api/visits/{id}",new{expectedVersion=0},HttpMethod.Delete)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await Send(c,$"/api/visits/{id}",new{expectedVersion=1},HttpMethod.Delete)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await Send(c,$"/api/visits/{id}",new{expectedVersion=1},HttpMethod.Delete)).StatusCode);
        Assert.Equal(0,(await c.GetFromJsonAsync<JsonElement>("/api/visits")).GetProperty("total").GetInt32());
        Assert.Equal(0,(await c.GetFromJsonAsync<JsonElement>("/api/customers/BR-01")).GetProperty("history").GetArrayLength());
        Assert.Equal(0,(await c.GetFromJsonAsync<JsonElement>("/api/offline/snapshot")).GetProperty("editors").GetArrayLength());
        Assert.Equal(HttpStatusCode.Gone,(await c.GetAsync($"/api/visits/{id}/editor")).StatusCode);
        Assert.Equal(HttpStatusCode.Gone,(await Send(c,$"/api/visits/{id}",original,HttpMethod.Put)).StatusCode);
        Assert.Equal(HttpStatusCode.Gone,(await Send(c,$"/api/visits/{id}",Body(survey,Guid.NewGuid(),"Παλιά offline αλλαγή",1),HttpMethod.Put)).StatusCode);
        var export=await c.GetAsync("/api/admin/visits/export");using var stream=await export.Content.ReadAsStreamAsync();using var book=new XLWorkbook(stream);Assert.Equal(1,book.Worksheet(1).LastRowUsed()!.RowNumber());
        var restored=await Send(c,$"/api/visits/{id}/restore",new{expectedVersion=2});Assert.Equal(HttpStatusCode.OK,restored.StatusCode);Assert.Equal(3,(await restored.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetInt32());
        Assert.Equal(1,(await c.GetFromJsonAsync<JsonElement>("/api/visits")).GetProperty("total").GetInt32());
        Assert.Equal(2,(await c.GetFromJsonAsync<JsonElement>($"/api/visits/{id}/revisions")).GetArrayLength());
    }

    [Fact] public async Task SellersDeleteOnlyTheirOwnVisitsAndAdminCanDeleteAnyones()
    {
        using var f=new ApiFactory();await f.Seed();using var admin=f.CreateClient();await Login(admin);
        foreach(var name in new[]{"one","two"})await Send(admin,"/api/admin/users",new{email=$"{name}@example.test",name,password="Integration-2026-Only",role="Seller"});
        using var one=f.CreateClient();await Login(one,"one@example.test");using var two=f.CreateClient();await Login(two,"two@example.test");
        var survey=await one.GetFromJsonAsync<JsonElement>("/api/survey");var id=Guid.NewGuid();await Send(one,$"/api/visits/{id}",Body(survey,Guid.NewGuid(),"Δική μου επίσκεψη"),HttpMethod.Put);
        Assert.Equal(HttpStatusCode.Forbidden,(await Send(two,$"/api/visits/{id}",new{expectedVersion=1},HttpMethod.Delete)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await Send(one,$"/api/visits/{id}",new{expectedVersion=1},HttpMethod.Delete)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await Send(two,$"/api/visits/{id}/restore",new{expectedVersion=2})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await Send(one,$"/api/visits/{id}/restore",new{expectedVersion=2})).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await Send(admin,$"/api/visits/{id}",new{expectedVersion=3},HttpMethod.Delete)).StatusCode);
    }
}
