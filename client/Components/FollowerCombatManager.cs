using EFT;
using friendlySAIN.Modules;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace friendlySAIN.Components
{
    internal class FollowerCombatManager
    {
        private pitAIBossPlayer boss;

        // Maps enemy ProfileId → the follower BotOwner closest to that enemy (the "party leader" for search).
        private readonly Dictionary<string, BotOwner> _partyLeaderByEnemyId = new Dictionary<string, BotOwner>(System.StringComparer.Ordinal);
        // Ref-count of active GroupSearch locks per enemy. While count > 0 the existing leader is frozen.
        private readonly Dictionary<string, int> _partyLeaderLockCount = new Dictionary<string, int>(System.StringComparer.Ordinal);
        private float _nextLeaderUpdateTime;

        public FollowerCombatManager(pitAIBossPlayer boss)
        {
            this.boss = boss;
        }

        // Attach a MonoBehaviour ticker to the boss player's game object.
        // The component's Update() calls ManualUpdate() every frame, and is
        // destroyed explicitly when the boss is disposed.
        public FollowerCombatManagerTicker AttachTicker(Player bossPlayer)
        {
            return FollowerCombatManagerTicker.Attach(bossPlayer.gameObject, this);
        }

        public void FollowerAdded() { }

        public void FollowerRemoved() { }

        /// <summary>Returns true if <paramref name="bot"/> is the designated search-party leader for <paramref name="enemyProfileId"/>.</summary>
        internal bool IsPartyLeader(BotOwner? bot, string enemyProfileId)
        {
            if (bot == null || string.IsNullOrEmpty(enemyProfileId)) return false;
            return _partyLeaderByEnemyId.TryGetValue(enemyProfileId, out BotOwner? leader)
                && leader != null && leader.ProfileId == bot.ProfileId;
        }

        /// <summary>Returns the designated party leader for <paramref name="enemyProfileId"/> if one is assigned.</summary>
        internal bool TryGetPartyLeader(string enemyProfileId, out BotOwner? leader)
        {
            return _partyLeaderByEnemyId.TryGetValue(enemyProfileId, out leader);
        }

        /// <summary>Prevents the party leader for <paramref name="enemyId"/> from being replaced by the distance-proximity selection until all locks are released.</summary>
        internal void LockPartyLeader(string enemyId)
        {
            if (string.IsNullOrEmpty(enemyId)) return;
            _partyLeaderLockCount.TryGetValue(enemyId, out int count);
            _partyLeaderLockCount[enemyId] = count + 1;
        }

        /// <summary>Releases one lock acquired via <see cref="LockPartyLeader"/>. When the count hits zero the leader can be reassigned.</summary>
        internal void UnlockPartyLeader(string enemyId)
        {
            if (string.IsNullOrEmpty(enemyId)) return;
            if (_partyLeaderLockCount.TryGetValue(enemyId, out int count) && count > 1)
                _partyLeaderLockCount[enemyId] = count - 1;
            else
                _partyLeaderLockCount.Remove(enemyId);
        }

        public void ManualUpdate()
        {
            if (Time.time < _nextLeaderUpdateTime) return;
            _nextLeaderUpdateTime = Time.time + 0.5f;

            bool inCombat = false;

            Dictionary<EnemyInfo, List<BotFollowerPlayer>> squads = new Dictionary<EnemyInfo, List<BotFollowerPlayer>>();

            foreach (BotFollowerPlayer follower in BossPlayers.GetFollowersByBoss(boss.realPlayer.ProfileId))
            {
                BotOwner owner = follower.GetBot();
                if (owner.Memory.HaveEnemy)
                {
                    inCombat = true;
                    EnemyInfo enemy = owner.Memory.GoalEnemy;
                    EnemyInfo key = squads.Keys.FirstOrDefault(x => x.ProfileId == enemy.ProfileId);
                    if (key == null)
                    {
                        key = enemy;
                        squads[key] = new List<BotFollowerPlayer>();
                    }
                    squads[key].Add(follower);
                }
            }

            // Rebuild party-leader assignments: for each enemy group, the closest follower leads.
            _partyLeaderByEnemyId.Clear();
            if (!inCombat) return;

            foreach (var kvp in squads)
            {
                EnemyInfo enemyInfo = kvp.Key;
                Vector3 enemyPos = enemyInfo.CurrPosition;
                BotOwner? closest = null;
                float bestSqrDist = float.MaxValue;

                foreach (BotFollowerPlayer fp in kvp.Value)
                {
                    BotOwner? bot = fp.GetBot();
                    if (bot == null || bot.IsDead) continue;
                    float sqrDist = (bot.Position - enemyPos).sqrMagnitude;
                    if (sqrDist < bestSqrDist) { bestSqrDist = sqrDist; closest = bot; }
                }

                if (closest != null)
                    _partyLeaderByEnemyId[enemyInfo.ProfileId] = closest;
            }

            // Remove stale lock entries for enemies that no longer have any followers engaging them.
            foreach (string lockedId in new System.Collections.Generic.List<string>(_partyLeaderLockCount.Keys))
            {
                if (!squads.Keys.Any(e => e.ProfileId == lockedId))
                    _partyLeaderLockCount.Remove(lockedId);
            }
        }
    }

    internal class FollowerCombatManagerTicker : MonoBehaviour
    {
        private FollowerCombatManager? _manager;

        public static FollowerCombatManagerTicker Attach(GameObject target, FollowerCombatManager manager)
        {
            FollowerCombatManagerTicker ticker = target.AddComponent<FollowerCombatManagerTicker>();
            ticker._manager = manager;
            return ticker;
        }

        private void Update()
        {
            try
            {
                _manager?.ManualUpdate();
            }
            catch (System.Exception ex)
            {
                Modules.Logger.LogError("FollowerCombatManagerTicker Update exception");
                Modules.Logger.LogError(ex);
            }
        }
    }
}
