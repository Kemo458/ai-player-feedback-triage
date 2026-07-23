using Microsoft.EntityFrameworkCore;
using PlayerFeedback.Core.Domain;

namespace PlayerFeedback.Core.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Game> Games => Set<Game>();
    public DbSet<Feedback> Feedback => Set<Feedback>();
    public DbSet<FeedbackAnalysis> Analyses => Set<FeedbackAnalysis>();
    public DbSet<FeedbackEntity> Entities => Set<FeedbackEntity>();
    public DbSet<GooglePlayImportJob> ImportJobs => Set<GooglePlayImportJob>();
    public DbSet<AggregateSummary> Summaries => Set<AggregateSummary>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Game>(e =>
        {
            e.ToTable("games");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.GooglePlayPackageId).HasMaxLength(200);
            e.Property(x => x.GooglePlayUrl).HasMaxLength(500);
            e.Property(x => x.SubmissionTokenHash).HasMaxLength(200);
            e.HasIndex(x => x.GooglePlayPackageId).IsUnique().HasFilter("google_play_package_id IS NOT NULL");
        });

        b.Entity<Feedback>(e =>
        {
            e.ToTable("feedback");
            e.HasKey(x => x.Id);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ExternalId).HasMaxLength(200);
            e.Property(x => x.AuthorName).HasMaxLength(120);
            e.Property(x => x.ContentHash).HasMaxLength(80);
            e.Property(x => x.AppVersion).HasMaxLength(50);
            e.Property(x => x.Device).HasMaxLength(100);
            e.Property(x => x.Locale).HasMaxLength(20);
            e.Property(x => x.LastErrorCode).HasMaxLength(80);
            e.Property(x => x.LastErrorMessage).HasMaxLength(1000);
            e.HasIndex(x => new { x.GameId, x.Source, x.ExternalId })
                .IsUnique()
                .HasFilter("external_id IS NOT NULL");
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
            e.HasIndex(x => new { x.GameId, x.Source });
            e.HasOne(x => x.Game).WithMany(g => g.Feedback).HasForeignKey(x => x.GameId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Analysis).WithOne(a => a.Feedback!)
                .HasForeignKey<FeedbackAnalysis>(a => a.FeedbackId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<FeedbackAnalysis>(e =>
        {
            e.ToTable("feedback_analysis");
            e.HasKey(x => x.Id);
            e.Property(x => x.PrimaryCategory).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Toxicity).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Sentiment).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Tags).HasColumnType("text[]");
            e.Property(x => x.Summary).HasMaxLength(400);
            e.Property(x => x.Provider).HasMaxLength(50);
            e.Property(x => x.Model).HasMaxLength(100);
            e.Property(x => x.PromptVersion).HasMaxLength(50);
            e.Property(x => x.RawResponseHash).HasMaxLength(80);
            e.HasIndex(x => x.FeedbackId).IsUnique();
        });

        b.Entity<FeedbackEntity>(e =>
        {
            e.ToTable("feedback_entity");
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Sentiment).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.NormalizedName).HasMaxLength(100);
            e.Property(x => x.Evidence).HasMaxLength(200);
            e.HasIndex(x => new { x.GameId, x.Type, x.NormalizedName });
            e.HasOne(x => x.Analysis).WithMany(a => a.Entities).HasForeignKey(x => x.AnalysisId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<GooglePlayImportJob>(e =>
        {
            e.ToTable("import_job");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.PackageId).HasMaxLength(200);
            e.Property(x => x.Language).HasMaxLength(10);
            e.Property(x => x.Country).HasMaxLength(10);
            e.Property(x => x.Sort).HasMaxLength(20);
            e.Property(x => x.LastErrorCode).HasMaxLength(80);
            e.Property(x => x.LastErrorMessage).HasMaxLength(1000);
            e.Property(x => x.IdempotencyKey).HasMaxLength(200);
            e.Property(x => x.CreatedByUserId).HasMaxLength(100);
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
            e.HasIndex(x => new { x.GameId, x.IdempotencyKey }).IsUnique()
                .HasFilter("idempotency_key IS NOT NULL");
        });

        b.Entity<AggregateSummary>(e =>
        {
            e.ToTable("aggregate_summary");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.SourceFilter).HasMaxLength(20);
            e.Property(x => x.CategoryFilter).HasMaxLength(20);
            e.Property(x => x.SeverityFilter).HasMaxLength(20);
            e.Property(x => x.SentimentFilter).HasMaxLength(20);
            e.Property(x => x.ScopeKey).HasMaxLength(120);
            e.Property(x => x.Overview).HasMaxLength(4000);
            e.Property(x => x.Provider).HasMaxLength(50);
            e.Property(x => x.Model).HasMaxLength(100);
            e.Property(x => x.PromptVersion).HasMaxLength(50);
            e.Property(x => x.InputFingerprint).HasMaxLength(80);
            e.HasIndex(x => new { x.GameId, x.ScopeKey }).IsUnique();
        });

        // snake_case column names to match the filters above; force UTC timestamptz for all dates.
        foreach (var entity in b.Model.GetEntityTypes())
        {
            foreach (var prop in entity.GetProperties())
            {
                prop.SetColumnName(ToSnakeCase(prop.GetColumnName()));
                var t = Nullable.GetUnderlyingType(prop.ClrType) ?? prop.ClrType;
                if (t == typeof(DateTime))
                    prop.SetColumnType("timestamp with time zone");
            }
        }
    }

    private static string ToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var sb = new System.Text.StringBuilder(input.Length + 8);
        for (int i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && (!char.IsUpper(input[i - 1]) || (i + 1 < input.Length && !char.IsUpper(input[i + 1]))))
                    sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
