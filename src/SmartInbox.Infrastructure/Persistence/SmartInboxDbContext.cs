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
    public DbSet<InboundEmail> InboundEmails => Set<InboundEmail>();
    public DbSet<OutgoingLetter> OutgoingLetters => Set<OutgoingLetter>();
    public DbSet<RegisterCounter> RegisterCounters => Set<RegisterCounter>();
    public DbSet<DocumentLink> DocumentLinks => Set<DocumentLink>();

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
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(10).HasDefaultValue(DocumentSource.Upload);
            e.HasIndex(x => x.Source);
            e.HasOne<InboundEmail>().WithMany().HasForeignKey(x => x.InboundEmailId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.ContentHash).HasMaxLength(64);
            e.HasIndex(x => x.ContentHash);
            e.Property(x => x.TextHash).HasMaxLength(64);
            e.HasIndex(x => x.TextHash);
            e.Property(x => x.ReferenceKey).HasMaxLength(100);
            e.HasIndex(x => x.ReferenceKey);
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

        b.Entity<OutgoingLetter>(e =>
        {
            e.Property(x => x.Number).HasMaxLength(60);
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.Year, x.Sequence });
            e.Property(x => x.Recipient).HasMaxLength(300);
            e.Property(x => x.SubjectEn).HasMaxLength(ReplyDraftRules.MaxSubject);
            e.Property(x => x.SubjectAr).HasMaxLength(ReplyDraftRules.MaxSubject);
            e.Property(x => x.IssuedBy).HasMaxLength(200);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.SentVia).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.SentNote).HasMaxLength(500);
            e.Property(x => x.CancelledBy).HasMaxLength(200);
            e.Property(x => x.CancelReason).HasMaxLength(500);
            e.HasIndex(x => x.DocumentId);
            e.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Document>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<DocumentLink>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Evidence).HasMaxLength(300);
            e.Property(x => x.RemovedBy).HasMaxLength(200);
            e.HasIndex(x => new { x.DocumentId, x.RelatedDocumentId }).IsUnique().HasFilter("[RelatedDocumentId] IS NOT NULL");
            e.HasIndex(x => x.RelatedDocumentId);
            e.HasIndex(x => x.OutgoingLetterId);
            e.HasOne<Document>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
            // No cascades from the other side: SQL Server allows only one cascade path per table.
            e.HasOne<Document>().WithMany().HasForeignKey(x => x.RelatedDocumentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<OutgoingLetter>().WithMany().HasForeignKey(x => x.OutgoingLetterId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<RegisterCounter>(e =>
        {
            e.HasIndex(x => new { x.Year, x.Scope }).IsUnique();
            // Two people issuing at once: the second save fails and is retried with the next number.
            e.Property(x => x.Next).IsConcurrencyToken();
        });

        b.Entity<InboundEmail>(e =>
        {
            e.Property(x => x.MessageId).HasMaxLength(500);
            e.HasIndex(x => x.MessageId).IsUnique();
            e.Property(x => x.Mailbox).HasMaxLength(320);
            e.Property(x => x.FromName).HasMaxLength(200);
            e.Property(x => x.FromAddress).HasMaxLength(320);
            e.Property(x => x.Subject).HasMaxLength(500);
            e.Property(x => x.BodyText).HasMaxLength(MailboxRules.MaxBodyText);
            e.Property(x => x.StoragePath).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Reason).HasMaxLength(1000);
            e.Property(x => x.DocumentIds).HasMaxLength(2000);
            e.Property(x => x.InReplyTo).HasMaxLength(500);
            e.Property(x => x.References).HasMaxLength(4000);
            e.Property(x => x.SkippedAttachments).HasMaxLength(4000);
            e.HasIndex(x => x.ReceivedAt);
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
            e.Property(x => x.Code).HasMaxLength(10);
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
