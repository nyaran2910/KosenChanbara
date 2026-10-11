using System;
using UnityEngine;

// Only explicitly published entries appear in the player-facing stage picker.
// Additional scene stages stay available to mode-specific stage selection.
[CreateAssetMenu(menuName = "School Festival/Stage Catalog")]
public sealed class GameStageCatalog : ScriptableObject
{
    [Serializable]
    public sealed class Entry
    {
        public string id = "platform";
        public string title = "水上の広場";
        [TextArea] public string description = "水に囲まれた広場。足もとに気をつけて！";
        [Min(0)] public int sceneStageIndex;
        public Sprite artwork;
    }

    public Entry[] stages = { new Entry() };
    public static GameStageCatalog Load()
    {
        GameStageCatalog catalog = Resources.Load<GameStageCatalog>("GameStageCatalog");
        return catalog != null ? catalog : CreateInstance<GameStageCatalog>();
    }
}
