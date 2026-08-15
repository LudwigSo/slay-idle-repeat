using System.Collections.ObjectModel;
using System.Globalization;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Model;

/// <summary>
/// The <c>Player</c> aggregate root — profile, the seven player-scoped currencies, the two Energy
/// banks, FTUE progress and the daily/weekly counter mechanism.
/// </summary>
/// <remarks>
/// <para>
/// Lives in <c>Core/Model/Player/</c> but in namespace <c>SlayIdleRepeat.Core.Model</c>, not
/// <c>...Model.Player</c>: a child namespace named <c>Player</c> would shadow the type
/// <c>Player</c>, making it unnameable from anywhere inside <c>Model</c> (<c>error CS0118</c>). The
/// directory is the file layout; the namespace is the layer.
/// </para>
/// <para>
/// Public getters, private constructor, <c>internal</c> mutators: the only public way to change state
/// is <c>GameRules.Apply</c>. <see cref="ToSnapshot"/> and <see cref="Rehydrate"/> are the exception,
/// the validating factory pair the persistence adapter needs to rebuild a player from a row.
/// </para>
/// <para>
/// It holds state and invariants; it does not compute. A handler computes and hands the answer to
/// <see cref="SetEnergy"/>; the aggregate's job is to refuse an answer that would break an invariant
/// — a currency never goes negative, and Energy never exceeds max + reserve.
/// </para>
/// <para>
/// The energy invariant is enforced on mutation, not on rehydration, and that is forced: a balance
/// patch that lowers the Energy cap leaves real players above the new one, and refusing to load such
/// a row would turn a tuning change into an account outage. So the rule is: a mutation may never push
/// a bank past its cap, and may never make an over-cap bank worse. A player already above the cap
/// stays loadable and drains by playing.
/// </para>
/// <para>
/// <see cref="MoveBalance"/> is the only method outside the constructor that writes <c>_wallet</c>,
/// and it is also the only one that writes <c>_energy</c> — an IL-scanning test requires any method
/// writing a currency-carrying field to construct a <c>CurrencyChanged</c>, and routing Energy through
/// the same method puts it under that guard too.
/// </para>
/// <para>
/// Deliberately absent: inventory, gear instances, the unopened-container shelf, pity counters and
/// lifetime feat counters — each deferred with a <c>GapRegister</c> entry keyed on a type that must
/// not yet exist, so the build fails the day one becomes writable without a home here. Entitlement
/// lives on the session instead, reached as <c>GameContext.Entitlements</c>. There is no factory for a
/// new player either: starting values are a later milestone's decision, and <see cref="Rehydrate"/> is
/// the only way to obtain one.
/// </para>
/// </remarks>
public sealed class Player
{
    /// <summary>The six player-scoped wallet currencies, in <see cref="CurrencyId"/> order.</summary>
    /// <remarks>
    /// <c>GOLD</c> is run-scoped and belongs to <c>Run</c>; <c>ENERGY</c> is player-scoped but held
    /// as <see cref="EnergyBanks"/> since it has two banks. Written out explicitly rather than
    /// filtered from every <c>CurrencyId</c>, so a new currency has to be adopted here deliberately.
    /// Wrapped in <see cref="Array.AsReadOnly{T}"/> rather than exposed as a bare array, so a caller
    /// cannot cast it back to <c>CurrencyId[]</c> and rewrite what a wallet is process-wide.
    /// </remarks>
    public static IReadOnlyList<CurrencyId> WalletCurrencies { get; } = Array.AsReadOnly(new[]
    {
        CurrencyId.CROWNS,
        CurrencyId.SOUL_SHARDS,
        CurrencyId.ENHANCE_STONES,
        CurrencyId.MERGE_DUST,
        CurrencyId.BEAST_FEED,
        CurrencyId.HONOR,
    });

    /// <summary>The wallet. Replaced wholesale on every movement rather than mutated in place.</summary>
    /// <remarks>
    /// Lets <see cref="Wallet"/> hand out the live object with no per-read allocation and no way to
    /// reach a mutable dictionary underneath. A wallet mutated in place would emit no field write for
    /// the IL scan that requires every currency mutation to construct a <c>CurrencyChanged</c> to see.
    /// </remarks>
    private IReadOnlyDictionary<CurrencyId, long> _wallet;

    private long _runsStarted;
    private EnergyBanks _energy;
    private DateTimeOffset _energyAnchorUtc;
    private DateTimeOffset _lastAppliedAtUtc;
    private FtueBeat _ftueBeat;
    private DateTimeOffset? _ftueCompletedAtUtc;
    private DateTimeOffset _dailyPeriodStartUtc;
    private DateTimeOffset _weeklyPeriodStartUtc;
    private int _loginCalendarDay;
    private bool _loginCalendarDayClaimed;

    /// <summary>The daily counters, and the read-only view handed out by <see cref="DailyCounters"/>.</summary>
    /// <remarks>Mutated in place, unlike <c>_wallet</c>, so the view stays valid across every increment and reset.</remarks>
    private readonly Dictionary<string, long> _dailyCounters;
    private readonly ReadOnlyDictionary<string, long> _dailyCountersView;
    private readonly Dictionary<string, long> _weeklyCounters;
    private readonly ReadOnlyDictionary<string, long> _weeklyCountersView;

    /// <summary>
    /// The (Chapter, Tier) pairs this player has cleared at least once, keyed
    /// <c>"{chapterId}:{tier}"</c>. The value is always 1; only the key's presence is read.
    /// </summary>
    private readonly Dictionary<string, long> _clearedChapterTiers;

    /// <inheritdoc cref="_clearedChapterTiers"/>
    private readonly ReadOnlyDictionary<string, long> _clearedChapterTiersView;

    // ---------------------------------------------------------------- feat counters (M4-13)

    /// <summary>The lifetime feat counters, and the view <see cref="FeatCounters"/> hands out.</summary>
    /// <remarks>
    /// Mutated in place like the daily and weekly counters, so the view stays valid across every
    /// increment. Unlike them it is never cleared: nothing on this aggregate resets it.
    /// </remarks>
    private readonly Dictionary<string, long> _featCounters;
    private readonly FeatCounters _featCountersView;

    private long _legendXp;

    /// <summary>The one constructor. Private; every value has already been checked by <see cref="Rehydrate"/>, the only caller.</summary>
    private Player(
        PlayerId id,
        string displayName,
        int legendLevel,
        long legendXp,
        long runsStarted,
        IReadOnlyDictionary<CurrencyId, long> wallet,
        EnergyBanks energy,
        DateTimeOffset energyAnchorUtc,
        DateTimeOffset lastAppliedAtUtc,
        FtueBeat ftueBeat,
        DateTimeOffset? ftueCompletedAtUtc,
        DateTimeOffset dailyPeriodStartUtc,
        Dictionary<string, long> dailyCounters,
        DateTimeOffset weeklyPeriodStartUtc,
        Dictionary<string, long> weeklyCounters,
        int loginCalendarDay,
        bool loginCalendarDayClaimed,
        Dictionary<string, long> clearedChapterTiers,
        Dictionary<string, long> featCounters)
    {
        Id = id;
        DisplayName = displayName;
        LegendLevel = legendLevel;
        _legendXp = legendXp;
        _runsStarted = runsStarted;
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
        _loginCalendarDay = loginCalendarDay;
        _loginCalendarDayClaimed = loginCalendarDayClaimed;
        _clearedChapterTiers = clearedChapterTiers;
        _clearedChapterTiersView = new ReadOnlyDictionary<string, long>(clearedChapterTiers);
        _featCounters = featCounters;
        _featCountersView = new FeatCounters(new ReadOnlyDictionary<string, long>(featCounters));
    }

    /// <summary>The aggregate root's identity.</summary>
    public PlayerId Id { get; }

    /// <summary>The player's display name, exactly as it was persisted. Never null or blank; otherwise never interpreted.</summary>
    public string DisplayName { get; }

    /// <summary>The player's Legend Level.</summary>
    /// <remarks>Get-only, with no mutator anywhere yet: the levelling curve and unlock ladder are a later milestone's.</remarks>
    public int LegendLevel { get; }

    /// <summary>Lifetime Legend XP. Never negative. Mutated by <see cref="GrantLegendXp"/>.</summary>
    public long LegendXp => _legendXp;

    /// <summary>The player's lifetime runs-started counter — the fourth argument fed into <c>runSeed</c> derivation.</summary>
    /// <remarks>
    /// Must be monotonic and never reset: two runs started in the same second draw different boards
    /// only because this advances. <see cref="BeginRun"/> is the sole mutator.
    /// </remarks>
    public long RunsStarted => _runsStarted;

    /// <summary>
    /// The six player-scoped wallet balances. Read-only, and every currency in
    /// <see cref="WalletCurrencies"/> is present — a missing key is a corrupt row, not a zero.
    /// </summary>
    /// <remarks>
    /// A frozen view, unlike <see cref="DailyCounters"/>: the wallet is replaced wholesale on every
    /// movement, so an object a caller holds never changes afterwards.
    /// </remarks>
    public IReadOnlyDictionary<CurrencyId, long> Wallet => _wallet;

    /// <summary>The two Energy banks — where the <c>ENERGY</c> currency lives.</summary>
    public EnergyBanks Energy => _energy;

    /// <summary>The instant Energy regeneration has been accrued up to.</summary>
    public DateTimeOffset EnergyAnchorUtc => _energyAnchorUtc;

    /// <summary>The instant the last command was applied to this player, which <c>AdvanceTime</c> rolls forward from.</summary>
    public DateTimeOffset LastAppliedAtUtc => _lastAppliedAtUtc;

    /// <summary>The tutorial beat this player has reached.</summary>
    public FtueBeat FtueBeat => _ftueBeat;

    /// <summary>When the tutorial completed. <c>null</c> until beat 10's spend commits.</summary>
    public DateTimeOffset? FtueCompletedAtUtc => _ftueCompletedAtUtc;

    /// <summary>Whether the tutorial is finished, defined as <c>completedAtUtc</c> being set.</summary>
    public bool IsFtueComplete => _ftueCompletedAtUtc is not null;

    /// <summary>The 05:00 UTC game-day boundary <see cref="DailyCounters"/> were last reset at.</summary>
    public DateTimeOffset DailyPeriodStartUtc => _dailyPeriodStartUtc;

    /// <summary>The daily counters for the current game day. Read-only; empty is the normal state.</summary>
    /// <remarks>
    /// A live view, unlike <see cref="Wallet"/>: the counters are mutated in place, so a caller
    /// holding this reference across a reset sees the new values. Read it, do not hold it.
    /// </remarks>
    public IReadOnlyDictionary<string, long> DailyCounters => _dailyCountersView;

    /// <summary>The Monday 05:00 UTC game-week boundary <see cref="WeeklyCounters"/> were last reset at.</summary>
    public DateTimeOffset WeeklyPeriodStartUtc => _weeklyPeriodStartUtc;

    /// <summary>The weekly counters for the current game week. Read-only.</summary>
    /// <remarks>A live view, like <see cref="DailyCounters"/> and unlike <see cref="Wallet"/>.</remarks>
    public IReadOnlyDictionary<string, long> WeeklyCounters => _weeklyCountersView;

    /// <summary>The login-calendar day currently open: the one the player may claim, counted from 1.</summary>
    /// <remarks>
    /// Open, not "the last one claimed" — a missed day leaves the same day open rather than skipping
    /// it, and a "last claimed" field would have nothing to say about a brand-new player.
    /// </remarks>
    public int LoginCalendarDay => _loginCalendarDay;

    /// <summary>Whether <see cref="LoginCalendarDay"/> has been claimed. While false, the calendar does not advance.</summary>
    public bool LoginCalendarDayClaimed => _loginCalendarDayClaimed;

    /// <summary>The (Chapter, Tier) pairs cleared at least once. See <see cref="_clearedChapterTiers"/>.</summary>
    internal IReadOnlyDictionary<string, long> ClearedChapterTiers => _clearedChapterTiersView;

    // ---------------------------------------------------------------- feat counters (M4-13)

    /// <summary>The player's lifetime feat counters. A live view, like <see cref="DailyCounters"/>.</summary>
    /// <remarks>
    /// Lifetime and additive: nothing on this aggregate clears it, and
    /// <see cref="ResetDailyCounters"/> and <see cref="ResetWeeklyCounters"/> deliberately do not
    /// reach it. A Feat is claimed retroactively against these counts, so a reset would understate
    /// a history that cannot be rebuilt.
    /// </remarks>
    public FeatCounters FeatCounters => _featCountersView;

    /// <summary>The lifetime count of one feat counter, or zero when nothing has advanced it.</summary>
    /// <param name="counterId">The counter's id. Never null, empty or whitespace.</param>
    /// <exception cref="ArgumentException"><paramref name="counterId"/> is blank.</exception>
    public long FeatCount(string counterId) => CountIn(_featCounters, counterId);

    /// <summary>The key <see cref="ClearedChapterTiers"/> is stored under.</summary>
    internal static string ChapterTierKey(int chapterId, DifficultyTier tier) =>
        chapterId.ToString(CultureInfo.InvariantCulture) + ":" + tier;

    /// <summary>Whether (<paramref name="chapterId"/>, <paramref name="tier"/>) has been cleared before.</summary>
    internal bool HasClearedChapterTier(int chapterId, DifficultyTier tier) =>
        _clearedChapterTiers.ContainsKey(ChapterTierKey(chapterId, tier));

    /// <summary>Records that (<paramref name="chapterId"/>, <paramref name="tier"/>) has now been cleared. Idempotent.</summary>
    internal void MarkChapterTierCleared(int chapterId, DifficultyTier tier) =>
        _clearedChapterTiers[ChapterTierKey(chapterId, tier)] = 1;

    /// <summary>
    /// The balance of one player-scoped wallet currency.
    /// </summary>
    /// <param name="currency">One of <see cref="WalletCurrencies"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="currency"/> is <c>GOLD</c> (run-scoped), <c>ENERGY</c> (held as
    /// <see cref="Energy"/>) or undefined. Each is refused with the reason rather than answering
    /// zero, which would read as "the player has none" for a balance on the wrong aggregate.
    /// </exception>
    public long BalanceOf(CurrencyId currency)
    {
        RequireWalletCurrency(currency, nameof(currency));

        return _wallet[currency];
    }

    /// <summary>The current count of a daily counter, or zero when nothing has registered it.</summary>
    /// <param name="counterKey">The counter's key. Never null, empty or whitespace.</param>
    /// <exception cref="ArgumentException"><paramref name="counterKey"/> is blank.</exception>
    public long DailyCount(string counterKey) => CountIn(_dailyCounters, counterKey);

    /// <inheritdoc cref="DailyCount"/>
    public long WeeklyCount(string counterKey) => CountIn(_weeklyCounters, counterKey);

    /// <summary>The persisted shape of this aggregate, stamped with the current <see cref="SnapshotSchema.SchemaVersion"/>.</summary>
    /// <remarks>
    /// The counter dictionaries are copied; the wallet is not — the wallet is already replaced
    /// wholesale on every movement, so the object handed out here can never change afterwards, while
    /// a shared counter reference would let a later increment rewrite an already-handed-out snapshot.
    /// </remarks>
    public PlayerSnapshot ToSnapshot() => new(
        SnapshotSchema.SchemaVersion,
        Id,
        DisplayName,
        LegendLevel,
        LegendXp,
        _runsStarted,
        _wallet,
        _energy,
        _energyAnchorUtc,
        _lastAppliedAtUtc,
        _ftueBeat,
        _ftueCompletedAtUtc,
        _dailyPeriodStartUtc,
        Copy(_dailyCounters),
        _weeklyPeriodStartUtc,
        Copy(_weeklyCounters),
        _loginCalendarDay,
        _loginCalendarDayClaimed,
        Copy(_clearedChapterTiers),
        Copy(_featCounters));

    /// <summary>The one validated entry point for a persisted player: a corrupt row fails loudly at the seam.</summary>
    /// <param name="snapshot">The persisted row.</param>
    /// <param name="content">
    /// The version-stamped content snapshot the command is reading; the Legend Level range is a
    /// tunable read from it.
    /// </param>
    /// <returns>
    /// The rehydrated aggregate, or a failure listing every validation the row failed, not just the
    /// first.
    /// </returns>
    /// <remarks>
    /// An unknown <see cref="PlayerSnapshot.SchemaVersion"/> hard-fails first and alone: no migration
    /// code exists yet, so a row from another version has no reader and "close enough" layouts would
    /// silently shift a field. A bad content set throws rather than failing: a missing or unauthorised
    /// tunable is every player's problem and belongs at the composition root, not in a per-row Result.
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

        // No energy check here, deliberately: the upper bound is enforced on mutation instead (see
        // the type's remarks), and the lower bound needs none — EnergyBanks refuses negative amounts
        // itself, and default(EnergyBanks) is (0, 0), a legitimate state.
        RequireIdentity(snapshot, faults);
        RequireProfile(snapshot, legend, faults);
        var wallet = ReadWallet(snapshot, faults);
        RequireTimestamps(snapshot, faults);
        RequireFtue(snapshot, faults);
        var daily = ReadCounters(snapshot.DailyCounters, nameof(PlayerSnapshot.DailyCounters), faults);
        var weekly = ReadCounters(snapshot.WeeklyCounters, nameof(PlayerSnapshot.WeeklyCounters), faults);
        RequireLoginCalendar(snapshot, faults);
        var clearedChapterTiers = ReadClearedChapterTiers(snapshot.ClearedChapterTiers, faults);
        var featCounters = ReadCounters(snapshot.FeatCounters, nameof(PlayerSnapshot.FeatCounters), faults);

        // The `is null` arms are unreachable while `faults` is empty — every path that returns null
        // also adds a fault — but written as a pattern so the correlation is checked, not asserted.
        if (faults.Count > 0 || wallet is null || daily is null || weekly is null ||
            clearedChapterTiers is null || featCounters is null)
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
            snapshot.RunsStarted,
            wallet,
            snapshot.Energy,
            snapshot.EnergyAnchorUtc,
            snapshot.LastAppliedAtUtc,
            snapshot.FtueBeatId,
            snapshot.FtueCompletedAtUtc,
            snapshot.DailyPeriodStartUtc,
            daily,
            snapshot.WeeklyPeriodStartUtc,
            weekly,
            snapshot.LoginCalendarDay,
            snapshot.LoginCalendarDayClaimed,
            clearedChapterTiers,
            featCounters));
    }

    /// <summary>Moves one player-scoped wallet currency and produces the <c>CurrencyChanged</c> that attributes it.</summary>
    /// <param name="currency">One of <see cref="WalletCurrencies"/>.</param>
    /// <param name="delta">Signed: positive is income, negative is a spend. Zero is permitted.</param>
    /// <param name="reason">
    /// Why it moved — a stable <c>lower_snake_case</c> token. Never blank; <c>CurrencyChanged</c>
    /// refuses that.
    /// </param>
    /// <returns>The event, with <see cref="DomainEvent.Sequence"/> left at <c>DomainEvent.UnstampedSequence</c>.</returns>
    /// <remarks>
    /// <c>internal</c>; the only public route is <c>GameRules.Apply</c>. Throws rather than returning
    /// a <see cref="Result{T}"/> on an unaffordable spend: the handler is expected to have refused it
    /// as a <c>RejectionReason</c> before it reaches here.
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

        // Checked: unchecked overflow would wrap a large grant to a negative balance silently.
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

        return MoveBalance(currency, delta, next, _energy, reason);
    }

    /// <summary>
    /// Grants Legend XP from a run's <c>FinalPayout</c>. Not a <see cref="CurrencyId"/> movement, so
    /// it emits no <c>CurrencyChanged</c>. Turning the new total into a <see cref="LegendLevel"/> is a
    /// later milestone's levelling curve; this seam only ever raises the lifetime total.
    /// </summary>
    /// <param name="amount">Legend XP to add. Never negative.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> is negative, or the total would overflow.</exception>
    internal void GrantLegendXp(long amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount), amount, "Legend XP is a lifetime total; it only ever grows.");
        }

        try
        {
            _legendXp = checked(_legendXp + amount);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "Granting " + amount.ToString(CultureInfo.InvariantCulture) + " Legend XP overflows " +
                "a 64-bit lifetime total. An amount this size is an economy defect upstream, not a " +
                "reward to store.");
        }
    }

    /// <summary>Writes the two Energy banks a rule computed, and produces the attributing <c>CurrencyChanged</c>.</summary>
    /// <param name="banks">
    /// The banks after the rule. The aggregate does not compute them; it refuses them if they break
    /// the invariant.
    /// </param>
    /// <param name="tuning">The energy numbers, so the caps can be derived for this Legend Level.</param>
    /// <param name="reason">Why Energy moved. A stable <c>lower_snake_case</c> token.</param>
    /// <returns>
    /// A <c>CurrencyChanged</c> for <see cref="CurrencyId.ENERGY"/> whose <c>Delta</c> is the change
    /// in both banks together — overflow into the Reserve is one movement, not two.
    /// </returns>
    /// <remarks>
    /// The ceiling is <c>max(cap, where the bank already is)</c>, not <c>cap</c>: a balance patch that
    /// lowers the cap must not throw on a player who was already full and has not been touched.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="tuning"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is blank.</exception>
    /// <exception cref="InvalidOperationException">The banks would exceed their ceiling.</exception>
    internal CurrencyChanged SetEnergy(EnergyBanks banks, EnergyTuning tuning, string reason)
    {
        ArgumentNullException.ThrowIfNull(tuning);

        var max = tuning.MaxEnergyAt(LegendLevel);

        RequireWithinCeiling(banks.Energy, _energy.Energy, max, "the main Energy bar", "10 §3");
        RequireWithinCeiling(
            banks.Reserve, _energy.Reserve, tuning.ReserveCapacityAt(LegendLevel), "the Energy Reserve", "28 C2");

        var delta = ((long)banks.Energy + banks.Reserve) - ((long)_energy.Energy + _energy.Reserve);

        return MoveBalance(CurrencyId.ENERGY, delta, delta, banks, reason);
    }

    /// <summary>The one accrual seam: writes the regenerated banks and moves the anchor by the span that produced them, in one call.</summary>
    /// <param name="banks"><c>EnergyMath.Accrue(...).Banks</c>.</param>
    /// <param name="anchorAdvance"><c>EnergyMath.Accrue(...).AnchorAdvance</c> — the same accrual's, never another's.</param>
    /// <param name="tuning">The energy numbers, so the ceiling can be derived for this Legend Level.</param>
    /// <param name="reason">Why Energy moved. A stable <c>lower_snake_case</c> token.</param>
    /// <remarks>
    /// Takes both halves of one accrual because they are one fact: writing the banks but forgetting
    /// the anchor would re-grant the same span of regeneration on every later command, and no
    /// aggregate-level invariant could catch it since each write is individually legal.
    /// <see cref="SetEnergy"/> stays for grants and spends, which legitimately move a balance without
    /// moving the anchor.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="tuning"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="anchorAdvance"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">The banks would exceed their ceiling.</exception>
    internal CurrencyChanged AccrueEnergy(
        EnergyBanks banks, TimeSpan anchorAdvance, EnergyTuning tuning, string reason)
    {
        // Checked first so a negative advance cannot leave the banks written and the anchor not.
        RequireForwardAnchor(anchorAdvance);

        var change = SetEnergy(banks, tuning, reason);

        AdvanceEnergyAnchor(anchorAdvance);

        return change;
    }

    /// <summary>Advances the regeneration anchor by the span a rule actually accrued.</summary>
    /// <remarks><c>private</c>, reachable only through <see cref="AccrueEnergy"/> — see its remarks.</remarks>
    private void AdvanceEnergyAnchor(TimeSpan accrued)
    {
        RequireForwardAnchor(accrued);

        _energyAnchorUtc += accrued;
    }

    /// <summary>The anchor only moves forwards.</summary>
    private static void RequireForwardAnchor(TimeSpan accrued)
    {
        if (accrued < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(accrued),
                accrued,
                "The regeneration anchor only moves forwards. A negative advance would hand the " +
                "player the same span of regeneration twice on the next command.");
        }
    }

    /// <summary>Advances the lifetime runs-started counter and answers the value the run being started is seeded with.</summary>
    /// <returns>The counter after the increment — the <c>runCounter</c> argument to <c>runSeed</c> derivation.</returns>
    /// <remarks>
    /// Returns the value rather than leaving the caller to read <see cref="RunsStarted"/> back, so an
    /// interleaved second <c>START_RUN</c> cannot seed two runs the same way.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The counter would overflow a 64-bit count.</exception>
    internal long BeginRun()
    {
        if (_runsStarted == long.MaxValue)
        {
            throw new InvalidOperationException(
                "The lifetime runs-started counter is at long.MaxValue and cannot advance. 02 §2 " +
                "feeds it into runSeed, so wrapping it to a negative would start re-seeding runs " +
                "with values the player has already played.");
        }

        return ++_runsStarted;
    }

    /// <summary>Records that a command has been applied at <paramref name="nowUtc"/>.</summary>
    /// <param name="nowUtc">
    /// <c>GameContext.NowUtc</c>. Must carry a zero offset and must not precede
    /// <see cref="LastAppliedAtUtc"/>.
    /// </param>
    /// <remarks>
    /// Equal is allowed, strictly-earlier is not: two commands can legitimately share an instant, but
    /// an earlier one means a clock moved backwards — a persistence defect this method refuses rather
    /// than silently accepting, since a backwards anchor would never self-correct.
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

    /// <summary>Clears the daily counters and records the 05:00 UTC boundary they were cleared at.</summary>
    /// <param name="periodStartUtc">
    /// The game-day boundary now in force: 05:00:00.000 UTC exactly, offset zero, and never before
    /// the boundary already recorded.
    /// </param>
    /// <remarks>
    /// The aggregate does not work out which boundary that is — computing it is arithmetic, and stays
    /// out of <c>Model</c>. It holds the invariant that whatever it is handed is a boundary the game
    /// actually has.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Not a 05:00 UTC boundary, or it goes backwards.</exception>
    internal void ResetDailyCounters(DateTimeOffset periodStartUtc)
    {
        RequireGameDayBoundary(periodStartUtc, nameof(periodStartUtc));
        RequireNotBefore(periodStartUtc, _dailyPeriodStartUtc, nameof(periodStartUtc), "daily");

        // The boundary already in force is a no-op, not a clear: this runs as lazy catch-up on every
        // command, and clearing on equality would wipe the day's progress several times an hour.
        if (periodStartUtc == _dailyPeriodStartUtc)
        {
            return;
        }

        _dailyCounters.Clear();
        _dailyPeriodStartUtc = periodStartUtc;
    }

    /// <summary>The weekly half. The game week starts Monday 05:00 UTC.</summary>
    /// <param name="periodStartUtc">A Monday at 05:00:00.000 UTC, offset zero, never going backwards.</param>
    /// <exception cref="ArgumentOutOfRangeException">Not a Monday 05:00 UTC boundary, or it goes backwards.</exception>
    internal void ResetWeeklyCounters(DateTimeOffset periodStartUtc)
    {
        RequireGameDayBoundary(periodStartUtc, nameof(periodStartUtc));

        // The weekday is GameCalendar.WeekStart's, the same definition GameRules.AdvanceTime reads.
        if (periodStartUtc.DayOfWeek != GameCalendar.WeekStart)
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

        // The boundary already in force is a no-op — see ResetDailyCounters.
        if (periodStartUtc == _weeklyPeriodStartUtc)
        {
            return;
        }

        _weeklyCounters.Clear();
        _weeklyPeriodStartUtc = periodStartUtc;
    }

    /// <summary>Advances the login calendar to the next day, which becomes open and unclaimed. The pointer only; nothing is paid out.</summary>
    /// <param name="tuning">The calendar numbers, so the cycle wrap is read from tuning rather than hard-coded.</param>
    /// <remarks>
    /// The pause rule is enforced here: it advances only when the currently open day has been
    /// claimed, so an unclaimed day is a silent no-op rather than a refusal — nothing is skipped or
    /// lost. "At most once per game day" is deliberately not checked here; that is the caller's
    /// per-game-day idempotence to enforce. It emits no event (a calendar day is not a currency) and
    /// returns nothing, since <see cref="LoginCalendarDay"/> and <see cref="LoginCalendarDayClaimed"/>
    /// already answer whether it moved.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="tuning"/> is null.</exception>
    internal void AdvanceLoginCalendar(LoginCalendarTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);

        if (!_loginCalendarDayClaimed)
        {
            return;
        }

        _loginCalendarDay = tuning.DayAfter(_loginCalendarDay);
        _loginCalendarDayClaimed = false;
    }

    /// <summary>Registers and advances a daily counter. The counter comes into existence on its first increment.</summary>
    /// <param name="counterKey">A stable <c>lower_snake_case</c> key owned by the system that counts. Deliberately not a closed enum.</param>
    /// <param name="amount">How much to add. Never negative — a counter counts, it does not settle.</param>
    /// <exception cref="ArgumentException"><paramref name="counterKey"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> is negative, or the count overflows.</exception>
    internal void CountDaily(string counterKey, long amount) =>
        Count(_dailyCounters, counterKey, amount, "daily");

    /// <inheritdoc cref="CountDaily"/>
    internal void CountWeekly(string counterKey, long amount) =>
        Count(_weeklyCounters, counterKey, amount, "weekly");

    // ---------------------------------------------------------------- feat counters (M4-13)

    /// <summary>Advances a lifetime feat counter. The counter comes into existence on its first increment.</summary>
    /// <param name="counterId">A stable <c>lower_snake_case</c> id owned by the projection that counts. Deliberately not a closed enum.</param>
    /// <param name="amount">
    /// How much to add. Never negative and never zero — a lifetime counter only ever grows, and a
    /// zero advance would register a counter that nothing has actually counted.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="counterId"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> is not positive, or the count overflows.</exception>
    internal void CountFeat(string counterId, long amount)
    {
        if (string.IsNullOrWhiteSpace(counterId))
        {
            throw new ArgumentException(BlankFeatCounterId, nameof(counterId));
        }

        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "A lifetime feat counter only ever grows; '" + counterId + "' cannot be advanced " +
                "by " + Text(amount) + ". These counters are never reset and never settled back " +
                "down — a Feat is claimed retroactively against the count, so an advance that " +
                "lowered it would pay out against a history the player did not have, and a zero " +
                "advance would register a counter nothing has actually counted.");
        }

        _featCounters.TryGetValue(counterId, out var current);

        try
        {
            _featCounters[counterId] = checked(current + amount);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "Advancing the lifetime feat counter '" + counterId + "' by " + Text(amount) +
                " from " + Text(current) + " overflows a 64-bit count.");
        }
    }

    /// <summary>Advances the tutorial to the next beat, as each beat's interaction completes.</summary>
    /// <param name="beat">The beat now reached. Strictly after the current one.</param>
    /// <remarks>Strictly forwards, and refused outright once the tutorial is complete.</remarks>
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

    /// <summary>Completes the tutorial. Set once beat 10's spend commits; no FTUE surface appears again after.</summary>
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

    /// <summary>The one place a balance changes, and therefore the one place that attributes the change.</summary>
    /// <remarks>
    /// Both currency stores are written here — <c>_wallet</c> for the wallet rows, <c>_energy</c> for
    /// <c>ENERGY</c> — so the IL scan requiring a <c>CurrencyChanged</c> on every currency-field write
    /// covers both. Validation belongs to the callers, so this method has exactly one job.
    /// </remarks>
    private CurrencyChanged MoveBalance(
        CurrencyId currency, long delta, long balance, EnergyBanks banks, string reason)
    {
        // Built before the write: CurrencyChanged refuses a blank Reason in its own initialiser, so
        // constructing it after the write would leave the balance moved with no attribution.
        var change = new CurrencyChanged(DomainEvent.UnstampedSequence, currency, delta, reason);

        if (currency == CurrencyId.ENERGY)
        {
            _energy = banks;
        }
        else
        {
            // `balance` is already the CHECKED sum MoveCurrency computed; re-adding here would redo
            // it unchecked, past the overflow guard the caller already ran.
            var next = new Dictionary<CurrencyId, long>(_wallet) { [currency] = balance };
            _wallet = new ReadOnlyDictionary<CurrencyId, long>(next);
        }

        return change;
    }

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

    /// <summary>The blank-id refusal for a feat counter, said once.</summary>
    /// <remarks>
    /// Separate from <see cref="RequireCounterKey"/>'s, which explains an OPEN key space in terms of
    /// the daily-reset systems that have not been written. A feat counter's id space is open for a
    /// different reason — what each Feat measures is not decided yet — and a reader who hits this
    /// needs that reason, not the other one.
    /// </remarks>
    private const string BlankFeatCounterId =
        "A feat counter id names the projection that owns it, so it is never blank. The id space is " +
        "deliberately open rather than a closed enum, because what each Feat measures is a decision " +
        "the milestone that ships Feats still has to take — but 'open' means the owner picks the " +
        "token, not that there is no token.";

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

    /// <summary>The 05:00 UTC invariant, asked of <see cref="GameCalendar"/> rather than restated.</summary>
    /// <remarks>
    /// This aggregate held its own copy of the boundary hour until <c>GameRules.AdvanceTime</c>
    /// needed the same number and could not call into <c>Model</c> from <c>Rules</c>; both now read
    /// one definition from <c>Primitives</c>.
    /// </remarks>
    private static void RequireGameDayBoundary(DateTimeOffset boundary, string parameterName)
    {
        RequireZeroOffset(boundary, parameterName);

        if (GameCalendar.IsGameDayBoundary(boundary))
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
        // default(PlayerId) runs no constructor, so its Value is null rather than validated.
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

        if (snapshot.RunsStarted < 0)
        {
            faults.Add(
                nameof(PlayerSnapshot.RunsStarted) + " is " + Text(snapshot.RunsStarted) + ". 02 §2 " +
                "calls it the lifetime runs-started counter and feeds it into runSeed, so it counts " +
                "upwards from zero and is never reset — a negative one would seed a run with a " +
                "value no play produced.");
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

        // Runs only once the instant is confirmed UTC, so one defect reports once, not twice.
        if (snapshot.DailyPeriodStartUtc.Offset == TimeSpan.Zero &&
            !GameCalendar.IsGameDayBoundary(snapshot.DailyPeriodStartUtc))
        {
            faults.Add(
                nameof(PlayerSnapshot.DailyPeriodStartUtc) + " is " +
                Text(snapshot.DailyPeriodStartUtc) + ", which is not a 05:00 UTC game-day boundary " +
                "(30 §2.3).");
        }

        // Both halves of the failure are named, since the guard can fire on either.
        if (snapshot.WeeklyPeriodStartUtc.Offset == TimeSpan.Zero &&
            !GameCalendar.IsGameWeekBoundary(snapshot.WeeklyPeriodStartUtc))
        {
            faults.Add(
                nameof(PlayerSnapshot.WeeklyPeriodStartUtc) + " is " +
                Text(snapshot.WeeklyPeriodStartUtc) + " — a " + snapshot.WeeklyPeriodStartUtc.DayOfWeek +
                " at " + Text(snapshot.WeeklyPeriodStartUtc.TimeOfDay) + " UTC. The game week starts " +
                "MONDAY 05:00 UTC (milestone assumption A2, derived from 27 §4).");
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

    /// <summary>The login calendar's floor, and only its floor.</summary>
    /// <remarks>
    /// No upper bound here either: a shortened cycle from a balance patch would otherwise turn a
    /// tuning change into an outage. <c>LoginCalendarTuning.DayAfter</c> wraps on the next advance
    /// instead of failing to load.
    /// </remarks>
    private static void RequireLoginCalendar(PlayerSnapshot snapshot, List<string> faults)
    {
        if (snapshot.LoginCalendarDay < LoginCalendarTuning.FirstDay)
        {
            faults.Add(
                nameof(PlayerSnapshot.LoginCalendarDay) + " is " + Text(snapshot.LoginCalendarDay) +
                ". 19 G numbers the login calendar from day " + Text(LoginCalendarTuning.FirstDay) +
                "; day 0 is what an uninitialised column reads as, and a player standing on it would " +
                "be paid one day behind the table for the rest of the cycle.");
        }
    }

    private static Dictionary<string, long>? ReadCounters(
        IReadOnlyDictionary<string, long>? counters, string field, List<string> faults)
    {
        if (counters is null)
        {
            faults.Add(field + " is null. An absent counter map is not an empty one.");
            return null;
        }

        // Copied into an ORDINAL dictionary: CanonicalStateWriter orders string keys ordinally, so
        // any other comparer would round-trip to a different hash than the one it was stored under.
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

    /// <summary>Reads <see cref="PlayerSnapshot.ClearedChapterTiers"/>. Unlike <see cref="ReadCounters"/>, <c>null</c> is not a fault.</summary>
    /// <remarks>This field was appended with a defaulted parameter, so a row from before it existed reads as "nothing cleared yet".</remarks>
    private static Dictionary<string, long>? ReadClearedChapterTiers(
        IReadOnlyDictionary<string, long>? clearedChapterTiers, List<string> faults)
    {
        if (clearedChapterTiers is null)
        {
            return new Dictionary<string, long>(StringComparer.Ordinal);
        }

        return ReadCounters(clearedChapterTiers, nameof(PlayerSnapshot.ClearedChapterTiers), faults);
    }

    /// <summary>The empty counter map every snapshot of a player with no counters shares.</summary>
    /// <remarks>Safe to share: it is read-only and empty, so nothing can distinguish a shared instance from a private one.</remarks>
    private static readonly ReadOnlyDictionary<string, long> NoCounters =
        new(new Dictionary<string, long>(0, StringComparer.Ordinal));

    /// <summary>An ordinal copy of a counter map, so no caller shares the aggregate's dictionary.</summary>
    /// <remarks>Short-circuits on empty, the normal state — this runs on every command, since the client recomputes its own state hash too.</remarks>
    private static ReadOnlyDictionary<string, long> Copy(Dictionary<string, long> counters) =>
        counters.Count == 0
            ? NoCounters
            : new ReadOnlyDictionary<string, long>(new Dictionary<string, long>(counters, StringComparer.Ordinal));

    /// <summary>Renders a value with <see cref="CultureInfo.InvariantCulture"/>, so messages read the same on every host.</summary>
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
