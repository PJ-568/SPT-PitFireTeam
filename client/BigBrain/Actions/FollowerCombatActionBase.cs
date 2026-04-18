using DrakiaXYZ.BigBrain.Brains;
using EFT;
using System;
using UnityEngine;

namespace friendlySAIN.BigBrain.Actions
{
    internal sealed class FollowerCombatActionData : CustomLayer.ActionData
    {
        public BotLogicDecision Decision { get; }
        public string Reason { get; }
        public GClass26? Data { get; }

        public FollowerCombatActionData(BotLogicDecision decision, string reason, GClass26? data)
        {
            Decision = decision;
            Reason = reason;
            Data = data;
        }
    }

    internal abstract class FollowerCombatActionBase : CustomLogic
    {
        protected FollowerCombatActionBase(BotOwner botOwner) : base(botOwner)
        {
        }

        protected void SetCombatSprint(bool sprint, bool withDebugCallback = false)
        {
            if (sprint)
            {
                BotOwner.SetPose(1f);
                BotOwner.SetTargetMoveSpeed(1f);
            }

            // Use the mover directly for follower combat run actions. BotOwner.Sprint(true)
            // drops current aiming target every tick, which can fight combat steering and turn
            // a run decision into a walk-looking movement state.
            BotOwner.Mover.Sprint(sprint, withDebugCallback);
        }

        protected CoverSearchType SetAttackCoverSearchType(CoverShootType shootType)
        {
            SetCombatCoverTactic(BotsGroup.BotCurrentTactic.Attack);
            return BotOwner.Tactic.SubTactic.SearchTypeAttack(shootType);
        }

        protected void SetCombatCoverTactic(BotsGroup.BotCurrentTactic tactic)
        {
            if (BotOwner.Tactic.ShallReturnToAttack && tactic != BotsGroup.BotCurrentTactic.Ambush)
            {
                BotOwner.Tactic.ShallReturnToAttack = false;
                BotOwner.Tactic.ReturnToAttackTime = 0f;
            }

            BotOwner.Tactic.SetTactic(tactic);
        }

        protected static GClass26? GetRawData(CustomLayer.ActionData data)
        {
            return (data as FollowerCombatActionData)?.Data;
        }

        protected static string? GetReason(CustomLayer.ActionData data)
        {
            return (data as FollowerCombatActionData)?.Reason;
        }

        protected static TData? GetData<TData>(CustomLayer.ActionData data) where TData : GClass26
        {
            return GetRawData(data) as TData;
        }

        protected bool WaitForEnemyAimAlignment(ref float startedAt, ref string? enemyProfileId, float maxAngle = 18f, float timeout = 0.65f)
        {
            EnemyInfo? goalEnemy = BotOwner?.Memory?.GoalEnemy;
            if (goalEnemy?.Person?.HealthController?.IsAlive != true || !goalEnemy.CanShoot)
            {
                startedAt = 0f;
                enemyProfileId = null;
                return false;
            }

            string currentEnemyId = goalEnemy.ProfileId ?? string.Empty;
            if (!string.Equals(enemyProfileId, currentEnemyId, StringComparison.Ordinal))
            {
                startedAt = Time.time;
                enemyProfileId = currentEnemyId;
            }
            else if (startedAt <= 0f)
            {
                startedAt = Time.time;
            }

            Vector3 lookPoint = GetEnemyShootLookPoint(goalEnemy);
            BotOwner.Steering.LookToPoint(lookPoint);

            Vector3 firePos = BotOwner.WeaponRoot != null
                ? BotOwner.WeaponRoot.position
                : BotOwner.Position + Vector3.up * 1.2f;
            Vector3 toEnemy = lookPoint - firePos;
            if (toEnemy.sqrMagnitude <= 0.001f)
            {
                return false;
            }

            Vector3 currentLook = BotOwner.WeaponRoot != null
                ? BotOwner.WeaponRoot.forward
                : BotOwner.LookDirection;
            if (currentLook.sqrMagnitude <= 0.001f && BotOwner.Transform != null)
            {
                currentLook = BotOwner.Transform.forward;
            }

            if (currentLook.sqrMagnitude <= 0.001f)
            {
                return false;
            }

            float angle = Vector3.Angle(currentLook.normalized, toEnemy.normalized);
            if (angle <= maxAngle)
            {
                return false;
            }

            return Time.time - startedAt < timeout;
        }

        private Vector3 GetEnemyShootLookPoint(EnemyInfo goalEnemy)
        {
            ShootPointClass? shootPoint = BotOwner.CurrentEnemyTargetPosition(false);
            if (shootPoint != null)
            {
                return shootPoint.Point;
            }

            return goalEnemy.GetBodyPartPosition();
        }
    }
}
