using System;
using System.Collections.Generic;

namespace Unity.Cursory.Core
{
    /// <summary>
    /// Holds the level list and the one level being played (Cursory's <c>CurrentLevel</c>,
    /// <c>SwitchToLevel</c> and <c>ResetPuzzles</c>). The server builds its bodies from <see cref="World"/>, a
    /// fresh copy of the level made on every load and reset, so play never changes the level assets. A load or
    /// reset raises the flags the server reads once per tick to resend the static geometry (BIBLE UC-LAW-4)
    /// and, on a level switch only, to show the level banner.
    /// </summary>
    public sealed class LevelLoader
    {
        private readonly List<LevelData> levels;
        private bool geometryRebroadcast;
        private int levelAnnouncement;

        /// <summary>Takes the levels numbered 1 to N in order, each valid, and loads level 1.</summary>
        /// <exception cref="ArgumentException">The list is empty, out of order, or holds an invalid level; the message lists every problem.</exception>
        public LevelLoader(IEnumerable<LevelData> levels)
        {
            if (levels == null) throw new ArgumentNullException(nameof(levels));
            this.levels = new List<LevelData>();
            var problems = new List<string>();
            foreach (var level in levels)
            {
                var expected = this.levels.Count + 1;
                if (level == null)
                {
                    problems.Add($"Level {expected} is missing.");
                    this.levels.Add(null);
                    continue;
                }
                if (level.Number != expected) problems.Add($"Level {expected} is numbered {level.Number}.");
                foreach (var error in level.Validate()) problems.Add($"Level {level.Number}: {error}");
                this.levels.Add(level.Clone());
            }
            if (this.levels.Count == 0) problems.Add("There are no levels.");
            if (problems.Count > 0) throw new ArgumentException(string.Join(" ", problems), nameof(levels));

            CurrentLevel = 1;
            World = this.levels[0].Clone();
        }

        /// <summary>How many levels there are.</summary>
        public int LevelCount => levels.Count;

        /// <summary>The level being played (1 to <see cref="LevelCount"/>).</summary>
        public int CurrentLevel { get; private set; }

        /// <summary>The live copy of the current level the server simulates. Replaced on every load and reset.</summary>
        public LevelData World { get; private set; }

        /// <summary>True when <paramref name="number"/> names a level.</summary>
        public bool Exists(int number) => number >= 1 && number <= levels.Count;

        /// <summary>A copy of level <paramref name="number"/> as authored.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="number"/> names no level.</exception>
        public LevelData Definition(int number)
        {
            if (!Exists(number)) throw new ArgumentOutOfRangeException(nameof(number), number, $"levels run from 1 to {levels.Count}");
            return levels[number - 1].Clone();
        }

        /// <summary>
        /// Loads level <paramref name="number"/> (clamped to 1 to <see cref="LevelCount"/>, as Cursory did),
        /// resends the geometry and announces the level.
        /// </summary>
        public void Load(int number)
        {
            CurrentLevel = Math.Max(1, Math.Min(levels.Count, number));
            Reset();
            levelAnnouncement = CurrentLevel;
        }

        /// <summary>Puts the current level back to its starting state and resends the geometry; no banner.</summary>
        public void Reset()
        {
            World = levels[CurrentLevel - 1].Clone();
            geometryRebroadcast = true;
        }

        /// <summary>True once after each load or reset: the server resends <see cref="LevelData.Geometry"/> to every client.</summary>
        public bool ConsumeGeometryRebroadcast()
        {
            var pending = geometryRebroadcast;
            geometryRebroadcast = false;
            return pending;
        }

        /// <summary>The level to show the banner for, once after a <see cref="Load"/>; 0 when there is none.</summary>
        public int ConsumeLevelAnnouncement()
        {
            var level = levelAnnouncement;
            levelAnnouncement = 0;
            return level;
        }
    }
}
