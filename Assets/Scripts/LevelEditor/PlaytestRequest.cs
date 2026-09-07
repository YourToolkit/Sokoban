#if UNITY_EDITOR
using System;
using Sokoban.Core;

namespace Sokoban.Runtime
{
    public static class PlaytestRequest
    {
        public static LevelDefinition Pending;
        public static event Action ExitRequested;
        public static bool TryConsume(out LevelDefinition definition)
        {
            definition = Pending?.DeepClone(); Pending = null; return definition != null;
        }
        public static void RequestExit() => ExitRequested?.Invoke();
    }
}

#endif
