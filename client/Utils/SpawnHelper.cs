using EFT;
using System.Collections.Generic;

namespace friendlySAIN.Utils
{
    internal class SpawnHelper
    {
        public const int DefaultPickups = 3;
        public const bool DefaultRestrictions = false;

        public static readonly List<string> spawnMemberIds = new List<string>();

        public static readonly List<WildSpawnType> spawnMemberIdsBoss = new List<WildSpawnType>();

        public static readonly List<string> spawnMemberIdsScav = new List<string>();

        public static bool ScavSquad = false;

        public static int ScavSquadSize = 0;

        public static int Pickups = DefaultPickups;

        public static bool Restrictions = DefaultRestrictions;

        // In no-backend mode, ensure recruit decisions have usable defaults.
        public static void EnsureRecruitDefaults()
        {
            if (Pickups < 1)
            {
                Pickups = DefaultPickups;
            }
        }
    }
}
