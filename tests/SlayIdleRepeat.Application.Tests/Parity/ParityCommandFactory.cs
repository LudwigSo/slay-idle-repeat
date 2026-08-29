using System.Reflection;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Application.Tests.Parity;

/// <summary>
/// One instance of any registered command, with drawn arguments.
/// </summary>
/// <remarks>
/// <para>
/// Reflective over the declared constructor rather than fifty-five hand-written <c>new</c>
/// expressions, which would be a second transcription of the vocabulary that silently stopped
/// driving whichever command it forgot. The vocabulary itself is never transcribed here: the walker
/// asks <c>GameRules.CommandTypesByWireName</c>, so a registry edit reaches this corpus with no
/// mirror edit.
/// </para>
/// <para>
/// Arguments are DRAWN rather than sampled at a fixed value. A fixed sample builds the same command
/// every time, and a thousand sequences of the same command is one sequence run a thousand times —
/// the walker needs a different <c>optionIndex</c>, a different slot and a different item id each
/// step or the corpus collapses. Ranges are small and mostly legal on purpose: an index far outside
/// any real board refuses on arrival, and a corpus of refusals compares the refusal path and nothing
/// else.
/// </para>
/// </remarks>
internal static class ParityCommandFactory
{
    /// <summary>The identifier vocabulary a drawn string comes from.</summary>
    /// <remarks>
    /// A mix a sample player owns nothing of and a few shapes the handlers recognise. The point is
    /// not to be accepted — an owned-item id cannot be drawn without reading the player's inventory,
    /// which would make the corpus depend on starting content — but to be a well-formed identifier so
    /// the refusal is the handler's own and not a parse failure the two sides could reach differently.
    /// </remarks>
    private static readonly string[] Identifiers =
    [
        "x", "GEAR_1", "PK_STRIKE", "TAL_A1", "BEAST_1", "AD_REVIVE", "OFFER_1", "CHEST_1",
        "GHOST_1", "MINI_TAP", "CONS_POTION", "MSG_1",
    ];

    /// <summary>
    /// The one parameter whose value is not drawn.
    /// </summary>
    /// <remarks>
    /// 🔴 <c>BEGIN_SESSION</c> carries the client's claim about which content it is on, and the wire
    /// refuses a claim that is not exactly the served version — before dispatch, and without
    /// consuming a sequence number. That check is a transport concern the in-process host does not
    /// have and is not meant to have, so a drawn hash would take the gateway down a path the host
    /// cannot follow and then desynchronise every command after it. Named by PARAMETER rather than by
    /// command type: this factory has no business knowing which command the wire happens to check.
    /// </remarks>
    private const string ContentClaimParameter = "ContentHash";

    /// <summary>Builds one command of the given type, drawing every argument.</summary>
    /// <param name="commandType">The registered command type.</param>
    /// <param name="rng">The corpus's draw stream.</param>
    /// <param name="contentVersion">The served content version, for the one claimed parameter.</param>
    /// <exception cref="InvalidOperationException">The type declares no single public constructor, or a parameter type has no draw.</exception>
    internal static GameCommand Build(Type commandType, DeterministicRng rng, string contentVersion)
    {
        ArgumentNullException.ThrowIfNull(commandType);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentVersion);

        // Single, not First: a tie-break between two constructors would silently pick, in a builder
        // that drives the whole vocabulary.
        var constructors = commandType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        if (constructors.Length != 1)
        {
            throw new InvalidOperationException(
                $"{commandType.Name} declares {constructors.Length} public constructors. If a second " +
                "is genuinely wanted, choose here deliberately rather than letting a tie-break pick.");
        }

        var constructor = constructors[0];
        var arguments = constructor.GetParameters()
            .Select(parameter => Draw(parameter, rng, contentVersion))
            .ToArray();

        return (GameCommand)constructor.Invoke(arguments);
    }

    private static object? Draw(ParameterInfo parameter, DeterministicRng rng, string contentVersion)
    {
        var type = parameter.ParameterType;

        if (string.Equals(parameter.Name, ContentClaimParameter, StringComparison.Ordinal))
        {
            return contentVersion;
        }

        if (type == typeof(int))
        {
            // Zero through five: every board index, shop slot, option index and quest slot a real
            // command carries is inside that band, so a drawn value lands on a legal one often enough
            // for the walk to make progress.
            return rng.Range(0, 6);
        }

        if (type == typeof(int?))
        {
            return rng.Range(1, 7);
        }

        if (type == typeof(bool))
        {
            return rng.Range(0, 2) == 0;
        }

        if (type == typeof(string))
        {
            return Identifiers[rng.Range(0, Identifiers.Length)];
        }

        if (type == typeof(DifficultyTier))
        {
            return Enum.GetValues<DifficultyTier>()[rng.Range(0, Enum.GetValues<DifficultyTier>().Length)];
        }

        if (type == typeof(GearSlot) || type == typeof(GearSlot?))
        {
            return Enum.GetValues<GearSlot>()[rng.Range(0, Enum.GetValues<GearSlot>().Length)];
        }

        if (type == typeof(GearFamily) || type == typeof(GearFamily?))
        {
            return Enum.GetValues<GearFamily>()[rng.Range(0, Enum.GetValues<GearFamily>().Length)];
        }

        if (type == typeof(GearInstanceId))
        {
            return new GearInstanceId(Identifiers[rng.Range(0, Identifiers.Length)]);
        }

        if (type == typeof(IReadOnlyList<string>))
        {
            return Draws(rng, () => Identifiers[rng.Range(0, Identifiers.Length)]);
        }

        if (type == typeof(IReadOnlyList<GearInstanceId>))
        {
            return Draws(rng, () => new GearInstanceId(Identifiers[rng.Range(0, Identifiers.Length)]));
        }

        // The EMPTY list rather than a fabricated row: an empty filter means "sweep nothing", a legal
        // payload, and a sampled rule would have to invent a rarity band and a ceiling that no design
        // document backs.
        if (type == typeof(IReadOnlyList<AutoSalvageRule>))
        {
            return Array.Empty<AutoSalvageRule>();
        }

        throw new InvalidOperationException(
            $"{parameter.Member.DeclaringType?.Name}.{parameter.Name} is a {type.Name}, which this " +
            "builder has no draw for. A payload type nothing here recognises is a new type in the " +
            "command vocabulary, and the parity corpus cannot drive a command it cannot construct.");
    }

    private static T[] Draws<T>(DeterministicRng rng, Func<T> draw)
    {
        var count = rng.Range(0, 3);
        var items = new T[count];
        for (var item = 0; item < count; item++)
        {
            items[item] = draw();
        }

        return items;
    }
}
