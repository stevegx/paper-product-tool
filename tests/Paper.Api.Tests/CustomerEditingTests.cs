using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace Paper.Api.Tests;

public sealed class CustomerEditingTests
{
    static Dictionary<string,object?> Input(Guid? mutation=null,int version=0)=>new()
    {
        ["mutationId"]=mutation??Guid.NewGuid(),["expectedVersion"]=version,["name"]="Νέο κατάστημα",["taxId"]="099999999",
        ["profession"]="Εστίαση",["address"]="Οδός Δοκιμής 10",["postalCode"]="10558",["city"]="Αθήνα",["phone"]="+30 2101234567",["taxOffice"]="Αθηνών"
    };
    static async Task<HttpResponseMessage> Send(HttpClient client,string path,object body,HttpMethod? method=null)
    {
        var csrf=await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");using var request=new HttpRequestMessage(method??HttpMethod.Post,path){Content=JsonContent.Create(body)};
        request.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());return await client.SendAsync(request);
    }
    static async Task Login(HttpClient client,string email="admin@example.test")=>Assert.Equal(HttpStatusCode.OK,(await Send(client,"/api/auth/login",new{email,password="Integration-2026-Only"})).StatusCode);
    static async Task<HttpClient> Seller(ApiFactory factory,HttpClient admin,string email,string name)
    {
        Assert.Equal(HttpStatusCode.OK,(await Send(admin,"/api/admin/users",new{email,name,password="Integration-2026-Only",role="Seller"})).StatusCode);
        var client=factory.CreateClient();await Login(client,email);return client;
    }
    [Theory][InlineData("name")][InlineData("taxId")][InlineData("profession")][InlineData("address")][InlineData("postalCode")][InlineData("city")][InlineData("phone")][InlineData("taxOffice")]
    public async Task EveryRequiredFieldIsValidatedOnServer(string field)
    {
        using var factory=new ApiFactory();using var client=factory.CreateClient();await Login(client);
        var input=Input();input[field]=" ";Assert.Equal(HttpStatusCode.BadRequest,(await Send(client,"/api/customers",input)).StatusCode);
        Assert.Equal(0,(await client.GetFromJsonAsync<JsonElement>("/api/overview")).GetProperty("customers").GetInt32());
    }
    [Fact] public async Task SellerCreatesOnceAndAnotherSellerCanEditWithVersionChecks()
    {
        using var factory=new ApiFactory();using var admin=factory.CreateClient();await Login(admin);
        using var first=await Seller(factory,admin,"first@example.test","Πωλητής Α");using var second=await Seller(factory,admin,"second@example.test","Πωλητής Β");
        var input=Input();
        var requests=await Task.WhenAll(Enumerable.Range(0,5).Select(_=>Send(first,"/api/customers",input)));
        foreach(var response in requests)Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var created=await requests[0].Content.ReadFromJsonAsync<JsonElement>();var code=created.GetProperty("code").GetString();
        Assert.Equal("Πωλητής Α",created.GetProperty("assignedSeller").GetString());Assert.Equal("",created.GetProperty("customerNotes").GetString());
        Assert.Equal(1,created.GetProperty("customerVersion").GetInt32());Assert.Equal(1,(await first.GetFromJsonAsync<JsonElement>("/api/overview")).GetProperty("customers").GetInt32());
        var results=await second.GetFromJsonAsync<JsonElement>("/api/customers?afm=099999999");Assert.Equal(code,results[0].GetProperty("code").GetString());
        var edit=Input(version:1);edit["name"]="Διορθωμένη επωνυμία";edit["customerNotes"]="Προαιρετική σημείωση πελάτη";
        var edited=await Send(second,$"/api/customers/{code}",edit,HttpMethod.Put);Assert.Equal(HttpStatusCode.OK,edited.StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await Send(second,$"/api/customers/{code}",edit,HttpMethod.Put)).StatusCode);
        var stale=await Send(first,$"/api/customers/{code}",Input(version:1),HttpMethod.Put);Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        var detail=await first.GetFromJsonAsync<JsonElement>($"/api/customers/{code}");var branch=detail.GetProperty("branch");
        Assert.Equal(2,branch.GetProperty("customerVersion").GetInt32());Assert.Equal("Πωλητής Α",branch.GetProperty("assignedSeller").GetString());
        Assert.Equal("Προαιρετική σημείωση πελάτη",branch.GetProperty("customerNotes").GetString());Assert.Equal(0,detail.GetProperty("total").GetDecimal());
        await factory.Seed();detail=await first.GetFromJsonAsync<JsonElement>($"/api/customers/{code}");Assert.Equal("Προαιρετική σημείωση πελάτη",detail.GetProperty("branch").GetProperty("customerNotes").GetString());
    }
    [Fact] public async Task ExistingTaxIdRequiresExplicitSeparateBranchConfirmation()
    {
        using var factory=new ApiFactory();using var client=factory.CreateClient();await Login(client);
        Assert.Equal(HttpStatusCode.OK,(await Send(client,"/api/customers",Input())).StatusCode);
        var next=Input();next["address"]="Άλλη οδός 20";var warning=await Send(client,"/api/customers",next);
        Assert.Equal(HttpStatusCode.Conflict,warning.StatusCode);Assert.Equal("existing_tax_id",(await warning.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        next["allowExistingTaxId"]=true;Assert.Equal(HttpStatusCode.OK,(await Send(client,"/api/customers",next)).StatusCode);
        Assert.Equal(2,(await client.GetFromJsonAsync<JsonElement>("/api/customers?afm=099999999")).GetArrayLength());
    }
    [Fact] public async Task ImportedCustomersCannotBeChangedThroughNewCustomerEditor()
    {
        using var factory=new ApiFactory();await factory.Seed();using var client=factory.CreateClient();await Login(client);
        Assert.Equal(HttpStatusCode.BadRequest,(await Send(client,"/api/customers/BR-01",Input(),HttpMethod.Put)).StatusCode);
    }
    [Fact] public async Task AdditiveUpgradePreservesLegacyCustomersAndCanRunTwice()
    {
        await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE Branches(Code TEXT PRIMARY KEY, TaxId TEXT NOT NULL, Name TEXT NOT NULL, AssignedSeller TEXT NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO Branches VALUES ('OLD','012345678','Existing customer','Seller')");
        await CustomerUpgrade.Run(db);await CustomerUpgrade.Run(db);
        var branch=await db.Branches.SingleAsync();Assert.Equal("Existing customer",branch.Name);Assert.Equal("012345678",branch.TaxId);
        Assert.Equal("",branch.Profession);Assert.Null(branch.CreatedById);Assert.Equal(0,branch.CustomerVersion);
        Assert.Equal(0,await db.CustomerMutations.CountAsync());
    }
}
