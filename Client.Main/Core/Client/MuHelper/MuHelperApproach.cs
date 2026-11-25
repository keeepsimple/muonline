namespace Client.Main.Core.Client
{
    /// <summary>
    /// Detects a MU Helper approach (walking to a drop or a target) that makes no progress, e.g. when no path leads
    /// there: <see cref="IsStuck"/> turns true once the hero has not got closer to the same id for <see cref="TimeoutMs"/>.
    /// </summary>
    public sealed class MuHelperApproach
    {
        public const double TimeoutMs = 4000;
        private const float MinProgress = 0.5f;

        private bool _tracking;
        private ushort _id;
        private float _bestDistance;
        private double _sinceMs;

        public bool IsStuck(ushort id, float distance, double nowMs)
        {
            if (!_tracking || id != _id || distance < _bestDistance - MinProgress)
            {
                _tracking = true;
                _id = id;
                _bestDistance = distance;
                _sinceMs = nowMs;
                return false;
            }

            return nowMs - _sinceMs >= TimeoutMs;
        }

        public void Reset() => _tracking = false;
    }
}
