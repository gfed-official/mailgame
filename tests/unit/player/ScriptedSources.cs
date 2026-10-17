using PerformativeMail.Sim.Players;

namespace PerformativeMail.Sim.Tests.Unit.PlayerCharacter;

/// <summary>
/// Scripted <see cref="IRemoteCapsuleSource"/> double (story 002; Story 010's replay feeds it). <see cref="SetTick"/> copies that tick's
/// script row into a fixed backing array, so the exposed span is stable and independent of the script after the call (the recorder's
/// sample-log behaviour, ADR-0013 section 7). A tick beyond the script, or a negative one, is empty.
/// </summary>
internal sealed class ScriptedRemoteCapsuleSource : IRemoteCapsuleSource
{
    public const int Capacity = 8;

    private readonly RemoteCapsule[][] _script;
    private readonly RemoteCapsule[] _backing = new RemoteCapsule[Capacity];
    private int _count;

    public ScriptedRemoteCapsuleSource(RemoteCapsule[][] script)
    {
        _script = script;
    }

    public ReadOnlySpan<RemoteCapsule> Capsules => _backing.AsSpan(0, _count);

    public void SetTick(int tick)
    {
        _count = 0;
        if ((uint)tick < (uint)_script.Length)
        {
            RemoteCapsule[] row = _script[tick];
            _count = Math.Min(row.Length, Capacity);
            row.AsSpan(0, _count).CopyTo(_backing);
        }
    }
}

/// <summary>One scripted belt lane sample for <see cref="ScriptedSupportSource"/>.</summary>
internal readonly record struct ScriptedBelt(int TileX, int TileZ, int Vx, int Vz, uint SegmentId);

/// <summary>
/// Scripted <see cref="ISupportTransformSource"/> double: per-tick vehicles and belt tiles, copied into fixed backing arrays on
/// <see cref="SetTick"/>.
/// </summary>
internal sealed class ScriptedSupportSource : ISupportTransformSource
{
    public const int Capacity = 8;

    private readonly VehiclePose[][] _vehicleScript;
    private readonly ScriptedBelt[][] _beltScript;
    private readonly VehiclePose[] _vehicles = new VehiclePose[Capacity];
    private readonly ScriptedBelt[] _belts = new ScriptedBelt[Capacity];
    private int _vehicleCount;
    private int _beltCount;

    public ScriptedSupportSource(VehiclePose[][] vehicleScript, ScriptedBelt[][]? beltScript = null)
    {
        _vehicleScript = vehicleScript;
        _beltScript = beltScript ?? [];
    }

    public ReadOnlySpan<VehiclePose> Vehicles => _vehicles.AsSpan(0, _vehicleCount);

    public void SetTick(int tick)
    {
        _vehicleCount = 0;
        _beltCount = 0;
        if ((uint)tick < (uint)_vehicleScript.Length)
        {
            VehiclePose[] row = _vehicleScript[tick];
            _vehicleCount = Math.Min(row.Length, Capacity);
            row.AsSpan(0, _vehicleCount).CopyTo(_vehicles);
        }

        if ((uint)tick < (uint)_beltScript.Length)
        {
            ScriptedBelt[] row = _beltScript[tick];
            _beltCount = Math.Min(row.Length, Capacity);
            row.AsSpan(0, _beltCount).CopyTo(_belts);
        }
    }

    public bool TryGetBeltVelocity(int tileX, int tileZ, out int vx, out int vz, out uint segmentId)
    {
        for (int i = 0; i < _beltCount; i++)
        {
            ScriptedBelt b = _belts[i];
            if (b.TileX == tileX && b.TileZ == tileZ)
            {
                vx = b.Vx;
                vz = b.Vz;
                segmentId = b.SegmentId;
                return true;
            }
        }

        vx = 0;
        vz = 0;
        segmentId = 0;
        return false;
    }
}
