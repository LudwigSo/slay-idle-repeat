namespace SlayIdleRepeat.Application.Queries;

/// <summary>Where a read is served from — named by the query, never fallen into.</summary>
/// <remarks>
/// No member sits at zero, so an uninitialised field, a zeroed struct or an absent JSON member is
/// no answer at all rather than a silent "primary" or "replica".
/// </remarks>
public enum ReadRouting
{
    /// <summary>The writable node: the read must see everything already committed to it.</summary>
    Primary = 1,

    /// <summary>A replica may answer, within the view's declared staleness budget.</summary>
    ReplicaEligible = 2,
}

/// <summary>A cross-player read model's view model, and the staleness it admits to.</summary>
/// <remarks>
/// The budget is declared here, on the shape the caller receives, and nowhere else: the cache
/// honours it and the screen states it, so two places to write it down would be two numbers to
/// drift. Static and abstract so it is a compile-time obligation — a view that forgot to state one
/// does not build.
/// </remarks>
public interface IReadModelView
{
    /// <summary>How stale an answer of this shape may be.</summary>
    static abstract TimeSpan StalenessBudget { get; }
}

/// <summary>The shape every query port takes.</summary>
/// <typeparam name="TView">The view model this port answers with.</typeparam>
/// <remarks>
/// <para>
/// A query port hands back view models and never an aggregate — an aggregate handed out of a read
/// is a rule one call away from a query. It mutates nothing, and it holds no game rule of its own:
/// a query that re-decides a rule answers the same question the rules answer, differently, the
/// first time either side grows a clause the other does not.
/// </para>
/// <para>
/// It also states how stale its answer may be, and it takes that number from
/// <typeparamref name="TView"/> rather than restating it, so the port and the view cannot disagree.
/// ⚠️ Both members are reached through this interface, not through the implementing class — an
/// adapter that wants to publish either on its own surface has to restate it, which is the one way
/// the two can be made to disagree.
/// </para>
/// <para>
/// 🔒 The player's own profile and run are never served through one of these. That read tolerates
/// no staleness at all — the client has already animated the command it is reading back — so it
/// stays a write-model read on the primary and wears none of this convention.
/// </para>
/// </remarks>
public interface IReadModelQuery<TView>
    where TView : IReadModelView
{
    /// <summary>How stale this port's answer may be — the view's own budget.</summary>
    TimeSpan StalenessBudget => TView.StalenessBudget;

    /// <summary>Where this port's answer is served from. Eventual by construction, so the replica is the default and the primary is the case that must be argued.</summary>
    ReadRouting Routing => ReadRouting.ReplicaEligible;
}
