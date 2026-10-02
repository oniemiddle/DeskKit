using Microsoft.EntityFrameworkCore.Design;

namespace DeskKit.Persistence.Data;

/// <summary>
/// Lets the EF Core tools build the model without starting the app.
/// </summary>
/// <remarks>
/// Only <c>dotnet ef</c> uses this, and it never opens the database: the path it
/// names is where the store would put it, so a migration is generated against the
/// real configuration rather than a copy of it.
/// </remarks>
public sealed class DeskKitDbContextFactory : IDesignTimeDbContextFactory<DeskKitDbContext>
{
    public DeskKitDbContext CreateDbContext(string[] args) =>
        new(DatabaseOptions.For(AppPaths.DatabasePath));
}
