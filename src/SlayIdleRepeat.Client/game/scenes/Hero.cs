using Godot;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// The hero diorama: a modelled rogue, and the two fists a weapon can be hung off.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The weapons are no longer part of the character.</b> <c>chr_hero_rogue.glb</c> used to
/// carry two daggers welded into its mesh, which meant a hero holding two daggers was the only
/// hero there could ever be — a different weapon needed a different character export. The daggers
/// are gone from the body and the model now carries two empties instead, <c>Socket_HandL</c> and
/// <c>Socket_HandR</c>, which glTF stores as ordinary nodes and the importer rebuilds as
/// <see cref="Node3D"/>s. A weapon is a separate scene parented to one of them.
/// </para>
/// <para>
/// 🔒 <b>A socket says where a fist is and nothing else.</b> It carries no side lean, and that is
/// a decision rather than an omission: a lean that throws a point-up sword away from the body
/// throws a point-down dagger into the leg, because turning a weapon over reverses which way the
/// lean carries its tip. So the pose that says "held point-up" or "held reversed" belongs to the
/// weapon, and lives on the root node of the weapon's own scene — see <c>WpnSword.tscn</c> and
/// <c>WpnDagger.tscn</c>. Swapping a weapon is then swapping one <see cref="PackedScene"/>, with
/// nothing here needing to know what a sword is.
/// </para>
/// <para>
/// ⚠️ <b>The sockets are inside the imported model, so they inherit its framing.</b> This scene
/// keeps <c>Model</c> at identity — feet on the origin, unturned, authored size, the convention
/// every enemy model shares — and each screen frames its own instance (Home scales, turns and
/// drops it; the board only scales it). A weapon authored at the model's own scale therefore
/// lands at the right size on every screen without a second number anywhere, and reframing an
/// instance reframes what it holds, which is the point.
/// </para>
/// <para>
/// ⚠️ <b>Nothing here is metallic, and nothing in a weapon may be either.</b> The build lights 3D
/// with one directional key, one fill and flat ambient on <see cref="AppRoot"/>, and has no
/// reflection probe and no sky — a true metal has nothing to reflect and renders black. The blades
/// and the brass are bright albedo at low roughness. A weapon added later inherits that constraint
/// whether or not it wants to.
/// </para>
/// <para>
/// 🔴 <b>Still not a RULED asset.</b> The kit carries no outline shell, no per-actor light rig,
/// and no row in <c>asset_manifest_art.json</c>, for the same three reasons the hero itself does
/// not — see the remarks on <see cref="Home"/>. Adding weapons does not discharge any of them.
/// </para>
/// </remarks>
public partial class Hero : Node3D
{
    /// <summary>Where this scene lives, for the screens that instantiate it.</summary>
    public const string ScenePath = "res://game/scenes/Hero.tscn";

    /// <summary>The empty in the model marking the hero's left fist.</summary>
    /// <remarks>
    /// Named by the exporter, not by this scene: these two strings are the seam between the
    /// <c>.blend</c> and the build, and <c>HeroWeaponKitTests</c> reads the shipped
    /// <c>.glb</c> to check they still name nodes that are in it. A rebake that dropped or
    /// renamed a socket would otherwise disarm every weapon in the game in perfect silence.
    /// </remarks>
    internal const string LeftSocketPath = "Model/Socket_HandL";

    /// <inheritdoc cref="LeftSocketPath"/>
    internal const string RightSocketPath = "Model/Socket_HandR";

    /// <summary>
    /// What a mounted weapon is called once it is in a socket, so the next mount can find it.
    /// </summary>
    private const string MountedName = "Weapon";

    /// <summary>Which fist a weapon is held in.</summary>
    public enum Hand
    {
        /// <summary>The hero's left, and the off hand.</summary>
        Left,

        /// <summary>The hero's right, and the main hand.</summary>
        Right,
    }

    /// <summary>The weapon scene the off hand starts holding, or null for an empty hand.</summary>
    /// <remarks>
    /// Exported rather than hard-coded so the starting loadout is a property of the scene, which
    /// is the one place the choice is visible. A screen that wants a different one calls
    /// <see cref="Equip"/>; it does not need its own copy of this scene.
    /// </remarks>
    [Export]
    public PackedScene? LeftHandWeapon { get; set; }

    /// <inheritdoc cref="LeftHandWeapon"/>
    [Export]
    public PackedScene? RightHandWeapon { get; set; }

    /// <inheritdoc/>
    public override void _Ready()
    {
        Equip(Hand.Left, LeftHandWeapon);
        Equip(Hand.Right, RightHandWeapon);
    }

    /// <summary>
    /// Puts a weapon in one fist, replacing whatever was in it.
    /// </summary>
    /// <param name="hand">The fist to mount into.</param>
    /// <param name="weapon">The weapon scene, or null to leave the hand empty.</param>
    public void Equip(Hand hand, PackedScene? weapon)
    {
        if (Socket(hand) is not { } socket)
        {
            return;
        }

        if (socket.GetNodeOrNull(MountedName) is { } held)
        {
            // Removed before it is freed, and the order is the whole point. QueueFree leaves a
            // node in the tree until the end of the frame, so mounting the replacement first
            // would collide with a name that is still taken — the engine would quietly rename the
            // new weapon, and the next call would find and free the corpse instead of the weapon
            // actually being held.
            socket.RemoveChild(held);
            held.QueueFree();
        }

        if (weapon is null)
        {
            return;
        }

        var mounted = weapon.Instantiate<Node3D>();
        mounted.Name = MountedName;
        socket.AddChild(mounted);
    }

    /// <summary>Empties one fist.</summary>
    /// <param name="hand">The fist to clear.</param>
    public void Unequip(Hand hand) => Equip(hand, null);

    /// <summary>Whether a fist is currently holding anything.</summary>
    /// <param name="hand">The fist to ask about.</param>
    /// <returns><c>true</c> when a weapon is mounted in that hand.</returns>
    public bool IsArmed(Hand hand) => Socket(hand)?.GetNodeOrNull(MountedName) is not null;

    /// <summary>
    /// The socket node for a hand, or null with the reason pushed.
    /// </summary>
    /// <remarks>
    /// Reported rather than thrown, for the same reason <see cref="ScreenStage"/> gives: a missing
    /// socket means the hero draws unarmed, which is wrong but watchable, and taking the
    /// application down over a node lookup on a decorative diorama is worse than saying so.
    /// </remarks>
    private Node3D? Socket(Hand hand)
    {
        if (!IsInsideTree())
        {
            return null;
        }

        var path = hand == Hand.Left ? LeftSocketPath : RightSocketPath;
        var socket = GetNodeOrNull<Node3D>(path);

        if (socket is null)
        {
            GD.PushError(
                $"The hero has no '{path}', so nothing can be put in that hand and it will draw " +
                "empty. The sockets come out of chr_hero_rogue.glb, which is exported from " +
                "assets/source/hero_rogue.blend by hero_kit.sockets() — a rebake that dropped " +
                "them takes every weapon in the game with it.");
        }

        return socket;
    }
}
