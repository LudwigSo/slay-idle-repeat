namespace SlayIdleRepeat.Core.Primitives;

/// <summary>
/// 🔒 The FTUE beat a player's tutorial has reached (`19` D7): <c>beatId ∈ B0…B10 plus B6b</c>,
/// twelve values, in script order.
/// </summary>
/// <remarks>
/// <para>
/// `19` D7 puts this on the <c>Player</c> aggregate — <em>"FTUE progress persists per beat: the
/// Player aggregate carries <c>ftueProgress { completedAtUtc | null, beatId }</c>"</em> — and the
/// list is closed by the authored script, not by a patch, so it is an enum rather than a content id
/// for the same three reasons <see cref="CurrencyId"/> is one.
/// </para>
/// <para>
/// 🔒 <b>Why it lives in <c>Primitives/</c> rather than beside the aggregate in
/// <c>Model/Player/</c>.</b> Measured, not assumed: a <b>public enum under <c>Core/Model/</c></b>
/// (outside the <c>Model/Snapshots/</c> exemption) fails
/// <c>AccessibilityBoundaryTests.Apply_is_the_only_public_mutation</c> with
/// <c>SlayIdleRepeat.Core.Model.&lt;name&gt;.value__ is a public mutable field</c> — an enum's
/// backing <c>value__</c> field is public, is neither <c>literal</c> nor <c>initonly</c>, and
/// carries no <c>[CompilerGenerated]</c>, so it matches that rule's public-mutable-field arm
/// exactly. <c>Primitives/</c> is where the other closed vocabulary this snapshot needs
/// (<see cref="CurrencyId"/>) already lives, and it is beneath both <c>Model</c> and
/// <c>Model/Snapshots/</c>, which both name this type.
/// </para>
/// <para>
/// 🔒 <b>The numbers are wire values.</b> Same rule as <see cref="CurrencyId"/> and
/// <see cref="RejectionReason"/>: <c>CanonicalStateWriter</c> writes the number, never the name, so
/// renumbering rewrites every <c>stateHash</c> that has ever carried a player. Append, never
/// renumber, never reuse. There is deliberately <b>no <c>0</c> member</b>, so
/// <c>default(FtueBeat)</c> cannot read as "the player is at beat 0, about to enter their name" —
/// <c>Player.Rehydrate</c> refuses the zero as the uninitialised value it is.
/// </para>
/// <para>
/// ⚠️ <b><see cref="B6B"/> is `19` D7's <c>B6b</c></b>, spelled with a capital because C# has no
/// case-only distinction to protect and a lower-case suffix beside eleven upper-case members reads
/// as a typo. It sits between <see cref="B6"/> and <see cref="B7"/> because that is where the
/// script puts it.
/// </para>
/// <para>
/// ⚠️ <b>What is deliberately absent.</b> There is no <c>COMPLETE</c> member. `19` D7 completes the
/// tutorial by setting <c>completedAtUtc</c> once beat 10's spend commits — completion is a
/// timestamp, not a thirteenth beat — and a member for it would make
/// <c>(B10, completedAtUtc: null)</c> and <c>(COMPLETE, completedAtUtc: null)</c> two spellings of
/// states the design gives one meaning. The seven <b>tutorial-only rule flags</b> of `19` D4.3 are
/// absent for a different reason: D4.3 item 2 calls <c>noAds</c> "a package flag" and
/// <c>IMPLEMENTATION_TRACKER.md</c> assigns "tutorial-only defs and flags (7)" to <b>M4-12</b>'s
/// <c>ftue.json</c> package. They describe the tutorial, not the player.
/// </para>
/// </remarks>
public enum FtueBeat
{
    /// <summary>`19` D7 — beat 0: name entry.</summary>
    B0 = 1,

    /// <summary>`19` D7 — beat 1.</summary>
    B1 = 2,

    /// <summary>`19` D7 — beat 2. Skip becomes available after this beat (`19` D6).</summary>
    B2 = 3,

    /// <summary>`19` D7 — beat 3.</summary>
    B3 = 4,

    /// <summary>`19` D7 — beat 4: the Treasure tile.</summary>
    B4 = 5,

    /// <summary>`19` D7 — beat 5: the tutorial shop.</summary>
    B5 = 6,

    /// <summary>`19` D7 — beat 6: the elite.</summary>
    B6 = 7,

    /// <summary>`19` D7 — beat 6b.</summary>
    B6B = 8,

    /// <summary>`19` D7 — beat 7: the mini-boss.</summary>
    B7 = 9,

    /// <summary>`19` D7 — beat 8: the tally.</summary>
    B8 = 10,

    /// <summary>`19` D7 — beat 9: Home, the forced equip.</summary>
    B9 = 11,

    /// <summary>
    /// `19` D7 — beat 10: the forced Talent Point spend. The tutorial is complete when this
    /// beat's spend commits, which is when <c>completedAtUtc</c> is set.
    /// </summary>
    B10 = 12,
}
