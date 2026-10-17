namespace SpikeGodotDotnet;

// Spike stub of ADR-0002's TickDriver: Godot-free integer accumulator.
// Advance(timestamp, freq) returns how many 30 Hz ticks to run this frame.
// Drop-not-bank: ticks beyond MaxTicksPerFrame are discarded, not carried.
public sealed class SpikeTickDriver
{
    public const int TickHz = 30;
    public const int MaxTicksPerFrame = 4;

    private long _lastTimestamp = -1;
    private long _accumulator;            // in (timestamp units * TickHz)

    public long TicksRun { get; private set; }
    public long TicksDropped { get; private set; }

    public int Advance(long timestamp, long freq)
    {
        if (_lastTimestamp < 0)
        {
            _lastTimestamp = timestamp;
            return 0;
        }

        long delta = timestamp - _lastTimestamp;
        _lastTimestamp = timestamp;
        if (delta < 0)
        {
            delta = 0;
        }

        if (delta > freq)
        {
            delta = freq; // clamp 1 s
        }

        // One tick = freq / TickHz timestamp units; scale by TickHz to stay integer.
        _accumulator += delta * TickHz;
        long due = _accumulator / freq;
        _accumulator -= due * freq;

        int run = (int)Math.Min(due, MaxTicksPerFrame);
        long dropped = due - run;
        TicksRun += run;
        TicksDropped += dropped;
        return run;
    }
}
