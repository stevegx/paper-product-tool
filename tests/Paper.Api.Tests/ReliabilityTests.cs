using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Xunit;

namespace Paper.Api.Tests;
public sealed class ReliabilityTests
{
    static async Task<HttpResponseMessage> Send(HttpClient c,string path,object body,HttpMethod? method=null)
    {
        var token=await c.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request=new HttpRequestMessage(method??HttpMethod.Post,path){Content=JsonContent.Create(body)};
        request.Headers.Add("X-CSRF-TOKEN",token.GetProperty("token").GetString());return await c.SendAsync(request);
    }
    static async Task Login(HttpClient c,string email="admin@example.test")=>Assert.Equal(HttpStatusCode.OK,(await Send(c,"/api/auth/login",new{email,password="Integration-2026-Only"})).StatusCode);
    static object Body(JsonElement survey,Guid mutation,int version=0,string notes="Πρώτη απάντηση",object[]? answers=null)=>new{mutationId=mutation,expectedVersion=version,branchCode="BR-01",surveyVersion=survey.GetProperty("version").GetString(),occurredAtUtc=DateTime.UtcNow.Date.AddDays(-1),notes,answers=answers??[]};
    [Fact] public async Task LostAcknowledgementAndConcurrentClicksCreateOneVisitAndEditsHaveVersions()
    {
        using var f=new ApiFactory();await f.Seed();using var c=f.CreateClient();await Login(c);
        var survey=await c.GetFromJsonAsync<JsonElement>("/api/survey");var id=Guid.NewGuid();var mutation=Guid.NewGuid();var body=Body(survey,mutation);
        var responses=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>Send(c,$"/api/visits/{id}",body,HttpMethod.Put)));
        foreach(var response in responses){Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal(1,(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetInt32());}
        Assert.Equal(1,(await c.GetFromJsonAsync<JsonElement>("/api/visits")).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict,(await Send(c,$"/api/visits/{id}",Body(survey,mutation,notes:"Changed payload"),HttpMethod.Put)).StatusCode);
        var update=await Send(c,$"/api/visits/{id}",Body(survey,Guid.NewGuid(),1,"Διορθώθηκε"),HttpMethod.Put);Assert.Equal(HttpStatusCode.OK,update.StatusCode);
        Assert.Equal(2,(await update.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict,(await Send(c,$"/api/visits/{id}",Body(survey,Guid.NewGuid(),1,"Παλαιό πρόχειρο"),HttpMethod.Put)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await Send(c,$"/api/visits/{id}",body,HttpMethod.Put)).StatusCode);
        var editor=await c.GetFromJsonAsync<JsonElement>($"/api/visits/{id}/editor");Assert.Equal("Διορθώθηκε",editor.GetProperty("visit").GetProperty("notes").GetString());
        var revisions=await c.GetFromJsonAsync<JsonElement>($"/api/visits/{id}/revisions");Assert.Equal(1,revisions.GetArrayLength());Assert.Contains("Πρώτη απάντηση",JsonSerializer.Deserialize<JsonElement>(revisions[0].GetProperty("beforeJson").GetString()!).GetProperty("notes").GetString());
    }
    [Fact] public async Task AdminQuestionChangesPreserveOfflineAndHistoricQuestionnaires()
    {
        using var f=new ApiFactory();await f.Seed();using var c=f.CreateClient();await Login(c);
        await Send(c,"/api/admin/questions",new{title="Αρχικός τίτλος",required=false,options=new[]{"Αρχική επιλογή","Άλλη επιλογή"}});
        var old=await c.GetFromJsonAsync<JsonElement>("/api/survey");var q=old.GetProperty("questions")[0];var option=q.GetProperty("options")[0];
        var change=new{expectedSurveyVersion=old.GetProperty("version").GetString(),title="Νέος τίτλος",required=false,selectionMode="Single",sectionKey="",options=new[]{new{id=option.GetProperty("id").GetGuid(),label="Νέα επιλογή"}}};
        Assert.Equal(HttpStatusCode.OK,(await Send(c,$"/api/admin/questions/{q.GetProperty("id").GetGuid()}",change,HttpMethod.Put)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Send(c,$"/api/admin/questions/{q.GetProperty("id").GetGuid()}",change,HttpMethod.Put)).StatusCode);
        var answers=new object[]{new{questionId=q.GetProperty("id").GetGuid(),optionIds=new[]{option.GetProperty("id").GetGuid()}}};var id=Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK,(await Send(c,$"/api/visits/{id}",Body(old,Guid.NewGuid(),answers:answers),HttpMethod.Put)).StatusCode);
        var editor=await c.GetFromJsonAsync<JsonElement>($"/api/visits/{id}/editor");Assert.Equal("Αρχικός τίτλος",editor.GetProperty("survey").GetProperty("questions")[0].GetProperty("title").GetString());Assert.Equal("Αρχική επιλογή",editor.GetProperty("visit").GetProperty("answers")[0].GetProperty("selectedOptions")[0].GetString());
        Assert.Equal(HttpStatusCode.OK,(await Send(c,$"/api/visits/{id}",Body(old,Guid.NewGuid(),1,"Διόρθωση παλιάς επίσκεψης",answers),HttpMethod.Put)).StatusCode);
        Assert.Equal("Νέος τίτλος",(await c.GetFromJsonAsync<JsonElement>("/api/survey")).GetProperty("questions")[0].GetProperty("title").GetString());
    }
    [Fact] public async Task ExportIncludesEveryFilteredVisitAndPreservesLeadingZeros()
    {
        using var f=new ApiFactory();await f.Seed();using var c=f.CreateClient();await Login(c);var survey=await c.GetFromJsonAsync<JsonElement>("/api/survey");
        for(var i=0;i<27;i++)Assert.Equal(HttpStatusCode.OK,(await Send(c,$"/api/visits/{Guid.NewGuid()}",Body(survey,Guid.NewGuid(),notes:$"Visit {i}"),HttpMethod.Put)).StatusCode);
        var page=await c.GetFromJsonAsync<JsonElement>("/api/visits");Assert.Equal(25,page.GetProperty("items").GetArrayLength());
        var response=await c.GetAsync("/api/admin/visits/export?afm=012345678");Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        using var stream=await response.Content.ReadAsStreamAsync();using var book=new XLWorkbook(stream);Assert.Equal(28,book.Worksheet("Επισκέψεις").LastRowUsed()!.RowNumber());Assert.Equal("012345678",book.Worksheet(1).Cell(2,4).GetString());Assert.Equal(2,book.Worksheets.Count);
        var empty=await c.GetAsync("/api/admin/visits/export?afm=999999999");using var emptyStream=await empty.Content.ReadAsStreamAsync();using var emptyBook=new XLWorkbook(emptyStream);Assert.Equal(1,emptyBook.Worksheet(1).LastRowUsed()!.RowNumber());
        var latest=await c.GetAsync("/api/admin/visits/export?afm=012345678&latestOnly=true");using var latestStream=await latest.Content.ReadAsStreamAsync();using var latestBook=new XLWorkbook(latestStream);Assert.Equal(2,latestBook.Worksheet(1).LastRowUsed()!.RowNumber());
    }
    [Fact] public async Task AdminCanAddNewOptionsWhileEditingAndPreserveOldAnswers()
    {
        using var f=new ApiFactory();await f.Seed();using var c=f.CreateClient();await Login(c);
        Assert.Equal(HttpStatusCode.OK,(await Send(c,"/api/admin/questions",new{title="Χώροι",required=false,options=new[]{"Κουζίνα","Τραπεζαρία"}})).StatusCode);
        var before=await c.GetFromJsonAsync<JsonElement>("/api/survey");var question=before.GetProperty("questions")[0];
        var questionId=question.GetProperty("id").GetGuid();var optionId=question.GetProperty("options")[0].GetProperty("id").GetGuid();
        var visitId=Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK,(await Send(c,$"/api/visits/{visitId}",Body(before,Guid.NewGuid(),answers:new object[]{new{questionId,optionIds=new[]{optionId}}}),HttpMethod.Put)).StatusCode);
        var change=new{expectedSurveyVersion=before.GetProperty("version").GetString(),title="Διαθέσιμοι χώροι",required=false,selectionMode="Multiple",sectionKey="",options=new[]{new{id=optionId,label="Χώρος κουζίνας"},new{id=Guid.Empty,label="Τουαλέτα"},new{id=Guid.Empty,label="Αποθήκη"}}};
        var response=await Send(c,$"/api/admin/questions/{questionId}",change,HttpMethod.Put);
        Assert.True(response.StatusCode==HttpStatusCode.OK,await response.Content.ReadAsStringAsync());
        var after=await c.GetFromJsonAsync<JsonElement>("/api/survey");var options=after.GetProperty("questions")[0].GetProperty("options");
        Assert.Equal(3,options.GetArrayLength());Assert.Equal(optionId,options[0].GetProperty("id").GetGuid());
        Assert.All(options.EnumerateArray(),o=>Assert.NotEqual(Guid.Empty,o.GetProperty("id").GetGuid()));
        Assert.Equal(3,options.EnumerateArray().Select(o=>o.GetProperty("id").GetGuid()).Distinct().Count());
        Assert.Equal(HttpStatusCode.Conflict,(await Send(c,$"/api/admin/questions/{questionId}",change,HttpMethod.Put)).StatusCode);
        var history=await c.GetFromJsonAsync<JsonElement>($"/api/visits/{visitId}/editor");
        Assert.Equal("Χώροι",history.GetProperty("survey").GetProperty("questions")[0].GetProperty("title").GetString());
        Assert.Equal("Κουζίνα",history.GetProperty("visit").GetProperty("answers")[0].GetProperty("selectedOptions")[0].GetString());
    }
    [Fact] public async Task AdminCanCorrectSellerVisitAndOtherSellersCannot()
    {
        using var f=new ApiFactory();await f.Seed();using var admin=f.CreateClient();await Login(admin);
        foreach(var n in new[]{"one","two"})await Send(admin,"/api/admin/users",new{email=$"{n}@example.test",name=n,password="Integration-2026-Only",role="Seller"});
        using var seller=f.CreateClient();await Login(seller,"one@example.test");using var other=f.CreateClient();await Login(other,"two@example.test");var survey=await seller.GetFromJsonAsync<JsonElement>("/api/survey");var id=Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK,(await Send(seller,$"/api/visits/{id}",Body(survey,Guid.NewGuid()),HttpMethod.Put)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await other.GetAsync($"/api/visits/{id}/editor")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await Send(other,$"/api/visits/{id}",Body(survey,Guid.NewGuid(),1),HttpMethod.Put)).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await seller.GetAsync("/api/admin/visits/export")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await Send(admin,$"/api/visits/{id}",Body(survey,Guid.NewGuid(),1,"Διόρθωση admin"),HttpMethod.Put)).StatusCode);
        var snapshot=await other.GetFromJsonAsync<JsonElement>("/api/offline/snapshot");Assert.Equal(0,snapshot.GetProperty("editors").GetArrayLength());Assert.Equal(4,snapshot.GetProperty("customers").GetArrayLength());
        var sellerVisit=await seller.GetFromJsonAsync<JsonElement>($"/api/visits/{id}/editor");
        var ownerId=sellerVisit.GetProperty("visit").GetProperty("userId").GetGuid();
        Assert.Equal(HttpStatusCode.OK,(await Send(other,$"/api/visits/{Guid.NewGuid()}",Body(survey,Guid.NewGuid()),HttpMethod.Put)).StatusCode);
        var all=await admin.GetFromJsonAsync<JsonElement>("/api/visits");Assert.Equal(2,all.GetProperty("total").GetInt32());
        var mine=await seller.GetFromJsonAsync<JsonElement>("/api/visits");Assert.Equal(1,mine.GetProperty("total").GetInt32());Assert.Equal(id,mine.GetProperty("items")[0].GetProperty("id").GetGuid());
        // A seller-supplied filter cannot expand visibility to another account.
        var forged=await other.GetFromJsonAsync<JsonElement>($"/api/visits?sellerId={ownerId}");Assert.Equal(1,forged.GetProperty("total").GetInt32());Assert.NotEqual(id,forged.GetProperty("items")[0].GetProperty("id").GetGuid());
        var filtered=await admin.GetFromJsonAsync<JsonElement>($"/api/visits?sellerId={ownerId}");Assert.Equal(1,filtered.GetProperty("total").GetInt32());Assert.Equal(id,filtered.GetProperty("items")[0].GetProperty("id").GetGuid());
    }
}
