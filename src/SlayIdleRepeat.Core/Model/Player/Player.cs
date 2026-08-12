using System.Collections.ObjectModel;
using System.Globalization;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Model;

/// <summary>
/// 🔒 The <c>Player</c> aggregate root (`30` §4) — profile, the seven player-scoped currencies,
/// the two Energy banks, FTUE progress and the daily/weekly counter mechanism.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Why this type lives in <c>Core/Model/Player/</c> but in namespace
/// <c>SlayIdleRepeat.Core.Model</c>.</b> Measured before a line of it was written: a namespace
/// <c>SlayIdleRepeat.Core.Model.Player</c> containing a type <c>Player</c> makes the type
/// <b>unnameable</b> from anywhere inside <c>SlayIdleRepeat.Core.Model</c> — the child namespace
/// shadows it, and the compiler answers <c>error CS0118: 'Player' is a namespace but is used like
/// a type</c>. That is not hypothetical: `30` §4.1 writes <c>WorldSlice(Player Player, Run? Run,
/// …)</c> and puts it in <c>Model/</c>, so <b>M1-06 would have hit it on its first line</b>, as
/// would M1-05's <c>Run</c> under a mirrored <c>Model/Run/</c>. `30` §11.4's structure block draws
/// <b>directories</b> (<c>Model/ ├── Player/ Run/ Guild/</c>) and never says the namespace mirrors
/// them; <c>Model/Snapshots/</c> keeps its own namespace because <c>Snapshots</c> collides with
/// nothing. The directory is the file layout; the namespace is the layer.
/// </para>
/// <para>
/// 🔒 <b>Public getters, private constructor, <c>internal</c> mutators</b> (`30` §11.2): <em>"The
/// only public way to change state in this game is <c>GameRules.Apply</c>. Everything else the
/// outside world can see is a getter."</em> The two public methods that are not getters are
/// <see cref="ToSnapshot"/> and <see cref="Rehydrate"/> — `30` §11.3's validating factory pair,
/// which is the one hole <c>internal</c> would otherwise leave, since the Postgres adapter has to
/// be able to rebuild a player from a row without <c>InternalsVisibleTo</c>.
/// </para>
/// <para>
/// 🔒 <b>It holds state and invariants; it does not compute</b> (`30` §11.5). Nothing here calls
/// <c>EnergyMath</c> — <c>Model</c> may not reference <c>Rules</c>, so it could not — and there is
/// no <c>Power</c>, no <c>XpToNextLevel</c>, no levelling curve. A handler computes and hands the
/// answer to <see cref="SetEnergy"/>; the aggregate's job is to refuse an answer that would break
/// an invariant. The two invariants `30` §11.5 names that are in scope here are <em>"a currency
/// never goes negative"</em> and <em>"Energy never exceeds max + reserve"</em>.
/// </para>
/// <para>
/// ⚠️ <b>The energy invariant is enforced on mutation, not on rehydration, and that is forced.</b>
/// <c>EnergyMath.Deposit</c> states plainly that banks above the current maximum <em>"are possible
/// after a balance patch that lowered <c>baseMax</c>"</em> and that the player <em>"drains back
/// under the cap by playing"</em>. If <see cref="Rehydrate"/> refused such a row, lowering a
/// tuning number would stop every already-full player from loading — a balance patch turning into
/// an account outage. So the rule this aggregate holds is the one that makes both documents true:
/// <b>a mutation may never push a bank past its cap, and may never make an over-cap bank worse.</b>
/// A player already above the cap stays loadable and drains.
/// </para>
/// <para>
/// 🔒 <b>One seam for currency.</b> <see cref="MoveBalance"/> is the only method outside the
/// constructor that writes <c>_wallet</c>, and it is also the only one that writes <c>_energy</c>.
/// That is deliberate rather than tidy:
/// <c>DomainPurityTests.Every_currency_mutation_emits_CurrencyChanged</c> is an IL scan that
/// requires any method writing a currency-carrying field to <b>construct</b> a
/// <c>CurrencyChanged</c>, and routing the Energy write through the same method puts the Energy
/// path under that guard too — <c>_energy</c> is typed <c>EnergyBanks</c>, so the rule's own field
/// predicate (a <c>CurrencyId</c>-typed or <c>*wallet*</c>/<c>*currenc*</c>-named field) would
/// never have found it on its own.
/// </para>
/// <para>
/// ⚠️ <b>What this aggregate deliberately does not carry.</b> `30` §4 lists inventory, gear
/// instances, the unopened-container shelf (`24` §4.0), pity counters (`24`) and lifetime feat
/// counters (`28` D) among <c>Player</c>'s contents. All four are deferred with an entry in
/// <c>SlayIdleRepeat.Architecture.Tests.GapRegister</c>, each keyed on a type that must not yet
/// exist, so the build fails on the day each becomes writable. Entitlement is absent for a
/// different reason: `30` §3 and `12` §2.1 put it on the <b>session</b>, and it reaches the domain
/// as <c>GameContext.Entitlements</c>. And there is no factory for a <em>new</em> player: a
/// starting Energy value, a starting daily boundary and the level a player begins at are decisions
/// `10` §3 and `07` §1 leave to the milestone that creates accounts (M4-10 / M1-11), and inventing
/// them here to make a convenient constructor is exactly what steering S6 forbids.
/// <see cref="Rehydrate"/> is the only way to obtain one, which is precisely what `30` §11.3 says
/// it should be.
/// </para>
/// </remarks>
public sealed class Player
{
    /// <summary>
    /// 🔒 The <see cref="DomainEvent.Sequence"/> an aggregate stamps on an event it produces.
    /// </summary>
    /// <remarks>
    /// Zero, and it is a placeholder rather than a value. <c>DomainEvent</c> is explicit that the
    /// ordinal <em>"is assigned by <c>GameRules.Apply</c> (M1-06) — never by a constructor, and
    /// never by a caller"</em>, because a mutator does not know its event's position in the list
    /// the command will return. <c>Apply</c> restamps with a <c>with</c> expression, which
    /// <c>CurrencyChanged</c> keeps possible by leaving every component but <c>Reason</c> as the
    /// positional <c>init</c>.
    /// </remarks>
    internal const int UnstampedSequence = 0;

    /// <summary>The UTC time of day every game day and game week begins at (`30` §2.3, `27` §4).</summary>
    /// <remarks>
    /// Not a 📐 tunable: `30` §2.3 writes 05:00 UTC into the reset rule itself — <em>"quest expiry,
    /// the wheel's free spin, ad caps and dungeon entries all reset at 05:00 UTC whether or not
    /// anyone logs in"</em> — and no <c>tuning/</c> document authors it as a dial. It is the
    /// boundary the design is written against, so it is a constant here and the invariants quote it.
    /// </remarks>
    private static readonly TimeSpan GameDayStart = TimeSpan.FromHours(5);

    /// <summary>
    /// 🔒 The <b>six</b> player-scoped wallet currencies, in <see cref="CurrencyId"/> order.
    /// </summary>
    /// <remarks>
    /// `10` §1 fixes eight currencies. <c>GOLD</c> is <c>RUN</c>-scoped (milestone assumption A3)
    /// and belongs to <c>Run</c>; <c>ENERGY</c> is player-scoped but has two banks, so it is held
    /// as <see cref="EnergyBanks"/> rather than as a wallet row — six plus Energy is the seven
    /// currencies this aggregate owns. Written out rather than filtered from
    /// <see cref="Enum.GetValues{TEnum}()"/>: a ninth currency appended to the enum has to be
    /// placed here deliberately, and a filter would silently adopt it into every player's wallet
    /// and every <c>stateHash</c> in existence.
    /// </remarks>
    public static IReadOnlyList<CurrencyId> WalletCurrencies { get; } = new[]
    {
        CurrencyId.CROWNS,
        CurrencyId.SOUL_SHARDS,
        CurrencyId.ENHANCE_STONES,
        CurrencyId.MERGE_DUST,
        CurrencyId.BEAST_FEED,
        CurrencyId.HONOR,
    };

    /// <summary>
    /// 🔒 The wallet. Replaced <b>wholesale</b> on every movement rather than mutated in place.
    /// </summary>
    /// <remarks>
    /// Two reasons, and the second is the load-bearing one. It lets <see cref="Wallet"/> hand out
    /// the live object with no per-read allocation and no way for a caller to reach a mutable
    /// dictionary underneath. And <c>Every_currency_mutation_emits_CurrencyChanged</c> watches for
    /// a <c>stfld</c> to this field: a wallet mutated in place would emit no field write at all
    /// after construction, so the rule the whole `30` §7 attribution chain rests on would be
    /// awake, pointed at a real field, and still unable to see a single balance change.
    /// </remarks>
    private IReadOnlyDictionary<CurrencyId, long> _wallet;

    private EnergyBanks _energy;
    private DateTimeOffset _energyAnchorUtc;
    private DateTimeOffset _lastAppliedAtUtc;
    private FtueBeat _ftueBeat;
    private DateTimeOffset? _ftueCompletedAtUtc;
    private DateTimeOffset _dailyPeriodStartUtc;
    private DateTimeOffset _weeklyPeriodStartUtc;

    /// <summary>
    /// The daily counters, and the read-only view handed out by <see cref="DailyCounters"/>.
    /// </summary>
    /// <remarks>
    /// Mutated in place — unlike <c>_wallet</c>, which the currency rule requires to be replaced —
    /// so the view is built once and stays valid across every increment and reset. A counter is not
    /// a currency and emits no event; there is nothing for a field write to prove.
    /// </remarks>
    private readonly Dictionary<string, long> _dailyCounters;
    private readonly ReadOnlyDictionary<string, long> _dailyCountersView;
    private readonly Dictionary<string, long> _weeklyCounters;
    private readonly ReadOnlyDictionary<string, long> _weeklyCountersView;

    /// <summary>
    /// The one constructor. Private, and it <b>trusts</b>: every value has already been checked by
    /// <see cref="Rehydrate"/>, which is the only caller.
    /// </summary>
    /// <remarks>
    /// Validation lives in one place rather than two. A constructor that re-checked would either
    /// duplicate the rules — two lists that drift — or throw where `30` §11.3 promises a
    /// <see cref="Result{T}"/>, which is the difference between a corrupt row failing at the seam
    /// with a description and a corrupt row failing three rules later with a stack trace.
    /// </remarks>
    private Player(
        PlayerId id,
        string displayName,
        int legendLevel,
        long legendXp,
        IReadOnlyDictionary<CurrencyId, long> wallet,
        EnergyBanks energy,
        DateTimeOffset energyAnchorUtc,
        DateTimeOffset lastAppliedAtUtc,
        FtueBeat ftueBeat,
        DateTimeOffset? ftueCompletedAtUtc,
        DateTimeOffset dailyPeriodStartUtc,
        Dictionary<string, long> dailyCounters,
        DateTimeOffset weeklyPeriodStartUtc,
        Dictionary<string, long> weeklyCounters)
    {
        Id = id;
        DisplayName = displayName;
        LegendLevel = legendLevel;
        LegendXp = legendXp;
        _wallet = wallet;
        _energy = energy;
        _energyAnchorUtc = energyAnchorUtc;
        _lastAppliedAtUtc = lastAppliedAtUtc;
        _ftueBeat = ftueBeat;
        _ftueCompletedAtUtc = ftueCompletedAtUtc;
        _dailyPeriodStartUtc = dailyPeriodStartUtc;
        _dailyCounters = dailyCounters;
        _dailyCountersView = new ReadOnlyDictionary<string, long>(dailyCounters);
        _weeklyPeriodStartUtc = weeklyPeriodStartUtc;
        _weeklyCounters = weeklyCounters;
        _weeklyCountersView = new ReadOnlyDictionary<string, long>(weeklyCounters);
    }

    /// <summary>The aggregate root's identity (`30` §4).</summary>
    public PlayerId Id { get; }

    /// <summary>
    /// The player's display name, exactly as it was persisted. ⚠️ Never null or blank, and
    /// otherwise never interpreted: the name lifecycle is `16` <b>O34</b> and the profanity filter
    /// is M4-10's (`07` §1).
    /// </summary>
    public string DisplayName { get; }

    /// <summary>The player's Legend Level. `07` §1.1 runs it 1..200.</summary>
    /// <remarks>
    /// Get-only, with no mutator anywhere in M1: the levelling curve, the level-up grants and the
    /// unlock ladder are all M4-10's (`07` §1), and a setter with no rule behind it is where a
    /// level gets awarded without its grants.
    /// </remarks>
    public int LegendLevel { get; }

    /// <summary>Lifetime Legend XP. Never negative. Get-only for the same reason as <see cref="LegendLevel"/>.</summary>
    public long LegendXp { get; }

    /// <summary>
    /// The six player-scoped wallet balances (`10` §1). Read-only, and every currency in
    /// <see cref="WalletCurrencies"/> is present — a missing key is a corrupt row, not a zero.
    /// </summary>
    public IReadOnlyDictionary<CurrencyId, long> Wallet => _wallet;

    /// <summary>The two Energy banks (`10` §3, `28` C) — where the <c>ENERGY</c> currency lives.</summary>
    public EnergyBanks Energy => _energy;

    /// <summary>
    /// The instant Energy regeneration has been accrued up to (recorded assumption <b>A1</b>).
    /// </summary>
    public DateTimeOffset EnergyAnchorUtc => _energyAnchorUtc;

    /// <summary>`30` §2.3's <c>state.LastAppliedAtUtc</c> — where <c>AdvanceTime</c> rolls forward from.</summary>
    public DateTimeOffset LastAppliedAtUtc => _lastAppliedAtUtc;

    /// <summary>`19` D7's <c>beatId</c> — the tutorial beat this player has reached.</summary>
    public FtueBeat FtueBeat => _ftueBeat;

    /// <summary>`19` D7's <c>completedAtUtc</c>. <c>null</c> until beat 10's spend commits.</summary>
    public DateTimeOffset? FtueCompletedAtUtc => _ftueCompletedAtUtc;

    /// <summary>Whether the tutorial is finished, which `19` D7 defines as <c>completedAtUtc</c> being set.</summary>
    public bool IsFtueComplete => _ftueCompletedAtUtc is not null;

    /// <summary>The 05:00 UTC game-day boundary <see cref="DailyCounters"/> were last reset at.</summary>
    public DateTimeOffset DailyPeriodStartUtc => _dailyPeriodStartUtc;

    /// <summary>The daily counters for the current game day. Read-only; empty is the normal state.</summary>
    public IReadOnlyDictionary<string, long> DailyCounters => _dailyCountersView;

    /// <summary>The Monday 05:00 UTC game-week boundary <see cref="WeeklyCounters"/> were last reset at (A2).</summary>
    public DateTimeOffset WeeklyPeriodStartUtc => _weeklyPeriodStartUtc;

    /// <summary>The weekly counters for the current game week. Read-only.</summary>
    public IReadOnlyDictionary<string, long> WeeklyCounters => _weeklyCountersView;

    /// <summary>
    /// The balance of one player-scoped wallet currency.
    /// </summary>
    /// <param name="currency">One of <see cref="WalletCurrencies"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="currency"/> is <c>GOLD</c> (run-scoped), <c>ENERGY</c> (held as
    /// <see cref="Energy"/>) or undefined. Each is refused with the reason, rather than answering
    /// zero — a zero here reads as "the player has none", which for <c>GOLD</c> is a lie about the
    /// wrong aggregate.
    /// </exception>
    public long BalanceOf(CurrencyId currency)
    {
        RequireWalletCurrency(currency, nameof(currency));

        return _wallet[currency];
    }

    /// <summary>The current count of a daily counter, or zero when nothing has registered it.</summary>
    /// <param name="counterKey">The counter's key. Never null, empty or whitespace.</param>
    /// <remarks>
    /// Zero for an unknown key is correct here and is not the "hole coerced to a default" S6
    /// forbids: a counter that has never been incremented in this game day genuinely stands at
    /// zero, and the alternative — every system pre-registering its keys at reset — would need the
    /// closed catalogue of counters that `30` §2.3's five systems do not yet have.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="counterKey"/> is blank.</exception>
    public long DailyCount(string counterKey) => CountIn(_dailyCounters, counterKey);

    /// <inheritdoc cref="DailyCount"/>
    public long WeeklyCount(string counterKey) => CountIn(_weeklyCounters, counterKey);

    /// <summary>
    /// 🔒 `30` §11.3 — the persisted shape of this aggregate, stamped with the <b>current</b>
    /// <see cref="SnapshotSchema.SchemaVersion"/>.
    /// </summary>
    /// <remarks>
    /// The counter dictionaries are <b>copied</b>; the wallet is not. That asymmetry is the
    /// storage decision showing through: <c>_wallet</c> is replaced wholesale on every movement, so
    /// the object handed out here can never change afterwards, while the counter dictionaries are
    /// mutated in place and a shared reference would let a later increment rewrite a snapshot
    /// already handed to a persistence adapter.
    /// </remarks>
    public PlayerSnapshot ToSnapshot() => new(
        SnapshotSchema.SchemaVersion,
        Id,
        DisplayName,
        LegendLevel,
        LegendXp,
        _wallet,
        _energy,
        _energyAnchorUtc,
        _lastAppliedAtUtc,
        _ftueBeat,
        _ftueCompletedAtUtc,
        _dailyPeriodStartUtc,
        Copy(_dailyCounters),
        _weeklyPeriodStartUtc,
        Copy(_weeklyCounters));

    /// <summary>
    /// 🔒 `30` §11.3 — the one validated entry point for a persisted player: <em>"a corrupt row
    /// fails loudly at the seam rather than silently three rules later."</em>
    /// </summary>
    /// <param name="snapshot">The persisted row.</param>
    /// <param name="content">
    /// The version-stamped content snapshot the command is reading (`30` §3). The Legend Level
    /// range is a 📐 tunable, so validating a row needs the data set the row is validated against.
    /// </param>
    /// <returns>
    /// The rehydrated aggregate, or a failure listing <b>every</b> validation the row failed —
    /// not just the first. A corrupt row is usually corrupt in more than one way, and one round
    /// trip per defect is one round trip too many when the row is already in production.
    /// </returns>
    /// <remarks>
    /// <para>
    /// 🔒 <b>An unknown <see cref="PlayerSnapshot.SchemaVersion"/> hard-fails, loudly.</b> The M1
    /// kickoff ruled that no migration code is written before soft launch and that written
    /// migrations become mandatory at M18. Until then a row from another schema version has no
    /// reader, and guessing that "close enough" layouts are compatible is how a field silently
    /// shifts by one position across an entire player base. It is checked <b>first and alone</b>:
    /// every validation below reads fields whose meaning the version defines.
    /// </para>
    /// <para>
    /// ⚠️ <b>A bad <em>content</em> set throws rather than failing.</b> A missing or unauthorised
    /// tunable raises the <c>ContentException</c> family out of this method, and that is the right
    /// channel: a corrupt row is one player's problem and belongs in a <see cref="Result{T}"/>; a
    /// data set that cannot answer "what is the maximum Legend Level" is every player's problem and
    /// belongs at the composition root that loaded it.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> or <paramref name="content"/> is null.</exception>
    /// <exception cref="MissingContentException">The content set is missing a tunable this validation needs.</exception>
    /// <exception cref="UnauthorisedTunableException">A tunable this validation needs holds a deliberate <c>null</c>.</exception>
    /// <exception cref="InvalidTunableException">A tunable this validation needs is authorised but unusable.</exception>
    public static Result<Player> Rehydrate(PlayerSnapshot snapshot, ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(content);

        if (snapshot.SchemaVersion != SnapshotSchema.SchemaVersion)
        {
            return Result<Player>.Failure(
                "PlayerSnapshot.SchemaVersion is " + Text(snapshot.SchemaVersion) + "; this build " +
                "reads " + Text(SnapshotSchema.SchemaVersion) + " and NO MIGRATION EXISTS. 14 §16.6 " +
                "makes a field added, removed or reordered a versioned migration, and the M1 kickoff " +
                "ruled that no migration code is written before soft launch (written migrations " +
                "become mandatory at M18). Reading this row against the current layout would shift " +
                "every field after the first change by one position, silently, for every player who " +
                "has it. Refusing is the loud failure at the seam 30 §11.3 asks for.");
        }

        var legend = LegendTuning.Read(content);
        var faults = new List<string>();

        // ⚠️ There is no energy check here, and that is deliberate twice over.
        //
        // The UPPER bound is enforced on mutation instead — see the type's remarks:
        // EnergyMath.Deposit makes banks above the current maximum a reachable, legitimate state
        // after a balance patch that lowered baseMax, so refusing to LOAD such a row would turn a
        // tuning change into an outage for every player who was full.
        //
        // The LOWER bound needs no check at all: EnergyBanks refuses a negative amount in its own
        // property initialisers, and — unlike default(PlayerId), whose Value is an invalid null —
        // default(EnergyBanks) is (0, 0), a legitimate state and the one a player who just spent
        // their last run is in. A guard here would be a branch no input can reach, which is
        // steering S1's defect rather than defence in depth.
        RequireIdentity(snapshot, faults);
        RequireProfile(snapshot, legend, faults);
        var wallet = ReadWallet(snapshot, faults);
        RequireTimestamps(snapshot, faults);
        RequireFtue(snapshot, faults);
        var daily = ReadCounters(snapshot.DailyCounters, nameof(PlayerSnapshot.DailyCounters), faults);
        var weekly = ReadCounters(snapshot.WeeklyCounters, nameof(PlayerSnapshot.WeeklyCounters), faults);

        if (faults.Count > 0)
        {
            return Result<Player>.Failure(
                "This PlayerSnapshot is not a state the game can be in (" + Text(faults.Count) +
                " problem(s)): " + string.Join(" | ", faults));
        }

        return Result<Player>.Success(new Player(
            snapshot.Id,
            snapshot.DisplayName,
            snapshot.LegendLevel,
            snapshot.LegendXp,
            wallet!,
            snapshot.Energy,
            snapshot.EnergyAnchorUtc,
            snapshot.LastAppliedAtUtc,
            snapshot.FtueBeatId,
            snapshot.FtueCompletedAtUtc,
            snapshot.DailyPeriodStartUtc,
            daily!,
            snapshot.WeeklyPeriodStartUtc,
            weekly!));
    }

    /// <summary>
    /// 🔒 Moves one player-scoped wallet currency and produces the `30` §7 <c>CurrencyChanged</c>
    /// that attributes it.
    /// </summary>
    /// <param name="currency">One of <see cref="WalletCurrencies"/>.</param>
    /// <param name="delta">Signed: positive is income, negative is a spend. Zero is permitted.</param>
    /// <param name="reason">
    /// 🔒 Why it moved — the attribution column of `21` §8.3's <c>income_attribution.csv</c>.
    /// A stable <c>lower_snake_case</c> token. Never blank; <c>CurrencyChanged</c> refuses that.
    /// </param>
    /// <returns>The event, with <see cref="DomainEvent.Sequence"/> left at <see cref="UnstampedSequence"/>.</returns>
    /// <remarks>
    /// <c>internal</c>, so the only public route to it is <c>GameRules.Apply</c> (`30` §11.2).
    /// Throws rather than returning a <see cref="Result{T}"/> on an unaffordable spend: refusing a
    /// player's request is a <c>RejectionReason</c> the handler produces <em>before</em> it gets
    /// here, so a negative balance reaching this point is a rule that forgot to check, not a
    /// player who cannot pay.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="currency"/> is not player-scoped, or the movement would overflow.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is blank.</exception>
    /// <exception cref="InvalidOperationException">The movement would take the balance negative.</exception>
    internal CurrencyChanged MoveCurrency(CurrencyId currency, long delta, string reason)
    {
        RequireWalletCurrency(currency, nameof(currency));

        var balance = _wallet[currency];

        // Checked, because `long.MaxValue + 1` wraps to a large negative in the default unchecked
        // context — a grant that silently bankrupts the player, straight past the guard below that
        // exists to stop exactly that.
        long next;
        try
        {
            next = checked(balance + delta);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(delta),
                delta,
                "Moving " + Text(currency) + " by " + Text(delta) + " from a balance of " +
                Text(balance) + " overflows a 64-bit balance. A movement this size is an economy " +
                "defect upstream — a multiplier chain, most likely — not an amount to store.");
        }

        if (next < 0)
        {
            throw new InvalidOperationException(
                "Moving " + Text(currency) + " by " + Text(delta) + " would take the balance from " +
                Text(balance) + " to " + Text(next) + ". 30 §11.5 makes 'a currency never goes " +
                "negative' an invariant of the Player aggregate. A spend the player cannot afford " +
                "is refused by the handler as a RejectionReason before it reaches the aggregate; " +
                "reaching here means a rule debited without checking.");
        }

        return MoveBalance(currency, delta, _energy, reason);
    }

    /// <summary>
    /// 🔒 Writes the two Energy banks a rule computed, and produces the <c>CurrencyChanged</c> that
    /// attributes the movement (<c>ENERGY</c> is one of `10` §1's currencies).
    /// </summary>
    /// <param name="banks">
    /// The banks after the rule — <c>EnergyMath.Accrue(...).Banks</c>, <c>EnergyMath.Grant(...)</c>
    /// or <c>EnergyMath.Spend(...).Banks</c>. The aggregate does not compute them (`30` §11.5); it
    /// refuses them if they break the invariant.
    /// </param>
    /// <param name="tuning">The energy numbers, so the caps can be derived for this Legend Level.</param>
    /// <param name="reason">Why Energy moved. A stable <c>lower_snake_case</c> token.</param>
    /// <returns>
    /// A <c>CurrencyChanged</c> for <see cref="CurrencyId.ENERGY"/> whose <c>Delta</c> is the change
    /// in the two banks <b>together</b>. Overflow into the Reserve is a movement within one
    /// currency, not two movements, so `21` §8.3 sees one row.
    /// </returns>
    /// <remarks>
    /// 🔒 <b>The ceiling is <c>max(cap, where the bank already is)</c>, not <c>cap</c>.</b> See the
    /// type's remarks: a balance patch that lowers <c>baseMax</c> leaves real players above the new
    /// cap, and <c>EnergyMath.Deposit</c> is written to leave them there and let them drain. A flat
    /// <c>&lt;= cap</c> assertion here would make the first accrual after such a patch throw for
    /// every one of them, on a value the rule deliberately did not change.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="tuning"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is blank.</exception>
    /// <exception cref="InvalidOperationException">The banks would exceed their ceiling.</exception>
    internal CurrencyChanged SetEnergy(EnergyBanks banks, EnergyTuning tuning, string reason)
    {
        ArgumentNullException.ThrowIfNull(tuning);

        RequireWithinCeiling(banks.Energy, _energy.Energy, MaxEnergyFor(tuning), "the main Energy bar", "10 §3");
        RequireWithinCeiling(banks.Reserve, _energy.Reserve, ReserveCapacityFor(tuning), "the Energy Reserve", "28 C2");

        var delta = ((long)banks.Energy + banks.Reserve) - ((long)_energy.Energy + _energy.Reserve);

        return MoveBalance(CurrencyId.ENERGY, delta, banks, reason);
    }

    /// <summary>
    /// Advances the regeneration anchor by the span a rule actually accrued — recorded assumption
    /// <b>A1</b>: <c>wholeUnits × regenInterval</c>, never to the instant asked about.
    /// </summary>
    /// <param name="accrued">
    /// <c>EnergyMath.Accrue(...).AnchorAdvance</c>. Never negative; zero is the normal answer when
    /// less than one interval has passed, and it must leave the sub-unit remainder in place.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="accrued"/> is negative.</exception>
    internal void AdvanceEnergyAnchor(TimeSpan accrued)
    {
        if (accrued < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(accrued),
                accrued,
                "The regeneration anchor only moves forwards. A negative advance would hand the " +
                "player the same span of regeneration twice on the next command.");
        }

        _energyAnchorUtc += accrued;
    }

    /// <summary>`30` §2.3 — records that a command has been applied at <paramref name="nowUtc"/>.</summary>
    /// <param name="nowUtc">
    /// <c>GameContext.NowUtc</c>. Must carry a zero offset and must not precede
    /// <see cref="LastAppliedAtUtc"/>.
    /// </param>
    /// <remarks>
    /// ⚠️ Equal is allowed, strictly-earlier is not. Two commands can legitimately share an instant
    /// — the server stamps <c>NowUtc</c> once per command and a client can send two inside the same
    /// millisecond — whereas an earlier instant means a clock moved backwards, and M1-08's
    /// <c>AdvanceTime</c> is specified to clamp that rather than pass it on.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="nowUtc"/> is offset or goes backwards.</exception>
    internal void MarkApplied(DateTimeOffset nowUtc)
    {
        RequireZeroOffset(nowUtc, nameof(nowUtc));

        if (nowUtc < _lastAppliedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nowUtc),
                nowUtc,
                "The last command was applied at " + Text(_lastAppliedAtUtc) + ", which is after " +
                Text(nowUtc) + ". 30 §2.3 rolls state forward FROM this instant, so moving it " +
                "backwards would replay every reset boundary in between.");
        }

        _lastAppliedAtUtc = nowUtc;
    }

    /// <summary>
    /// 🔒 `30` §2.3 — clears the daily counters and records the 05:00 UTC boundary they were
    /// cleared at. The daily half of the mechanism M1-08's <c>AdvanceTime</c> drives.
    /// </summary>
    /// <param name="periodStartUtc">
    /// The game-day boundary now in force: 05:00:00.000 UTC exactly, offset zero, and never before
    /// the boundary already recorded.
    /// </param>
    /// <remarks>
    /// The aggregate does not work out <em>which</em> boundary that is — computing it from
    /// <c>NowUtc</c> is arithmetic, and `30` §11.5 keeps arithmetic out of <c>Model</c>. It holds
    /// the invariant that whatever it is handed is a boundary the game actually has.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Not a 05:00 UTC boundary, or it goes backwards.</exception>
    internal void ResetDailyCounters(DateTimeOffset periodStartUtc)
    {
        RequireGameDayBoundary(periodStartUtc, nameof(periodStartUtc));
        RequireNotBefore(periodStartUtc, _dailyPeriodStartUtc, nameof(periodStartUtc), "daily");

        _dailyCounters.Clear();
        _dailyPeriodStartUtc = periodStartUtc;
    }

    /// <summary>
    /// 🔒 The weekly half. Milestone assumption <b>A2</b> (derived from `27` §4): the game week
    /// starts <b>Monday 05:00 UTC</b>.
    /// </summary>
    /// <param name="periodStartUtc">A Monday at 05:00:00.000 UTC, offset zero, never going backwards.</param>
    /// <exception cref="ArgumentOutOfRangeException">Not a Monday 05:00 UTC boundary, or it goes backwards.</exception>
    internal void ResetWeeklyCounters(DateTimeOffset periodStartUtc)
    {
        RequireGameDayBoundary(periodStartUtc, nameof(periodStartUtc));

        if (periodStartUtc.DayOfWeek != DayOfWeek.Monday)
        {
            throw new ArgumentOutOfRangeException(
                nameof(periodStartUtc),
                periodStartUtc,
                Text(periodStartUtc) + " is a " + periodStartUtc.DayOfWeek + ". The game week starts " +
                "MONDAY at 05:00 UTC (milestone assumption A2, derived from 27 §4), so a weekly " +
                "boundary on any other day would settle guild weeks and weekly counters against a " +
                "week the rest of the game does not have.");
        }

        RequireNotBefore(periodStartUtc, _weeklyPeriodStartUtc, nameof(periodStartUtc), "weekly");

        _weeklyCounters.Clear();
        _weeklyPeriodStartUtc = periodStartUtc;
    }

    /// <summary>
    /// Registers and advances a daily counter — the increment half of the mechanism. The counter
    /// comes into existence on its first increment; nothing has to declare it in advance.
    /// </summary>
    /// <param name="counterKey">
    /// A stable <c>lower_snake_case</c> key owned by the system that counts. ⚠️ Deliberately not a
    /// closed enum: `30` §2.3's five daily-reset systems (quest expiry, the wheel's free spin, ad
    /// caps, dungeon entries, daily-shop stock) do not exist yet, and freezing their vocabulary
    /// here would invent it (S6).
    /// </param>
    /// <param name="amount">How much to add. Never negative — a counter counts, it does not settle.</param>
    /// <exception cref="ArgumentException"><paramref name="counterKey"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> is negative, or the count overflows.</exception>
    internal void CountDaily(string counterKey, long amount) =>
        Count(_dailyCounters, counterKey, amount, "daily");

    /// <inheritdoc cref="CountDaily"/>
    internal void CountWeekly(string counterKey, long amount) =>
        Count(_weeklyCounters, counterKey, amount, "weekly");

    /// <summary>
    /// `19` D7 — advances the tutorial to the next beat, <em>"server-side as each beat's
    /// interaction completes"</em>.
    /// </summary>
    /// <param name="beat">The beat now reached. Strictly after the current one.</param>
    /// <remarks>
    /// ⚠️ Strictly forwards, and refused outright once the tutorial is complete. `19` D7's resume
    /// table only ever re-presents the <em>current</em> beat, and `19` D6 says the skip is
    /// <em>"never re-offered after completion"</em> — so a beat moving backwards, or moving at all
    /// after <c>completedAtUtc</c>, is a replayed or duplicated command, not a player.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="beat"/> is undefined or not later.</exception>
    /// <exception cref="InvalidOperationException">The tutorial is already complete.</exception>
    internal void AdvanceFtue(FtueBeat beat)
    {
        if (!Enum.IsDefined(beat))
        {
            throw new ArgumentOutOfRangeException(
                nameof(beat),
                beat,
                "19 D7 runs beatId over B0..B10 plus B6b and nothing else.");
        }

        if (IsFtueComplete)
        {
            throw new InvalidOperationException(
                "The tutorial completed at " + Text(_ftueCompletedAtUtc!.Value) + "; it cannot " +
                "advance to " + beat + ". 19 D6: no FTUE surface ever appears again once it is " +
                "complete.");
        }

        if (beat <= _ftueBeat)
        {
            throw new ArgumentOutOfRangeException(
                nameof(beat),
                beat,
                "The tutorial is at " + _ftueBeat + "; " + beat + " is not later. 19 D7 advances a " +
                "beat as its interaction completes and its resume table re-presents the current " +
                "beat, so a beat that does not move forwards is a replayed command.");
        }

        _ftueBeat = beat;
    }

    /// <summary>
    /// `19` D7 — completes the tutorial. <em>"The FTUE is complete when beat 10's spend commits;
    /// <c>completedAtUtc</c> is set and no FTUE surface ever appears again."</em>
    /// </summary>
    /// <param name="atUtc">When it completed. Offset zero.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="atUtc"/> carries a non-zero offset.</exception>
    /// <exception cref="InvalidOperationException">The tutorial is not at beat 10, or is already complete.</exception>
    internal void CompleteFtue(DateTimeOffset atUtc)
    {
        RequireZeroOffset(atUtc, nameof(atUtc));

        if (IsFtueComplete)
        {
            throw new InvalidOperationException(
                "The tutorial already completed at " + Text(_ftueCompletedAtUtc!.Value) + ". 19 D5's " +
                "payout is granted once, keyed on the run; completing twice would grant it twice.");
        }

        if (_ftueBeat != FtueBeat.B10)
        {
            throw new InvalidOperationException(
                "The tutorial is at " + _ftueBeat + ", not " + FtueBeat.B10 + ". 19 D7 completes it " +
                "when beat 10's spend commits — completing earlier would skip the forced Talent " +
                "Point spend that beat 10 exists for.");
        }

        _ftueCompletedAtUtc = atUtc;
    }

    /// <summary>
    /// 🔒 The <b>one</b> place a balance changes, and therefore the one place that has to attribute
    /// the change.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both currency stores are written here — <c>_wallet</c> for the six wallet rows,
    /// <c>_energy</c> for <c>ENERGY</c> — so
    /// <c>DomainPurityTests.Every_currency_mutation_emits_CurrencyChanged</c> covers both. That
    /// rule finds this method by its <c>stfld</c> to <c>_wallet</c> and then requires a
    /// <c>newobj</c> on <c>CurrencyChanged</c> in the same method body; deleting the construction
    /// below turns the build red, which is the demonstration this design exists for.
    /// </para>
    /// <para>
    /// Validation is the callers' — <see cref="MoveCurrency"/> and <see cref="SetEnergy"/> — so
    /// that this method has exactly one job and cannot grow a branch that writes without emitting.
    /// </para>
    /// </remarks>
    private CurrencyChanged MoveBalance(CurrencyId currency, long delta, EnergyBanks banks, string reason)
    {
        if (currency == CurrencyId.ENERGY)
        {
            _energy = banks;
        }
        else
        {
            var next = new Dictionary<CurrencyId, long>(_wallet) { [currency] = _wallet[currency] + delta };
            _wallet = new ReadOnlyDictionary<CurrencyId, long>(next);
        }

        return new CurrencyChanged(UnstampedSequence, currency, delta, reason);
    }

    /// <summary>
    /// Max Energy at this player's Legend Level — `10` §3's <c>baseMax + perLegendLevel ×
    /// (level − 1)</c>, capped.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>A second site for the same formula, and it is forced rather than chosen.</b>
    /// <c>EnergyMath.MaxEnergy</c> owns it, and `30` §11.4 forbids <c>Model</c> from referencing
    /// <c>Rules</c> — so an aggregate holding `30` §11.5's <em>"Energy never exceeds max +
    /// reserve"</em> cannot call the rule that knows the maximum. Both sides read the same authored
    /// numbers out of <see cref="EnergyTuning"/>, and
    /// <c>PlayerEnergyTests.The_aggregates_ceiling_is_the_same_number_EnergyMath_computes</c> pins
    /// the two against each other across the whole authored Legend Level range, so a change to one
    /// cannot silently diverge from the other.
    /// </remarks>
    private int MaxEnergyFor(EnergyTuning tuning)
    {
        var grown = tuning.BaseMax + ((long)tuning.PerLegendLevel * (LegendLevel - 1));

        return (int)Math.Min(grown, tuning.MaxCap);
    }

    /// <summary>`28` C2 — the Reserve's capacity: <c>reserveMultipleOfMax ×</c> Max Energy.</summary>
    /// <remarks>Same forced duplication as <see cref="MaxEnergyFor"/>, pinned by the same test.</remarks>
    private int ReserveCapacityFor(EnergyTuning tuning) =>
        (int)Math.Min((long)MaxEnergyFor(tuning) * tuning.ReserveMultipleOfMax, int.MaxValue);

    private void RequireWithinCeiling(int next, int current, int cap, string bank, string citation)
    {
        var ceiling = Math.Max(cap, current);
        if (next <= ceiling)
        {
            return;
        }

        throw new InvalidOperationException(
            Text(next) + " exceeds " + Text(ceiling) + ", the ceiling for " + bank + " at Legend " +
            "Level " + Text(LegendLevel) + " (" + citation + ", cap " + Text(cap) + ", currently " +
            Text(current) + "). 30 §11.5 makes 'Energy never exceeds max + reserve' an invariant of " +
            "the Player aggregate. The ceiling is the higher of the cap and where the bank already " +
            "stands, so a player left above the cap by a balance patch can still be written back " +
            "unchanged and drain by playing — but nothing may push a bank further past it.");
    }

    private static void RequireWalletCurrency(CurrencyId currency, string parameterName)
    {
        if (WalletCurrencies.Contains(currency))
        {
            return;
        }

        var because = currency switch
        {
            CurrencyId.GOLD =>
                "GOLD is RUN-scoped (10 §1, tuning/currencies.json, milestone assumption A3): it is " +
                "spent or lost when the run ends and lives on the Run aggregate, not here.",
            CurrencyId.ENERGY =>
                "ENERGY is player-scoped but has two banks (28 C), so it is held as Player.Energy " +
                "and moved through Player.SetEnergy — a single wallet row could not describe the " +
                "main bar and the Reserve.",
            _ =>
                "10 §1 fixes eight currencies and this is not one of them; an undefined CurrencyId " +
                "is an uninitialised field, not a balance.",
        };

        throw new ArgumentOutOfRangeException(parameterName, currency, because);
    }

    private static void Count(Dictionary<string, long> counters, string counterKey, long amount, string period)
    {
        RequireCounterKey(counterKey, nameof(counterKey));

        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "A " + period + " counter counts upwards; '" + counterKey + "' cannot be advanced " +
                "by " + Text(amount) + ". 30 §2.3's counters are cleared at their period boundary, " +
                "never settled back down — a negative advance would be a system undoing a use it " +
                "had already recorded, which is a refund and belongs in the rule that granted it.");
        }

        counters.TryGetValue(counterKey, out var current);

        try
        {
            counters[counterKey] = checked(current + amount);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "Advancing the " + period + " counter '" + counterKey + "' by " + Text(amount) +
                " from " + Text(current) + " overflows a 64-bit count. A count that large inside " +
                "one period is a loop that did not terminate.");
        }
    }

    private static long CountIn(IReadOnlyDictionary<string, long> counters, string counterKey)
    {
        RequireCounterKey(counterKey, nameof(counterKey));

        return counters.TryGetValue(counterKey, out var count) ? count : 0L;
    }

    private static void RequireCounterKey(string counterKey, string parameterName)
    {
        if (!string.IsNullOrWhiteSpace(counterKey))
        {
            return;
        }

        throw new ArgumentException(
            "A counter key names the system that owns the counter, so it is never blank. The keys " +
            "are open rather than a closed enum because 30 §2.3's five daily-reset systems do not " +
            "exist yet — but 'open' means the owner picks the token, not that there is no token.",
            parameterName);
    }

    private static void RequireGameDayBoundary(DateTimeOffset boundary, string parameterName)
    {
        RequireZeroOffset(boundary, parameterName);

        if (boundary.TimeOfDay == GameDayStart)
        {
            return;
        }

        throw new ArgumentOutOfRangeException(
            parameterName,
            boundary,
            Text(boundary) + " is not a game-day boundary. 30 §2.3 resets quest expiry, the wheel's " +
            "free spin, ad caps and dungeon entries at 05:00 UTC 'whether or not anyone logs in', " +
            "so a period that started at any other time of day is a period the rest of the game " +
            "does not agree exists.");
    }

    private static void RequireNotBefore(
        DateTimeOffset boundary, DateTimeOffset current, string parameterName, string period)
    {
        if (boundary >= current)
        {
            return;
        }

        throw new ArgumentOutOfRangeException(
            parameterName,
            boundary,
            "The " + period + " counters were last reset at " + Text(current) + ", which is after " +
            Text(boundary) + ". Resetting to an earlier boundary would clear a period that has " +
            "already been counted against, handing back every cap the player has already spent.");
    }

    private static void RequireZeroOffset(DateTimeOffset instant, string parameterName)
    {
        if (instant.Offset == TimeSpan.Zero)
        {
            return;
        }

        throw new ArgumentOutOfRangeException(
            parameterName,
            instant,
            Text(instant) + " carries a " + Text(instant.Offset) + " offset. Every instant in this " +
            "aggregate is UTC: CanonicalStateWriter encodes a DateTimeOffset as Unix milliseconds, " +
            "so two offsets naming the same instant hash IDENTICALLY while record equality calls " +
            "them different — the same class of defect as the -0.0 the writer already refuses. " +
            "Convert at the edge; the domain stores UTC.");
    }

    private static void RequireIdentity(PlayerSnapshot snapshot, List<string> faults)
    {
        // default(PlayerId) runs no constructor, so its Value is null rather than validated —
        // PlayerId's own remarks name Rehydrate as the seam that has to catch it.
        if (string.IsNullOrWhiteSpace(snapshot.Id.Value))
        {
            faults.Add(
                nameof(PlayerSnapshot.Id) + " is blank or default(PlayerId), so this row names no " +
                "player. PlayerId validates in its constructor, which default(PlayerId) never runs.");
        }

        if (string.IsNullOrWhiteSpace(snapshot.DisplayName))
        {
            faults.Add(
                nameof(PlayerSnapshot.DisplayName) + " is blank. 16 O34 leaves the name lifecycle " +
                "(uniqueness, rename, sanction) open and M4-10 owns the profanity filter, so this " +
                "is the only thing checked about it — and a blank name is a row that renders as " +
                "nothing on every screen that shows one.");
        }
    }

    private static void RequireProfile(PlayerSnapshot snapshot, LegendTuning legend, List<string> faults)
    {
        if (snapshot.LegendLevel < legend.Minimum || snapshot.LegendLevel > legend.Maximum)
        {
            faults.Add(
                nameof(PlayerSnapshot.LegendLevel) + " is " + Text(snapshot.LegendLevel) +
                ", outside the " + Text(legend.Minimum) + ".." + Text(legend.Maximum) + " that " +
                "07 §1.1 authors at " + LegendTuning.MinimumReference + " / " +
                LegendTuning.MaximumReference + ". Max Energy is derived from it, and " +
                "EnergyMath.MaxEnergy counts levels GAINED — a level below the minimum subtracts " +
                "from the base tank.");
        }

        if (snapshot.LegendXp < 0)
        {
            faults.Add(
                nameof(PlayerSnapshot.LegendXp) + " is " + Text(snapshot.LegendXp) + ". Legend XP " +
                "is lifetime banked income (02 §5.1a) and is never spent, so it cannot be negative.");
        }
    }

    private static IReadOnlyDictionary<CurrencyId, long>? ReadWallet(PlayerSnapshot snapshot, List<string> faults)
    {
        if (snapshot.Wallet is null)
        {
            faults.Add(nameof(PlayerSnapshot.Wallet) + " is null. An absent wallet is not an empty one.");
            return null;
        }

        var wallet = new Dictionary<CurrencyId, long>(WalletCurrencies.Count);
        var faulted = false;

        foreach (var currency in WalletCurrencies)
        {
            if (!snapshot.Wallet.TryGetValue(currency, out var balance))
            {
                faults.Add(
                    nameof(PlayerSnapshot.Wallet) + " has no row for " + Text(currency) + ". All " +
                    "six player-scoped currencies are always present: a missing row read as zero " +
                    "would be indistinguishable from a balance a migration dropped.");
                faulted = true;
                continue;
            }

            if (balance < 0)
            {
                faults.Add(
                    nameof(PlayerSnapshot.Wallet) + "[" + Text(currency) + "] is " + Text(balance) +
                    ". 30 §11.5 makes 'a currency never goes negative' an invariant of this " +
                    "aggregate.");
                faulted = true;
                continue;
            }

            wallet[currency] = balance;
        }

        foreach (var currency in snapshot.Wallet.Keys)
        {
            if (WalletCurrencies.Contains(currency))
            {
                continue;
            }

            faults.Add(
                nameof(PlayerSnapshot.Wallet) + " carries a row for " + Text(currency) + ", which " +
                "is not player-scoped. GOLD belongs to the Run aggregate (assumption A3) and " +
                "ENERGY is held as PlayerSnapshot.Energy; a second copy of either is a second " +
                "source of truth.");
            faulted = true;
        }

        return faulted ? null : new ReadOnlyDictionary<CurrencyId, long>(wallet);
    }

    private static void RequireTimestamps(PlayerSnapshot snapshot, List<string> faults)
    {
        RequireUtc(snapshot.EnergyAnchorUtc, nameof(PlayerSnapshot.EnergyAnchorUtc), faults);
        RequireUtc(snapshot.LastAppliedAtUtc, nameof(PlayerSnapshot.LastAppliedAtUtc), faults);
        RequireUtc(snapshot.DailyPeriodStartUtc, nameof(PlayerSnapshot.DailyPeriodStartUtc), faults);
        RequireUtc(snapshot.WeeklyPeriodStartUtc, nameof(PlayerSnapshot.WeeklyPeriodStartUtc), faults);

        if (snapshot.FtueCompletedAtUtc is { } completed)
        {
            RequireUtc(completed, nameof(PlayerSnapshot.FtueCompletedAtUtc), faults);
        }

        // The boundary checks run only on an instant already known to be UTC: "05:00 on a clock two
        // hours ahead" is not a game-day boundary, and reporting it as one as well as as an offset
        // would be two faults for one defect.
        if (snapshot.DailyPeriodStartUtc.Offset == TimeSpan.Zero &&
            snapshot.DailyPeriodStartUtc.TimeOfDay != GameDayStart)
        {
            faults.Add(
                nameof(PlayerSnapshot.DailyPeriodStartUtc) + " is " +
                Text(snapshot.DailyPeriodStartUtc) + ", which is not a 05:00 UTC game-day boundary " +
                "(30 §2.3).");
        }

        if (snapshot.WeeklyPeriodStartUtc.Offset != TimeSpan.Zero)
        {
            return;
        }

        if (snapshot.WeeklyPeriodStartUtc.TimeOfDay != GameDayStart ||
            snapshot.WeeklyPeriodStartUtc.DayOfWeek != DayOfWeek.Monday)
        {
            faults.Add(
                nameof(PlayerSnapshot.WeeklyPeriodStartUtc) + " is " +
                Text(snapshot.WeeklyPeriodStartUtc) + ", a " + snapshot.WeeklyPeriodStartUtc.DayOfWeek +
                ". The game week starts MONDAY 05:00 UTC (milestone assumption A2, derived from " +
                "27 §4).");
        }
    }

    private static void RequireUtc(DateTimeOffset instant, string field, List<string> faults)
    {
        if (instant.Offset == TimeSpan.Zero)
        {
            return;
        }

        faults.Add(
            field + " is " + Text(instant) + ", carrying a " + Text(instant.Offset) + " offset. " +
            "Every persisted instant is UTC: CanonicalStateWriter encodes a DateTimeOffset as Unix " +
            "milliseconds, so two offsets naming one instant share a stateHash while record " +
            "equality calls the two snapshots different.");
    }

    private static void RequireFtue(PlayerSnapshot snapshot, List<string> faults)
    {
        if (!Enum.IsDefined(snapshot.FtueBeatId))
        {
            faults.Add(
                nameof(PlayerSnapshot.FtueBeatId) + " is " + Text((int)snapshot.FtueBeatId) +
                ", which is not one of 19 D7's beats (B0..B10 plus B6b). FtueBeat has no zero " +
                "member on purpose, so this is what an uninitialised column reads as.");
        }

        if (snapshot.FtueCompletedAtUtc is not null && snapshot.FtueBeatId != FtueBeat.B10)
        {
            faults.Add(
                nameof(PlayerSnapshot.FtueCompletedAtUtc) + " is set while " +
                nameof(PlayerSnapshot.FtueBeatId) + " is " + snapshot.FtueBeatId + ". 19 D7 " +
                "completes the tutorial when beat 10's spend commits, so a completed tutorial is " +
                "always at B10 — this row claims a payout was banked at a beat that never reached it.");
        }
    }

    private static Dictionary<string, long>? ReadCounters(
        IReadOnlyDictionary<string, long> counters, string field, List<string> faults)
    {
        if (counters is null)
        {
            faults.Add(field + " is null. An absent counter map is not an empty one.");
            return null;
        }

        // Copied into an ORDINAL dictionary rather than kept: the caller may hold a mutable
        // reference to the map it handed in, and CanonicalStateWriter orders string keys ordinally,
        // so a map that compared its keys any other way would round-trip to a different hash than
        // the one it was stored under.
        var copy = new Dictionary<string, long>(counters.Count, StringComparer.Ordinal);
        var faulted = false;

        foreach (var (key, count) in counters)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                faults.Add(field + " carries a blank counter key. A counter key names its owner.");
                faulted = true;
                continue;
            }

            if (count < 0)
            {
                faults.Add(
                    field + "['" + key + "'] is " + Text(count) + ". A counter counts upwards from " +
                    "zero and is cleared at its period boundary; it is never settled back down.");
                faulted = true;
                continue;
            }

            copy[key] = count;
        }

        return faulted ? null : copy;
    }

    /// <summary>An ordinal copy of a counter map, so no caller shares the aggregate's dictionary.</summary>
    private static ReadOnlyDictionary<string, long> Copy(Dictionary<string, long> counters) =>
        new(new Dictionary<string, long>(counters, StringComparer.Ordinal));

    /// <summary>
    /// 🔒 Renders a value with <see cref="CultureInfo.InvariantCulture"/>.
    /// </summary>
    /// <remarks>
    /// Same reason <see cref="EnergyTuning"/> has one: `14` §8.2 wants <c>Core</c> reading
    /// identically everywhere, and a bare interpolation renders <c>12.08.2026 05:00:00 +00:00</c>
    /// on a German laptop and <c>08/12/2026 05:00:00 +00:00</c> in the container — two diagnostics
    /// for one corrupt row, and a message a reader cannot grep. Enums and strings are interpolated
    /// directly; their rendering does not consult a culture.
    /// </remarks>
    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc cref="Text(int)"/>
    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc cref="Text(int)"/>
    private static string Text(CurrencyId value) => value.ToString();

    /// <inheritdoc cref="Text(int)"/>
    private static string Text(TimeSpan value) => value.ToString("c", CultureInfo.InvariantCulture);

    /// <inheritdoc cref="Text(int)"/>
    private static string Text(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
}
