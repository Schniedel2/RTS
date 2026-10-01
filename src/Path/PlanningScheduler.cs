using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RTS;

/// <summary>One shared game-thread budget for all incremental navigation jobs.</summary>
public sealed class PlanningScheduler
{
    public const int MaximumStepsPerUpdate = 2048;
    public const int QuantumSteps = 16;
    public const double MaximumMillisecondsPerUpdate = 2.0;
    private readonly Queue<Job> _jobs = new();
    private int _generation;
    public int Generation => _generation;
    private sealed record Job(IEnumerator<int> Work, Func<bool> Valid, Action Complete, Action? Cancelled, int Generation, string MeasurementName);
    public int PendingJobs => _jobs.Count;
    public int LastSteps { get; private set; }
    public double LastMilliseconds { get; private set; }

    public void Enqueue(IEnumerable<int> work, Func<bool> valid, Action complete, Action? cancelled = null,
        string measurementName = "Path.JobSlice") =>
        _jobs.Enqueue(new(work.GetEnumerator(), valid, complete, cancelled, _generation, measurementName));

    public void Reset()
    {
        _generation++;
        while (_jobs.TryDequeue(out Job? job)) { job.Work.Dispose(); job.Cancelled?.Invoke(); }
        LastSteps = 0;
        LastMilliseconds = 0;
    }

    public void Update(int maximumSteps = MaximumStepsPerUpdate, double maximumMilliseconds = MaximumMillisecondsPerUpdate)
    {
        using var measurement = PerformanceMeasurements.Measure("Path.PlanningUpdate");
        LastSteps = 0;
        long started = Stopwatch.GetTimestamp();
        int visited = 0;
        try
        {
            while (_jobs.Count > 0 && LastSteps < maximumSteps &&
                Stopwatch.GetElapsedTime(started).TotalMilliseconds < maximumMilliseconds && visited++ < 256)
            {
                Job job = _jobs.Dequeue();
                if (job.Generation != _generation || !job.Valid()) { job.Work.Dispose(); job.Cancelled?.Invoke(); continue; }
                bool finished = false;
                try
                {
                    using var slice = PerformanceMeasurements.Measure(job.MeasurementName);
                    for (int step = 0; step < QuantumSteps && LastSteps < maximumSteps; step++)
                    {
                        if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= maximumMilliseconds) break;
                        LastSteps++; // Includes initialization, reconstruction, candidates and end-of-job checks.
                        if (!job.Work.MoveNext()) { finished = true; break; }
                    }
                }
                catch { job.Work.Dispose(); throw; }
                if (finished)
                {
                    job.Work.Dispose();
                    if (job.Generation == _generation && job.Valid()) job.Complete();
                    else job.Cancelled?.Invoke();
                }
                else _jobs.Enqueue(job);
            }
        }
        finally { LastMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds; }
    }
}
