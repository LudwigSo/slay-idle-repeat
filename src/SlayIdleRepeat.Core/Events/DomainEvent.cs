using System.Globalization;
using System.Text;

namespace SlayIdleRepeat.Core.Events;

/// <summary>
/// 🔒 The base of the domain event hierarchy (`30` §7). <c>GameRules.Apply</c> returns a list of
/// these, and four features in the design set read it.
/// </summary>
/// <param name="Sequence">
/// 🔒 The event's <b>ordinal within one <c>Apply</c> call's event list</b> — see the remarks for
/// what that does and does not mean.
/// </param>
/// <remarks>
/// <para>
/// <b>Why the events exist at all.</b> They are not decoration. Four features are downstream of
/// this list and had no other specified source:
/// </para>
/// <list type="bullet">
///   <item><b>Analytics</b> (`14` §10.1) — the 30 named server-emitted events are projected from
///   this list. Complete by construction, because the domain cannot change state without producing
///   one. ⚠️ Those 30 names are PostHog event names owned by task <c>X-05</c>; they are
///   <i>derived from</i> this hierarchy and are not more records to declare here.</item>
///   <item><b>The economy event log</b> (`14` §7.1) — the same list, appended to Postgres.</item>
///   <item><b>Feats</b> (`28` D) — counters increment off the event list rather than off bespoke
///   hooks.</item>
///   <item><b>Client replay</b> (`14` §2.4) — the event list <i>is</i> the animation script.</item>
/// </list>
/// <para>
/// ⚠️ <b>This is not event sourcing.</b> The aggregates are the source of truth and are stored as
/// state; events are an <i>output</i> used for analytics, logging, replay and Feats. Adopting
/// event sourcing as the persistence model is a much larger decision and is explicitly not taken
/// (`30` §7, `30` §12.6).
/// </para>
/// <para>
/// 🔒 <b>What <see cref="Sequence"/> is, and who assigns it.</b> It is the ordinal of the event
/// within the event list produced by a <b>single</b> <c>Apply</c> call: it orders the animation
/// script (`14` §2.4) and the economy-log rows (`14` §7.1) that one command produced. It is
/// assigned by <c>GameRules.Apply</c> (M1-06) — <b>never</b> by a constructor, and never by a
/// caller reaching in from <c>Application</c> or an adapter. A handler that builds an event does
/// not know its position in the list yet; <c>Apply</c> is the only place that does.
/// </para>
/// <para>
/// ⚠️ It is <b>not</b> `14` §16.3's wire <c>sequence</c>. That one is the per-run/per-player
/// <i>command</i> counter carried on the request envelope, and it lives in
/// <c>SlayIdleRepeat.Contracts</c>. Two different numbers, one word; conflating them would make
/// the economy log unorderable and the idempotency protocol wrong at the same time.
/// </para>
/// <para>
/// <b>Public, and abstract.</b> Public because all four consumers live outside <c>Core</c>
/// (`30` §11.2). Abstract because the list is a vocabulary of named happenings: a bare
/// <c>DomainEvent</c> in the returned list would be an analytics row with no event name, a log row
/// with no meaning and an animation frame with no instruction.
/// </para>
/// <para>
/// ⚠️ <b>Only one subtype exists today, and the absences are deliberate.</b> `30` §7 sketches six
/// events. Four of them name payload types no milestone has authored — <c>DieFace</c> (M3-04),
/// <c>TileType</c> (M3-03), <c>GearInstance</c> (M4-03), <c>GuildId</c> (M14) — and
/// <c>PityCounterAdvanced</c> has no producer until <c>LuckService</c> (M4-01). Inventing any of
/// those to make an event compile would put a guessed type at the bottom of the dependency graph
/// for three later milestones to build on. Each is declared instead in
/// <c>SlayIdleRepeat.Architecture.Tests.GapRegister</c>, keyed on the type whose arrival makes the
/// deferral stale — so the build fails on the day the event should be written, rather than the
/// hole waiting to be noticed. <b>The event arrives with the system that gives its payload
/// meaning.</b>
/// </para>
/// <para>
/// <b>What an event may hold.</b> Primitives and value objects that describe <i>what changed</i>.
/// Not an aggregate, not a slice of <c>Content</c>, and — 🔒 — not a timestamp: time enters the
/// domain as <c>GameContext.NowUtc</c> and <c>IClockPort</c> must not appear in <c>Core</c> at all
/// (`30` §3). An event that stamped itself would be the same ambient clock one indirection
/// further out.
/// </para>
/// </remarks>
public abstract record DomainEvent(int Sequence)
{
    /// <summary>
    /// 🔒 The <see cref="Sequence"/> a producer stamps on an event it has just built, before
    /// <c>GameRules.Apply</c> knows where in the list it belongs.
    /// </summary>
    /// <remarks>
    /// Zero, and it is a placeholder rather than a value — the remarks above are explicit that the
    /// ordinal is <c>Apply</c>'s to assign and never a constructor's or a caller's. It lives here
    /// rather than on the first aggregate that needed it (M1-04's <c>Player</c>) because every
    /// later producer needs the same one: <c>Run</c> (M1-05) reaching into an unrelated aggregate
    /// for it, or restating the literal, is how two producers end up with two placeholders and
    /// <c>Apply</c> can no longer tell an unstamped event from a first one.
    /// </remarks>
    internal const int UnstampedSequence = 0;

    /// <summary>
    /// 🔒 Renders this event's members with <see cref="CultureInfo.InvariantCulture"/>. Every event
    /// in the hierarchy overrides it and appends its own; this base renders
    /// <see cref="Sequence"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Why the hierarchy needs a hand-written one at all</b> (carried-forward item 9). A
    /// record's <em>synthesized</em> <c>PrintMembers</c> appends each member through
    /// <c>StringBuilder.Append(object)</c>, which formats with the <b>ambient</b> culture: a
    /// <c>Delta</c> of −10 renders as <c>−10</c> (U+2212 MINUS SIGN) under <c>sv-SE</c> and as
    /// <c>-10</c> (U+002D) in the CI container. `14` §8.2 wants <c>Core</c> reading identically on
    /// every platform, and
    /// <c>AmbientApiTests.Core_and_Application_contain_no_culture_sensitive_formatting</c> cannot
    /// see it — the IL scan matches a call whose declaring type is the formattable's, and the boxing
    /// hides that.
    /// </para>
    /// <para>
    /// ⚠️ <b>Diagnostic only, today, and that is why it is cheap to fix now.</b> Events feed no
    /// <c>stateHash</c> — `14` §16.6 hashes the snapshots, through
    /// <c>CanonicalStateWriter</c>, which is invariant by construction — so nothing about the game's
    /// correctness turns on this text. What turns on it is every log line, every test failure and
    /// every bug report that quotes an event, and the fix gets one event more expensive with each
    /// one M3, M4, M12 and M14 add. <c>GameContext</c> made the same call for the same reason.
    /// </para>
    /// <para>
    /// 🔒 It is a <b>convention with a rule behind it</b>:
    /// <c>AmbientApiTests.Every_domain_event_declares_an_invariant_PrintMembers</c> fails the build
    /// for an event under <c>Core/Events/</c> that does not declare one, so the next event inherits
    /// the convention rather than having to be told about it.
    /// </para>
    /// </remarks>
    /// <param name="builder">The builder the record's <c>ToString()</c> is assembling into.</param>
    /// <returns><see langword="true"/>, so <c>ToString()</c> spaces the closing brace.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    protected virtual bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(CultureInfo.InvariantCulture, $"{nameof(Sequence)} = {Sequence}");

        return true;
    }
}
