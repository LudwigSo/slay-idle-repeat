using System.Globalization;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Model;

/// <summary>One saved loadout preset: a slot, the player's name for it, and what it restores.</summary>
/// <remarks>
/// <para>
/// Immutable, with an <c>internal</c> constructor: overwriting a preset replaces the whole object
/// rather than editing one, so a preset a caller is holding can never change under it — which is what
/// makes it safe for the aggregate to hand out the live instance.
/// </para>
/// <para>
/// 🔒 <b>The name is not profanity-filtered, and that is a decision rather than an omission.</b>
/// `27` §1's filter is stated over a guild's name and tag and the M4 kickoff extended it to the hero
/// name; both of those are shown to other players. A preset name is private — `07` §4's own examples
/// are "Boss push", "Gold farm", "PvP" — and nothing in the design set filters it. Filtering it
/// anyway would mean a player's private label for their own build could be refused by a word list
/// nobody wrote for it.
/// </para>
/// <para>
/// 🔴 <b>No length bound is enforced, because none is authored.</b> `07` §4 and `09` §2.1 both say
/// "named" and neither says how long, and the hero name's own 12 is `07` §1's number for a different
/// field. What is refused here is what makes a name unusable rather than merely long — blank, and
/// control or format characters, which are invisible in every field and can reorder the text around
/// them. The absent bound is left absent and greppable rather than filled with a plausible number.
/// </para>
/// </remarks>
public sealed class LoadoutPreset
{
    /// <summary>The one constructor. <c>internal</c>; every value has already been checked by <see cref="Create"/> or <see cref="Rehydrate"/>.</summary>
    internal LoadoutPreset(int slot, string name, Loadout loadout)
    {
        Slot = slot;
        Name = name;
        Loadout = loadout;
    }

    /// <summary>Which slot this preset occupies, counted from 1.</summary>
    public int Slot { get; }

    /// <summary>The player's own name for it, exactly as they typed it.</summary>
    public string Name { get; }

    /// <summary>What applying it restores.</summary>
    public Loadout Loadout { get; }

    /// <summary>The persisted shape.</summary>
    /// <returns>The row.</returns>
    public LoadoutPresetSnapshot ToSnapshot() => new(Slot, Name, Loadout.ToSnapshot());

    /// <summary>Builds a preset, refusing a slot or a name no preset may have.</summary>
    /// <param name="slot">The slot to occupy, at or above <see cref="Content.PresetTuning.FirstSlot"/>.</param>
    /// <param name="name">The player's name for it.</param>
    /// <param name="loadout">What it restores. A snapshot of the live loadout, taken by the caller.</param>
    /// <returns>The preset, or a failure naming what was wrong with it.</returns>
    /// <remarks>
    /// There is deliberately no upper bound on <paramref name="slot"/> here: how many slots a player
    /// may write is an <em>entitlement</em> question that needs the authored free allowance and the
    /// session's Plus flag, and neither is visible from inside the aggregate. The handler answers it.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="loadout"/> is null.</exception>
    internal static Result<LoadoutPreset> Create(int slot, string name, Loadout loadout)
    {
        ArgumentNullException.ThrowIfNull(loadout);

        var fault = Fault(slot, name);

        return fault is null
            ? Result<LoadoutPreset>.Success(new LoadoutPreset(slot, name, loadout))
            : Result<LoadoutPreset>.Failure(fault);
    }

    /// <summary>Reads a persisted preset.</summary>
    /// <param name="snapshot">The persisted row.</param>
    /// <returns>The preset, or a failure naming what was wrong with the row.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is null.</exception>
    internal static Result<LoadoutPreset> Rehydrate(LoadoutPresetSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var fault = Fault(snapshot.Slot, snapshot.Name);

        if (fault is not null)
        {
            return Result<LoadoutPreset>.Failure(fault);
        }

        if (snapshot.Loadout is null)
        {
            return Result<LoadoutPreset>.Failure(
                "preset slot " + Text(snapshot.Slot) + " carries no loadout at all. An absent loadout is " +
                "not an empty one: read as empty, applying the preset would strip the hero.");
        }

        var loadout = Loadout.Rehydrate(snapshot.Loadout);

        return loadout.IsFailure
            ? Result<LoadoutPreset>.Failure("preset slot " + Text(snapshot.Slot) + ": " + loadout.Error)
            : Result<LoadoutPreset>.Success(
                new LoadoutPreset(snapshot.Slot, snapshot.Name, loadout.Value));
    }

    /// <summary>What is wrong with a slot and a name, or <see langword="null"/> when nothing is.</summary>
    private static string? Fault(int slot, string name)
    {
        if (slot < Content.PresetTuning.FirstSlot)
        {
            return "preset slot " + Text(slot) + " is below " + Text(Content.PresetTuning.FirstSlot) +
                   ". Presets are counted from one, so slot 0 is what an uninitialised column reads " +
                   "as rather than a slot a player asked for.";
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return "preset slot " + Text(slot) + " has a blank name. 07 §4 saves NAMED presets, and a " +
                   "preset that renders as nothing is one the player cannot tell from the next.";
        }

        foreach (var character in name)
        {
            if (!char.IsControl(character) &&
                char.GetUnicodeCategory(character) != UnicodeCategory.Format)
            {
                continue;
            }

            return "preset slot " + Text(slot) + " has a name carrying U+" +
                   ((int)character).ToString("X4", CultureInfo.InvariantCulture) +
                   ", a control or format character. It is invisible in every field that shows the " +
                   "name and can reorder or truncate the text around it.";
        }

        return null;
    }

    /// <summary>Renders a number with <see cref="CultureInfo.InvariantCulture"/>, so messages read the same on every host.</summary>
    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}
