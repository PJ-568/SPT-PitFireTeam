using EFT;
using friendlySAIN.Utils;
using UnityEngine;

namespace friendlySAIN.BigBrain
{
    /// <summary>
    /// Old-plugin-style push helper extracted for future integration.
    /// Current combat flow is still driven by FollowerCombatDefault.
    /// </summary>
    internal sealed class FollowerCombatPush
    {
        private const string PushReasonPrefix = "push.";

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
            Utils.Enemy.EnemyDistance distanceToEnemy = Utils.Enemy.Distance(goalEnemy);
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
                        pushDecision = botOwner.CanSprintPlayer
                            ? BotLogicDecision.runToEnemy
                            : BotLogicDecision.goToEnemy;
                    }
                    else if (distanceToEnemy <= Utils.Enemy.EnemyDistance.Close)
                    {
                        pushDecision = BotLogicDecision.goToEnemy;
                    }
                    else
                    {
                        pushDecision = botOwner.CanSprintPlayer
                            ? BotLogicDecision.runToEnemy
                            : BotLogicDecision.goToEnemy;
                    }

                    if (!Utils.Enemy.IsClosestEnemy(botOwner) && distanceToEnemy <= Utils.Enemy.EnemyDistance.Mid)
                    {
                        pushDecision = BotLogicDecision.goToEnemy;
                    }

                    if (!enemyVisible || pushOrdered)
                    {
                        SetAttackTactic();
                        BotLogicDecision moveDecision = enemyVisible ? BotLogicDecision.goToEnemy : pushDecision;
                        return CreatePushDecision(moveDecision);
                    }

                    if (distanceToEnemy >= Utils.Enemy.EnemyDistance.Mid)
                    {
                        CustomNavigationPoint? approachPoint = combatCommon.GetApproachableCover(true);
                        if (TryCreateApproachCoverDecision(approachPoint, out AICoreActionResultStruct<BotLogicDecision, GClass26> approachDecision))
                        {
                            return approachDecision;
                        }

                        return CreatePushDecision(BotLogicDecision.attackMoving);
                    }

                    if (distanceToEnemy == Utils.Enemy.EnemyDistance.VeryClose)
                    {
                        return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "pushDogFight");
                    }

                    return CreatePushDecision(BotLogicDecision.attackMoving);
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

                    SetAttackTactic();
                    return CreatePushDecision(BotLogicDecision.attackMoving);
                }

                if (distanceToEnemy <= Utils.Enemy.EnemyDistance.VeryClose)
                {
                    return CreatePushDecision(BotLogicDecision.goToEnemy);
                }

                CustomNavigationPoint? blindApproach = combatCommon.GetApproachableCover(distanceToEnemy > Utils.Enemy.EnemyDistance.Mid);
                if (TryCreateApproachCoverDecision(blindApproach, out AICoreActionResultStruct<BotLogicDecision, GClass26> blindApproachDecision))
                {
                    return blindApproachDecision;
                }

                return combatCommon.EnemySearch("push.search");
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
                Vector3 enemyAnchor = FollowerCombatCommon.GetEnemyAnchor(goalEnemy);
                Vector3 centerPosition = (botOwner.Position + enemyAnchor) * 0.5f;
                float radius = distanceToEnemy >= Utils.Enemy.EnemyDistance.Mid ? 120f : 40f;
                CustomNavigationPoint? shootCover = combatCommon.GetClosestShootCover(centerPosition, radius);
                if (TryCreateApproachCoverDecision(shootCover, out AICoreActionResultStruct<BotLogicDecision, GClass26> shootCoverDecision))
                {
                    return shootCoverDecision;
                }

                return combatCommon.EnemySearch("push.search");
            }

            return combatCommon.EnemySearch("push.search");
        }


        private bool TryCreateApproachCoverDecision(
            CustomNavigationPoint? cover,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (cover == null)
            {
                return false;
            }

            combatCommon.AssignCover(cover);
            decision = CreatePushDecision(BotLogicDecision.runToCover);
            return true;
        }

        private static AICoreActionResultStruct<BotLogicDecision, GClass26> CreatePushDecision(BotLogicDecision action)
        {
            string suffix = action switch
            {
                BotLogicDecision.runToEnemy => "run",
                BotLogicDecision.goToEnemy => "goToEnemy",
                BotLogicDecision.attackMoving => "attackMoving",
                BotLogicDecision.attackMovingWithSuppress => "attackMovingSuppress",
                BotLogicDecision.runToCover => "runToCover",
                BotLogicDecision.goToPointTactical => "search",
                _ => action.ToString(),
            };

            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(action, $"{PushReasonPrefix}{suffix}");
        }

        private void SetAttackTactic()
        {
            if (botOwner.Tactic.ShallReturnToAttack)
            {
                botOwner.Tactic.ShallReturnToAttack = false;
                botOwner.Tactic.ReturnToAttackTime = 0f;
            }

            botOwner.Tactic.SetTactic(BotsGroup.BotCurrentTactic.Attack);
        }

    }
}
