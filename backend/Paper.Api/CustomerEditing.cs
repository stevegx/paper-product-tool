using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using static Paper.Api.ApiHelpers;
namespace Paper.Api;

public sealed record CustomerInput(Guid MutationId,int ExpectedVersion,string? Name,string? TaxId,string? Profession,string? Address,string? PostalCode,string? City,string? Phone,string? TaxOffice,string? CustomerNotes,bool AllowExistingTaxId=false);
public static class CustomerEditing
{
    public static void MapCustomerEditing(this WebApplication app)
    {
        var api=app.MapGroup("/api").RequireAuthorization();
        api.MapPost("/customers",(CustomerInput input,ClaimsPrincipal user,AppDbContext db,WriteGate gate,CancellationToken ct)=>Save($"NEW-{input.MutationId:N}",input,true,user,db,gate,ct));
        api.MapPut("/customers/{code}",(string code,CustomerInput input,ClaimsPrincipal user,AppDbContext db,WriteGate gate,CancellationToken ct)=>Save(code,input,false,user,db,gate,ct));
    }
    static string Required(string? value,string label,int max)
    {
        var text=(value??"").Trim();
        if(text.Length==0||text.Length>max)throw new InputException($"Συμπλήρωσε το πεδίο «{label}» (έως {max} χαρακτήρες).");
        return text;
    }
    static async Task<IResult> Save(string code,CustomerInput input,bool create,ClaimsPrincipal user,AppDbContext db,WriteGate gate,CancellationToken ct)
    {
        if(input.MutationId==Guid.Empty||input.ExpectedVersion<0||create&&input.ExpectedVersion!=0)throw new InputException("Δεν αναγνωρίστηκε η αποστολή του πελάτη. Άνοιξε ξανά τη φόρμα.");
        var name=Required(input.Name,"Επωνυμία",512);var tax=Required(input.TaxId,"ΑΦΜ",9);
        var profession=Required(input.Profession,"Επάγγελμα",256);var address=Required(input.Address,"Διεύθυνση",512);
        var postal=Required(input.PostalCode,"ΤΚ",6).Replace(" ","");var city=Required(input.City,"Πόλη",128);
        var phone=Required(input.Phone,"Τηλέφωνο",32);var office=Required(input.TaxOffice,"ΔΟΥ",128);var notes=(input.CustomerNotes??"").Trim();
        if(!Regex.IsMatch(tax,"^[0-9]{9}$"))throw new InputException("Το ΑΦΜ χρειάζεται ακριβώς 9 ψηφία.");
        if(!Regex.IsMatch(postal,"^[0-9]{5}$"))throw new InputException("Ο ΤΚ χρειάζεται ακριβώς 5 ψηφία.");
        if(!Regex.IsMatch(phone,@"^[+()0-9\s.-]+$")||phone.Count(char.IsDigit) is <10 or >15)throw new InputException("Συμπλήρωσε έγκυρο τηλέφωνο με 10–15 ψηφία.");
        if(notes.Length>2000)throw new InputException("Οι σημειώσεις πελάτη δέχονται έως 2.000 χαρακτήρες.");
        var actor=UserId(user);var key=$"{actor:N}:{input.MutationId:N}";
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{code,create,input}))));
        await gate.Lock.WaitAsync(ct);
        try
        {
            var receipt=await db.CustomerMutations.FindAsync([key],ct);
            if(receipt is not null)return receipt.BodyHash==hash?Results.Content(receipt.ResultJson,"application/json"):Results.Conflict(new{code="mutation_reused",message="Η ίδια αποστολή πελάτη περιέχει διαφορετικά στοιχεία. Οι αλλαγές παραμένουν στη συσκευή."});
            var branch=await db.Branches.SingleOrDefaultAsync(b=>b.Code==code,ct);
            if(create&&branch is not null)return Results.Conflict(new{code="customer_conflict",message="Ο πελάτης υπάρχει ήδη. Άνοιξε την καρτέλα του πριν αλλάξεις στοιχεία.",current=branch});
            if(!create&&(branch is null||!branch.CreatedById.HasValue))return Results.BadRequest(new{message="Η επεξεργασία αφορά πελάτες που δημιουργήθηκαν μέσα από την εφαρμογή."});
            if((branch?.CustomerVersion??0)!=input.ExpectedVersion)return Results.Conflict(new{code="customer_conflict",message="Τα στοιχεία του πελάτη άλλαξαν. Σύγκρινε τις νεότερες αλλαγές πριν αποθηκεύσεις.",current=branch});
            if(!input.AllowExistingTaxId&&(create||tax!=branch!.TaxId))
            {
                var existing=await db.Branches.AsNoTracking().Where(b=>b.TaxId==tax&&b.Code!=code).OrderBy(b=>b.Code).Take(20).ToListAsync(ct);
                if(existing.Count>0)return Results.Conflict(new{code="existing_tax_id",message="Υπάρχουν ήδη καρτέλες με αυτό το ΑΦΜ. Έλεγξέ τες και επιβεβαίωσε ότι πρόκειται για ξεχωριστό υποκατάστημα.",existing});
            }
            if(branch is null)
            {
                var seller=await db.Users.SingleAsync(u=>u.Id==actor,ct);
                branch=new Branch{Code=code,AssignedSeller=seller.Name,CreatedById=actor,CustomerCreatedAtUtc=DateTime.UtcNow};db.Branches.Add(branch);
            }
            branch.Name=name;branch.TaxId=tax;branch.Profession=profession;branch.Address=address;branch.PostalCode=postal;branch.City=city;branch.Phone=phone;branch.TaxOffice=office;branch.CustomerNotes=notes;
            branch.CustomerVersion++;branch.CustomerUpdatedAtUtc=DateTime.UtcNow;
            var result=JsonSerializer.Serialize(branch,VisitEndpoints.WebJson);
            db.CustomerMutations.Add(new CustomerMutation{Id=key,BodyHash=hash,ResultJson=result});
            await db.SaveChangesAsync(ct);return Results.Content(result,"application/json");
        }
        catch(DbUpdateException exception) when(exception is not DbUpdateConcurrencyException)
        {
            // Another instance may have committed the same mutation during this request.
            db.ChangeTracker.Clear();
            var receipt=await db.CustomerMutations.FindAsync([key],ct);
            if(receipt is not null&&receipt.BodyHash==hash)return Results.Content(receipt.ResultJson,"application/json");
            throw;
        }
        finally {gate.Lock.Release();}
    }
}
