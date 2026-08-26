using System.Reflection;

namespace SlayIdleRepeat.Adapters.Persistence.Postgres;

/// <summary>The shipped migration history: every numbered SQL file embedded in this assembly.</summary>
public static class PostgresMigrations
{
    private const string ResourcePrefix = "SlayIdleRepeat.Adapters.Persistence.Postgres.Migrations.";

    /// <summary>Every embedded migration script, in apply order.</summary>
    /// <exception cref="InvalidOperationException">No migration is embedded — a broken csproj glob, which must fail the boot, not apply nothing.</exception>
    public static IReadOnlyList<MigrationScript> All()
    {
        var assembly = typeof(PostgresMigrations).Assembly;

        var scripts = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) &&
                           name.EndsWith(".sql", StringComparison.Ordinal))
            .Select(name => new MigrationScript(name[ResourcePrefix.Length..], ReadResource(assembly, name)))
            .ToArray();

        return scripts.Length > 0
            ? MigrationPlan.Ordered(scripts)
            : throw new InvalidOperationException(
                "No migration script is embedded under " + ResourcePrefix + ". The runner applying " +
                "an empty history reports a migrated database that has no tables — fail here, at " +
                "the boot, where the missing EmbeddedResource glob is one diff away.");
    }

    private static string ReadResource(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("The manifest names '" + name + "' but the stream is missing.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
