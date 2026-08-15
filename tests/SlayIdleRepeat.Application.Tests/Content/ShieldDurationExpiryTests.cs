using System.Text.Json;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>A declared exception that expires by itself: a DSL SHIELD's duration is dropped by the current pipeline, and this test fires the day content depends on it.</summary>
/// <remarks>
/// <c>IAttackPipeline.GrantWard</c> carries no duration — an expiring form exists at the battle
/// layer (<c>BattleServices.GrantWard</c>), but the op layer only reaches <c>EffectOpSeams</c>, so
/// <c>DamageAndHealingOps.Shield</c> drops <c>effect.Duration</c> and the ward lasts the whole
/// fight. The subject set is empty today because no authored effect JSON exists yet, so this case
/// passes vacuously by construction — the floor below guards against it passing vacuously for the
/// wrong reason instead (an empty or unreadable data tree). When it fires, the fix is not to delete
/// it: widen the seam so the engine can end a timed ward, then delete this case in the same commit.
/// </remarks>
public sealed class ShieldDurationExpiryTests
{
    /// <summary>
    /// A floor under the scan (steering S3). The real tree carried well over a hundred documents when
    /// this landed; the floor is far below that and exists only to break the silence if
    /// <see cref="RepoData.Documents"/> starts returning nothing.
    /// </summary>
    private const int DocumentFloor = 20;

    /// <summary>No shipped effect authors a SHIELD with a duration, because the engine cannot honour one yet.</summary>
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

    /// <summary>Every object in the tree that is a SHIELD effect carrying a duration.</summary>
    /// <remarks>A structural walk rather than a text search: <c>"SHIELD"</c> appears in the shipped data as the elite-modifier id <c>SHIELDED</c>, which would make a grep report a false positive.</remarks>
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
