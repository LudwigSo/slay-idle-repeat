using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Model.Gear;

namespace SlayIdleRepeat.Core.Tests.Rules.Forge;

/// <summary>
/// Hermetic forge fixtures: the numbers the three operations read, and a fusion-shaped selection.
/// </summary>
/// <remarks>
/// Items come from <see cref="Inventories.Item"/> rather than a second builder, so a fusion case and
/// an inventory case describe the same kind of item — a fixture pair that drifted would let a merge
/// rule pass over items no container could hold.
/// </remarks>
internal static class Forges
{
    /// <summary>The forge numbers every case here reads.</summary>
    internal static ForgeTuning Tuning { get; } = ForgeTuning.Read(ForgeDocuments.Shipped);

    /// <summary>The gear tables the affix re-roll reads.</summary>
    internal static DropsTuning Drops => Inventories.Drops;

    /// <summary>The authored mercy rule the enhancement rate is raised by.</summary>
    internal static EnhanceRule Mercy { get; } = LuckTuning.Read(LuckDocuments.LuckOnly()).Enhance;

    /// <summary>A draw stream over a named seed. Fixed, because a fusion's affixes are a draw.</summary>
    /// <param name="seed">The seed. Named at the call site so a case's answer is reproducible.</param>
    /// <returns>The stream, at index 0.</returns>
    internal static DeterministicRng Draws(ulong seed = 0x5EED) =>
        new(seed, RngStreams.Forge, position: 0);

    /// <summary>
    /// Three items that differ only by identity — the shape a legal fusion takes.
    /// </summary>
    /// <param name="rarity">The band all three share.</param>
    /// <param name="enhanceLevel">The level all three share.</param>
    /// <param name="family">The base item all three share.</param>
    /// <returns>The three inputs, in the order a client would name them.</returns>
    internal static IReadOnlyList<GearInstance> Triple(
        Rarity rarity = Rarity.C, int enhanceLevel = 0, GearFamily family = GearFamily.BLADE) =>
    [
        Inventories.Item("merge_a", family, rarity, enhanceLevel: enhanceLevel),
        Inventories.Item("merge_b", family, rarity, enhanceLevel: enhanceLevel),
        Inventories.Item("merge_c", family, rarity, enhanceLevel: enhanceLevel),
    ];
}
