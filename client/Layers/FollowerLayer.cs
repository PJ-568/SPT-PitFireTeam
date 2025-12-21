using EFT;
using EFT.InventoryLogic;
using UnityEngine;

using friendlySAIN.Modules;
using friendlySAIN.Brains;
using friendlySAIN.Layers.Tactics;
using friendlySAIN.Components;

using StandardBrain = GClass26;

namespace friendlySAIN.Layers
{
    /**
     * Overwrite of the PatrolAssault layer to make our followers follow the player.
     */
    public class FollowerLayer : GClass130
    {


        protected CustomNavigationPoint customNavigationPoint_0;

        protected bool _triedToSwitchToMain = false;

        protected bool _triedFillMagazines = false;

        protected bool _tryReloadSec = false;
        protected bool _triedToReloadSecondary = false;

        protected float float_3 = 0f;
        protected float float_5 = 0f;

        protected float float_6 = 0f;

        protected float float_4 = 0f;

        protected FollowerCommonLayer commonLayer;

        public FollowerLayer(BotOwner bot, int priority) : base(bot, priority)
        {
            float_5 = Time.time + 60f;
            float_3 = Time.time + 60f;
            commonLayer = new FollowerCommonLayer(bot, priority);
        }
        public override void OnActivate()
        {
            base.OnActivate();
            commonLayer?.OnActivate();
        }
        public override void Dispose()
        {
            base.Dispose();
            commonLayer?.Dispose();
        }
        public override bool ShallUseNow()
        {
            botOwner_0.PriorityAxeTarget.FindTarget();
            var brain = botOwner_0.Brain.BaseBrain as FollowerBrain;

            bool shouldUse = false;

            if (brain == null) return false;

            bool hasEnemy = botOwner_0.Memory.HaveEnemy;

            if (brain.UnderFire && !hasEnemy)
                shouldUse = true;
            else if (!hasEnemy)
                shouldUse = HasBoss() && !InteractableObjects.IsTaker(botOwner_0) && !InteractableObjects.IsOpener(botOwner_0);

            if (!shouldUse)
            {
                _triedToSwitchToMain = false;
                _triedFillMagazines = false;
                _triedToReloadSecondary = false;
                _tryReloadSec = false;

                float_6 = Time.time + 5f;
                float_4 = Time.time + 10f;

            }

            return shouldUse;
        }

        public override string Name()
        {
            return "FLBPlayer";
        }
        protected virtual bool HasBoss()
        {
            return botOwner_0.BotFollower.HaveBoss;
        }

        protected virtual pitAIBossPlayer GetBoss()
        {
            return (pitAIBossPlayer)botOwner_0.BotFollower.BossToFollow;
        }

        protected virtual Vector3 GetBossPosition()
        {
            return HasBoss() ? GetBoss().Position : botOwner_0.GetPlayer.Transform.position;
        }

        public override AICoreActionResultStruct<BotLogicDecision, StandardBrain> GetDecision()
        {

            AICoreActionResultStruct<BotLogicDecision, StandardBrain>? aicoreActionResultStruct = commonLayer.NeedHeal(out customNavigationPoint_0);

            if (aicoreActionResultStruct != null)
            {
                return (AICoreActionResultStruct<BotLogicDecision, StandardBrain>)aicoreActionResultStruct;
            }

            var brain = botOwner_0.Brain.BaseBrain as FollowerBrain;

            if (brain != null && brain.UnderFire && !botOwner_0.Memory.IsInCover)
            {
                GetCoverPoint(botOwner_0.GetPlayer.Transform.position, 50f);
                if (customNavigationPoint_0 != null)
                {
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.runToCover, "runHide");
                }
            }

            // start auto healing process when not in combat
            if (botOwner_0.Medecine.FirstAid.Using || botOwner_0.Medecine.SurgicalKit.Using)
            {
                float_4 = Time.time + 10f;
            }
            else
            {
                AICoreActionResultStruct<BotLogicDecision, StandardBrain> lastDecision = botOwner_0.Brain.Agent.LastResult();

                if (lastDecision.Action == BotLogicDecision.heal)
                {
                    float_4 = Time.time + 10f;
                }
            }
            // - disabled for now
            /* if (float_4 < Time.time)
             {
                 float_4 = Time.time + 5f;
                 try
                 {
                     Player player = botOwner_0.GetPlayer;

                     List<EBodyPart> damagedParts = new List<EBodyPart>();

                     foreach (var part in GClass3058.RealBodyParts)
                     {
                         if (!player.ActiveHealthController.GetBodyPartHealth(part, false).AtMaximum) damagedParts.Add(part);
                     }

                     EBodyPart randomPart = damagedParts.Random();

                     if (damagedParts.Count > 0)
                     {
                         if(player.ActiveHealthController.IsBodyPartBroken(randomPart)) player.ActiveHealthController.RemoveNegativeEffects(randomPart);
                         if (player.ActiveHealthController.IsBodyPartDestroyed(randomPart)) player.ActiveHealthController.RestoreBodyPart(randomPart, 0);
                        player.ActiveHealthController.ChangeHealth(randomPart, 5f, GClass3051.StimulatorUse);
                     }
                 }
                 catch (Exception e)
                 {
                     Modules.Logger.LogError(e);
                 }
             }*/

            if (botOwner_0.SmokeGrenade.IsInSmoke)
            {
                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.goToCoverPoint, "PeaceSmoke");
            }
            if (botOwner_0.PeaceHardAim.HaveActions())
            {
                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.peaceHardAim, "PeaceHardAi");
            }
            if (botOwner_0.PeaceLook.HaveActions())
            {
                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.peaceLook, "PeaceLook");
            }

            if (!HasBoss())
            {
                if (botOwner_0.FriendlyTilt.HaveActions())
                {
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.friendlyTilt, "FriendlyTil");
                }
                if (botOwner_0.EatDrinkData.HaveActions())
                {
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.eatDrink, "EatDrinkDat");
                }
                if (botOwner_0.Gesture.HaveRequest())
                {
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.gesture, "Gesture");
                }
                if (botOwner_0.PeacefulActions.HaveActions())
                {
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.peaceful, "Peaceful");
                }
            }

            botOwner_0.PatrollingData.SetTargetMoveSpeed();
            botOwner_0.PatrollingData.PointChooser.ShallChangeWay(false);


            if (botOwner_0.PatrollingData.Way == null)
            {
                botOwner_0.PatrollingData.PointChooser.ChooseStartWay();
            }

            PatrolWay way = botOwner_0.PatrollingData.Way;


            if (float_6 < Time.time && botOwner_0.WeaponManager.IsWeaponReady && !botOwner_0.WeaponManager.Reload.Reloading)
            {
                // try to reload secondary weapon when out of combat
                if (
                    !_triedToReloadSecondary &&
                    botOwner_0.WeaponManager.Selector.CanChangeToSecondWeapons &&
                    botOwner_0.WeaponManager.Selector.SecondPrimaryWeaponItem != null &&
                    botOwner_0.WeaponManager.Selector.LastEquipmentSlot != EquipmentSlot.SecondPrimaryWeapon &&
                    botOwner_0.WeaponManager.Selector.SecondPrimaryWeaponItem.GetCurrentMagazine() != null &&
                    botOwner_0.WeaponManager.Selector.SecondPrimaryWeaponItem.GetCurrentMagazine().MaxCount != (botOwner_0.WeaponManager.Selector.SecondPrimaryWeaponItem as Weapon).GetCurrentMagazineCount()
                )
                {
                    botOwner_0.WeaponManager.Selector.TryChangeWeapon(true);

                    _triedToReloadSecondary = true;
                    _tryReloadSec = true;

                    float_5 = Time.time + 5f;
                    float_3 = Time.time + 5f;
                }

                // try switch to main weapon when out of combat - useful for bots that have launchers or shotguns as secondary weapon
                if (
                    !_tryReloadSec && !_triedToSwitchToMain &&
                    (botOwner_0.WeaponManager.Selector.LastEquipmentSlot == EquipmentSlot.SecondPrimaryWeapon ||
                     botOwner_0.WeaponManager.Selector.LastEquipmentSlot == EquipmentSlot.Holster)
                )
                {
                    botOwner_0.WeaponManager.Selector.TryChangeToMain();
                    float_5 = Time.time + 5f;
                    float_3 = Time.time + 5f;
                    _triedToSwitchToMain = true;
                }

                // try reload current weapon
                if (
                    float_5 < Time.time &&
                    (
                        botOwner_0.WeaponManager.Selector.LastEquipmentSlot == EquipmentSlot.FirstPrimaryWeapon ||
                        botOwner_0.WeaponManager.Selector.LastEquipmentSlot == EquipmentSlot.SecondPrimaryWeapon ||
                        botOwner_0.WeaponManager.Selector.LastEquipmentSlot == EquipmentSlot.Holster
                    ) &&
                    botOwner_0.WeaponManager.IsWeaponReady && (float)botOwner_0.WeaponManager.Reload.BulletCount / (float)botOwner_0.WeaponManager.Reload.MaxBulletCount < 0.35f
                )
                {
                    float_5 = Time.time + 30f;
                    _tryReloadSec = botOwner_0.WeaponManager.Reload.TryReload();
                    float_3 = Time.time + 10f;
                }

                // mark that reloading is done
                if (_tryReloadSec && !botOwner_0.WeaponManager.Reload.Reloading)
                {
                    _tryReloadSec = false;
                }

                // try fill magazines of current weapon
                if (float_3 < Time.time && botOwner_0.WeaponManager.IsWeaponReady && !botOwner_0.WeaponManager.Reload.Reloading && !_triedFillMagazines)
                {
                    botOwner_0.WeaponManager.Reload.TryFillMagazines();
                    _triedFillMagazines = true;
                    float_3 = Time.time + 20f;
                }
            }

            if (HasBoss())
            {
                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.followerPatrol, "BossFollow");
            }

            if (way != null && way.PatrolType == PatrolType.reserved && botOwner_0.Settings.FileSettings.Patrol.CAN_CHOOSE_RESERV)
            {
                botOwner_0.PatrollingData.ComeToPoint();
                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.alternativePatrol, "RESER");
            }

            return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.simplePatrol, "Basic");

        }


        public override AICoreActionEndStruct EndSimplePatrol()
        {
            string reason;
            if (method_16(out reason))
            {
                return new AICoreActionEndStruct(reason, true);
            }
            if (botOwner_0.PatrollingData.Way.PatrolType == PatrolType.reserved)
            {
                return new AICoreActionEndStruct("way is alt", true);
            }
            if (HasBoss())
            {
                return new AICoreActionEndStruct("has boss", true);
            }
            return aICoreActionEndStruct_1;
        }

        public override AICoreActionEndStruct EndHeal()
        {
            return commonLayer.EndHeal();
        }

        public override AICoreActionEndStruct EndStimulators()
        {
            return commonLayer.EndStimulators();
        }

        public override AICoreActionEndStruct EndSuppressFire()
        {
            return new AICoreActionEndStruct("enemy.None", true);
        }

        public override AICoreActionEndStruct EndSuppressGrenade()
        {
            return new AICoreActionEndStruct("enemy.None", true);
        }

        public override AICoreActionEndStruct EndRunToEnemy()
        {
            if (!botOwner_0.Memory.HaveEnemy)
            {
                return new AICoreActionEndStruct("enemy.None", true);
            }

            return base.EndRunToEnemy();
        }

        public override AICoreActionEndStruct EndAttackMoving()
        {
            if (!botOwner_0.Memory.HaveEnemy)
            {
                return new AICoreActionEndStruct("enemy.None", true);
            }

            return base.EndAttackMoving();
        }

        public override AICoreActionEndStruct EndDogFight()
        {
            if (!botOwner_0.Memory.HaveEnemy)
            {
                return new AICoreActionEndStruct("enemy.None", true);
            }
            return base.EndDogFight();
        }

        public override AICoreActionEndStruct EndRunToCover()
        {
            if (!botOwner_0.Memory.HaveEnemy) return new AICoreActionEndStruct("enemy,None", true);

            if (botOwner_0.BewareGrenade.SawGrenadeSoFar(5f))
            {
                return new AICoreActionEndStruct("saw grenade", true);
            }
            if (botOwner_0.Memory.IsInCover)
            {
                return new AICoreActionEndStruct("InCover", true);
            }

            if (base.method_3())
            {
                return new AICoreActionEndStruct("StartD", true);
            }
            return this.aICoreActionEndStruct_1;
        }

        public override AICoreActionEndStruct ShallEndCurrentDecision(AICoreActionResultStruct<BotLogicDecision, StandardBrain> curDecision)
        {
            if (curDecision.Action == BotLogicDecision.heal
            )
            {
                return EndHeal();
            }

            if (curDecision.Action == BotLogicDecision.healStimulators)
            {
                return EndStimulators();
            }

            if (curDecision.Action == BotLogicDecision.runToCover && (curDecision.Reason == "runToHeal" || curDecision.Reason == "relocateFast"))
            {
                return new AICoreActionEndStruct("enemy.None", true);
            }

            if (!botOwner_0.Memory.HaveEnemy && curDecision.Action == BotLogicDecision.goToCoverPoint && curDecision.Reason != "PeaceSmoke")
            {
                return new AICoreActionEndStruct("enemy.None", true);
            }

            return base.ShallEndCurrentDecision(curDecision);
        }

        protected virtual void GetCoverPoint(Vector3 centerPosition, float searchRadius)
        {

            CustomNavigationPoint point1 = Utils.Covers.GetCoverPoint(botOwner_0, centerPosition, searchRadius);

            customNavigationPoint_0 = point1;
            botOwner_0.Memory.SetCoverPoints(point1);
        }
    }
}
