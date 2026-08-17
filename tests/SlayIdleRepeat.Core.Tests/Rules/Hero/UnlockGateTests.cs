using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Rules.Hero;
using SlayIdleRepeat.Core.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Hero;

/// <summary>
/// <see cref="UnlockTuning"/> and <see cref="UnlockGate"/> — `07` §1.1's unlock ladder, read from the
/// tuning document and compared against a Legend Level.
/// </summary>
public sealed class UnlockGateTests
{
    private static readonly UnlockTuning Ladder = UnlockTuning.Read(ProgressionDocuments.Shipped);

    /// <summary>
    /// `07` §1.1's nine keyed rows, at the levels the table gives them.
    /// </summary>
    /// <remarks>
    /// Transcribed from the document rather than read off the data, which is the only way this can
    /// mean anything: a list derived from <c>progression.json</c> would say the ladder is right
    /// because the ladder says so.
    /// </remarks>
    [Theory]
    [InlineData(UnlockTuning.PetSlot1, 5)]
    [InlineData(UnlockTuning.Forge, 8)]
    [InlineData(UnlockTuning.Pvp, 10)]
    [InlineData(UnlockTuning.PetSlot2, 15)]
    [InlineData(UnlockTuning.MountSlot, 20)]
    [InlineData(UnlockTuning.PetSlot3, 30)]
    [InlineData(UnlockTuning.TalentBranchFortune, 40)]
    [InlineData(UnlockTuning.MythicTier, 60)]
    [InlineData(UnlockTuning.CodexMastery, 100)]
    public void Each_07_section_1_1_row_unlocks_at_the_level_the_table_gives_it(string unlock, int level)
    {
        Ladder.LevelFor(unlock).ShouldBe(level);
    }

    /// <summary>
    /// The gate opens <b>at</b> the rung, and not one level below it.
    /// </summary>
    /// <remarks>
    /// Both sides, at two different rungs. A comparison written <c>&gt;</c> where it should be
    /// <c>&gt;=</c> passes the "locked below" half on its own and locks every system one level late
    /// for every player in the game.
    /// </remarks>
    [Theory]
    [InlineData(UnlockTuning.Forge, 8)]
    [InlineData(UnlockTuning.MythicTier, 60)]
    public void The_gate_opens_at_the_rung_and_not_a_level_below_it(string unlock, int rung)
    {
        UnlockGate.IsUnlocked(unlock, rung - 1, Ladder).ShouldBeFalse();
        UnlockGate.IsUnlocked(unlock, rung, Ladder).ShouldBeTrue();
        UnlockGate.IsUnlocked(unlock, rung + 1, Ladder).ShouldBeTrue();
    }

    /// <summary>
    /// 🔒 An unlock the ladder does not author is refused, never answered as open or as locked.
    /// </summary>
    /// <remarks>
    /// Answering "locked" would be safe-looking and wrong — a typo in a caller would silently gate a
    /// system nobody could ever reach — and answering "open from level 1" would ungate it for
    /// everybody. Both are decisions this rule is not entitled to take on a caller's behalf.
    /// </remarks>
    [Fact]
    public void An_unlock_the_ladder_does_not_author_is_refused()
    {
        Should.Throw<MissingContentException>(() => UnlockGate.IsUnlocked("PET_SLOT_4", 200, Ladder))
            .Message.ShouldContain("PET_SLOT_4");
    }

    /// <summary>A blank unlock id is a caller defect rather than a gate.</summary>
    [Fact]
    public void A_blank_unlock_id_is_refused()
    {
        Should.Throw<ArgumentException>(() => UnlockGate.IsUnlocked("  ", 200, Ladder));
    }

    /// <summary>
    /// 🔒 The ladder is floored by identity: every `07` §1.1 row is required, and dropping one from
    /// the data fails the read rather than leaving the gate it guards with nothing to compare against.
    /// </summary>
    /// <remarks>
    /// Steering S3, in the direction that matters: a reader over an open key space is satisfied by an
    /// empty object, and every rung it cannot find would then be a <c>MissingContentException</c> at
    /// some future call site rather than a failure here.
    /// </remarks>
    [Fact]
    public void A_ladder_missing_an_07_section_1_1_row_is_refused()
    {
        UnlockTuning.RowsAuthoredBy07.Count.ShouldBe(
            9, "07 §1.1's table has ten rows and nine of them are keyed unlocks; the tenth is the cap.");

        var incomplete = ProgressionDocuments.With(
            unlocks: ContentValue.Object(
                ProgressionDocuments.ShippedUnlocks
                    .Where(row => !string.Equals(row.Unlock, UnlockTuning.MountSlot, StringComparison.Ordinal))
                    .ToDictionary(row => row.Unlock, row => ContentValue.Number(row.Level), StringComparer.Ordinal)));

        Should.Throw<MissingContentException>(() => UnlockTuning.Read(incomplete))
            .Message.ShouldContain(UnlockTuning.MountSlot);
    }

    /// <summary>
    /// The `07` §1.1 cap row is the Legend Level range's, and a rung above it is refused.
    /// </summary>
    /// <remarks>
    /// The table's tenth row — "200 | Level cap" — is deliberately not a rung: a second copy of the
    /// maximum under <c>#/unlocks</c> could disagree with the range. What replaces it is this
    /// refusal, which is the only way the two could contradict each other while both look plausible.
    /// </remarks>
    [Fact]
    public void A_rung_above_the_Legend_Level_cap_is_refused()
    {
        var beyond = ProgressionDocuments.With(
            unlocks: ContentValue.Object(
                ProgressionDocuments.ShippedUnlocks.ToDictionary(
                    row => row.Unlock,
                    row => ContentValue.Number(
                        string.Equals(row.Unlock, UnlockTuning.CodexMastery, StringComparison.Ordinal)
                            ? ProgressionDocuments.ShippedLegendLevelMax + 1
                            : row.Level),
                    StringComparer.Ordinal)));

        Should.Throw<InvalidTunableException>(() => UnlockTuning.Read(beyond))
            .Message.ShouldContain("no account can ever reach");
    }

    /// <summary>
    /// The ladder is an open key space: rows other documents author are read alongside `07` §1.1's.
    /// </summary>
    /// <remarks>
    /// A system's unlock level is authored by the document that owns the system — `25` for dungeons,
    /// `27` §2 for guilds, `26` for events — so a closed enum here would make every future system a
    /// code edit. This is the half that proves the reader is not filtering to the nine.
    /// </remarks>
    [Fact]
    public void Rows_other_documents_author_are_read_alongside_them()
    {
        Ladder.LevelFor("DUNGEONS").ShouldBe(8);
        Ladder.LevelFor("GUILDS").ShouldBe(15, "27 §2 unlocks guilds at Legend Level 15.");
        Ladder.LevelFor("EVENTS").ShouldBe(12);
    }

    /// <summary>The block's <c>_doc</c> prose is not read as a rung.</summary>
    /// <remarks>
    /// 🔴 <b>The fixture ladder authors a <c>_doc</c> member, and that is the whole case.</b> It used
    /// to assert the absence of a key the fixture never carried — so <c>UnlockTuning.Read</c>'s
    /// <c>continue</c> over the member could be deleted and this stayed green, while the shipped
    /// <c>progression.json#/unlocks</c> <em>does</em> carry one and would have been read as a rung
    /// and refused as a level outside the Legend range. The rung count comes with it: "no <c>_doc</c>
    /// key" is also satisfied by a reader that dropped every member, and the count is what tells the
    /// two apart.
    /// </remarks>
    [Fact]
    public void The_doc_member_is_read_as_prose_and_not_as_a_rung()
    {
        ProgressionDocuments.Shipped
            .Read(UnlockTuning.UnlocksReference)
            .MemberNames.ShouldContain(
                ProgressionDocuments.ShippedUnlocksDocMember,
                "the premise: a ladder with no _doc member exercises none of this.");

        Ladder.Ladder.Keys.ShouldNotContain(ProgressionDocuments.ShippedUnlocksDocMember);

        Ladder.Ladder.Count.ShouldBe(
            ProgressionDocuments.ShippedUnlocks.Count,
            "every rung the document authors is read and the prose member is not, so the ladder holds " +
            "exactly the authored rows — a reader that skipped more than the prose would leave a gate " +
            "with nothing to compare against.");
    }
}
