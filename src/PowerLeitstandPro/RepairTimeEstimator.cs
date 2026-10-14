namespace PowerControlProWpf;

internal sealed class RepairTimeEstimator
{
    private readonly List<(TimeSpan Time, double Percent)> _samples = [];
    internal void Reset() => _samples.Clear();

    internal void Add(TimeSpan elapsed, double percent)
    {
        if (percent <= 0 || percent >= 100) return;
        if (_samples.Count > 0 && percent <= _samples[^1].Percent) return;
        _samples.Add((elapsed, percent));
    }

    internal TimeSpan? Estimate(TimeSpan elapsed)
    {
        if (_samples.Count < 3 || elapsed < TimeSpan.FromSeconds(20) ||
            elapsed - _samples[^1].Time > TimeSpan.FromSeconds(30)) return null;
        var first = _samples[0];
        var last = _samples[^1];
        var seconds = (last.Time - first.Time).TotalSeconds;
        var progress = last.Percent - first.Percent;
        if (seconds < 10 || progress < 5) return null;
        var remainingSeconds = seconds * (100 - last.Percent) / progress;
        return remainingSeconds is > 0 and < 43200 ? TimeSpan.FromSeconds(remainingSeconds) : null;
    }
}
