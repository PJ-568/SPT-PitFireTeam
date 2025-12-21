using EFT;
using System.Collections.Generic;

namespace friendlySAIN.Utils
{
    internal class SpawnHelper
    {
        public static readonly List<string> spawnMemberIds = new List<string>();

        public static readonly List<WildSpawnType> spawnMemberIdsBoss = new List<WildSpawnType>();

        public static readonly List<string> spawnMemberIdsScav = new List<string>();

        public static bool ScavSquad = false;

        public static int ScavSquadSize = 0;

        public static int Pickups = 0;

        public static bool Restrictions = false;
    }
}
