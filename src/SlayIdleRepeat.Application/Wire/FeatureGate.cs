using System.Globalization;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;

namespace SlayIdleRepeat.Application.Wire;

/// <summary>The host-side kill-switch gate behind 14 §16.2's <c>FEATURE_DISABLED</c>.</summary>
/// <remarks>
/// <para>
/// Asked before dispatch, so a killed feature's command never reaches the domain — the value is
/// transport-tier. It reads the four switches <c>FeatureFlags</c> declares and nothing else, and
/// each arm is the switch's own stated meaning: a killed chapter cannot be started, a killed
/// placement cannot be claimed, PvP offline refuses the three PvP commands.
/// </para>
/// <para>
/// ⚠️ <c>PlusOfferEnabled</c> gates no command here, and that is a named absence rather than a
/// gap: the switch withdraws an OFFER — a storefront surface — and no registry command is "the
/// Plus offer"; the surface that reads it is the shop's, not this gate's. If a purchasable offer
/// id ever maps to the switch, the arm lands here beside the others.
/// </para>
/// </remarks>
public static class FeatureGate
{
    /// <summary>Whether the flags kill this command.</summary>
    /// <param name="command">The typed command.</param>
    /// <param name="flags">The kill switches the composition root resolved.</param>
    /// <returns><c>true</c> to refuse with <c>FEATURE_DISABLED</c>.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static bool IsDisabled(GameCommand command, FeatureFlags flags)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(flags);

        return command switch
        {
            UploadGhostCommand or StartDuelCommand or SubmitDuelCommand => !flags.PvpEnabled,
            ClaimAdRewardCommand claim => !flags.IsAdPlacementEnabled(claim.PlacementId),
            StartRunCommand start =>
                !flags.IsChapterEnabled(start.ChapterId.ToString(CultureInfo.InvariantCulture)),
            _ => false,
        };
    }
}
