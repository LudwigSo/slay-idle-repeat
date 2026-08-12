namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>
/// 🔒 The actor-id layout of the combat log — the meaning of
/// <see cref="CombatEvent.SourceId"/> and <see cref="CombatEvent.TargetId"/>.
/// </summary>
/// <remarks>
/// <para>
/// `05` §7 types both slots as <see cref="byte"/> and says nothing else. Two things follow that
/// the document does not state, and both have to be settled before M2-08 assigns its first id.
/// </para>
/// <para>
/// <b>1. A "no actor" value is required.</b> <see cref="CombatEventType.BattleStart"/>,
/// <see cref="CombatEventType.BattleEnd"/> and <see cref="CombatEventType.RunEffectQueued"/> name
/// no target — and <c>RunEffectQueued</c>'s target is the <b>run</b> (`18` §5's <c>RUN</c>), which
/// is not an actor at all. Using <c>0</c> for "none" would make every such event read as the hero,
/// so <see cref="None"/> is the top of the byte range and <see cref="MaxId"/> is one below it.
/// </para>
/// <para>
/// <b>2. The layout is fixed, not packed.</b> `05` §3.1 fixes the actor order —
/// <em>"hero, pets in slot order, enemies by index"</em> — and `05` §3.2 makes pets untargetable
/// and unkillable, so a pet's slot never vacates mid-fight. Reserving all three pet ids whether or
/// not they are filled costs three ids out of 255 and buys a property the replayer wants: an
/// actor's id is a function of its role, not of how many pets the player happened to bring, so
/// <c>id 5</c> is the second enemy in every fight.
/// </para>
/// <list type="table">
///   <item><term>0</term><description><see cref="Hero"/></description></item>
///   <item><term>1..3</term><description>pets, in slot order (`05` §3.1)</description></item>
///   <item><term>4..254</term><description>enemies by index, then summons</description></item>
///   <item><term>255</term><description><see cref="None"/></description></item>
/// </list>
/// <para>
/// 🔒 <b>Summons take the next free id and never reuse a dead one.</b> `05` §3.1: <em>"summons
/// enter at the end of the enemy index list"</em>. Reusing the id of a dead summon would make two
/// different actors indistinguishable in the log, and the log is the replay (`05` §3.1 step 7) —
/// the replayer would draw the second one resuming the first one's HP bar.
/// </para>
/// <para>
/// <b>In a duel</b> (`05` §3.3) both sides are hero-shaped. The attacker's side takes the hero
/// block (<c>0..3</c>) and the defender's side starts at <see cref="FirstEnemy"/> — hero
/// <c>4</c>, pets <c>5..7</c> — because `05` §3.3 has the attacker's side act first and `05` §3.2
/// has enemies occupy the far block. M2-14 owns the duel; this is the layout it inherits.
/// </para>
/// <para>
/// <b>Is 255 reachable?</b> Not by any authored fight. The standing population is at most
/// 1 hero + 3 pets + 5 enemies (`05` §3) = 9, and only summons grow the list. The most prolific
/// summoner in `17` is Rimehold phase 3 (<c>PERIODIC 10 s</c>, 2 shards) — 18 spawns across the
/// 90 s cap (`05` §3) — against 246 ids above <see cref="FirstEnemy"/>. A boss would have to
/// summon roughly one actor every 0.4 s for the whole fight to exhaust the range.
/// <see cref="CombatLog"/> refuses an id it cannot address rather than trusting that arithmetic:
/// an unreachable ceiling that is silently wrapped is worse than one that fails loudly.
/// </para>
/// </remarks>
internal static class CombatActor
{
    /// <summary>🔒 "No actor." Not a participant — see the type remarks.</summary>
    public const byte None = 255;

    /// <summary>The highest id an actor may take. One below <see cref="None"/>.</summary>
    public const byte MaxId = 254;

    /// <summary>The hero. Always id 0, in PvE and as the attacker in a duel.</summary>
    public const byte Hero = 0;

    /// <summary>The first pet slot. Pets occupy <c>1..3</c> in slot order (`05` §3.1).</summary>
    public const byte FirstPet = 1;

    /// <summary>How many pet ids are reserved, filled or not (`05` §3: 0–3 pets).</summary>
    public const int PetSlots = 3;

    /// <summary>The first enemy id — and the defending hero's id in a duel (`05` §3.3).</summary>
    public const byte FirstEnemy = 4;

    /// <summary>The id of the pet in <paramref name="slot"/> (<c>0..2</c>).</summary>
    /// <param name="slot">The zero-based pet slot.</param>
    /// <exception cref="ArgumentOutOfRangeException">The slot is outside <c>0..2</c>.</exception>
    public static byte Pet(int slot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, PetSlots);

        return (byte)(FirstPet + slot);
    }

    /// <summary>The id of the enemy at <paramref name="index"/> in the enemy list.</summary>
    /// <param name="index">The zero-based enemy index, summons included.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The index is negative, or names an actor above <see cref="MaxId"/>.
    /// </exception>
    public static byte Enemy(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, MaxId - FirstEnemy);

        return (byte)(FirstEnemy + index);
    }

    /// <summary>Whether the id names a participant rather than <see cref="None"/>.</summary>
    /// <param name="id">The id to test.</param>
    public static bool IsActor(byte id) => id != None;
}
