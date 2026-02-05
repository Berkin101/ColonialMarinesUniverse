using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.Entrenching;

[RegisterComponent, NetworkedComponent]
[Access(typeof(BarricadeSystem))]
public sealed partial class BarricadeDirtComponent : Component
{
    /// <summary>
    /// Optionally set stackable material received upon deconstruction.
    /// </summary>
    [DataField]
    public EntProtoId Material = "dirtPile";

    /// <summary>
    /// Optional maximum amount of material received upon deconstruction.
    /// </summary>
    [DataField]
    public int MaxMaterial = 10;

    /// <summary>
    /// Optionally set damage interval at which material is lost.
    /// </summary>
    [DataField]
    public int MaterialLossDamageInterval = 75;
}
