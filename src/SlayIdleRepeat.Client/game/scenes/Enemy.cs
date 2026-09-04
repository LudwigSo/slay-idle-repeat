using Godot;
using SlayIdleRepeat.Client.Game.Presenters;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// One enemy on the battle stage: the template every catalogue model is instanced under.
/// </summary>
/// <remarks>
/// <para>
/// The models ship facing +Z, the way glTF faces, and an actor rig looks along its own −Z, so the
/// model is turned by <see cref="ModelYawDegrees"/> to look the way its rig does. An export rather
/// than a constant for the reason every other number on the battle is — see the banner on
/// <c>BattleReplay.tscn</c>.
/// </para>
/// <para>
/// 🔒 An enemy the catalogue has no model for is drawn as a bare capsule named <c>Unknown</c>: a
/// visible absence rather than a stand-in borrowed from another row, so a missing model reads as
/// missing on screen and never as the wrong creature.
/// </para>
/// </remarks>
public partial class Enemy : Node3D
{
    /// <summary>Where this scene lives, for the world that instantiates it.</summary>
    public const string ScenePath = "res://game/scenes/Enemy.tscn";

    /// <summary>The child <see cref="Dress"/> replaces, so the next dressing can find the last.</summary>
    private const string ModelName = "Model";

    /// <summary>What the capsule standing in for a missing model is called.</summary>
    private const string UnknownName = "Unknown";

    /// <summary>How far the model is turned about the vertical to look along the rig's −Z.</summary>
    [Export] public float ModelYawDegrees { get; set; } = 180f;

    /// <summary>The colour every engine paints a missing asset in, so the capsule reads as an absence at a glance.</summary>
    private static readonly Color PlaceholderColour = new(1f, 0f, 1f);

    /// <summary>How tall the dressed model stands, so a plate can sit above it.</summary>
    public float Height { get; private set; }

    /// <summary>
    /// Puts a model on this enemy, replacing whatever it wore. Null dresses it as <c>Unknown</c>.
    /// </summary>
    /// <param name="model">The catalogue's model, or null when the catalogue has none.</param>
    public void Dress(EnemyModel? model)
    {
        if (GetNodeOrNull(ModelName) is { } worn)
        {
            // Removed before it is freed: QueueFree leaves the node in the tree until the end of
            // the frame, so the replacement would collide with a name that is still taken.
            RemoveChild(worn);
            worn.QueueFree();
        }

        var dressed = Body(model);

        dressed.Name = ModelName;
        dressed.RotationDegrees = new Vector3(0f, ModelYawDegrees, 0f);

        AddChild(dressed);
    }

    private Node3D Body(EnemyModel? model)
    {
        if (model is not null)
        {
            if (GD.Load<PackedScene>(model.ScenePath) is { } scene)
            {
                Height = model.HeightUnits;

                return scene.Instantiate<Node3D>();
            }

            GD.PushError(
                $"The enemy model '{model.ScenePath}' could not be loaded, so this enemy is drawn " +
                "as a bare capsule named Unknown.");
        }

        var capsule = new CapsuleMesh
        {
            Material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = PlaceholderColour,
            },
        };

        Height = capsule.Height;

        var body = new Node3D();

        body.AddChild(new MeshInstance3D
        {
            Name = UnknownName,
            Mesh = capsule,
            Position = new Vector3(0f, capsule.Height / 2f, 0f),
        });

        return body;
    }
}
