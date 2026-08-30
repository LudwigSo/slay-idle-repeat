using SlayIdleRepeat.Application.Wire;

namespace SlayIdleRepeat.Client.Game.Net;

/// <summary>
/// The device credential this process is holding, kept in memory and persisted nowhere.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Not written to <c>user://</c>, not written to any settings file, never rendered.</b> The
/// device secret is the anonymous account's root credential and the only sanctioned home for it is
/// the platform keystore, which this repository does not have yet. Inventing a store here would be
/// filling that hole with a plausible value.
/// </para>
/// <para>
/// 🔴 <b>Consequence, stated rather than hidden:</b> every cold start mints a NEW anonymous account,
/// so nothing done through the wire survives a restart. That is why the arm this belongs to is a
/// developer arm and not a shipped one.
/// </para>
/// <para>
/// No interface, deliberately: a seam with one implementation is a seam nothing checks.
/// </para>
/// </remarks>
public sealed class EphemeralDeviceCredentials
{
    /// <summary>What this says about itself when it is holding nothing.</summary>
    private const string NothingHeld = "no device credential has been registered in this process";

    /// <summary>Whether a credential has been minted in this process.</summary>
    public bool IsHeld => Held is not null;

    /// <summary>The credential held, or <c>null</c> before one has been minted.</summary>
    public WireCredentials? Held { get; private set; }

    /// <summary>Takes the credential a fresh registration issued.</summary>
    /// <param name="registration">What the device route answered.</param>
    /// <exception cref="ArgumentNullException"><paramref name="registration"/> is null.</exception>
    public void Adopt(WireDeviceRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        Held = new WireCredentials(registration.DeviceId, registration.DeviceSecret);
    }

    /// <summary>Says that this holds a secret rather than printing it.</summary>
    /// <remarks>
    /// Delegates to the credential's own rendering rather than assembling a second one, so there is
    /// one place that decides what a stored secret is allowed to look like.
    /// </remarks>
    public override string ToString() => Held is { } held ? held.ToString() : NothingHeld;
}
