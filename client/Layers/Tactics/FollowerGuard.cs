using Comfort.Common;
using EFT;
using EFT.InventoryLogic;

using System;
using System.Collections.Generic;
using UnityEngine;
using Selector = GClass454;
using StandardBrain = GClass26;

using friendlySAIN.Utils;
using friendlySAIN.Components;

namespace friendlySAIN.Layers.Tactics
{
    /** 
     * This class is not meant to be used directly as a brain layer, but within one
     * Ovewrite of "Kill Logic" layer that is able to use grenader launcher as support
     * **/
    public class FollowerGuard : GClass59
    {
        protected float coverTimer = 0f;
        protected float holdTimer = 0f;

        private bool existingCommon = false;

        private FollowerCommonLayer commonLayer;

        private float float_6 = 0f;
        private float float_10 = 0f;

        private float float_11 = 0f;
        private readonly List<Vector3> list_1 = new List<Vector3>();

        private readonly Selector gclass396_0 = null;

        public CustomNavigationPoint NavigationPoint
        {
            get
            {
                return customNavigationPoint_0;
            }
        }

        public FollowerCommonLayer CommonLayer { get { return commonLayer; } }

        protected FollowerPusherLayer PusherLayer = null;
        public FollowerGuard(BotOwner bot, int priority, FollowerPusherLayer pusherLayer = null) : base(bot, priority)
        {
            if (pusherLayer != null)
            {
                commonLayer = pusherLayer.CommonLayer;
                existingCommon = true;
                PusherLayer = pusherLayer;
            }
            else commonLayer = new FollowerCommonLayer(bot, priority);

            gclass396_0 = botOwner_0.WeaponManager.Selector as Selector;
        }

        public override string Name()
        {
            return "FBGuard";
        }
        // dummy 
        public override bool ShallUseNow()
        {
            return true;
        }

        public override void OnActivate()
        {
            base.OnActivate();
            if (!existingCommon) commonLayer?.OnActivate();

            if (botOwner_0.WeaponManager.Grenades != null)
            {
                botOwner_0.WeaponManager.Grenades.OnGrenadeThrowStart += OnThrowGrenade;
            }
        }
        public override void Dispose()
        {
            base.Dispose();
            if (!existingCommon) commonLayer?.Dispose();

            if (botOwner_0.WeaponManager.Grenades != null) botOwner_0.WeaponManager.Grenades.OnGrenadeThrowStart -= OnThrowGrenade;
        }
        public void OrdersChanged()
        {
            commonLayer.OrdersChanged();
        }

        public bool ShallGoNearBoss()
        {
            return commonLayer.ShallGoNearBoss();
        }

        public void OnThrowGrenade()
        {
            float killa_AFTER_GRENADE_SUPPRESS_DELAY = botOwner_0.Settings.FileSettings.Boss.KILLA_AFTER_GRENADE_SUPPRESS_DELAY;
            EnemyInfo goalEnemy = botOwner_0.Memory.GoalEnemy;
            if (killa_AFTER_GRENADE_SUPPRESS_DELAY > 0f && goalEnemy != null && !goalEnemy.CanShoot)
            {
                nullable_0 = new BotLogicDecision?(BotLogicDecision.holdPosition);
                HoldFor(killa_AFTER_GRENADE_SUPPRESS_DELAY);
            }
        }

        public override void DecisionChanged(AICoreActionResultStruct<BotLogicDecision, StandardBrain>? prevDecision, AICoreActionResultStruct<BotLogicDecision, StandardBrain> nextDecision)
        {
            commonLayer.DecisionChanged(prevDecision, nextDecision);
            base.DecisionChanged(prevDecision, nextDecision);
        }

        public bool HasGrenadeLauncher()
        {
            if (!botOwner_0.WeaponManager.Selector.CanChangeToSecondWeapons) return false;
            Selector selector = botOwner_0.WeaponManager.Selector as Selector;

            if (selector != null && (selector.SecondPrimaryWeaponItem as Weapon) != null && (selector.SecondPrimaryWeaponItem as Weapon).IsGrenadeLauncher)
            {
                return true;
            }

            return false;
        }
        // simplified version of method_30 for GClass52
        public AICoreActionResultStruct<BotLogicDecision, StandardBrain>? SuppressFire(bool grenadePriority)
        {
            ThrowWeapType? throwWeapType = null;
            EnemyInfo enemyInfo = this.botOwner_0.Memory.GoalEnemy;
            if (grenadePriority)
            {
                if (throwWeapType != null && this.botOwner_0.WeaponManager.Grenades.HaveGrenadeOfType(throwWeapType.Value) && this.botOwner_0.SuppressGrenade.Init(enemyInfo, throwWeapType, null, AIGreandeAng.ang45))
                {
                    this.HoldFor(this.botOwner_0.Settings.FileSettings.Boss.KILLA_AFTER_GRENADE_SUPPRESS_DELAY);
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.suppressGrenade, "SupGrenade");
                }
                throwWeapType = new ThrowWeapType?(ThrowWeapType.frag_grenade);
                if (this.botOwner_0.WeaponManager.Grenades.HaveGrenadeOfType(throwWeapType.Value))
                {
                    if (this.botOwner_0.SuppressGrenade.Init(enemyInfo, throwWeapType, null, AIGreandeAng.ang45))
                    {
                        return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.suppressGrenade, "SupGrenade2");
                    }
                }
                else
                {
                    throwWeapType = new ThrowWeapType?(ThrowWeapType.stun_grenade);
                    if (this.botOwner_0.WeaponManager.Grenades.HaveGrenadeOfType(throwWeapType.Value) && this.botOwner_0.SuppressGrenade.Init(enemyInfo, throwWeapType, null, AIGreandeAng.ang45))
                    {
                        return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.suppressGrenade, "SupGrenade3");
                    }
                }
            }

            ShootPointClass shootPointClass = botOwner_0.CurrentEnemyTargetPosition(true);
            if (Utils.Utils.CanShootToTarget(shootPointClass, this.botOwner_0.WeaponRoot.position, this.botOwner_0.LookSensor.Mask, false))
            {
                botOwner_0.Steering.LookToPoint(shootPointClass.Point);
                this.botOwner_0.SuppressShoot.InitToPoint(enemyInfo.CurrPosition, null);
                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.suppressFire, "SupFire");
            }

            CustomNavigationPoint customNavigationPoint;
            if (!this.method_29(shootPointClass, this.botOwner_0.Settings.FileSettings.Boss.KILLA_DIST_TO_GO_TO_SUPPRESS, out customNavigationPoint) && customNavigationPoint != null)
            {
                botOwner_0.Steering.LookToPoint(shootPointClass.Point);
                this.botOwner_0.SuppressShoot.InitToPoint(enemyInfo.CurrPosition, customNavigationPoint);
                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.suppressFire, "SupFire2");
            }

            return null;
        }

        public AICoreActionResultStruct<BotLogicDecision, StandardBrain>? GetSuppressDecision()
        {

            if (float_6 < Time.time) return null;

            float_6 = Time.time + Utils.Utils.Random(3f, 5f);

            Vector3 playerPos = commonLayer.HasBoss() ? commonLayer.GetBoss().Player().Transform.position : botOwner_0.GetPlayer.Position;

            if (list_1.Count > 0 && HasGrenadeLauncher())
            {
                List<Vector3> goodPoints = list_1.ApplyFilter((Vector3 pos) =>
                {
                    if (!Utils.Utils.IsDangerPositionFarEnough(pos, new Vector3[] { playerPos }, 18f * 18f)) return false;
                    return true;
                });
                list_1.Clear();
                list_1.AddRange(goodPoints);

                if (list_1.Count > 0)
                {
                    ShootPointClass shootPointClass = botOwner_0.CurrentEnemyTargetPosition(true);

                    CustomNavigationPoint suppPosition = Covers.GetCoverPoint(botOwner_0, botOwner_0.Position, 70f, point =>
                    {
                        if (Utils.Utils.CanShootToTarget(shootPointClass, point, botOwner_0.LookSensor.Mask, false))
                        {
                            point.CanIShootToEnemy = true;
                            return true;
                        }
                        return false;
                    });
                    botOwner_0.SuppressShoot.InitToPoints(new List<Vector3> { list_1[0] }, suppPosition);

                    float delay = (float)list_1.Count * 2f;
                    foreach (Vector3 position in list_1)
                    {
                        Singleton<BotEventHandler>.Instance.ArtilleryStart(position, 20f, delay);
                    }
                    float_10 = Time.time + 60f;
                    list_1.Clear();
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.suppressFire, "grSuppress");
                }
            }

            EnemyInfo goalEnemy = botOwner_0.Memory.GoalEnemy;

            if (goalEnemy != null && !goalEnemy.IsVisible)
            {
                if (botOwner_0.SmokeGrenade.ShallShoot())
                {
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.shootToSmoke, "StM");
                }
                if (botOwner_0.SmokeGrenade.IsInSmoke)
                {
                    GetClosestCoverPoint(botOwner_0.GetPlayer.Position, 60f);
                    if (customNavigationPoint_0 != null)
                    {
                        botOwner_0.Memory.SetCoverPoints(customNavigationPoint_0);
                        return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.runToCover, "IsInSmoke");
                    }
                }
            }

            if (!botOwner_0.Memory.GoalEnemy.IsSuppressed() && goalEnemy.ShallISuppress())
            {
                bool useGrenade = botOwner_0.Settings.FileSettings.Core.CanGrenade && Utils.Utils.Random(0f, 2f) > 1f && Utils.Enemy.Distance(botOwner_0) == Utils.Enemy.EnemyDistance.Close;

                if ((playerPos - goalEnemy.CurrPosition).sqrMagnitude < 12f * 12f)
                {
                    useGrenade = false;
                }

                AICoreActionResultStruct<BotLogicDecision, StandardBrain>? suppress = SuppressFire(useGrenade);
                if (suppress.HasValue) return suppress.Value;
                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(SuppressFallback(), "No Sup");
            }

            return null;
        }

        public override AICoreActionResultStruct<BotLogicDecision, StandardBrain> GetDecision()
        {
            Vector3 botPosition = botOwner_0.GetPlayer.Transform.position;
            EnemyInfo goalEnemy = botOwner_0.Memory.GoalEnemy;

            if (goalEnemy == null) return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>((BotLogicDecision)CustomBotDecisions.GuardToCover, "coverBoss");

            bool enemyVisible = goalEnemy.IsVisible;
            Vector3 enemyPos = goalEnemy.CurrPosition;
            float enemyDistance = goalEnemy.Distance;

            if (enemyVisible)
            {
                // enemy visible (can't shoot) and we are not in cover
                if (!botOwner_0.Memory.IsInCover)
                {
                    // - find cover to shoot from
                    GetClosestAttackCoverPoint(botPosition, 80f);

                    if (customNavigationPoint_0 != null)
                    {
                        if (!commonLayer.SprintDistance(customNavigationPoint_0.Position, 25f))
                        {
                            return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.goToCoverPointTactical, "relocate");
                        }
                        return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.runToCover, "relocateFast");
                    }

                    // - fallback
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.dogFight, "dgf");

                }
                else
                {
                    // - try to shot enemy
                    if (botOwner_0.Memory.CurCustomCoverPoint != null && botOwner_0.Memory.CurCustomCoverPoint.CanIShootToEnemy)
                        return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.shootFromCover, "shootFromCover");
                    // - else find better spot
                    else
                    {
                        bool getClose = false;
                        if (
                            Enemy.Distance(botOwner_0) <= Enemy.EnemyDistance.Close &&
                            botOwner_0.Memory.AttackImmediately && Enemy.GetEnemiesAtLocation(botOwner_0, botOwner_0.Memory.GoalEnemy, enemyPos) < 3)
                        {
                            getClose = true;
                        }

                        if (getClose)
                        {
                            botOwner_0.Tactic.SetTactic(BotsGroup.BotCurrentTactic.Attack);
                            return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.goToEnemy, "pushEnemy");
                        }
                        else
                        {
                            GetClosestAttackCoverPoint(botOwner_0.Position, 30f);
                        }

                        if (customNavigationPoint_0 != null)
                            return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.attackMoving, "relocate");
                    }
                }
            }
            // enemy not visible
            else
            {
                // - see if we can do a supress
                AICoreActionResultStruct<BotLogicDecision, StandardBrain>? supportDecision = null;

                try
                {
                    supportDecision = GetSuppressDecision();
                }
                catch (Exception ex)
                {
                    Modules.Logger.LogInfo("supportDecision Error: " + ex.Message);
                    Modules.Logger.LogInfo("Trace: " + ex.StackTrace);
                }

                if (supportDecision.HasValue) return supportDecision.Value;

                // - approach enemy if close enough
                if (
                    Enemy.Distance(botOwner_0) <= Enemy.EnemyDistance.Close && Enemy.GetEnemiesAtLocation(botOwner_0, botOwner_0.Memory.GoalEnemy, enemyPos) < 4)
                {
                    GetApproachablePoint(true);
                    if (customNavigationPoint_0 != null)
                    {
                        return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.attackMoving, "getInCloseSlow");
                    }

                    if (PusherLayer != null) return PusherLayer.EngageEnemy(true);
                    else
                    {
                        botOwner_0.Tactic.SetTactic(BotsGroup.BotCurrentTactic.Attack);
                        return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.goToEnemy, "pushEnemy");
                    }
                }
                else if (Enemy.Distance(botOwner_0) == Enemy.EnemyDistance.Mid)
                {
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>((BotLogicDecision)CustomBotDecisions.GuardToCover, "coverBoss");
                }
                else if (!(botOwner_0.Memory.AttackImmediately && Enemy.GetEnemiesAtLocation(botOwner_0, botOwner_0.Memory.GoalEnemy, enemyPos) < 3))
                {
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>((BotLogicDecision)CustomBotDecisions.GuardToCover, "coverBoss");
                }

                // - look for a shooting spot
                GetApproachablePoint(enemyDistance > Utils.Props.searchRadius);
                if (customNavigationPoint_0 == null)
                    GetClosestAttackCoverPoint(commonLayer.GetBoss().realPlayer.Transform.position, 50f);

                if (customNavigationPoint_0 != null && coverTimer < Time.time)
                {
                    coverTimer = Time.time + Utils.Utils.Random(3f, 5f);
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.runToCover, "relocateFast");
                }

                if (commonLayer.ShallGoNearBoss())
                {
                    customNavigationPoint_0 = commonLayer.GetClosestCoverPointGroup(commonLayer.GetBoss().realPlayer.Transform.position, commonLayer.coverSearchRadius);

                    if (customNavigationPoint_0 != null)
                    {
                        if (!commonLayer.SprintDistance(customNavigationPoint_0.Position))
                        {
                            botOwner_0.GoToSomePointData.SetPoint(customNavigationPoint_0.Position);
                            return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.goToPoint, "regroupToBoss");
                        }
                        else
                            return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.runToCover, "regroupToBossFast");
                    }
                }
            }
            // - fallback
            return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>((BotLogicDecision)CustomBotDecisions.GuardToCover, "coverBoss");
        }

        public AICoreActionResultStruct<BotLogicDecision, StandardBrain> GrenadierDecision()
        {
            return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.suppressFire, "suppressFireLauncher");
        }

        public AICoreActionResultStruct<BotLogicDecision, StandardBrain>? CanDoGrenadierSuppressRequest(Ray rayDirection)
        {
            if (!HasGrenadeLauncher()) return null;

            RaycastHit[] hits = new RaycastHit[20];

            float scanDistance = 120f;

            float sphereRadius = scanDistance / 2;
            float sphereDistance = scanDistance / 2;

            int numHits = Physics.SphereCastNonAlloc(
                rayDirection,
                sphereRadius,
                hits,
                sphereDistance,
                LayerMaskClass.PlayerMask
            );

            Vector3 playerPos = commonLayer.HasBoss() ? commonLayer.GetBoss().Player().Transform.position : botOwner_0.GetPlayer.Position;

            List<Vector3> list_2 = new List<Vector3>();

            for (int i = 0; i < numHits; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.collider != null && hit.collider.gameObject != null)
                {

                    var enemy = hit.collider.gameObject.GetComponent<Player>();

                    bool isenemy = false;

                    if (enemy != null)
                    {
                        if (commonLayer.HasBoss())
                        {
                            pitAIBossPlayer boss = commonLayer.GetBoss();
                            if (boss.Followers.Find(fl => fl.ProfileId == enemy.ProfileId) != null) continue;

                            isenemy = boss.bossGroup.IsEnemy(enemy);

                            if (!isenemy && boss.bossGroup.IsPlayerEnemy(enemy)) isenemy = true;
                        }
                        else if (botOwner_0.BotsGroup.IsEnemy(enemy) || botOwner_0.BotsGroup.IsPlayerEnemy(enemy))
                        {
                            isenemy = true;
                        }

                    }

                    if (isenemy)
                    {
                        Vector3 enemyPos = enemy.Transform.position;
                        // - check if enemy is far enough from the player
                        if (!Utils.Utils.IsDangerPositionFarEnough(playerPos, new Vector3[]
                        {
                                enemyPos
                        }, 12f * 12f)) continue;

                        list_2.Add(enemyPos);
                    }
                }
            }

            if (list_2.Count < 1) return null;

            if (botOwner_0.WeaponManager.Selector.LastEquipmentSlot != EquipmentSlot.SecondPrimaryWeapon)
                botOwner_0.WeaponManager.Selector.TryChangeWeapon(true);

            ShootPointClass shootPointClass = botOwner_0.CurrentEnemyTargetPosition(true);

            CustomNavigationPoint suppPosition = Covers.GetCoverPoint(botOwner_0, botOwner_0.Position, 70f, point =>
            {
                if (Utils.Utils.CanShootToTarget(shootPointClass, point, botOwner_0.LookSensor.Mask, false))
                {
                    point.CanIShootToEnemy = true;
                    return true;
                }
                return false;
            });

            botOwner_0.SuppressShoot.InitToPoints(list_2, suppPosition);

            float delay = (float)list_2.Count * 2f;

            foreach (Vector3 position in list_2)
            {
                Singleton<BotEventHandler>.Instance.ArtilleryStart(position, 20f, delay);
            }

            return GrenadierDecision();
        }

        public BotLogicDecision SuppressFallback()
        {

            BotRequest request = botOwner_0.BotRequestController.CurRequest;
            if (request != null && request.BotRequestType == BotRequestType.suppressionFire) request.Complete();
            return (BotLogicDecision)CustomBotDecisions.GuardToCover;
        }
        /**
         * Action to switch to shotgun when closing in on the enemy
         */
        public void AutoToShotgun(AICoreActionResultStruct<BotLogicDecision, StandardBrain> decision)
        {
            try
            {
                bool canDo = botOwner_0.WeaponManager.Selector.CanChangeToSecondWeapons;

                if (!canDo) return;

                canDo = botOwner_0.WeaponManager.Selector.SecondPrimaryWeaponItem != null;

                if (!canDo) return;

                canDo = botOwner_0.WeaponManager.Selector.SecondPrimaryWeaponItem is ShotgunItemClass;

                if (!canDo) return;



                EnemyInfo goalEnemy = botOwner_0.Memory.GoalEnemy;

                if (goalEnemy == null) return;

                List<string> decisions = new List<string> {
                    "getInCloseSlow",
                    "getInCloseFast",
                    "pushEnemy"

                };

                List<string> closeDecisions = new List<string> {
                    "enemy.Search",
                    "wait4it",
                    "waitAbit",
                    "coverBoss",
                    "getInCloseSlow",
                };

                List<string> ignoreDecisions = new List<string> {
                    "runToHeal",
                    "healInCover",
                    "usingStims",
                    "moveToHeal"
                };

                Utils.Enemy.EnemyDistance enemyDistant = Utils.Enemy.Distance(botOwner_0);
                bool isclose = enemyDistant <= Utils.Enemy.EnemyDistance.VeryClose;
                if (isclose && goalEnemy.PersonalLastSeenTime <= 1f) isclose = false;

                if (ignoreDecisions.Contains(decision.Reason)) return;

                if (goalEnemy.HaveSeen && Time.time - goalEnemy.PersonalLastSeenTime > 1.5f && (float_11 < Time.time || isclose))
                {
                    float_11 = Time.time + Utils.Utils.Random(3f, 5f);
                    // switch back to primary weapon if we are not in a close combat situation
                    if (
                        (!isclose && (enemyDistant > Enemy.EnemyDistance.Mid || !decisions.Contains(decision.Reason))) ||
                        (isclose && !closeDecisions.Contains(decision.Reason))
                    )
                    {
                        if (botOwner_0.WeaponManager.Selector.LastEquipmentSlot == EquipmentSlot.FirstPrimaryWeapon) return;

                        Weapon primaryWeapon = botOwner_0.GetPlayer.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.FirstPrimaryWeapon).ContainedItem as Weapon;
                        if (primaryWeapon != null && primaryWeapon.GetCurrentMagazine() != null && primaryWeapon.GetCurrentMagazine().Cartridges.Count > 0)
                        {
                            botOwner_0.WeaponManager.Selector.TryChangeToMain();
                        }

                        return;
                    }
                    // switch to shotgun if we are in close combat situation or closing in on the enemy
                    if (botOwner_0.WeaponManager.Selector.LastEquipmentSlot == EquipmentSlot.SecondPrimaryWeapon) return;

                    Weapon SecondaryWeapon = botOwner_0.GetPlayer.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.SecondPrimaryWeapon).ContainedItem as Weapon;
                    if (SecondaryWeapon != null && SecondaryWeapon.GetCurrentMagazine() != null && SecondaryWeapon.GetCurrentMagazine().Cartridges.Count >= 4)
                    {
                        botOwner_0.WeaponManager.Selector.TryChangeWeapon(true);
                    }
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("AutoToShotgun Error: ");
                Modules.Logger.LogError(ex);
            }
        }

        public override AICoreActionEndStruct EndHoldPosition()
        {
            if (method_35())
            {
                return new AICoreActionEndStruct("massSpr", true);
            }
            EnemyInfo goalEnemy = botOwner_0.Memory.GoalEnemy;
            if (Time.time - goalEnemy.GroupInfo.EnemyLastSeenTimeReal < 2f)
            {
                bool_2 = false;
                return new AICoreActionEndStruct("smb seen", true);
            }
            return base.EndHoldPosition();
        }

        public override AICoreActionEndStruct EndShootFromCover()
        {
            if (method_35())
            {
                return new AICoreActionEndStruct("massSpr", true);
            }
            return base.EndShootFromCover();
        }

        private bool method_35()
        {
            if (float_10 > Time.time)
            {
                return false;
            }
            float_10 = Time.time + 10f;
            if (gclass396_0 != null && gclass396_0.EquipmentSlot != EquipmentSlot.SecondPrimaryWeapon)
            {
                return false;
            }
            int num = 0;
            list_1.Clear();
            foreach (KeyValuePair<IPlayer, EnemyInfo> keyValuePair in botOwner_0.EnemiesController.EnemyInfos)
            {
                if (keyValuePair.Value.IsVisible)
                {
                    num++;
                    list_1.Add(keyValuePair.Value.CurrPosition);
                }
            }
            if (num > botOwner_0.Settings.FileSettings.Boss.BIG_PIPE_ARTILLERY_COUNT)
            {
                return true;
            }
            list_1.Clear();
            return false;
        }


        protected virtual void GetClosestAttackCoverPoint(Vector3 centerPosition, float searchRadius = 150f)
        {
            customNavigationPoint_0 = PusherLayer.GetClosestAttackCoverPoint(centerPosition, searchRadius);
        }

        protected virtual void GetClosestCoverPoint(Vector3 centerPosition, float searchRadius)
        {
            customNavigationPoint_0 = commonLayer.GetClosestCoverPoint(centerPosition, searchRadius);
        }

        public virtual void GetApproachablePoint(bool inbetween = false)
        {
            customNavigationPoint_0 = commonLayer.GetApproachableCover(inbetween);
        }

        public void GetAssaultPoint()
        {
            customNavigationPoint_0 = Utils.Covers.FindPointForAssault(botOwner_0);
            botOwner_0.Memory.SetCoverPoints(customNavigationPoint_0);
        }
    }
}
