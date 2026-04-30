using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using UnityEngine;

namespace pitTeam.BigBrain.Actions
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
            if (sprint && BotOwner.Mover.Sprinting) return;
            else if (!sprint && !BotOwner.Mover.Sprinting) return;
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

        protected void StopCombatShooting()
        {
            ShootData? shootData = BotOwner?.ShootData;
            shootData?.EndShoot();

            var shootController = BotOwner?.WeaponManager?.ShootController;
            if (shootController != null)
            {
                shootController.SetTriggerPressed(false);
            }
        }

        protected void TryPreferPrimaryAtRange(EnemyInfo? goalEnemy)
        {
            if (goalEnemy == null)
            {
                return;
            }

            if (BossPlayers.Instance?.GetFollower(BotOwner)?.CombatTactic == FollowerCombatTactic.Marksman)
            {
                return;
            }

            BotWeaponSelector? selector = BotOwner?.WeaponManager?.Selector;
            if (selector == null)
            {
                return;
            }

            if (BotOwner.WeaponManager.IsMelee)
            {
                selector.ChangeToMain();
                return;
            }

            selector.TryChangeToMain();
        }

        protected bool WaitForEnemyAimAlignment(ref float startedAt, float maxAngle = 32f, float timeout = 0.12f)
        {
            EnemyInfo? goalEnemy = BotOwner?.Memory?.GoalEnemy;
            if (goalEnemy?.Person?.HealthController?.IsAlive != true || !goalEnemy.CanShoot)
            {
                startedAt = 0f;
                return false;
            }

            if (startedAt <= 0f)
            {
                startedAt = Time.time;
            }

            Vector3 lookPoint = GetEnemyShootLookPoint(goalEnemy);
            BotOwner.Steering.LookToPoint(lookPoint);

            Vector3 lookOrigin = BotOwner.Transform != null
                ? BotOwner.Transform.position + Vector3.up * 1.2f
                : BotOwner.Position + Vector3.up * 1.2f;
            Vector3 toEnemy = lookPoint - lookOrigin;
            if (toEnemy.sqrMagnitude <= 0.001f)
            {
                return false;
            }

            Vector3 currentLook = BotOwner.LookDirection;
            if (currentLook.sqrMagnitude <= 0.001f && BotOwner.Transform != null)
            {
                currentLook = BotOwner.Transform.forward;
            }

            if (currentLook.sqrMagnitude <= 0.001f)
            {
                return false;
            }

            // Faster than Vector3.Angle for this hot path: compare normalized dot to cosine threshold.
            float denominator = Mathf.Sqrt(currentLook.sqrMagnitude * toEnemy.sqrMagnitude);
            if (denominator <= 0.0001f)
            {
                return false;
            }

            float alignmentDot = Vector3.Dot(currentLook, toEnemy) / denominator;
            float requiredDot = Mathf.Cos(maxAngle * Mathf.Deg2Rad);
            if (alignmentDot >= requiredDot)
            {
                startedAt = 0f;
                return false;
            }

            bool keepWaiting = Time.time - startedAt < timeout;
            if (keepWaiting)
            {
                StopCombatShooting();
                return true;
            }

            startedAt = 0f;
            return false;
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
