namespace SlayIdleRepeat.Core.Commands;

/// <summary>
/// 🔒 The base of the command vocabulary (`30` §2, `14` §2.3) — <em>what the player intends</em>,
/// and the second argument of <c>GameRules.Apply</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Public, and abstract.</b> Public because `30` §11.2 makes the command hierarchy <em>"the input
/// vocabulary — also the wire protocol"</em>, so every composition root builds one. Abstract because
/// a bare <c>GameCommand</c> names no intent: `14` §2.3's registry is 49 named rows, and a command
/// with no row is a request the server has no rule for.
/// </para>
/// <para>
/// ⚠️ <b>It declares no members, and the emptiness is a decision rather than an omission.</b> A
/// command is a value: the parameters `14` §2.3 lists for it and nothing else. Three things that
/// might plausibly have lived here do not, each because it belongs somewhere the design already put
/// it:
/// </para>
/// <list type="bullet">
///   <item><b>The wire name</b> (`14` §2.3's <c>SCREAMING_SNAKE</c> id) is declared <b>once</b>, on
///   the command's row in <c>GameRules</c>' dispatch table, together with its handler and its
///   <c>CommandKind</c>. Putting it here as well would be two declarations of one fact, and the
///   registration is the one a <c>Type</c> can be looked up in without an instance — which is
///   exactly what <c>SlayIdleRepeat.Core.Tests.CommandSeedPin</c> needs and what its type-name
///   heuristic existed to fake (carried-forward item 4).</item>
///   <item><b>Whether the command runs inside a run</b> is <c>CommandKind</c>, also on the
///   registration: <c>Apply</c> reads it to decide whether to build a <c>RunRngScope</c> and whether
///   to slide the run's `14` §16.3 TTL, and neither of those is the command's business.</item>
///   <item><b>A timestamp, a seed or a player id.</b> Time and entropy enter the domain as
///   <c>GameContext</c> (`30` §3) and the aggregates arrive as <c>WorldSlice</c> (`30` §4.1). A
///   command that carried any of them would be a second, unvalidated door into <c>Apply</c>.</item>
/// </list>
/// <para>
/// 🔒 <b>The 49 concrete commands landed in M1-02</b>, and the order was forced:
/// <c>DomainPurityTests.Every_command_type_is_handled_by_Apply</c> fails the build for any
/// <em>concrete</em> subtype that no type on the dispatch surface names, so the base had to land
/// before the vocabulary did — with zero subtypes the rule quantified over nothing and stayed green.
/// From M1-02 it is fully loaded, and <c>Commands.GameCommandTests</c> pins the hierarchy at
/// <b>49</b>: `14` §2.3 is exhaustive, so a fiftieth concrete subtype is a command the wire has no
/// name for.
/// </para>
/// <para>
/// A <c>record</c>, so equality is by value: `14` §3.2's idempotency replays a stored outcome for a
/// repeated command, and two commands describing the same intent must compare equal for that
/// comparison to mean anything.
/// </para>
/// </remarks>
public abstract record GameCommand;
