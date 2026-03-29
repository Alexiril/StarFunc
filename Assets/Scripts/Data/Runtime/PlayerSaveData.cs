using System;
using System.Collections.Generic;

namespace StarFunc.Data
{
    [Serializable]
    public class PlayerSaveData
    {
        // Versioning
        public int SaveVersion = 1;
        public int Version;
        public long LastModified;

        // Progression
        public Dictionary<string, SectorProgress> SectorProgress = new();
        public Dictionary<string, LevelProgress> LevelProgress = new();
        public int CurrentSectorIndex;

        // Economy
        public int TotalFragments;

        // Lives
        public int CurrentLives;
        public long LastLifeRestoreTimestamp;

        // Shop — owned permanent items (skins, themes)
        public List<string> OwnedItems = new();

        // Consumables
        public Dictionary<string, int> Consumables = new();

        // Statistics
        public int TotalLevelsCompleted;
        public int TotalStarsCollected;
        public float TotalPlayTime;

        /// <summary>
        /// Increments Version and updates LastModified. Call on every mutation
        /// (level completion, purchase, life spend, etc.).
        /// </summary>
        public void IncrementVersion()
        {
            Version++;
            LastModified = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
    }
}
