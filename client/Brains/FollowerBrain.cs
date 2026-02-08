using Comfort.Common;
using EFT;
using EFT.InventoryLogic;

using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

using friendlySAIN.Modules;
using friendlySAIN.Layers;
using friendlySAIN.Components;

using static EFT.Player;

using HandEvent = GEventArgs1;

using LauncherSelector = GClass466;
using WeaponSelector = BotWeaponSelector;

namespace friendlySAIN.Brains
{
    public class FollowerBrain : BaseBrain
    {
        FollowerFightLayer fightLayer;

        protected pitAIBossPlayer _boss;

        protected string _currentTactic = null;
        protected string _defaultTactic = null;



        public string currentTactic
        {
            get
            {
                return _currentTactic;
            }
        }

        public string defaultTactic
        {
            get
            {
                return _defaultTactic;
            }
        }

        protected int _defaultFollowDistance = 12;
        protected int _followDistance;

        public int followDistance
        {
            get
            {
                return _followDistance;
            }
        }

        protected bool _canPatrol = false;

        public bool canPatrol
        {
            get
            {
                return _canPatrol;
            }
        }

        protected bool _needsProtection = true;

        public bool bossNeedsProtection
        {
            get
            {
                return _needsProtection;
            }
        }

        private float _gotShot = 0f;

        private float _underFire = 0f;

        private float _hitFreq = 0f;

        private const float _BULLET_HEAR_DIST = 50f * 50f;
        private const float _BULLET_IMPACT_DISPERSION = 5f * 5f;

        public bool WasHit
        {
            get { return _gotShot > Time.time; }
        }

        public bool UnderFire
        {
            get { return _underFire > Time.time; }
        }

        private List<Vector3> processedSoundPositions = new List<Vector3>();

        private float _lastSoundTime = 0f;

        private float _lastGunshotTime = 0f;

        public event Action<BotOwner> OnDispose;

        private float _busyTimer = 0f;

        private const float TIME_TO_RESET_HEAL_FIRSTAID = 15f;
        private const float TIME_TO_RESET_HEAL_STIMS = 3f;
        private const float TIME_TO_RESET_HEAL_SURGERY = 40f;
        private const float TIME_TO_RESET_WEAPONS_GRENADE = 3f;
        private const float TIME_TO_RESET_WEAPONS_SWAP = 3f;

        private const float TIME_TO_RESET_HANDS = 5f;

        private bool GRENADE_THROWING = false;

        public bool IsThrowingGrenade
        {
            get { return GRENADE_THROWING; }
        }

        public pitAIBossPlayer playerBoss
        {
            get { return _boss; }
        }

        public FollowerBrain(BotOwner owner, pitAIBossPlayer boss) : base(owner)
        {
            AddLayers();

            _followDistance = _defaultFollowDistance;

            _boss = boss;

            owner.GetPlayer.HealthController.DiedEvent += OnDead;
            owner.Memory.OnAddEnemy += OnAddEnemy;
            owner.GetPlayer.BeingHitAction += BeingHitAction;

            owner.WeaponManager.Grenades.OnGrenadeThrowStart += OnThrow;

            _currentTactic = "Default";

        }

        public override void ManualUpdate()
        {
            base.ManualUpdate();
            try
            {
                if (CheckIfBusy()) return;

                if (_owner.WeaponManager != null)
                {
                    if (CheckActionBusy(_owner.WeaponManager.Grenades.ThrowindNow || GRENADE_THROWING, TIME_TO_RESET_WEAPONS_GRENADE))
                        return;

                    if (CheckActionBusy(_owner.WeaponManager.Selector.IsChanging, TIME_TO_RESET_WEAPONS_SWAP))
                        return;
                }
                // this might not be needed since 3.11
                var meds = _owner.Medecine;
                if (meds != null)
                {
                    if (CheckActionBusy(meds.Stimulators?.Using == true, TIME_TO_RESET_HEAL_STIMS)) return;
                    if (CheckActionBusy(meds.FirstAid?.Using == true, TIME_TO_RESET_HEAL_FIRSTAID)) return;
                    if (CheckActionBusy(meds.SurgicalKit?.Using == true, TIME_TO_RESET_HEAL_SURGERY)) return;
                }


                var _isinteracting = _owner.GetPlayer.HandsController.IsInInteraction() || _owner.GetPlayer.HandsController.IsInInteractionStrictCheck();

                if (_isinteracting && CheckActionBusy(true, TIME_TO_RESET_HANDS)) return;

                ResetBusyState();
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError(ex);
            }
        }

        private bool CheckActionBusy(bool isActionActive, float resetTime)
        {
            if (isActionActive)
            {
                if (_busyTimer == 0f)
                {
                    _busyTimer = Time.time + resetTime;
                    return true; // Hands are busy
                }
                else if (_busyTimer > Time.time)
                {
                    return true; // Hands are still busy, timer not finished
                }
                else
                {
                    HandsReset();
                    _owner.WeaponManager.Selector.TakePrevWeapon();
                }
            }
            return false; // Hands not busy
        }

        private bool CheckIfBusy()
        {
            return _busyTimer > 0f && _busyTimer > Time.time;
        }

        private void ResetBusyState()
        {
            _busyTimer = 0f;
            GRENADE_THROWING = false;
        }
        /** Add the brain layers for the follower bot. Order matter in terms of the initial priority */
        public virtual void AddLayers()
        {
            // - follow
            FollowerLayer followLayer = new FollowerLayer(_owner, 50);
            method_0(1, followLayer, true);
            // - requests
            FollowerRequestLayer layer4 = new FollowerRequestLayer(_owner, 55);
            method_0(2, layer4, true);

            // - fight
            FollowerFightLayer layer6 = new FollowerFightLayer(_owner, 60);
            fightLayer = layer6;
            method_0(3, layer6, true);

            // - grenade and BTR
            FollowerAvoidDanger layer = new FollowerAvoidDanger(_owner, 80);
            method_0(4, layer, true);
            // - weapon malfunction
            GClass115 layer3 = new GClass115(_owner, 88, 300f);
            method_0(5, layer3, true);
            // - stay at position in prone mode
            //GClass123 layer8 = new GClass123(_owner, 10, false, CoverLevel.Lay);
            //method_0(7, layer8, true);
            // - item taker
            FollowerLootLayer layer9 = new FollowerLootLayer(_owner, 51);
            method_0(6, layer9, true);
            // - door opener 
            FollowerDoorLayer layer10 = new FollowerDoorLayer(_owner, 52);
            method_0(7, layer10, true);
        }

        public override string ShortName()
        {
            return "FLBPlayer";
        }

        public override GClass671 EventsPriority()
        {
            return new GClass671(1, 75, 45, 76);
        }

        protected virtual void OnDead(EDamageType damageType)
        {
            // on follower dead, the closest follower to him will react
            try
            {
                BotOwner flw = null;
                float dist = Mathf.Infinity;
                if (_boss != null && _boss.realPlayer != null) foreach (var item in BossPlayers.GetFollowersByBoss(_boss.realPlayer.ProfileId))
                    {
                        if (!item.IsBot(_owner))
                        {
                            BotOwner bt = item.GetBot();
                            if (!bt.IsDead && bt.BotState == EBotState.Active)
                            {
                                float d = (bt.Position - _owner.Position).sqrMagnitude;
                                if (d < dist)
                                {
                                    dist = d;
                                    flw = bt;
                                }
                            }
                        }
                    }

                if (flw != null && dist < 30f * 30f)
                {
                    flw.BotTalk.TrySay(EPhraseTrigger.OnFriendlyDown, false);
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError(ex);
            }

        }
        // taken from SAIN
        protected float CalcTurnSpeed(Vector3 currLookDirection, Vector3 targetDirection)
        {
            float min = 125f;
            float max = 360f;
            float maxAngle = 150f;
            float minAngle = 5f;

            float angle = Vector3.Angle(currLookDirection, targetDirection.normalized);

            if (angle >= maxAngle)
            {
                return max;
            }

            if (angle <= minAngle)
            {
                return min;
            }

            float angleDiff = maxAngle - minAngle;
            float targetDiff = angle - minAngle;
            float ratio = targetDiff / angleDiff;
            float result = Mathf.Lerp(min, max, ratio);
            return result;
        }

        /** Bot should turn to the direction from where he got shot, if he is nor already engaging */
        protected void BeingHitAction(DamageInfoStruct damageInfo, EBodyPart bodyType, float damageReducedByArmor)
        {
            if (damageInfo.Player != null)
            {
                if (_owner.BotFollower.HaveBoss)
                {
                    if (_owner.BotFollower.BossToFollow.Player().ProfileId == damageInfo.Player.iPlayer.ProfileId) return;
                    if (_owner.BotFollower.BossToFollow.Followers.Find(bt => bt.ProfileId == damageInfo.Player.iPlayer.ProfileId)) return;
                }

                Vector3? pos = damageInfo.Player.iPlayer?.Position;

                if (pos.HasValue)
                {
                    try
                    {
                        Vector3 direction = pos.Value - _owner.GetPlayer.Transform.position;

                        if (_owner.Memory.HaveEnemy && (_owner.Memory.GoalEnemy.IsVisible || Time.time - _owner.Memory.GoalEnemy.PersonalLastSeenTime < 1.5f)) return;


                        if (direction.sqrMagnitude < 1f)
                        {
                            direction = direction.normalized;
                        }

                        direction = direction * 20f; // ensure the bot will not look down at the ground

                        _owner.Steering.LookToPoint(direction, CalcTurnSpeed(_owner.LookDirection, direction));

                        if (_gotShot > Time.time && _underFire < Time.time)
                        {
                            _underFire = Time.time + 5f;
                            _owner.Memory.SetUnderFire(damageInfo.Player.iPlayer);
                            _owner.CalcGoal();
                        }

                        _gotShot = Time.time + 3f;
                    }
                    catch
                    {
                    }
                }
            }
        }

        public virtual void FakeShot(Vector3 direction)
        {
            if (_owner.Memory.HaveEnemy && _owner.Memory.GoalEnemy.IsVisible) return;
            _gotShot = Time.time + 3f;
            _owner.Steering.LookToPoint(direction, CalcTurnSpeed(_owner.LookDirection, direction));
        }

        /** On enemy sound heard make the bot either look towards the direction of the enemy or automatically make the enemy a target **/
        public virtual void SoundHeard(Player enemy, Vector3 position, float distance, AISoundType type)
        {

            if ((type == AISoundType.silencedGun || type == AISoundType.gun) && Time.time < _lastGunshotTime + 3f) return;

            // on gun shot if there is a line of sight, turn immmediately
            if ((type == AISoundType.silencedGun || type == AISoundType.gun) && !WasHit)
            {
                Vector3 shootdir = position - _owner.GetPlayer.Transform.position;

                if (shootdir.sqrMagnitude < 1f)
                {
                    shootdir = shootdir.normalized;
                }

                shootdir *= 20f; // ensure the bot will not look down at the ground

                if (distance <= 35f)
                {
                    FakeShot(shootdir);
                    if (Utils.Utils.GetNavDistance(_owner.Position, position) <= 15f)
                    {
                        EnemyInfo enInfo = Utils.Enemy.MakeEnemy(_owner, enemy);
                        enInfo?.SetVisible(true);
                    }
                }
                else if (
                    Utils.Utils.CanShootToTarget(new ShootPointClass(_owner.GetPlayer.MainParts[BodyPartType.head].Position, 1f), enemy.PlayerBones.WeaponRoot.position, _owner.LookSensor.Mask) ||
                    Utils.Utils.CanShootToTarget(new ShootPointClass(_owner.GetPlayer.MainParts[BodyPartType.head].Position, 1f), enemy.PlayerBones.WeaponRoot.position, _owner.LookSensor.Mask)
                )
                {
                    FakeShot(shootdir);
                    _lastGunshotTime = Time.time;
                    return;
                }
            }
            // turn and face the step sound 
            else if (type == AISoundType.step)
            {
                Vector3 positionZone = new Vector3(
                   Mathf.Floor(position.x / 8f) * 8f,
                   Mathf.Floor(position.y / 8f) * 8f,
                   Mathf.Floor(position.z / 8f) * 8f
               );

                bool wasProcessed = processedSoundPositions.Contains(positionZone);

                if (wasProcessed && Time.time - _lastSoundTime > 5f) return;

                Vector3 dir = position - _owner.GetPlayer.Transform.position;

                if (dir.sqrMagnitude < 1f)
                {
                    dir = dir.normalized;
                }

                dir *= 20f; // ensure the bot will not look down at the ground

                if (!wasProcessed)
                {
                    processedSoundPositions.Add(positionZone);
                    if (processedSoundPositions.Count > 20) processedSoundPositions.RemoveAt(0);
                }

                if (distance <= 8f)
                {
                    FakeShot(dir);
                    EnemyInfo enInfo = Utils.Enemy.MakeEnemy(_owner, enemy);
                    enInfo?.SetVisible(true);
                }
                else
                {
                    _lastSoundTime = Time.time;

                    FakeShot(dir);
                }
            }
        }
        /** INSPIRED FROM SAIN - follower to feel bullets flying **/
        public virtual void BulletFelt(EftBulletClass bullet)
        {
            if (_owner.Memory.HaveEnemy) return;
            if (Time.time > _hitFreq) return;

            Player shooter = Singleton<GameWorld>.Instance.GetAlivePlayerByProfileID(bullet.PlayerProfileID);

            if (!(_owner.EnemiesController.IsEnemy(shooter) || (bullet.Player.iPlayer != null && _owner.BotsGroup.IsEnemy(bullet.Player.iPlayer))))
            {
                return;
            }

            _hitFreq = Time.time + 1f;
            float distance = (bullet.CurrentPosition - _owner.Position).sqrMagnitude;

            if (distance > _BULLET_HEAR_DIST) return;

            float dispersion = distance / _BULLET_IMPACT_DISPERSION;

            Vector3 random = UnityEngine.Random.onUnitSphere;
            random.y = 0;
            random = random.normalized * dispersion;
            Vector3 estimatedPos = shooter.Transform.position + random;

            FakeShot(estimatedPos);
        }
        public void OnThrow()
        {
            GRENADE_THROWING = true;
            _busyTimer = 0f;
        }
        protected virtual void OnAddEnemy(IPlayer player)
        {
            // how does the boss get added as Enemy here ?? - fix it
            if (
                player != null &&
                (
                    (
                        player.ProfileId == _boss.Player().ProfileId)
                    )
                )
            {
                _owner.Memory.DeleteInfoAboutEnemy(player);
                _owner.BotsGroup.RemoveEnemy(player);
                _owner.BotsGroup.AddAlly((Player)_boss.Player());
            }
        }

        public override void Dispose()
        {
            // remove this bot from being a follower
            BotFollowerPlayer follower = BossPlayers.GetFollowers().Find(fl => fl.GetBot().ProfileId == _owner.ProfileId && fl.IsSquadMate);
            // save PMC progress
            if (follower != null && _owner.Side != EPlayerSide.Savage)
            {
                BossPlayers.SaveFollowersProgress(new List<BotFollowerPlayer>() { follower });
            }
            BossPlayers.RemoveFollower(_owner, _boss);
            Dismissed();
            base.Dispose();
        }

        public virtual void Dismissed()
        {
            try
            {
                // clear info about this bot
                InteractableObjects.ClearStoredItems(_owner.ProfileId);
                InteractableObjects.RemoveTaker(_owner);
                NpcMessage.RemoveNpc(_owner.ProfileId);

                OnDispose?.Invoke(_owner);

                if (_owner.GetPlayer != null)
                {
                    if (_owner.GetPlayer.HealthController != null)
                        _owner.GetPlayer.HealthController.DiedEvent -= OnDead;

                    _owner.GetPlayer.BeingHitAction -= BeingHitAction;
                }

                if (_owner.Memory != null)
                    _owner.Memory.OnAddEnemy -= OnAddEnemy;

                if (_owner.WeaponManager != null && _owner.WeaponManager.Grenades != null)
                    _owner.WeaponManager.Grenades.OnGrenadeThrowStart -= OnThrow;
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError(ex);
            }
        }

        public virtual void SetBossTactic(string tactic)
        {
            if (fightLayer != null)
            {
                // whatever tactic we initially set when calling AddBotFollower, that becomes the default one
                if (_defaultTactic == null && tactic != null)
                {
                    _defaultTactic = tactic;
                    // - if default is Support Tactic - change the weapon selector
                    if (_defaultTactic == "Guard")
                    {
                        SetGrenadierSelector();
                    }
                    else if (_defaultTactic.ToLower() == "marksman")
                    {
                        _owner.Settings.FileSettings.Core.CanGrenade = false;
                    }
                }
                else if (tactic == null && _defaultTactic != null) tactic = _defaultTactic;

                fightLayer.SetBossFightTactic(tactic);
                BossOrdersChanged();
            }
        }

        public virtual void SetTactic(string tactic)
        {
            _currentTactic = tactic;
        }

        public virtual void SetFollowDistance(int distance)
        {
            _followDistance = distance;
        }
        public virtual void ResetFollowDistance()
        {
            _followDistance = _defaultFollowDistance;
        }

        public virtual void SetCanPatrol(bool patrol)
        {
            _canPatrol = patrol;
        }

        public virtual void SetBossNeedsProtection(bool value)
        {
            _needsProtection = value;
            if (fightLayer != null)
            {
                fightLayer.CoverType(value ? "close" : "far");
            }
        }

        public virtual void BossOrdersChanged()
        {
            if (fightLayer != null)
            {
                fightLayer.OrdersChanged();
            }
        }

        public virtual void BossOrdersReset()
        {
            if (fightLayer != null)
            {
                fightLayer.OrdersReset();
            }
        }

        private void SetGrenadierSelector()
        {
            try
            {
                // do not change the selector if the bot does not have a grenade launcher as a secondary weapon
                if (
                !_owner.WeaponManager.Selector.CanChangeToSecondWeapons ||
                _owner.WeaponManager.Selector.SecondPrimaryWeaponItem == null ||
                !(_owner.WeaponManager.Selector.SecondPrimaryWeaponItem as Weapon).IsGrenadeLauncher
            )
                {
                    return;
                }

                _owner.WeaponManager.Selector.Dispose();
                _owner.WeaponManager.Selector = new LauncherSelector(_owner);

                LauncherSelector selector = _owner.WeaponManager.Selector as LauncherSelector;

                selector.OnActiveEquipmentSlotChanged = (Action<EquipmentSlot>)Delegate.Combine(selector.OnActiveEquipmentSlotChanged, new Action<EquipmentSlot>(_owner.WeaponManager.method_0));

                selector.UpdateWeaponsList();
                selector.TakeMainWeapon();

                selector.Activate();
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("SetGrenadierSelector Error: ");
                Modules.Logger.LogError(ex);
            }
        }

        public void ReactivateWeaponSelector()
        {
            _owner.WeaponManager.Selector.Dispose();

            if (
                _owner.WeaponManager.Selector.CanChangeToSecondWeapons &&
                _owner.WeaponManager.Selector.SecondPrimaryWeaponItem != null &&
                (_owner.WeaponManager.Selector.SecondPrimaryWeaponItem as Weapon).IsGrenadeLauncher
            )
            {
                _owner.WeaponManager.Selector = new LauncherSelector(_owner);
            }
            else
                _owner.WeaponManager.Selector = new WeaponSelector(_owner);

            _owner.WeaponManager.Selector.OnActiveEquipmentSlotChanged = (Action<EquipmentSlot>)Delegate.Combine(_owner.WeaponManager.Selector.OnActiveEquipmentSlotChanged, new Action<EquipmentSlot>(_owner.WeaponManager.method_0));

            _owner.WeaponManager.Selector.UpdateWeaponsList();
            _owner.WeaponManager.Selector.TakeMainWeapon();

            _owner.WeaponManager.Selector.Activate();

        }


        public void AddExtraAmmoForWeapon(Weapon weapon = null)
        {

            InventoryController inventory = GetInventoryController();
            SearchableItemItemClass secureContainer;

            try
            {
                secureContainer = (SearchableItemItemClass)inventory.Inventory.Equipment.GetSlot(EquipmentSlot.SecuredContainer).ContainedItem;
            }
            catch
            {
                Modules.Logger.LogError("Cannot access secure container of bot, extra ammo will not be added");
                return;
            }

            if (secureContainer == null)
            {
                Modules.Logger.LogError("Bot has no secure container, cannot add extra ammo");
                return;
            }

            StashGridClass stashGridClass = secureContainer.Grids.FirstOrDefault();

            if (stashGridClass == null)
            {
                return;
            }

            if (weapon == null) weapon = _owner.AIData.Player.HandsController.Item as Weapon;

            Item ammoToAdd =
                    weapon.GetCurrentMagazine()?.FirstRealAmmo()
                    ?? Singleton<ItemFactoryClass>.Instance.CreateItem(
                        MongoID.Generate(),
                        weapon.CurrentAmmoTemplate._id,
                        null
                    );

            if (ammoToAdd == null)
            {
                Modules.Logger.LogError("Bot has no weapon to add ammo");
                return;
            }

            int ammoAdded = 0;

            for (int i = 0; i < 10; i++)
            {
                Item ammo = ammoToAdd.CloneItem();
                ammo.StackObjectsCount = ammo.StackMaxSize;

                var location = stashGridClass.FindFreeSpace(ammo);

                if (location != null)
                {
                    var result = stashGridClass.AddItemWithoutRestrictions(ammo, location);

                    if (result.Succeeded)
                    {
                        ammoAdded += ammo.StackObjectsCount;
                    }
                    else
                    {
                        Modules.Logger.LogError("Failed to add ammo to bot's secure container");
                        break;
                    }
                }
                else
                {
                    Modules.Logger.LogInfo("No more space in secure container for ammo");
                    break;
                }
            }

        }

        private InventoryController GetInventoryController()
        {
            return _owner.GetPlayer.InventoryController;
        }

        // Credit to Lacyway's "Hands are Not Busy" mod https://github.com/Lacyway/HandsAreNotBusy/blob/main/HANB_Component.cs
        public void HandsReset()
        {
            Player player = Singleton<GameWorld>.Instance.GetAlivePlayerByProfileID(_owner.ProfileId);
            InventoryController inventoryController = player.InventoryController;
            if (inventoryController == null)
            {
                return;
            }
            int length = inventoryController.List_0.Count;
            if (length > 0)
            {
                HandEvent[] args = new HandEvent[length];
                inventoryController.List_0.CopyTo(args);
                foreach (HandEvent queuedEvent in args)
                {
                    inventoryController.RemoveActiveEvent(queuedEvent);
                }
            }

            AbstractHandsController handsController = player.HandsController;

            if (handsController is FirearmController currentFirearmController)
            {
                player.MovementContext.OnStateChanged -= currentFirearmController.method_17;
                player.Physical.OnSprintStateChangedEvent -= currentFirearmController.method_16;
                currentFirearmController.RemoveBallisticCalculator();
            }

            try
            {
                player.SpawnController(player.method_156());
            }
            catch
            {
            }

            if (player.LastEquippedWeaponOrKnifeItem != null)
            {
                InteractionsHandlerClass.Discard(player.LastEquippedWeaponOrKnifeItem, inventoryController, true);

                player.ProcessStatus = EProcessStatus.None;
                player.TrySetLastEquippedWeapon();
            }
            else
            {
                player.ProcessStatus = EProcessStatus.None;
                player.SetFirstAvailableItem(PlayerOwner.Class1667.class1667_0.method_0);
            }

            player.SetInventoryOpened(false);
            handsController?.Destroy();

            if (handsController != null)
            {
                GameObject.Destroy(handsController);
            }


            if (player.HandsController is FirearmController firearmController && firearmController.Weapon != null)
            {
                Traverse.Create(player.ProceduralWeaponAnimation).Field("_firearmAnimationData").SetValue(firearmController);
            }
        }
    }
}
