using Godot;

namespace SpikeAdr0019;

public static class Util
{
    public static bool HasArg(string a)
    {
        foreach (string s in OS.GetCmdlineUserArgs())
        {
            if (s == a) { return true; }
        }
        return false;
    }

    public static string? ArgValue(string key)
    {
        foreach (string s in OS.GetCmdlineUserArgs())
        {
            if (s.StartsWith(key + "=", System.StringComparison.Ordinal)) { return s.Substring(key.Length + 1); }
        }
        return null;
    }

    public static void Log(string msg)
    {
        GD.Print("[oq1] f=" + Engine.GetProcessFrames() + " " + msg);
    }
}
