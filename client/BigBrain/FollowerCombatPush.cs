using EFT;
using friendlySAIN.Utils;
using UnityEngine;
using UnityEngine.AI;

namespace friendlySAIN.BigBrain
{
    /// <summary>
    /// Old-plugin-style push helper extracted for future integration.
    /// Current combat flow is still driven by FollowerCombatDefault.
    /// </summary>
    internal sealed class FollowerCombatPush
    {
        private readonly BotOwner botOwner;
        private readonly FollowerCombatCommon combatCommon;

        public FollowerCombatPush(BotOwner botOwner, FollowerCombatCommon combatCommon)
        {
            this.botOwner = botOwner;
            this.combatCommon = combatCommon;
        }

        /// <summary>
        /// Ported from old plugin EngageEnemy intent: decide direct engage pressure
        /// based on visibility, distance band, and low-threat checks.
        /// </summary>
        public AICoreActionResultStruct<BotLogicDecision, GClass26> EngageEnemy(bool pushOrdered = false, bool enemyLowThreat = false)
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "engageNoEnemy");
            }

            bool enemyVisible = goalEnemy.IsVisible;
            Utils.Enemy.EnemyDistance distanceToEnemy = Utils.Enemy.Distance(botOwner);
            float enemiesAtLocation = enemyLowThreat || string.IsNullOrEmpty(goalEnemy.ProfileId)
                ? 1f
                : Utils.Enemy.GetEnemiesAtLocation(botOwner, goalEnemy, goalEnemy.CurrPosition);

            // Old pusher behavior: push aggressively if ordered or if attack-immediate conditions align.
            if (botOwner.Memory.AttackImmediately || pushOrdered)
            {
                if ((distanceToEnemy <= Utils.Enemy.EnemyDistance.Close && enemiesAtLocation < 2f) ||
                    (pushOrdered && enemiesAtLocation < 4f))
                {
                    BotLogicDecision pushDecision;
                    if (pushOrdered)
                    {
                        pushDecision = BotLogicDecision.runToEnemy;
                    }
                    else if (distanceToEnemy <= Utils.Enemy.EnemyDistance.Close)
                    {
                        pushDecision = BotLogicDecision.goToEnemy;
                    }
                    else
                    {
                        pushDecision = BotLogicDecision.runToEnemy;
                    }

                    if (!Utils.Enemy.IsClosestEnemy(botOwner) && distanceToEnemy <= Utils.Enemy.EnemyDistance.Mid)
                    {
                        pushDecision = BotLogicDecision.goToEnemy;
                    }

                    if (!enemyVisible || pushOrdered)
                    {
                        botOwner.Tactic.SetTactic(BotsGroup.BotCurrentTactic.Attack);
                        return new AICoreActionResultStruct<BotLogicDecision, GClass26>(enemyVisible ? BotLogicDecision.goToEnemy : pushDecision, "pushEnemy");
                    }

                    if (distanceToEnemy >= Utils.Enemy.EnemyDistance.Mid)
                    {
                        CustomNavigationPoint? approachPoint = combatCommon.GetApproachableCover(true);
                        if (approachPoint != null)
                        {
                            botOwner.Memory.BotCurrentCoverInfo.SetCover(approachPoint, true);
                            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.runToCover, "getInCloseFast");
                        }

                        return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.attackMoving, "getInCloseSlow");
                    }

                    if (distanceToEnemy == Utils.Enemy.EnemyDistance.VeryClose)
                    {
                        return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "pushDogFight");
                    }

                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.attackMoving, "pushAdvance");
                }

                // Push wanted but unsafe/imperfect conditions.
                if (enemyVisible)
                {
                    if (botOwner.Memory.IsInCover && botOwner.Memory.CurCustomCoverPoint?.CanIShootToEnemy == true)
                    {
                        return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromCover, "pushShootFromCover");
                    }

                    if (distanceToEnemy <= Utils.Enemy.EnemyDistance.VeryClose)
                    {
                        return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "pushDogFight");
                    }

                    botOwner.Tactic.SetTactic(BotsGroup.BotCurrentTactic.Attack);
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.attackMoving, "pushReposition");
                }

                if (distanceToEnemy <= Utils.Enemy.EnemyDistance.VeryClose)
                {
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.goToEnemy, "pushVeryCloseBlind");
                }

                CustomNavigationPoint? blindApproach = combatCommon.GetApproachableCover(distanceToEnemy > Utils.Enemy.EnemyDistance.Mid);
                if (blindApproach != null)
                {
                    botOwner.Memory.BotCurrentCoverInfo.SetCover(blindApproach, true);
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.runToCover, "getInCloseFast");
                }

                return EnemySearch();
            }

            // Old plugin "intimidation" fallback: maintain pressure from cover or hold lane.
            if (botOwner.Memory.IsInCover && botOwner.Memory.CurCustomCoverPoint?.CanIShootToEnemy == true)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromCover, "pressureShootFromCover");
            }

            if (distanceToEnemy <= Utils.Enemy.EnemyDistance.VeryClose)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "pressureDogFight");
            }

            if (!enemyVisible && Time.time - goalEnemy.PersonalLastSeenTime < Random.Range(2f, 3f))
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "pressureHold");
            }

            if (distanceToEnemy >= Utils.Enemy.EnemyDistance.Mid)
            {
                Vector3 enemyAnchor = GetEnemySearchAnchor(goalEnemy);
                Vector3 centerPosition = (botOwner.Position + enemyAnchor) * 0.5f;
                float radius = distanceToEnemy >= Utils.Enemy.EnemyDistance.Mid ? 120f : 40f;
                CustomNavigationPoint? shootCover = combatCommon.GetClosestShootCover(centerPosition, radius);
                if (shootCover != null)
                {
                    botOwner.Memory.BotCurrentCoverInfo.SetCover(shootCover, true);
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.runToCover, "getInCloseFast");
                }

                return EnemySearch();
            }

            return EnemySearch();
        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26> EnemySearch(string reason = "enemySearch")
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "enemySearchNoEnemy");
            }

            Vector3 enemyAnchor = GetEnemySearchAnchor(goalEnemy);
            Vector3 searchPoint = enemyAnchor;

            // Prefer an approach cover with a clear shot from a nearby tactical point.
            CustomNavigationPoint? approachCover = combatCommon.GetApproachableCover();
            if (approachCover != null)
            {
                searchPoint = approachCover.Position;
            }
            else if (NavMesh.SamplePosition(enemyAnchor, out NavMeshHit hit, 8f, -1))
            {
                ShootPointClass shootPoint = new ShootPointClass(enemyAnchor + Vector3.up * 1.1f, 1f);
                Vector3 firePos = hit.position + Vector3.up * 1.2f;
                if (Utils.Utils.CanShootToTarget(shootPoint, firePos, botOwner.LookSensor.Mask, false))
                {
                    searchPoint = hit.position;
                }
            }

            botOwner.GoToSomePointData.SetPoint(searchPoint);
            botOwner.Tactic.SetTactic(BotsGroup.BotCurrentTactic.Attack);
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.goToPointTactical, reason);
        }

        private static Vector3 GetEnemySearchAnchor(EnemyInfo goalEnemy)
        {
            Vector3 lastKnown = goalEnemy.EnemyLastPositionReal;
            if (!float.IsNaN(lastKnown.x) && !float.IsNaN(lastKnown.y) && !float.IsNaN(lastKnown.z) &&
                !float.IsInfinity(lastKnown.x) && !float.IsInfinity(lastKnown.y) && !float.IsInfinity(lastKnown.z) &&
                lastKnown != Vector3.zero)
            {
                return lastKnown;
            }

            return goalEnemy.CurrPosition;
        }
    }
}
