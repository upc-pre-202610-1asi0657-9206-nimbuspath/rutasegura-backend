using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Monolith.Shared.Infrastructure.Persistence.EFC.Configuration;

namespace RutaSegura.Monolith.Administration.IntegrationTests;

/// <summary>
///     The model built with the MySQL provider, without opening a connection. The other suites run on SQLite, which
///     accepts names that MySQL rejects: a 65-character FK name once made a migration fail only in production.
/// </summary>
public sealed class MySqlModelTests
{
    private const int MySqlIdentifierMaxLength = 64;

    [Fact]
    public void EveryIdentifier_FitsTheMySqlLimit()
    {
        using var db = MySqlContext();
        var model = db.GetService<IDesignTimeModel>().Model.GetRelationalModel();

        var names = model.Tables.SelectMany(table => new[] { table.Name }
            .Concat(table.Columns.Select(column => column.Name))
            .Concat(table.UniqueConstraints.Select(key => key.Name))
            .Concat(table.ForeignKeyConstraints.Select(fk => fk.Name))
            .Concat(table.Indexes.Select(index => index.Name)));

        names.Where(name => name.Length > MySqlIdentifierMaxLength).ShouldBeEmpty();
    }

    [Fact]
    public void MigrationSnapshot_MatchesTheModel()
    {
        using var db = MySqlContext();

        db.Database.HasPendingModelChanges().ShouldBeFalse();
    }

    private static AppDbContext MySqlContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseMySQL("Server=localhost;Database=rutasegura_model_only;Uid=none;Pwd=none").Options);
}
