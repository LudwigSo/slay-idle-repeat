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
/// 🔴 <b>No <em>design</em> bound is authored on the name or the slot number.</b> `07` §4 and
/// `09` §2.1 both say "named" without saying how long, `12` §2 grants a subscriber "unlimited"
/// presets, and the hero name's own 12 is `07` §1's number for a different field. None of those
/// holes is filled here.
/// </para>
/// <para>
/// ⚠️ <b>What <em>is</em> bounded is what an untrusted client can make the persisted row grow to,
/// and that is a storage decision rather than a design one.</b> A preset is appended to
/// <c>PlayerSnapshot.Presets</c>, which is rehydrated and canonically hashed on <em>every</em>
/// command — so an unbounded name and an unbounded slot number let a client make every future
/// command of that account slower and eventually make the row unencodable at all. The two ceilings
/// are deliberately far above anything the design set describes (its own examples are "Boss push",
/// "Gold farm", "PvP", and its own allowance is three): they refuse abuse and constrain no design
/// decision.
/// </para>
/// <para>
/// 🔒 <b>Both are read, not invented here.</b> They were two <c>const</c>s in this file until the M4
/// review, which meant two numbers no document authorised sat where no rule in the repo could see
/// them — a <c>const</c> is folded to a literal and leaves no IL trace. They are now authored at
/// <c>ads.json#/presetStorage</c>, whose <c>_doc</c> says in as many words that no design section
/// asks for either, and reach this type through <see cref="Content.PresetTuning"/>. Authoring a real
/// design limit replaces them at that pointer rather than in this file.
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
    /// <param name="tuning">The authored preset numbers, including the two storage bounds.</param>
    /// <returns>The preset, or a failure naming what was wrong with it.</returns>
    /// <remarks>
    /// There is deliberately no <em>design</em> upper bound on <paramref name="slot"/> here: how many
    /// slots a player may write is an <em>entitlement</em> question that needs the authored free
    /// allowance and the session's Plus flag, and neither is visible from inside the aggregate. The
    /// handler answers it. What is checked here is the storage bound, which is a different question
    /// with a different answer.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="loadout"/> or <paramref name="tuning"/> is null.</exception>
    internal static Result<LoadoutPreset> Create(
        int slot, string name, Loadout loadout, Content.PresetTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(loadout);
        ArgumentNullException.ThrowIfNull(tuning);

        var fault = ShapeFault(slot, name) ?? StorageFault(slot, name, tuning);

        return fault is null
            ? Result<LoadoutPreset>.Success(new LoadoutPreset(slot, name, loadout))
            : Result<LoadoutPreset>.Failure(fault);
    }

    /// <summary>Reads a persisted preset.</summary>
    /// <param name="snapshot">The persisted row.</param>
    /// <returns>The preset, or a failure naming what was wrong with the row.</returns>
    /// <remarks>
    /// 🔒 <b>The two storage bounds are checked on write and deliberately <em>not</em> here.</b> They
    /// are authored content now, and content changes without a build — so applying them on load would
    /// mean lowering <c>longestNameTextElements</c> tomorrow makes today's stored presets unloadable,
    /// turning an authoring edit into an account outage. That is the same trade <c>HeroNameRule</c>
    /// refuses by name, and for the same reason. What is checked here is the row's <em>shape</em> —
    /// a slot below the first, a blank name, a control character — none of which is tunable and none
    /// of which a legitimately written row can ever have had.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is null.</exception>
    internal static Result<LoadoutPreset> Rehydrate(LoadoutPresetSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var fault = ShapeFault(snapshot.Slot, snapshot.Name);

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

    /// <summary>
    /// What is wrong with a slot and a name that <b>no</b> preset may ever have had, or
    /// <see langword="null"/> when nothing is. Checked on write and on load alike: none of these is
    /// tunable, so tightening one cannot strand a row that was legitimate when it was written.
    /// </summary>
    private static string? ShapeFault(int slot, string name)
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

    /// <summary>
    /// What exceeds an authored storage bound, or <see langword="null"/> when nothing does. Checked
    /// on write only — see <see cref="Rehydrate"/>'s remarks for why not on load.
    /// </summary>
    private static string? StorageFault(int slot, string name, Content.PresetTuning tuning)
    {
        if (slot > tuning.HighestSlot)
        {
            return "preset slot " + Text(slot) + " is above " + Text(tuning.HighestSlot) + ", the " +
                   "highest a row can store. 12 §2 grants a subscriber unlimited presets and this " +
                   "does not take that back — it bounds what an untrusted client can make the " +
                   "persisted row grow to, which is rehydrated and hashed on every command. The " +
                   "number is authored at " + Content.PresetTuning.HighestSlotReference + ".";
        }

        return new StringInfo(name).LengthInTextElements > tuning.LongestNameTextElements
            ? "preset slot " + Text(slot) + " has a name longer than " +
              Text(tuning.LongestNameTextElements) + " characters, the longest a row can store. " +
              "07 §4's own examples are 'Boss push', 'Gold farm' and 'PvP'; this bounds the " +
              "persisted row rather than the player's choice of label. The number is authored at " +
              Content.PresetTuning.LongestNameReference + "."
            : null;
    }

    /// <summary>Renders a number with <see cref="CultureInfo.InvariantCulture"/>, so messages read the same on every host.</summary>
    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}
