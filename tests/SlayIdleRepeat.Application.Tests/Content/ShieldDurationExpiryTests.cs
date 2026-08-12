using System.Text.Json;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// 🔴 A <b>declared exception that expires by itself</b> (steering S4) — M2-09 left `18` §6's
/// durations unreachable from a DSL <c>SHIELD</c>, and this is what fires the day content depends on
/// them.
/// </summary>
/// <remarks>
/// <para>
/// <c>IAttackPipeline.GrantWard</c> carries no duration. M2-03 declared it that way and M2-09
/// deliberately did not widen it, because M2-10, M2-12 and M2-14 are all coding against the
/// signature. `05` §4.1's segments <em>do</em> support <c>expiresAt?</c> and the expiring form exists
/// (<c>BattleServices.GrantWard</c>) — but that is a battle-layer route, and the op layer holds only
/// an <c>EffectOpSeams</c>, so <c>DamageAndHealingOps.Shield</c> drops <c>effect.Duration</c> on the
/// floor and the ward lasts the whole fight.
/// </para>
/// <para>
/// ⚠️ <b>The subject set is empty today, and that is DECLARED rather than discovered.</b> `18` §7's
/// perks are M3-07's and no authored effect JSON exists anywhere under <c>game-data/content/</c> yet
/// — <c>SubjectSetFloorTests.Pending</c> tracks all ten of `18` §8 step 1's sources for that reason.
/// So this case passes vacuously by construction, exactly like the rules
/// <c>SubjectSetFloorTests</c>' own remarks describe, and the floor below is what stops it passing
/// vacuously for the <em>wrong</em> reason (the data tree moving, or the scan stopping matching).
/// </para>
/// <para>
/// 🔒 <b>When it fires, the fix is not to delete it.</b> A <c>SHIELD</c> with a duration is content
/// asking for a ward that ends, and the engine must be able to end one before the content ships:
/// widen the seam (`18` §6's bookkeeping is M2-06's evaluator driven by M2-10's slot-2 sweep), then
/// delete this case in the same commit.
/// </para>
/// </remarks>
public sealed class ShieldDurationExpiryTests
{
    /// <summary>
    /// A floor under the scan (steering S3). The real tree carried well over a hundred documents when
    /// this landed; the floor is far below that and exists only to break the silence if
    /// <see cref="RepoData.Documents"/> starts returning nothing.
    /// </summary>
    private const int DocumentFloor = 20;

    /// <summary>
    /// 🔴 No shipped effect authors a <c>SHIELD</c> with a <c>duration</c>, because M2-09 cannot
    /// honour one.
    /// </summary>
    [Fact]
    public void No_shipped_effect_authors_a_SHIELD_with_a_duration()
    {
        var documents = RepoData.Documents;

        documents.Count.ShouldBeGreaterThanOrEqualTo(
            DocumentFloor,
            "the scan reads the real game-data tree; an empty one would pass this case over nothing");

        var offenders = new List<string>();

        foreach (var (path, text) in documents)
        {
            if (!path.EndsWith(".json", StringComparison.Ordinal))
            {
                continue;
            }

            using var document = JsonDocument.Parse(text);

            Walk(document.RootElement, path, offenders);
        }

        offenders.ShouldBeEmpty(
            "a DSL SHIELD's duration is DROPPED by DamageAndHealingOps.Shield — the ward it grants " +
            "lasts the whole fight. See this class's remarks: widen IAttackPipeline.GrantWard (or " +
            "route `18` §6's bookkeeping into BattleServices.GrantWard) before shipping the content, " +
            "then delete this case.");
    }

    /// <summary>
    /// Every object in the tree that is a `18` §1 <c>SHIELD</c> effect carrying a <c>duration</c>.
    /// </summary>
    /// <remarks>
    /// A structural walk rather than a text search: <c>"SHIELD"</c> appears in the shipped data as
    /// the elite-modifier id <c>SHIELDED</c> and would make a grep report a false positive on
    /// <c>content/enemies/enemies.json</c>, which is how a rule becomes noise people suppress.
    /// </remarks>
    private static void Walk(JsonElement element, string path, List<string> offenders)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("op", out var op) &&
                    op.ValueKind == JsonValueKind.String &&
                    string.Equals(op.GetString(), "SHIELD", StringComparison.Ordinal) &&
                    element.TryGetProperty("duration", out _))
                {
                    var id = element.TryGetProperty("id", out var authored) ? authored.GetString() : "(no id)";
                    offenders.Add($"{path}: effect '{id}' is a SHIELD with a duration");
                }

                foreach (var property in element.EnumerateObject())
                {
                    Walk(property.Value, path, offenders);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, path, offenders);
                }

                break;

            default:
                break;
        }
    }
}
