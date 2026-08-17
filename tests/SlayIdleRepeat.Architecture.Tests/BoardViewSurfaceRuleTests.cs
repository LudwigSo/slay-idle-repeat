using Mono.Cecil;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// 🔒 `30` §11.2 / `03` §1.1 — the containment of M7-05b's board surface: <b>the VIEW is exported,
/// the PRODUCER is not.</b>
/// </summary>
/// <remarks>
/// <para>
/// `03` §1.1's board is deliberately never persisted — it regenerates from <c>RunSeed</c> on every
/// command — so until M7-05b nothing outside <c>Core</c> could see a tile track or a fork preview at
/// all, and the client's Board screen (S05) could draw neither. The widening that fixed it is
/// <em>narrow by construction</em>: five names in <c>Domain.PublicRuleTypes</c>, a projection that
/// takes two already-public snapshots and hands back read-only records.
/// </para>
/// <para>
/// ⚠️ <b>Nothing in the existing suite would notice that narrowness being lost.</b>
/// <c>Handlers_and_Rules_are_internal</c> is satisfied the moment a name is added to
/// <c>Domain.PublicRuleTypes</c>, and <c>PublicRuleTypeFloorTests</c> asks whether the listed names
/// resolve and are public — neither asks <em>which</em> types they are. A <c>BoardView.Graph</c>
/// property, a <c>Project(RunSnapshot, DeterministicRng)</c> overload, or a public
/// <c>BoardGenerator</c> added "so the client can preview a board" would each pass every rule in
/// this repository and would hand the outside world the machinery that decides where a run goes.
/// </para>
/// <para>
/// So three rules: the producer stays <c>internal</c>; the public surface's signature closure names
/// nothing beyond the five plus the two snapshots it already took; and no public board member hands
/// out a draw stream.
/// </para>
/// <para>
/// 🔒 A <b>new file</b>, on <c>BossEngineRuleTests</c>' precedent and for its reason: the names below
/// are this rule's own subject set rather than <c>Infrastructure/Domain.cs</c>'s, and
/// <see cref="The_boards_producer_stays_internal"/> floors every one of them BY NAME (steering S3),
/// so a rename cannot empty the set.
/// </para>
/// </remarks>
public sealed class BoardViewSurfaceRuleTests
{
    /// <summary>The board namespace both halves of this file live in.</summary>
    private const string BoardNamespace = "SlayIdleRepeat.Core.Rules.Board";

    /// <summary>
    /// The board's machinery: the types that DECIDE a board and MOVE a run along one. Every one of
    /// them must stay <c>internal</c>.
    /// </summary>
    /// <remarks>
    /// Named individually rather than "everything under <c>Rules/Board/</c> except the five",
    /// because that phrasing is satisfied by an empty directory. Each name is floored on its own, so
    /// a rename is a failure rather than a silent shrink of the subject set (steering S3).
    /// </remarks>
    private static readonly (string Name, string Reason)[] Machinery =
    {
        ("BoardGenerator", "the weighted draw, constraints C1-C7 and the fork placement — it DECIDES a board"),
        ("BoardGraph", "the graph itself, with the edge list a caller could walk to route a run"),
        ("BoardNode", "the graph's own node record; the exported track node is a projection of it"),
        ("BoardEdge", "the edges are how a run moves; exporting them exports the topology"),
        ("EdgeKind", "only meaningful beside an edge, which is not exported"),
        ("NodeId", "the graph's identity type; the view hands out plain ints so this stays internal"),
        ("ForkPreview", "the producer's preview record; BoardFork is what the client is given"),
        ("MovementEngine", "advancing a run along the board is GameRules.Apply's business (30 §11.2)"),
        ("BoardResolution", "the one seam that resolves a run's board, and it takes a live RunRngScope"),
        ("ChapterBoardConfig", "the generator's input; a caller holding one can generate boards"),
        ("ChapterBoardTuning", "the reader that builds that input out of content"),
        ("TileKindIds", "the TILE_* wire ids; 30 §11.6 keeps one vocabulary and the wire owns it"),
        ("EnemyPowerFormula", "05 §2's per-node scaling — a rules calculation, not a view"),
    };

    /// <summary>
    /// The five names M7-05b exports, and the whole of the surface the two rules below are stated
    /// over.
    /// </summary>
    /// <remarks>
    /// Restated here rather than filtered out of <c>Domain.PublicRuleTypes</c>: that list is the
    /// exemption arm of another rule and holds <c>CombatSimulator</c>'s closure too, so deriving this
    /// set from it would make these rules quietly follow whatever anybody adds there — which is the
    /// drift they exist to catch. <c>PublicRuleTypeFloorTests</c> pins the list; this pins the board
    /// half of it.
    /// </remarks>
    private static readonly string[] BoardSurface =
    {
        "BoardView", "BoardTrackNode", "BoardFork", "TileKind", "ForkLabel",
    };

    /// <summary>
    /// The <c>Core</c> types a public board member may name besides the five: the two snapshots
    /// <c>Project</c> takes, both public long before this branch.
    /// </summary>
    private static readonly string[] AlreadyPublicInputs =
    {
        "SlayIdleRepeat.Core.Model.Snapshots.RunSnapshot",
        "SlayIdleRepeat.Core.Content.ContentSnapshot",
    };

    /// <summary>
    /// Types a public board member may never name, whatever <see cref="AlreadyPublicInputs"/> is
    /// later widened to.
    /// </summary>
    private static readonly (string FullName, string Reason)[] NeverInTheBoardSurface =
    {
        ("SlayIdleRepeat.Core.Rng.DeterministicRng",
            "14 §8.1's draw stream. It is already public — this is not about its accessibility but " +
            "about the board surface not HANDING one out: a caller holding the run's board stream " +
            "can generate a board of its own choosing, or move the tracked counter past a Portal draw."),
        ("SlayIdleRepeat.Core.Rng.RngStreams",
            "the stream registry. A board member that named it would be offering a caller the choice " +
            "of which stream a board comes off, which 03 §1.1 fixes to one."),
        (BoardNamespace + ".BoardGenerator",
            "the producer. M7-05b exports the VIEW; a public member returning or taking the generator " +
            "hands out the thing that decides a board."),
        (BoardNamespace + ".BoardGraph",
            "the graph. Exporting it exports the edge list, which is the run's routing."),
    };

    /// <summary>
    /// 🔒 `30` §11.2 — the board's producer stays <c>internal</c>. M7-05b's widening exports the
    /// <b>view</b> and not the machinery that decides a board or moves a run along one.
    /// </summary>
    /// <remarks>
    /// Floored by NAME rather than by count (steering S3): a name that resolves to nothing is an
    /// offender in its own right, so renaming <c>BoardGenerator</c> cannot quietly empty this rule's
    /// subject set and leave a public producer unwatched.
    /// </remarks>
    [Fact]
    public void The_boards_producer_stays_internal()
    {
        var offenders = new List<string>();

        foreach (var (name, reason) in Machinery)
        {
            var matches = Domain.CoreTypes
                .Where(t => t.Name.Equals(name, StringComparison.Ordinal))
                .ToArray();

            if (matches.Length == 0)
            {
                offenders.Add(
                    $"'{name}' resolves to no Core type. This rule is stated over it by identity — {reason} " +
                    "— so a rename empties its share of the subject set and the type it names could go " +
                    "public unwatched. Rename the entry in the same commit, or delete it and say why the " +
                    "type no longer exists.");

                continue;
            }

            offenders.AddRange(
                matches
                    .Where(t => t.IsPublic)
                    .Select(t =>
                        $"{t.FullName} is public. It is board MACHINERY — {reason} — and the whole point of " +
                        "M7-05b's widening of 30 §11.2's public surface was to export the VIEW and not the " +
                        "PRODUCER: BoardView, BoardTrackNode, BoardFork and their two enums, so the client's " +
                        "Board screen can draw a track it cannot generate, route or advance. If this type " +
                        "genuinely has to leave Core, that is a decision for a kickoff, not a keyword."));
        }

        ArchRule.Empty(
            offenders,
            "30 §11.2: the board's producer stays internal — M7-05b exports the view, not the machinery " +
            "that decides a board (03 §1.1).");
    }

    /// <summary>
    /// 🔒 `30` §11.2 — the public board surface's signature closure names nothing else: every
    /// <c>Core</c> type reached from a public member of the five is one of the five, or one of the
    /// two snapshots the projection already took.
    /// </summary>
    /// <remarks>
    /// This is the rule that fires the day somebody adds a <c>BoardView.Graph</c>, a
    /// <c>ForkPreview</c>-returning member or a <c>Project</c> overload taking a
    /// <c>DeterministicRng</c>. R16's discipline, applied to the board half of the list: the surface
    /// is ENUMERATED, so growing it costs a line in a diff.
    /// </remarks>
    [Fact]
    public void The_public_board_surface_names_nothing_beyond_its_own_five_types()
    {
        var offenders = new List<string>();
        var surface = ResolveSurface(offenders);
        var permitted = BoardSurface
            .Select(n => BoardNamespace + "." + n)
            .Concat(AlreadyPublicInputs)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var type in surface)
        {
            foreach (var (member, reference) in PublicSignatureTypes(type))
            {
                var resolved = Domain.CoreTypes.FirstOrDefault(
                    t => t.FullName.Equals(reference.FullName, StringComparison.Ordinal));

                if (resolved is null || permitted.Contains(resolved.FullName))
                {
                    continue;
                }

                offenders.Add(
                    $"{type.FullName}.{member} names {resolved.FullName}, which is neither one of M7-05b's " +
                    "five exported board types nor one of the two snapshots BoardView.Project already " +
                    "took. 30 §11.2's public surface is ENUMERATED (R16): a member that reaches a sixth " +
                    "Core type widens what the client can hold without anybody deciding to widen it. " +
                    "Project it into the view's own records, or make the member internal.");
            }
        }

        ArchRule.Empty(
            offenders,
            "30 §11.2: the board view's signature closure is the five exported types plus RunSnapshot " +
            "and ContentSnapshot — nothing else leaks out with it.");
    }

    /// <summary>
    /// 🔒 `30` §11.2 / `14` §8.1 — the public board surface exposes no draw: no public member of the
    /// five names a <c>DeterministicRng</c>, the stream registry, or the generator.
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>DeterministicRng</c> and <c>RngStreams</c> are <b>already public</b> in <c>Core/Rng/</c>,
    /// so this is not a claim about their accessibility — it is a claim about the board surface not
    /// handing one out. Stated separately from the closure rule above and not folded into it: that
    /// rule's permitted set is a list somebody can edit, and "it was already public anyway" is
    /// exactly the argument that would get one added to it. This list is not an allowlist and has no
    /// such escape hatch.
    /// </remarks>
    [Fact]
    public void The_public_board_surface_hands_out_no_draw_stream()
    {
        var offenders = new List<string>();
        var surface = ResolveSurface(offenders);

        foreach (var type in surface)
        {
            foreach (var (member, reference) in PublicSignatureTypes(type))
            {
                var hit = NeverInTheBoardSurface.FirstOrDefault(
                    b => b.FullName.Equals(reference.FullName, StringComparison.Ordinal));

                if (hit.FullName is not null)
                {
                    offenders.Add(
                        $"{type.FullName}.{member} names {reference.FullName} — {hit.Reason} 03 §1.1's board " +
                        "regenerates from the run seed inside Core, and the view is the READ of that " +
                        "layout; a member that takes or returns the draw makes it something a caller " +
                        "can steer.");
                }
            }
        }

        ArchRule.Empty(
            offenders,
            "30 §11.2 / 14 §8.1: the board view is a read-only projection — it hands out no draw " +
            "stream and no generator.");
    }

    /// <summary>
    /// The five exported types, with the S3 floor that every one of them resolves to a public
    /// <c>Core</c> type under <c>Rules/</c>.
    /// </summary>
    /// <remarks>
    /// By NAMED MEMBER rather than by count: the two rules above are "no member of set S does X", so
    /// an empty S passes forever — and S is empty exactly when the board projection has been renamed
    /// away or never landed, which is the state this file must not report success over.
    /// </remarks>
    private static IReadOnlyList<TypeDefinition> ResolveSurface(List<string> offenders)
    {
        var resolved = new List<TypeDefinition>(BoardSurface.Length);

        foreach (var name in BoardSurface)
        {
            var type = Domain.CoreTypes.FirstOrDefault(
                t => t.Name.Equals(name, StringComparison.Ordinal) &&
                     Il.IsUnder(Il.NamespaceOf(t), BoardNamespace));

            if (type is null)
            {
                offenders.Add(
                    $"'{name}' resolves to no Core type under {BoardNamespace}. M7-05b's public board " +
                    "surface is these five and nothing else, so a missing one leaves both surface rules " +
                    "quantifying over less than they were written against (steering S3).");

                continue;
            }

            if (!type.IsPublic)
            {
                offenders.Add(
                    $"{type.FullName} is in the exported board surface but is not public. The Board screen " +
                    "(S05) is a separate assembly with no InternalsVisibleTo grant, so an internal member " +
                    "of the five is a surface the named consumer cannot reach at all — and both rules " +
                    "below then govern a type nothing outside Core can see.");

                continue;
            }

            resolved.Add(type);
        }

        return resolved;
    }

    /// <summary>
    /// Every type named by a public member's signature: property types, public field types, and the
    /// return and parameter types of every public method and constructor.
    /// </summary>
    /// <remarks>
    /// ⚠️ Properties are walked as PROPERTIES rather than through their accessors, and that is
    /// load-bearing. An auto-property's <c>get</c> carries <c>[CompilerGenerated]</c>, so the
    /// method-filter this suite normally uses — <c>!Domain.IsCompilerGenerated</c>, which is what
    /// keeps a record's <c>Equals</c> and <c>op_Equality</c> out — would skip every one of
    /// <c>BoardView</c>'s members and leave both rules above quantifying over the constructors alone.
    /// </remarks>
    private static IEnumerable<(string Member, TypeReference Reference)> PublicSignatureTypes(TypeDefinition type)
    {
        foreach (var property in type.Properties.Where(p => p.GetMethod is { IsPublic: true }))
        {
            foreach (var reference in Il.Flatten(property.PropertyType))
            {
                yield return (property.Name, reference);
            }
        }

        foreach (var field in type.Fields.Where(f => f.IsPublic && !Domain.IsCompilerGenerated(f)))
        {
            foreach (var reference in Il.Flatten(field.FieldType))
            {
                yield return (field.Name, reference);
            }
        }

        foreach (var method in type.Methods.Where(m => m.IsPublic && !Domain.IsCompilerGenerated(m)))
        {
            foreach (var reference in Il.SignatureTypes(method).SelectMany(Il.Flatten))
            {
                yield return (method.Name, reference);
            }
        }
    }
}
