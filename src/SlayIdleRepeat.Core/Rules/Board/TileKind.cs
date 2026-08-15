namespace SlayIdleRepeat.Core.Rules.Board;

/// <summary>The closed set of 14 tile kinds a board node can hold, including the boss.</summary>
internal enum TileKind
{
    Enemy,
    Elite,
    Boss,
    Shrine,
    Curse,
    Treasure,
    Shop,
    Campfire,
    Minigame,
    Event,
    Portal,
    Cache,
    DiceForge,
    Empty,
}

/// <summary>
/// String-id round-trip for <see cref="TileKind"/> — the <c>TILE_*</c> ids content JSON and the
/// wire use.
/// </summary>
internal static class TileKindIds
{
    /// <summary>The <c>TILE_*</c> id for a <see cref="TileKind"/>.</summary>
    public static string ToId(TileKind kind) => kind switch
    {
        TileKind.Enemy => "TILE_ENEMY",
        TileKind.Elite => "TILE_ELITE",
        TileKind.Boss => "TILE_BOSS",
        TileKind.Shrine => "TILE_SHRINE",
        TileKind.Curse => "TILE_CURSE",
        TileKind.Treasure => "TILE_TREASURE",
        TileKind.Shop => "TILE_SHOP",
        TileKind.Campfire => "TILE_CAMPFIRE",
        TileKind.Minigame => "TILE_MINIGAME",
        TileKind.Event => "TILE_EVENT",
        TileKind.Portal => "TILE_PORTAL",
        TileKind.Cache => "TILE_CACHE",
        TileKind.DiceForge => "TILE_DICE_FORGE",
        TileKind.Empty => "TILE_EMPTY",
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind), kind, "not one of 03 §2's 14 tile kinds."),
    };

    /// <summary>Attempts to parse a <c>TILE_*</c> id. False for anything outside the set.</summary>
    public static bool TryParse(string? id, out TileKind kind)
    {
        switch (id)
        {
            case "TILE_ENEMY": kind = TileKind.Enemy; return true;
            case "TILE_ELITE": kind = TileKind.Elite; return true;
            case "TILE_BOSS": kind = TileKind.Boss; return true;
            case "TILE_SHRINE": kind = TileKind.Shrine; return true;
            case "TILE_CURSE": kind = TileKind.Curse; return true;
            case "TILE_TREASURE": kind = TileKind.Treasure; return true;
            case "TILE_SHOP": kind = TileKind.Shop; return true;
            case "TILE_CAMPFIRE": kind = TileKind.Campfire; return true;
            case "TILE_MINIGAME": kind = TileKind.Minigame; return true;
            case "TILE_EVENT": kind = TileKind.Event; return true;
            case "TILE_PORTAL": kind = TileKind.Portal; return true;
            case "TILE_CACHE": kind = TileKind.Cache; return true;
            case "TILE_DICE_FORGE": kind = TileKind.DiceForge; return true;
            case "TILE_EMPTY": kind = TileKind.Empty; return true;
            default: kind = default; return false;
        }
    }

    /// <summary>Parses a <c>TILE_*</c> id.</summary>
    /// <exception cref="ArgumentException">The id is not one of the 14 ids.</exception>
    public static TileKind Parse(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (TryParse(id, out var kind))
        {
            return kind;
        }

        throw new ArgumentException($"'{id}' is not one of 03 §2's TILE_* ids.", nameof(id));
    }
}
