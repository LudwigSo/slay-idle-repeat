namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>
/// The playhead over a hero's walk across the board: which node it is drawn on, which hop is in
/// flight, and how far through that hop it is.
/// </summary>
/// <remarks>
/// <para>
/// The presenter is the truth and this is a playhead over it. A command applies its state change at
/// once — HP, gold and the number rolled are all correct the instant it returns — and the hero then
/// walks the distance the run already moved. So the scene draws the numbers from the presenter and
/// the POSITION from here.
/// </para>
/// <para>
/// Engine-free on purpose, so the one part of the animation with a wrong answer possible — has the
/// walk finished, how far through is this hop, does a long frame land where several short ones do —
/// is testable without booting anything. The arc itself is a curve through space with nothing to get
/// wrong, and stays in the scene where <c>Vector3</c> lives.
/// </para>
/// </remarks>
public sealed class BoardWalk
{
    /// <summary>
    /// How long one hop takes.
    /// </summary>
    /// <remarks>
    /// ⚠️ Not authored by any design document. `03` and `13` say the board is read by rolling and
    /// watching, and name no duration. Chosen so a six lands inside about a second — long enough to
    /// count the hops, short enough not to be waited on.
    /// </remarks>
    private const double HopSeconds = 0.18;

    private readonly bool _reducedMotion;
    private readonly List<int> _hops = new();

    private int _index;
    private double _elapsed;

    /// <summary>Creates a playhead.</summary>
    /// <param name="reducedMotion">
    /// When true, every hop completes on its first advance: the hero still walks each node in order,
    /// but no frame is spent in between. The same seam <c>BattleReplayPresenter</c> takes, and the
    /// one M7-07's review found a screen quietly ignoring.
    /// </param>
    public BoardWalk(bool reducedMotion = false) => _reducedMotion = reducedMotion;

    /// <summary>
    /// The node the hero is DRAWN on — not the node the run stands on. <c>null</c> only at the
    /// trailhead, before the first roll has put the hero on the board.
    /// </summary>
    public int? ShownNodeId { get; private set; }

    /// <summary>Whether hops are still outstanding.</summary>
    public bool InProgress => _index < _hops.Count;

    /// <summary>
    /// The node the hop in flight left, or <c>null</c> when nothing is in flight — which is always
    /// the node currently shown, since the hero leaves where it is standing.
    /// </summary>
    /// <remarks>
    /// Derived rather than stored. Stored, it had to be written in two places — once when a walk
    /// begins and once per completed hop — and the second write is easy to put on the wrong side of
    /// the assignment that moves <see cref="ShownNodeId"/>, which draws every hop after the first
    /// from the node before last.
    /// </remarks>
    public int? HopFromNodeId => InProgress ? ShownNodeId : null;

    /// <summary>The node the hop in flight is arriving at. Meaningless unless <see cref="InProgress"/>.</summary>
    public int HopToNodeId => InProgress ? _hops[_index] : ShownNodeId ?? 0;

    /// <summary>How far through the hop in flight the playhead is, 0 to 1.</summary>
    public double HopProgress => InProgress ? Math.Clamp(_elapsed / HopSeconds, 0d, 1d) : 1d;

    /// <summary>
    /// Puts the hero on a node with no walk at all — a first read, a screen resumed after another
    /// took over, or a walk the reconstruction refused to name.
    /// </summary>
    public void SnapTo(int? nodeId)
    {
        _hops.Clear();
        _index = 0;
        _elapsed = 0d;
        ShownNodeId = nodeId;
    }

    /// <summary>Starts walking these nodes, in order.</summary>
    /// <remarks>
    /// A list longer than <see cref="BoardPath.MaxHops"/> is put down at its last node rather than
    /// walked. Nothing legal produces one, so a long list is a reconstruction fault, and walking it
    /// would hold every control on the screen disabled for as long as it took.
    /// </remarks>
    /// <param name="hops">The nodes to walk to, in order. An empty list snaps nowhere and changes nothing.</param>
    /// <exception cref="ArgumentNullException"><paramref name="hops"/> is null.</exception>
    public void Begin(IReadOnlyList<int> hops)
    {
        ArgumentNullException.ThrowIfNull(hops);

        if (hops.Count == 0)
        {
            return;
        }

        if (hops.Count > BoardPath.MaxHops)
        {
            SnapTo(hops[^1]);
            return;
        }

        _hops.Clear();
        _hops.AddRange(hops);
        _index = 0;
        _elapsed = 0d;
    }

    /// <summary>Moves the playhead on.</summary>
    /// <remarks>
    /// Frame-rate independent by construction: elapsed seconds accumulate, and a single long frame
    /// lands the hero exactly where several short ones summing to the same time would. A playhead
    /// that stepped per frame would run at half speed on a thirty-frame handset and be authored
    /// against a sixty-frame desktop.
    /// </remarks>
    /// <param name="deltaSeconds">Seconds since the last advance. A negative or non-finite value moves nothing.</param>
    /// <returns><c>true</c> on the one call that finishes the walk, and never otherwise.</returns>
    public bool Advance(double deltaSeconds)
    {
        if (!InProgress || !double.IsFinite(deltaSeconds) || deltaSeconds <= 0d)
        {
            return false;
        }

        _elapsed += deltaSeconds;

        while (InProgress && (_reducedMotion || _elapsed >= HopSeconds))
        {
            _elapsed = _reducedMotion ? 0d : _elapsed - HopSeconds;
            ShownNodeId = _hops[_index];
            _index++;
        }

        if (InProgress)
        {
            return false;
        }

        _hops.Clear();
        _index = 0;
        _elapsed = 0d;

        return true;
    }
}
