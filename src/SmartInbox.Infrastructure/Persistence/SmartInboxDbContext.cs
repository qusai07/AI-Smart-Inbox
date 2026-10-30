using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SmartInbox.Application;
using SmartInbox.Domain;
using SmartInbox.Infrastructure.Identity;

namespace SmartInbox.Infrastructure.Persistence;

public class SmartInboxDbContext(DbContextOptions<SmartInboxDbContext> options) : IdentityDbContext<AppUser>(options), IAppDbContext
{
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentAnalysis> Analyses => Set<DocumentAnalysis>();
    public DbSet<ActionItem> ActionItems => Set<ActionItem>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<RoutingDecision> RoutingDecisions => Set<RoutingDecision>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<DocumentEvent> DocumentEvents => Set<DocumentEvent>();
    public DbSet<DocumentComment> Comments => Set<DocumentComment>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<WebhookEndpoint> WebhookEndpoints => Set<WebhookEndpoint>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
    public DbSet<ReplyDraft> ReplyDrafts => Set<ReplyDraft>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b); // Identity tables

        b.Entity<AppUser>(e =>
        {
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Document>(e =>
        {
            e.Property(x => x.HandlingBy).HasMaxLength(200);
            e.Property(x => x.UploadedBy).HasMaxLength(200);
            e.Property(x => x.FileName).HasMaxLength(260);
            e.Property(x => x.ContentType).HasMaxLength(100);
            e.Property(x => x.StoragePath).HasMaxLength(500);
            e.Property(x => x.ErrorMessage).HasMaxLength(1000);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.Status);
            e.Property(x => x.Handling).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.HandlingNote).HasMaxLength(1000);
            e.HasIndex(x => new { x.AssignedDepartmentId, x.Handling });
            e.HasOne(x => x.AssignedDepartment).WithMany().HasForeignKey(x => x.AssignedDepartmentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Analysis).WithOne().HasForeignKey<DocumentAnalysis>(x => x.DocumentId);
            e.HasMany(x => x.ActionItems).WithOne().HasForeignKey(x => x.DocumentId);
            e.HasMany(x => x.RoutingDecisions).WithOne().HasForeignKey(x => x.DocumentId);
            e.HasMany(x => x.Notifications).WithOne().HasForeignKey(x => x.DocumentId);
            e.HasMany(x => x.Events).WithOne().HasForeignKey(x => x.DocumentId);
            e.HasMany(x => x.Comments).WithOne().HasForeignKey(x => x.DocumentId);
            e.Property(x => x.AssigneeId).HasMaxLength(450);
            e.Property(x => x.AssigneeName).HasMaxLength(200);
            e.HasIndex(x => x.AssigneeId);
        });

        b.Entity<DocumentAnalysis>(e =>
        {
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Priority).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Language).HasMaxLength(10);
            e.Property(x => x.ModelUsed).HasMaxLength(100);
            e.HasOne(x => x.SuggestedDepartment).WithMany().HasForeignKey(x => x.SuggestedDepartmentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<RoutingDecision>(e =>
        {
            e.Property(x => x.DecidedBy).HasMaxLength(200);
            e.Property(x => x.DecidedByEmail).HasMaxLength(320);
            e.HasOne(x => x.FinalDepartment).WithMany().HasForeignKey(x => x.FinalDepartmentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<DocumentComment>(e =>
        {
            e.Property(x => x.AuthorId).HasMaxLength(450);
            e.Property(x => x.AuthorName).HasMaxLength(200);
            e.Property(x => x.Text).HasMaxLength(2000);
            e.HasIndex(x => new { x.DocumentId, x.CreatedAt });
        });

        b.Entity<DocumentEvent>(e =>
        {
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Actor).HasMaxLength(200);
            e.Property(x => x.Note).HasMaxLength(1000);
            e.HasIndex(x => new { x.DocumentId, x.At });
        });

        b.Entity<Notification>(e =>
        {
            e.Property(x => x.To).HasMaxLength(320);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20).HasDefaultValue(NotificationKind.Routed);
            e.Property(x => x.DedupKey).HasMaxLength(200);
            e.HasIndex(x => x.DedupKey).IsUnique().HasFilter("[DedupKey] IS NOT NULL");
            e.Property(x => x.Error).HasMaxLength(1000);
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
            e.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ApiKey>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(ApiKeyService.MaxNameLength);
            e.Property(x => x.Prefix).HasMaxLength(20);
            e.HasIndex(x => x.Prefix).IsUnique();
            e.Property(x => x.Hash).HasMaxLength(64);
            e.Property(x => x.Role).HasMaxLength(20);
            e.Property(x => x.CreatedBy).HasMaxLength(200);
            e.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<WebhookEndpoint>(e =>
        {
            e.Property(x => x.Url).HasMaxLength(WebhookRules.MaxUrlLength);
            e.Property(x => x.Description).HasMaxLength(200);
            e.Property(x => x.Secret).HasMaxLength(1000);
            e.Property(x => x.PreviousSecret).HasMaxLength(1000);
            e.Property(x => x.Events).HasMaxLength(1000);
            e.Property(x => x.DisabledReason).HasMaxLength(500);
            e.Property(x => x.CreatedBy).HasMaxLength(200);
            e.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<WebhookDelivery>(e =>
        {
            e.Property(x => x.EventId).HasMaxLength(40);
            e.Property(x => x.EventType).HasMaxLength(50);
            e.Property(x => x.DedupKey).HasMaxLength(200);
            e.HasIndex(x => new { x.EndpointId, x.DedupKey }).IsUnique().HasFilter("[DedupKey] IS NOT NULL");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.LastError).HasMaxLength(1000);
            e.Property(x => x.LastResponseSnippet).HasMaxLength(WebhookRules.MaxResponseSnippet);
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
            e.HasIndex(x => new { x.EndpointId, x.CreatedAt });
            e.HasOne<WebhookEndpoint>().WithMany().HasForeignKey(x => x.EndpointId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ReplyDraft>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.TemplateKey).HasMaxLength(50);
            e.Property(x => x.Engine).HasMaxLength(100);
            e.Property(x => x.Instructions).HasMaxLength(ReplyDraftRules.MaxInstructions);
            e.Property(x => x.SubjectEn).HasMaxLength(ReplyDraftRules.MaxSubject);
            e.Property(x => x.SubjectAr).HasMaxLength(ReplyDraftRules.MaxSubject);
            e.Property(x => x.BodyEn).HasMaxLength(ReplyDraftRules.MaxBody);
            e.Property(x => x.BodyAr).HasMaxLength(ReplyDraftRules.MaxBody);
            e.Property(x => x.MissingInfo).HasMaxLength(4000);
            e.Property(x => x.Commitments).HasMaxLength(4000);
            e.Property(x => x.RequestedBy).HasMaxLength(200);
            e.Property(x => x.EditedBy).HasMaxLength(200);
            e.Property(x => x.DecidedBy).HasMaxLength(200);
            e.Property(x => x.Error).HasMaxLength(1000);
            e.HasIndex(x => new { x.DocumentId, x.Version }).IsUnique();
            e.HasOne<Document>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Department>(e =>
        {
            e.Property(x => x.SignatoryNameEn).HasMaxLength(150);
            e.Property(x => x.SignatoryNameAr).HasMaxLength(150);
            e.Property(x => x.SignatoryTitleEn).HasMaxLength(150);
            e.Property(x => x.SignatoryTitleAr).HasMaxLength(150);
            e.Property(x => x.NameEn).HasMaxLength(150);
            e.Property(x => x.NameAr).HasMaxLength(150);
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.HeadEmail).HasMaxLength(320);
            e.HasData(DepartmentSeed.All);
        });
    }
}
