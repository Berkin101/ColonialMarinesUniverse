using Content.Shared.DoAfter;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._RMC14.Entrenching;

[Serializable, NetSerializable]
public sealed partial class DirtPileBuildDoAfterEvent : SimpleDoAfterEvent
{
    [DataField(required: true)]
    public NetCoordinates Coordinates;

    [DataField(required: true)]
    public Direction Direction;

    public DirtPileBuildDoAfterEvent(NetCoordinates coordinates, Direction direction)
    {
        Coordinates = coordinates;
        Direction = direction;
    }
}
