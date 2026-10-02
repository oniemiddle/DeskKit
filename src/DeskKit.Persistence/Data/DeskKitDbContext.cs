using Microsoft.EntityFrameworkCore;

namespace DeskKit.Persistence.Data;

/// <summary>
/// The application's database: one row of preferences and one row per placed
/// widget.
/// </summary>
/// <remarks>
/// The schema is owned by EF Core's migrations. A build tells a database it
/// understands from one written by a newer build by comparing the migration ids
/// compiled into it with the ids recorded in the database's
/// <c>__EFMigrationsHistory</c> table, rather than by carrying a version number of
/// its own —see <c>StateStore</c>.
/// </remarks>
public sealed class DeskKitDbContext : DbContext
{
    /// <summary>Lengths the stored strings are declared with.</summary>
    private const int ShortText = 64;

    public DeskKitDbContext(DbContextOptions<DeskKitDbContext> options)
        : base(options)
    {
    }

    public DbSet<SettingsEntity> Settings => Set<SettingsEntity>();

    public DbSet<WidgetEntity> Widgets => Set<WidgetEntity>();

    public DbSet<MetaEntity> Meta => Set<MetaEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var settings = modelBuilder.Entity<SettingsEntity>();
        settings.ToTable("Settings");
        settings.HasKey(entity => entity.Id);
        settings.Property(entity => entity.Theme).HasMaxLength(ShortText).IsRequired();
        settings.Property(entity => entity.Language).HasMaxLength(ShortText).IsRequired();

        var widgets = modelBuilder.Entity<WidgetEntity>();
        widgets.ToTable("Widgets");
        widgets.HasKey(entity => entity.InstanceId);
        widgets.Property(entity => entity.InstanceId).HasMaxLength(ShortText);
        widgets.Property(entity => entity.WidgetId).HasMaxLength(ShortText).IsRequired();
        widgets.Property(entity => entity.SettingsJson).IsRequired();

        // Order is only ever read with a sort on it, and ties are broken by the
        // instance id, so a listing comes out the same way every run.
        widgets.HasIndex(entity => entity.Order);

        var meta = modelBuilder.Entity<MetaEntity>();
        meta.ToTable("Meta");
        meta.HasKey(entity => entity.Key);
        meta.Property(entity => entity.Key).HasMaxLength(ShortText);
        meta.Property(entity => entity.Value).HasMaxLength(512).IsRequired();
    }
}
