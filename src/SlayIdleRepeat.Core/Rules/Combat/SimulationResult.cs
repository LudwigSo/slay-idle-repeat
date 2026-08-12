namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>
/// 🔒 `05` §7 — everything one <c>Simulate(seed, heroSnapshot, enemySnapshot)</c> produces.
/// </summary>
/// <param name="HeroWon">
/// Whether the hero side won. On the 90 s timeout (`05` §3) this is the side with the higher
/// remaining HP fraction, not a draw.
/// </param>
/// <param name="DurationTicks">
/// How many ticks the fight ran, <c>1..1800</c>. The replayer's total length: at ×1 the battle
/// lasts <c>DurationTicks × 0.05 s</c>, at ×3 a third of that (`05` §8).
/// </param>
/// <param name="HeroHpRemaining">The hero's HP at the final tick, rounded to 4 dp (`05` §1.1).</param>
/// <param name="Log">
/// Every <see cref="CombatEvent"/>, in emission order — which `05` §3.1 step 7 makes the same
/// thing as tick order. This <b>is</b> the replay (`05` §8); nothing else is needed to draw the
/// fight.
/// </param>
/// <param name="LogHash">
/// 🔒 `05` §7 — <em>"an FNV-1a hash over the serialised event list"</em>, computed by
/// <c>CanonicalStateWriter.HashCombatLog</c> over <see cref="Log"/>. `11` §6 compares the
/// client-reported value against the server-computed one to detect tampering, and M5-12 compares
/// it across x64 and ARM64 as the determinism gate.
/// </param>
/// <remarks>
/// <para>
/// 🔒 <b>Exactly these five fields.</b> `05` §7 fixes them, and in particular there is <b>no queue
/// field for run effects</b>: the queue <i>is</i> the <see cref="CombatEventType.RunEffectQueued"/>
/// entries of <see cref="Log"/>, read in log order (`18` §2.5). A sixth field holding them would be
/// a second copy of state the log already carries, and only one of the two would be inside
/// <see cref="LogHash"/>.
/// </para>
/// <para>
/// <b>Two shape notes against `05` §7's literal declaration.</b> <see cref="HeroHpRemaining"/> is
/// <see cref="double"/> for the reasons set out on <see cref="CombatEvent"/> — `14` §16.6 has no
/// <c>float</c> row and `05` §1.1 rounds HP to 4 dp like every other combat number. And
/// <see cref="Log"/> is an <see cref="IReadOnlyList{T}"/> rather than a <c>List&lt;CombatEvent&gt;</c>:
/// the wire shape is identical — a count and the elements in stored order — but a result the
/// consumer can append to is a replay that can be edited after the outcome was fixed, which is the
/// one thing `05` §8's <em>"the outcome is already determined"</em> depends on not being possible.
/// </para>
/// <para>
/// ⚠️ <b>Accessibility — an unresolved contradiction between two documents, not a naming choice.</b>
/// `05` §7 declares this type and <see cref="CombatEvent"/> <b>public</b>, and `30` §11.2 makes
/// <c>CombatSimulator</c> public, so a public <c>Simulate</c> returning an internal
/// <c>SimulationResult</c> would not even compile. But `30` §11.2 is 🔒 and reads <em>"The only
/// two <c>Rules</c> types that are public"</em>, naming <c>CombatSimulator</c> and
/// <c>PowerCalculator</c> — and <c>Domain.PublicRuleTypes</c> transcribes exactly that closed list.
/// The two documents disagree, and widening the list is a change to a <b>locked</b> section rather
/// than a mechanical edit.
/// <para>
/// They are <c>internal</c> today because M2-15 ships no simulator, so nothing yet forces the
/// question — and they are fully tested through the `30` §11.3 <c>InternalsVisibleTo</c> grant, so
/// only the visibility is deferred, not the verification. 🔒 <b>Landing
/// <c>CombatSimulator</c> requires a ruling on the `05` §7 / `30` §11.2 conflict first</b>, and
/// then both halves in one commit: these types public, and their names added to
/// <c>Domain.PublicRuleTypes</c>. The deferral expires by itself —
/// <c>SubjectSetFloorTests</c> declares <c>CombatSimulator</c> pending and its rule fails the
/// moment a type by that name exists.
/// </para>
/// </para>
/// </remarks>
internal sealed record SimulationResult(
    bool HeroWon,
    int DurationTicks,
    double HeroHpRemaining,
    IReadOnlyList<CombatEvent> Log,
    ulong LogHash);
