using System;

namespace RTS;

/// <summary>Measures cumulative objective progress, rather than command refreshes or motion in circles.</summary>
public sealed class AIProgressWatch
{
    private string? _objective;
    private float _best = float.NegativeInfinity;
    public float SecondsWithoutProgress { get; private set; }
    public bool MadeProgress { get; private set; }

    public bool Update(string objective, float progress, float elapsed, float timeout, float minimumGain = 0.01f)
    {
        MadeProgress = false;
        if (!float.IsFinite(elapsed) || elapsed < 0 || !float.IsFinite(progress)) return false;
        if (_objective != objective)
        {
            _objective = objective;
            _best = progress;
            SecondsWithoutProgress = 0;
            return true;
        }
        if (progress >= _best + minimumGain)
        {
            _best = progress;
            SecondsWithoutProgress = 0;
            MadeProgress = true;
        }
        else SecondsWithoutProgress += elapsed;
        return SecondsWithoutProgress < timeout;
    }

    public void Reset()
    {
        _objective = null;
        _best = float.NegativeInfinity;
        SecondsWithoutProgress = 0;
        MadeProgress = false;
    }

    /// <summary>Give recovery time without forgetting the best position reached before the stall.</summary>
    public void RestartDeadline() => SecondsWithoutProgress = 0;
}
