using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;

namespace Paper.Api;

public sealed class Branch
{
    public string Code { get; set; } = "";
    public string TaxId { get; set; } = "";
    public string Name { get; set; } = "";
    public string AssignedSeller { get; set; } = "";
}
public sealed class ProductCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
public sealed class CategoryTurnover
{
    public string BranchCode { get; set; } = "";
    public int CategoryId { get; set; }
    public int Year { get; set; }
    public decimal Amount { get; set; }
    public Branch Branch { get; set; } = null!;
    public ProductCategory Category { get; set; } = null!;
}
public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "Seller";
    public bool Active { get; set; } = true;
}
public sealed class SurveyQuestion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public bool Required { get; set; }
    public bool Active { get; set; } = true;
    public int Position { get; set; }
    public string SectionKey { get; set; } = "";
    public string SectionTitle { get; set; } = "";
    public string SelectionMode { get; set; } = "Multiple";
    public List<SurveyOption> Options { get; set; } = [];
}
public sealed class SurveyOption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SurveyQuestionId { get; set; }
    public string Label { get; set; } = "";
    public int Position { get; set; }
}
public sealed class Visit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string BranchCode { get; set; } = "";
    public Branch Branch { get; set; } = null!;
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string Notes { get; set; } = "";
    public string AnswersJson { get; set; } = "[]";
    public Guid SubmissionId { get; set; }
    public int Version { get; set; } = 1;
    public DateTime? OccurredAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? LastEditedById { get; set; }
    public string SurveyJson { get; set; } = "";
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public Guid? DeletedById { get; set; }
}
public sealed record SavedAnswer(Guid QuestionId, string Title, string[] SelectedOptions, string SectionKey = "", string SectionTitle = "", Guid[]? SelectedOptionIds = null);
public sealed class SurveyRelease
{
    public string Id { get; set; } = "";
    public string QuestionsJson { get; set; } = "";
}
public sealed class VisitMutation
{
    public string Id { get; set; } = "";
    public string BodyHash { get; set; } = "";
    public string ResultJson { get; set; } = "";
}
public sealed class VisitRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VisitId { get; set; }
    public Guid EditorId { get; set; }
    public string EditorName { get; set; } = "";
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
    public string BeforeJson { get; set; } = "";
    public string AfterJson { get; set; } = "";
}
public sealed class ImportRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int Year { get; set; }
    public int BranchCount { get; set; }
    public int TurnoverCount { get; set; }
}
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<ProductCategory> Categories => Set<ProductCategory>();
    public DbSet<CategoryTurnover> Turnovers => Set<CategoryTurnover>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<SurveyQuestion> Questions => Set<SurveyQuestion>();
    public DbSet<Visit> Visits => Set<Visit>();
    public DbSet<ImportRun> Imports => Set<ImportRun>();
    public DbSet<SurveyRelease> SurveyReleases => Set<SurveyRelease>();
    public DbSet<VisitMutation> VisitMutations => Set<VisitMutation>();
    public DbSet<VisitRevision> VisitRevisions => Set<VisitRevision>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Branch>().HasKey(x => x.Code);
        b.Entity<Branch>().Property(x => x.Code).HasMaxLength(64);
        b.Entity<Branch>().Property(x => x.TaxId).HasMaxLength(9);
        b.Entity<Branch>().Property(x => x.Name).HasMaxLength(512);
        b.Entity<Branch>().Property(x => x.AssignedSeller).HasMaxLength(128);
        b.Entity<Branch>().HasIndex(x => x.TaxId);
        b.Entity<ProductCategory>().Property(x => x.Name).HasMaxLength(256);
        b.Entity<ProductCategory>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<CategoryTurnover>().HasKey(x => new { x.BranchCode, x.CategoryId, x.Year });
        b.Entity<CategoryTurnover>().Property(x => x.Amount).HasPrecision(18, 2);
        b.Entity<CategoryTurnover>().HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchCode).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CategoryTurnover>().HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AppUser>().Property(x => x.Email).HasMaxLength(254);
        b.Entity<AppUser>().Property(x => x.Name).HasMaxLength(128);
        b.Entity<AppUser>().HasIndex(x => x.Email).IsUnique();
        b.Entity<SurveyQuestion>().Property(x => x.Title).HasMaxLength(256);
        b.Entity<SurveyQuestion>().Property(x => x.SectionKey).HasMaxLength(64);
        b.Entity<SurveyQuestion>().Property(x => x.SectionTitle).HasMaxLength(256);
        b.Entity<SurveyQuestion>().Property(x => x.SelectionMode).HasMaxLength(16);
        b.Entity<SurveyOption>().Property(x => x.Label).HasMaxLength(128);
        b.Entity<SurveyQuestion>().HasMany(x => x.Options).WithOne().HasForeignKey(x => x.SurveyQuestionId);
        b.Entity<Visit>().Property(x => x.Notes).HasMaxLength(2000);
        b.Entity<Visit>().HasIndex(x => new { x.UserId, x.SubmissionId }).IsUnique();
        b.Entity<Visit>().HasIndex(x => x.CreatedAtUtc);
        b.Entity<Visit>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<Visit>().HasQueryFilter(x => !x.IsDeleted);
        b.Entity<SurveyRelease>().Property(x => x.Id).HasMaxLength(64);
        b.Entity<VisitMutation>().Property(x => x.Id).HasMaxLength(80);
        b.Entity<VisitMutation>().Property(x => x.BodyHash).HasMaxLength(64);
        b.Entity<VisitRevision>().HasIndex(x => x.VisitId);
        b.Entity<VisitRevision>().Property(x => x.EditorName).HasMaxLength(128);
        b.Entity<Visit>().Property(x => x.CreatedAtUtc).HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        b.Entity<ImportRun>().Property(x => x.CreatedAtUtc).HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        b.Entity<Visit>().HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchCode).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Visit>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
