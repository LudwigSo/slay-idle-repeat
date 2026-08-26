namespace SlayIdleRepeat.Adapters.Persistence.Postgres;

/// <summary>The shipped migration history: every numbered SQL file embedded in this assembly.</summary>
public static class PostgresMigrations
{
    /// <summary>Every embedded migration script, in apply order.</summary>
    public static IReadOnlyList<MigrationScript> All() =>
        throw new NotImplementedException("M5-05 phase 3 implements the migration set.");
}
