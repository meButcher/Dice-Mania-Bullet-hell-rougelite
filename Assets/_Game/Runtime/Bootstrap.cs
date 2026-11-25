using DiceMania.Game;
using UnityEngine;

namespace DiceMania
{
    /// <summary>
    /// Boots the game in any scene: no prefabs or scene setup required.
    /// (Assets/_Game/Scenes/Main.unity exists only so builds have a scene to load.)
    /// </summary>
    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindAnyObjectByType<GameDirector>() != null) return;
            var go = new GameObject("DiceMania");
            go.AddComponent<GameDirector>();
        }
    }
}
