using System.Diagnostics;

namespace CraftUntilHighlighted;

/// <summary>
/// A moving average of how long a call takes, in microseconds, for debug mode's readout of Tick and Render. Each sample
/// moves the average a twentieth of the way towards itself, so a single slow frame shows without swamping it.
/// </summary>
internal sealed class CallTimeAverage
{
    private const double SampleWeight = 0.05;

    private double _microseconds = -1d;

    /// <summary>The average so far, or negative before the first sample.</summary>
    public double Microseconds => _microseconds;

    /// <summary>Adds the time since startedAt, a Stopwatch.GetTimestamp value.</summary>
    public void AddSince(long startedAt)
    {
        var sample = (Stopwatch.GetTimestamp() - startedAt) * 1_000_000d / Stopwatch.Frequency;

        _microseconds = _microseconds < 0d ? sample : _microseconds + (sample - _microseconds) * SampleWeight;
    }
}
