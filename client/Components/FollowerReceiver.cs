using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;

using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

using friendlySAIN.Actions;
using friendlySAIN.Modules;
using friendlySAIN.Requests;
using friendlySAIN.Brains;

namespace friendlySAIN.Components
{

    public class FollowerReceiver : BotReceiver
    {

        private static float closestTime = 0f;

        private static BotOwner closestPlayer = null;

        private static Player lookedAtPlayer = null;
        private static float lookedAtTime = 0f;

        private static readonly float maxGestusDistance = 15f;
        public FollowerReceiver(BotOwner owner) : base(owner)
        {
            Receivers.AddReceiver(owner.ProfileId, this);
        }

        protected static Player IsRequesterLookingAtSomeone(Player requester, float magnitude = 27f)
        {
            if (lookedAtPlayer != null && lookedAtTime > Time.time) return lookedAtPlayer;

            float shpere_FRIENDY_FIRE_SIZE = 0.4f;

            LayerMask playerMask = LayerMaskClass.PlayerMask;

            RaycastHit[] array = new RaycastHit[10];
            Ray ray = requester.InteractionRay;

            pitAIBossPlayer boss = BossPlayers.GetBoss(requester.ProfileId);

            lookedAtPlayer = null;
            try
            {
                if (boss != null && Physics.SphereCastNonAlloc(ray, shpere_FRIENDY_FIRE_SIZE, array, magnitude, playerMask) > 0)
                {
                    foreach (RaycastHit raycastHit in array)
                    {
                        if (raycastHit.collider != null && raycastHit.collider.gameObject != null)
                        {
                            if (
                                Utils.Utils.CanShootToTarget(new ShootPointClass(raycastHit.point, 1), ray.origin, LayerMaskClass.HighPolyWithTerrainMask)
                            )
                            //if (!Physics.Linecast(ray.origin, raycastHit.point, GameWorld.LootMaskObstruction))
                            {
                                BotOwner bot = raycastHit.collider.gameObject.GetComponent<BotOwner>();
                                if (bot != null && BossPlayers.IsFollower(bot, boss))
                                {

                                    lookedAtPlayer = bot.GetPlayer;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError(ex);
            }

            lookedAtTime = Time.time + 0.5f;
            return lookedAtPlayer;
        }

        protected static bool IsRequesterLookingAt(BotOwner bot, Player requester, float distance = 27f)
        {
            Player at = IsRequesterLookingAtSomeone(requester, distance);
            return at != null && at.ProfileId == bot.ProfileId;
        }

        protected static bool IsClosestBot(BotOwner bot, IPlayer requester)
        {

            if (closestTime > Time.time)
            {
                if (closestPlayer == null) return false;

                if (closestPlayer.ProfileId == bot.ProfileId)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            BotOwner closest = null;
            float dist = Mathf.Infinity;
            pitAIBossPlayer boss = BossPlayers.GetBoss(requester.ProfileId);

            if (boss == null) return false;

            Vector3 bossPos = boss.realPlayer.Transform.position;

            boss.Followers.ForEach(fl =>
            {
                if (fl != null)
                {
                    Vector3 pos = fl.GetPlayer.Transform.position;
                    float fldist = (bossPos - pos).sqrMagnitude;
                    if (fldist < dist)
                    {
                        closest = fl;
                        dist = fldist;
                    }
                }

            });

            closestTime = Time.time + 0.5f;

            closestPlayer = closest;

            if (closestPlayer.ProfileId == bot.ProfileId)
            {
                return true;
            }

            return false;
        }

        protected static void StopCurrRequest(BotOwner botOwner_0, Player requester = null, bool all = false)
        {
            if (requester)
            {
                botOwner_0.BotRequestController.TryStopCurrent(requester, false);
            }

            if (botOwner_0.BotRequestController.CurRequest != null)
            {
                botOwner_0.BotRequestController.CurRequest.Complete();
                botOwner_0.BotRequestController.CurRequest = null;
            }

            if (all)
            {
                var _listOfRequests = botOwner_0.BotsGroup.RequestsController._listOfRequests;
                _listOfRequests.FindAll(request =>
                {
                    bool good = false;
                    var reqExecutor = AccessTools.Field(typeof(BotRequest), "Executor").GetValue(request) as BotOwner;
                    if (reqExecutor == botOwner_0)
                    {
                        good = true;
                    }
                    return good;
                }).ForEach(r =>
                {
                    r.Complete();
                });

            }
        }

        public virtual void Initiate()
        {
            Singleton<BotEventHandler>.Instance.OnQETilt += base.method_4;
            Singleton<BotEventHandler>.Instance.OnGestusShow += GestusShown;
            Singleton<BotEventHandler>.Instance.OnPhraseSay += PhraseSaid;
            Singleton<BotEventHandler>.Instance.OnHardAimDelegate += base.method_3;
            EPhraseTrigger[] array = (EPhraseTrigger[])Enum.GetValues(typeof(EPhraseTrigger));
        }

        public virtual void Destroy()
        {
            Singleton<BotEventHandler>.Instance.OnQETilt -= base.method_4;
            Singleton<BotEventHandler>.Instance.OnGestusShow -= GestusShown;
            Singleton<BotEventHandler>.Instance.OnPhraseSay -= PhraseSaid;
            Singleton<BotEventHandler>.Instance.OnHardAimDelegate -= base.method_3;

            Receivers.RemoveReceiver(this);
        }

        public bool IsBossRequester(IPlayer requester)
        {


            if (requester == null)
            {
                return false;
            }

            if (!botOwner_0.BotFollower.HaveBoss) return false;


            return botOwner_0.BotFollower.BossToFollow.Player().ProfileId == requester.ProfileId;
        }

        public bool IsAllyRequester(IPlayer requester)
        {
            return IsBossRequester(requester) || (requester != null && botOwner_0.BotsGroup.IsAlly(requester));
        }

        public virtual void GestusShown(GClass516 data)
        {

            EInteraction gesture = data.Gesture;

            bool isBossCommunicating = IsBossRequester(data.Player);

            bool isAllyCommunicating = IsAllyRequester(data.Player);

            bool isAssisting = (botOwner_0.Brain.BaseBrain as FollowerBrain).currentTactic == "Assist";

            float gestusDistance = (botOwner_0.GetPlayer.Transform.position - data.Player.Transform.position).magnitude;

            bool shouldDefault = !BossPlayers.IsPlayerBoss(data.Player.ProfileId);

            bool notBusy = !botOwner_0.Memory.HaveEnemy;

            List<EInteraction> bossNoGesture = new List<EInteraction>
            {
            };
            List<EInteraction> bossBusyIgnore = new List<EInteraction>
            {

            };
            List<EInteraction> allyBusyIgnore = new List<EInteraction>
            {
                EInteraction.ComeWithMeGesture,
                EInteraction.HoldGesture,
                EInteraction.ThereGesture
            };

            List<EInteraction> allyGestures = new List<EInteraction>
            {
                EInteraction.HoldGesture,
                EInteraction.ThereGesture,
                EInteraction.FriendlyGesture
            };

            bool isFollowerBoss = false;
            foreach (WildSpawnType role in Utils.Props.BossFollowersType)
            {
                if (botOwner_0.IsRole(role))
                {
                    isFollowerBoss = true;
                    isAssisting = false;
                    break;
                }
            }


            if (isBossCommunicating)
            {
                if (isFollowerBoss)
                {
                    if (bossNoGesture.Contains(gesture))
                    {

                        if (notBusy)
                        {
                            botOwner_0.Gesture.TryGestus(EInteraction.NoGesture, false);
                        }
                        return;
                    }

                    if (!notBusy && bossBusyIgnore.Contains(gesture))
                    {
                        return;
                    }
                }
                else if (isAssisting)
                {
                    if (!notBusy && allyBusyIgnore.Contains(gesture))
                    {
                        return;
                    }
                }
            }
            else if (isAllyCommunicating)
            {
                if (!notBusy) return;
                if (!allyGestures.Contains(gesture)) return;
            }
            else
            {
                return;
            }

            Player playerRequester = Singleton<GameWorld>.Instance.GetAlivePlayerByProfileID(data.Player.ProfileId);

            // on gesture "stop" nearby bots will hold position
            if (gesture == EInteraction.HoldGesture)
            {
                if (gestusDistance <= maxGestusDistance)
                {
                    StopCurrRequest(botOwner_0, playerRequester, true);

                    if (botOwner_0.Memory.HaveEnemy)
                    {
                        if (!botOwner_0.Memory.GoalEnemy.IsVisible) botOwner_0.BotTalk.Say(EPhraseTrigger.Negative, true, null);
                        return;
                    }

                    FollowerHold holdit = new FollowerHold(playerRequester);
                    holdit.AddPossibleExecutors(botOwner_0);

                    if (playerRequester.AIData.AskRequests.TryAdd(holdit, botOwner_0.BotsGroup.RequestsController))
                    {
                        botOwner_0.Gesture.TryGestus(EInteraction.OkGesture, false);
                    }
                    else
                    {
                        botOwner_0.Gesture.TryGestus(EInteraction.NoGesture, false);
                    }
                }

                return;
            }
            // on gesture "come here" only the bot that the player is looking at will come to the player
            // on gesture "go there", the closest bot to the user will move forward  
            if (gesture == EInteraction.ComeWithMeGesture || gesture == EInteraction.ThereGesture)
            {
                bool goThere = gesture == EInteraction.ThereGesture;
                if (
                    (IsRequesterLookingAt(botOwner_0, playerRequester) && !goThere) ||
                    (goThere && gestusDistance <= maxGestusDistance && IsClosestBot(botOwner_0, playerRequester))
                )
                {
                    if (!botOwner_0.BotTalk.IsSilenced) botOwner_0.BotTalk.SetSilence(2f);

                    bool hadHold = botOwner_0.BotRequestController.CurRequest?.BotRequestType == BotRequestType.wait;

                    StopCurrRequest(botOwner_0, playerRequester, botOwner_0.Memory.HaveEnemy);

                    if (botOwner_0.Memory.HaveEnemy) (botOwner_0.Brain.BaseBrain as FollowerBrain).BossOrdersChanged();

                    FollowerGoCheck gclass = new FollowerGoCheck(data.Player, goThere ? BotRequestType.goToPoint : BotRequestType.followMe);
                    gclass.AddPossibleExecutors(botOwner_0);

                    if (playerRequester.AIData.AskRequests.TryAdd(gclass, botOwner_0.BotsGroup.RequestsController))
                    {
                        if (hadHold)
                        {
                            FollowerHold holdRequest = new FollowerHold(playerRequester);
                            holdRequest.SetGroup(botOwner_0.BotsGroup.RequestsController);
                            holdRequest.AddPossibleExecutors(botOwner_0);
                        }
                        if (gesture != EInteraction.ThereGesture) botOwner_0.Gesture.TryGestus(EInteraction.OkGesture, false);
                    }
                    else
                    {
                        botOwner_0.Gesture.TryGestus(EInteraction.NoGesture, false);
                        if (botOwner_0.Memory.HaveEnemy) (botOwner_0.Brain.BaseBrain as FollowerBrain).BossOrdersReset();
                    }


                }
                return;
            }
            // on gesture "over there" the bot will turn to the direction the player is looking at
            if (gesture == (EInteraction)CustomGestures.OverThere)
            {
                (botOwner_0.Brain.BaseBrain as FollowerBrain).BossOrdersChanged();
                if (!botOwner_0.Memory.HaveEnemy && !botOwner_0.BotTalk.IsSilenced)
                {
                    botOwner_0.BotTalk.SetSilence(5f);
                }
                FollowerEnemyCheck.CheckBossReport(botOwner_0);

                return;
            }

            if (gesture == EInteraction.OkGesture)
            {
                if (
                    gestusDistance <= maxGestusDistance &&
                    IsRequesterLookingAt(botOwner_0, playerRequester) &&
                    !botOwner_0.Memory.HaveEnemy
                )
                {
                    Utils.Utils.SetTimeout(() =>
                    {
                        if (botOwner_0.BotState == EBotState.Active) botOwner_0.Gesture.TryGestus(EInteraction.OkGesture, false);
                    }, 500);
                }
                return;
            }

            base.method_6(data);
        }

        public virtual void PhraseSaid(BotEventHandler.GClass670 info)
        {
            IPlayer requester = info.PlayerRequester;
            Player playerRequester = Singleton<GameWorld>.Instance.GetAlivePlayerByProfileID(requester.ProfileId);

            bool isBossCommunicating = IsBossRequester(requester);

            bool isAllyRequesting = IsAllyRequester(requester);

            bool isAssisting = (botOwner_0.Brain.BaseBrain as FollowerBrain).currentTactic == "Assist";

            bool shouldDefault = !BossPlayers.IsPlayerBoss(requester.ProfileId);

            pitAIBossPlayer boss = BossPlayers.GetBoss(requester.ProfileId);

            bool isClose = (botOwner_0.GetPlayer.Transform.position - requester.Transform.position).magnitude < 23f;
            bool notBusy = !botOwner_0.Memory.HaveEnemy;

            List<EPhraseTrigger> bossNoPhrase = new List<EPhraseTrigger>
            {
                EPhraseTrigger.OpenDoor,
                EPhraseTrigger.Gogogo,
                EPhraseTrigger.Silence,
                EPhraseTrigger.Fire,
                EPhraseTrigger.GetBack,
            };
            List<EPhraseTrigger> bossBusyIgnore = new List<EPhraseTrigger>
            {
                EPhraseTrigger.Stop,
                EPhraseTrigger.FollowMe,
            };

            List<EPhraseTrigger> bossIgnore = new List<EPhraseTrigger>
            {
                EPhraseTrigger.OnYourOwn
            };

            List<EPhraseTrigger> allyNoPhrase = new List<EPhraseTrigger>{
                EPhraseTrigger.Silence,
                EPhraseTrigger.Fire,
                EPhraseTrigger.GetBack,
                EPhraseTrigger.GoForward,
                EPhraseTrigger.Gogogo,
                EPhraseTrigger.OpenDoor,
                EPhraseTrigger.HoldPosition

            };

            List<EPhraseTrigger> allyBusyIgnore = new List<EPhraseTrigger>
            {
                EPhraseTrigger.Stop,
                EPhraseTrigger.FollowMe
            };

            List<EPhraseTrigger> allyIgnore = new List<EPhraseTrigger>
            {
            };

            bool isFollowerBoss = false;
            foreach (WildSpawnType role in Utils.Props.BossFollowersType)
            {
                if (botOwner_0.IsRole(role))
                {
                    isFollowerBoss = true;
                    break;
                }
            }

            // on Attention reset bot request and enemy state
            if (info.phrase == EPhraseTrigger.Look)
            {
                StopCurrRequest(botOwner_0, playerRequester, true);

                // force current layer to trigger end decision
                AccessTools.Field(typeof(BaseLogicLayerAbstractClass), "bool_1").SetValue(botOwner_0.Brain.BaseBrain.CurLayerInfo, true);
                // try to get bot unstuck in item taker logic
                InteractableObjects.RemoveTaker(botOwner_0);
                // try to get bot unstuck in open door logic
                InteractableObjects.RemoveOpener(botOwner_0);
                // clear current enemy
                if (botOwner_0.Memory.HaveEnemy)
                {
                    botOwner_0.Memory.GoalEnemy.GroupInfo.EnemyLastSeenTimeSense = 0f;
                    botOwner_0.Memory.GoalEnemy.GroupInfo.IsHaveSeen = false;
                    botOwner_0.Memory.GoalEnemy = null;
                }
                // reset hands in case they are stuck
                (botOwner_0.Brain.BaseBrain as FollowerBrain).HandsReset();

                return;
            }

            if (isBossCommunicating)
            {
                // AI Boss followers and AI Bosses will not take several commands
                if (isFollowerBoss)
                {
                    if (bossIgnore.Contains(info.phrase))
                    {
                        return;
                    }

                    if (bossNoPhrase.Contains(info.phrase))
                    {

                        if (!botOwner_0.Memory.HaveEnemy)
                        {
                            botOwner_0.Gesture.TryGestus(EInteraction.NoGesture, false);
                            botOwner_0.BotTalk.TrySay(EPhraseTrigger.Negative, false);
                        }
                        return;
                    }

                    if (botOwner_0.Memory.HaveEnemy && bossBusyIgnore.Contains(info.phrase))
                    {
                        return;
                    }
                }
                // Bots in "Assist" will not take several commands
                else if (isAssisting)
                {
                    if (allyNoPhrase.Contains(info.phrase))
                    {
                        if (notBusy && isClose)
                        {
                            botOwner_0.BotTalk.TrySay(EPhraseTrigger.Negative, false);
                            botOwner_0.Gesture.TryGestus(EInteraction.NoGesture, false);
                        }
                        return;
                    }
                    else if (!notBusy && allyBusyIgnore.Contains(info.phrase))
                    {
                        return;
                    }

                    if (allyIgnore.Contains(info.phrase))
                    {
                        return;
                    }
                }

            }

            if (isAllyRequesting)
            {
                // suppression fire
                if (info.phrase == EPhraseTrigger.Suppress)
                {
                    bool isGrenadier = false;

                    GClass454 selector = botOwner_0.WeaponManager.Selector as GClass454;
                    if (
                        selector != null &&
                        selector.SecondPrimaryWeaponItem as Weapon != null &&
                        (selector.SecondPrimaryWeaponItem as Weapon).IsGrenadeLauncher &&
                        (botOwner_0.Brain.BaseBrain as FollowerBrain)?.defaultTactic == "Guard"
                    )
                    {
                        isGrenadier = true;
                    }
                    // - only boss can request the grenadier to suppress
                    // - only close bots can suppress
                    if (isGrenadier && !isBossCommunicating)
                    {
                        return;
                    }

                    StopCurrRequest(botOwner_0, playerRequester, true);

                    FollowerSuppress gclass = new FollowerSuppress(requester);
                    gclass.AddPossibleExecutors(botOwner_0);

                    EnemyInfo enemyInfo = null;
                    // - switch to enemy closest to boss, if its not the grenadier
                    if (!isGrenadier && isBossCommunicating)
                    {
                        if (!botOwner_0.Memory.HaveEnemy)
                        {
                            boss.PrioritizeEnemy(botOwner_0, boss.ClosestEnemy());
                            enemyInfo = botOwner_0.Memory.GoalEnemy;

                        }
                        else
                        {
                            enemyInfo = botOwner_0.Memory.GoalEnemy;
                        }
                    }

                    if (botOwner_0.Memory.HaveEnemy && playerRequester.AIData.AskRequests.TryAdd(gclass, botOwner_0.BotsGroup.RequestsController))
                    {
                        botOwner_0.BotTalk.TrySay(EPhraseTrigger.Covering, false);
                    }

                    return;
                }
                // on Spreadout look for a random cover
                else if (info.phrase == EPhraseTrigger.Spreadout)
                {
                    if (!isClose) return;

                    StopCurrRequest(botOwner_0, playerRequester, true);

                    (botOwner_0.Brain.BaseBrain as FollowerBrain).BossOrdersChanged();

                    FollowerTakeCover gclass = new FollowerTakeCover(requester);
                    gclass.AddPossibleExecutors(botOwner_0);

                    if (playerRequester.AIData.AskRequests.TryAdd(gclass, botOwner_0.BotsGroup.RequestsController))
                    {
                        if (notBusy || !botOwner_0.Memory.GoalEnemy.IsVisible)
                        {
                            botOwner_0.BotTalk.TrySay(EPhraseTrigger.Going, false);
                        }
                    }

                    return;
                }
            }

            if (!isBossCommunicating)
            {
                return;
            }

            Player botLookedAt = IsRequesterLookingAtSomeone(playerRequester, 37f);

            if (botLookedAt != null && botLookedAt.ProfileId == botOwner_0.ProfileId)
            {
                isClose = true;
            }

            // on Cover Me follow close and try to cover player in fights
            if (info.phrase == EPhraseTrigger.CoverMe && (botLookedAt == null || isClose))
            {
                var brain = botOwner_0.Brain.BaseBrain as FollowerBrain;
                if (brain == null) return;

                // - make bot follow boss near
                brain.ResetFollowDistance();
                brain.SetCanPatrol(false);
                // - reset bot tactic
                brain.SetBossTactic(null);
                // - cover boss when under attack
                brain.SetBossNeedsProtection(true);
                brain.BossOrdersChanged();

                StopCurrRequest(botOwner_0, playerRequester, true);

                // - regroup to boss
                FollowerRegroup gclass = new FollowerRegroup(requester);
                gclass.AddPossibleExecutors(botOwner_0);

                if (playerRequester.AIData.AskRequests.TryAdd(gclass, botOwner_0.BotsGroup.RequestsController))
                {
                    if (isClose && (notBusy || !botOwner_0.Memory.GoalEnemy.IsVisible))
                    {
                        botOwner_0.BotTalk.TrySay(EPhraseTrigger.Roger, false);
                    }
                }
                else
                    (botOwner_0.Brain.BaseBrain as FollowerBrain).BossOrdersReset();

                return;
            }
            // on Get Back follow at a distance
            if (info.phrase == EPhraseTrigger.GetBack && (botLookedAt == null || botLookedAt.ProfileId == botOwner_0.ProfileId))
            {
                (botOwner_0.Brain.BaseBrain as FollowerBrain).SetFollowDistance(20);

                if (isClose)
                {
                    botOwner_0.BotTalk.TrySay(EPhraseTrigger.Roger, false);
                    botOwner_0.Gesture.TryGestus(EInteraction.NoGesture, true);
                }
                return;
            }
            // on Regroup all shall come near the boss
            if (info.phrase == EPhraseTrigger.Regroup)
            {
                (botOwner_0.Brain.BaseBrain as FollowerBrain).BossOrdersChanged();

                StopCurrRequest(botOwner_0, playerRequester, true);

                FollowerRegroup gclass = new FollowerRegroup(requester);
                gclass.AddPossibleExecutors(botOwner_0);

                if (playerRequester.AIData.AskRequests.TryAdd(gclass, botOwner_0.BotsGroup.RequestsController))
                {
                    if (isClose && (notBusy || !botOwner_0.Memory.GoalEnemy.CanShoot))
                    {
                        botOwner_0.BotTalk.TrySay(EPhraseTrigger.Roger, false);
                        botOwner_0.Gesture.TryGestus(EInteraction.OkGesture, false);
                    }
                }
                else
                {
                    (botOwner_0.Brain.BaseBrain as FollowerBrain).BossOrdersReset();

                    if (isClose && (notBusy || !botOwner_0.Memory.GoalEnemy.IsVisible))
                    {
                        botOwner_0.BotRequestController.TrySayNegative(requester, gclass.BotRequestType);
                    }
                }

                return;
            }
            // on Follow Me reset to follower patrol
            if (info.phrase == EPhraseTrigger.FollowMe && (botLookedAt == null || botLookedAt.ProfileId == botOwner_0.ProfileId))
            {

                (botOwner_0.Brain.BaseBrain as FollowerBrain).SetCanPatrol(false);

                if (botOwner_0.Memory.HaveEnemy)
                {
                    botOwner_0.Gesture.TryGestus(EInteraction.NoGesture, true);
                    botOwner_0.BotTalk.TrySay(EPhraseTrigger.DontKnow, false);

                    return;
                }

                StopCurrRequest(botOwner_0, playerRequester, true);

                if (isClose)
                {
                    botOwner_0.Gesture.TryGestus(EInteraction.OkGesture, true);
                }
                return;
            }
            // on Need Help closest bot shall come near boss
            if (info.phrase == EPhraseTrigger.NeedHelp)
            {
                BotOwner closest = null;
                float dist = Mathf.Infinity;
                Vector3 bossPos = boss.realPlayer.Transform.position;

                FollowerRegroup gclass = new FollowerRegroup(requester);

                boss.Followers.ForEach(fl =>
                {
                    if (gclass.CanRequest(botOwner_0))
                    {
                        Vector3 pos = fl.GetPlayer.Transform.position;
                        float fldist = (bossPos - pos).sqrMagnitude;
                        if (fldist < dist)
                        {
                            closest = fl;
                            dist = fldist;
                        }
                    }

                });


                if (closest != null && closest.ProfileId == botOwner_0.ProfileId)
                {
                    (botOwner_0.Brain.BaseBrain as FollowerBrain).BossOrdersChanged();
                    StopCurrRequest(botOwner_0, playerRequester, true);

                    gclass.AddPossibleExecutors(botOwner_0);

                    if (playerRequester.AIData.AskRequests.TryAdd(gclass, botOwner_0.BotsGroup.RequestsController))
                    {
                        gclass.AddPossibleExecutors(botOwner_0);

                        if (isClose) botOwner_0.BotTalk.TrySay(EPhraseTrigger.Roger, false);
                    }
                    else
                        (botOwner_0.Brain.BaseBrain as FollowerBrain).BossOrdersReset();
                }
                return;
            }
            // one Silence be quiet for a minute
            if (info.phrase == EPhraseTrigger.Silence)
            {
                botOwner_0.BotTalk.SetSilence(120f);
                if (isClose)
                {
                    botOwner_0.Gesture.TryGestus(EInteraction.OkGesture, false);
                }
                return;
            }
            // on Go Forward move closer to enemy
            if (info.phrase == EPhraseTrigger.GoForward && (botLookedAt == null || botLookedAt.ProfileId == botOwner_0.ProfileId))
            {
                StopCurrRequest(botOwner_0, playerRequester, true);

                (botOwner_0.Brain.BaseBrain as FollowerBrain).BossOrdersChanged();
                // if has enemy, on "go forward" move in closer to the enemy
                if (botOwner_0.Memory.HaveEnemy)
                {
                    FollowerRushEnemy gclass = new FollowerRushEnemy(botOwner_0, playerRequester);
                    gclass.AddPossibleExecutors(botOwner_0);
                    if (playerRequester.AIData.AskRequests.TryAdd(gclass, botOwner_0.BotsGroup.RequestsController))
                    {
                        if (isClose) botOwner_0.BotTalk.TrySay(EPhraseTrigger.Going, false);
                        return;
                    }

                }
                // else move somewhere in front of the player
                else
                {
                    FollowerGoCheck gclass = new FollowerGoCheck(playerRequester);
                    gclass.AddPossibleExecutors(botOwner_0);

                    if (playerRequester.AIData.AskRequests.TryAdd(gclass, botOwner_0.BotsGroup.RequestsController))
                    {
                        if (isClose)
                        {
                            botOwner_0.BotTalk.TrySay(EPhraseTrigger.Going, true);
                            botOwner_0.Gesture.TryGestus(EInteraction.OkGesture, true);
                        }
                        return;
                    }
                }

                (botOwner_0.Brain.BaseBrain as FollowerBrain).BossOrdersReset();
                return;
            }
            // on Hold Position switch to hold tactic 
            if (info.phrase == EPhraseTrigger.HoldPosition && (botLookedAt == null || botLookedAt.ProfileId == botOwner_0.ProfileId))
            {
                (botOwner_0.Brain.BaseBrain as FollowerBrain).SetBossTactic("Defend");
                (botOwner_0.Brain.BaseBrain as FollowerBrain).BossOrdersChanged();

                if (botOwner_0.BotRequestController.CurRequest != null && botOwner_0.BotRequestController.CurRequest.BotRequestType != BotRequestType.wait)
                    StopCurrRequest(botOwner_0, playerRequester);

                if (isClose)
                {
                    botOwner_0.BotTalk.TrySay(EPhraseTrigger.Roger, false);
                    botOwner_0.Gesture.TryGestus(EInteraction.OkGesture, true);
                }
                return;
            }
            // on Stop hold in place
            if (info.phrase == EPhraseTrigger.Stop && (botLookedAt == null || botLookedAt.ProfileId == botOwner_0.ProfileId))
            {
                if (botOwner_0.Memory.HaveEnemy)
                {
                    if (isClose && !botOwner_0.Memory.GoalEnemy.IsVisible) botOwner_0.BotTalk.Say(EPhraseTrigger.Negative, true, null);
                    return;
                }

                StopCurrRequest(botOwner_0, playerRequester, true);

                FollowerHold holdit = new FollowerHold(playerRequester);
                holdit.AddPossibleExecutors(botOwner_0);

                if (playerRequester.AIData.AskRequests.TryAdd(holdit, botOwner_0.BotsGroup.RequestsController))
                {
                    if (isClose)
                    {
                        botOwner_0.Gesture.TryGestus(EInteraction.OkGesture, true);
                        botOwner_0.BotTalk.TrySay(EPhraseTrigger.Roger, false);
                    }
                }
                else
                {
                    if (isClose)
                    {
                        botOwner_0.BotTalk.TrySay(EPhraseTrigger.Negative, false);
                        botOwner_0.Gesture.TryGestus(EInteraction.NoGesture, true);
                    }
                }
                return;
            }
            // on Go Go Go reset tactic
            if (info.phrase == EPhraseTrigger.Gogogo && (botLookedAt == null || botLookedAt.ProfileId == botOwner_0.ProfileId))
            {
                (botOwner_0.Brain.BaseBrain as FollowerBrain).SetBossTactic(null);
                (botOwner_0.Brain.BaseBrain as FollowerBrain).BossOrdersChanged();

                if (botOwner_0.BotRequestController.CurRequest != null && botOwner_0.BotRequestController.CurRequest.BotRequestType != BotRequestType.wait)
                    StopCurrRequest(botOwner_0, playerRequester);

                if (isClose && notBusy)
                {
                    botOwner_0.BotTalk.TrySay(EPhraseTrigger.Roger, false);
                }
                return;
            }
            // on Contact scan for enemies in front
            if (info.phrase == EPhraseTrigger.OnRepeatedContact)
            {
                (botOwner_0.Brain.BaseBrain as FollowerBrain).BossOrdersChanged();
                FollowerEnemyCheck.CheckBossReport(botOwner_0);
                return;
            }
            // open door request
            if (info.phrase == EPhraseTrigger.OpenDoor && !botOwner_0.Memory.HaveEnemy && isClose)
            {
                Door door = InteractableObjects.GetCurDoor();
                if (door != null)
                {
                    BotOwner closest = null;
                    float dist = 10f;
                    boss.Followers.ForEach(fl =>
                    {
                        Vector3 pos = fl.GetPlayer.Transform.position;
                        float fldist = (door.transform.position - pos).magnitude;
                        if (fldist < dist)
                        {
                            closest = fl;
                            dist = fldist;
                        }

                    });
                    // - the closest bot shall open the door
                    if (closest != null && closest == botOwner_0)
                    {
                        // -- cannot open locked doors
                        if (door.DoorState == EDoorState.Locked)
                        {
                            botOwner_0.BotTalk.TrySay(EPhraseTrigger.Negative, false);
                            botOwner_0.Gesture.TryGestus(EInteraction.NoGesture, false);
                            return;
                        }

                        StopCurrRequest(botOwner_0, playerRequester, true);

                        FollowerOpenDoorRequest gclass = new FollowerOpenDoorRequest(door, requester);
                        gclass.AddPossibleExecutors(botOwner_0);

                        if (playerRequester.AIData.AskRequests.TryAdd(gclass, botOwner_0.BotsGroup.RequestsController))
                        {
                            botOwner_0.BotTalk.TrySay(EPhraseTrigger.Roger, false);
                        }
                        else
                        {
                            botOwner_0.BotTalk.TrySay(EPhraseTrigger.Negative, false);
                            botOwner_0.Gesture.TryGestus(EInteraction.NoGesture, false);
                            InteractableObjects.RemoveOpener(botOwner_0);
                        }
                    }
                }
                return;
            }
            // loot item
            if (info.phrase == EPhraseTrigger.LootGeneric || info.phrase == EPhraseTrigger.LootWeapon)
            {
                if (!isClose) return;

                if (!notBusy && botOwner_0.Memory.GoalEnemy.HaveSeen && Time.time - botOwner_0.Memory.GoalEnemy.PersonalLastSeenTime < 3f)
                {

                    botOwner_0.BotTalk.TrySay(EPhraseTrigger.DontKnow, false);
                    return;
                }

                if ((botOwner_0.Brain.BaseBrain as FollowerBrain).UnderFire)
                {
                    botOwner_0.BotTalk.TrySay(EPhraseTrigger.DontKnow, false);
                    return;
                }

                LootItem item = InteractableObjects.GetCurLootItem();
                if (item != null)
                {
                    float dist = Mathf.Infinity;
                    BotOwner closest = null;
                    try
                    {
                        boss.Followers.ForEach(fl =>
                        {
                            Vector3 pos = fl.GetPlayer.Transform.position;
                            float fldist = (item.transform.position - pos).sqrMagnitude;
                            //fl.HealthController.
                            if (fldist < dist)
                            {
                                closest = fl;
                                dist = fldist;
                            }

                        });
                    }
                    catch
                    {
                        closest = null;
                    }

                    if (closest != null && closest.ProfileId == botOwner_0.ProfileId)
                    {
                        if (InteractableObjects.SetTaker(botOwner_0))
                        {
                            bool fromWait = botOwner_0.BotRequestController.CurRequest != null && botOwner_0.BotRequestController.CurRequest.BotRequestType == BotRequestType.wait;

                            StopCurrRequest(botOwner_0, playerRequester);

                            FollowerTakeLootRequest gclass = new FollowerTakeLootRequest(requester);
                            gclass.AddPossibleExecutors(botOwner_0);

                            if (playerRequester.AIData.AskRequests.TryAdd(gclass, botOwner_0.BotsGroup.RequestsController))
                            {
                                Modules.Logger.LogInfo("Loot request added");
                                if (isClose) botOwner_0.BotTalk.TrySay(EPhraseTrigger.Roger, false);
                                if (fromWait)
                                {
                                    FollowerHold holdRequest = new FollowerHold(playerRequester);
                                    holdRequest.SetGroup(botOwner_0.BotsGroup.RequestsController);
                                    holdRequest.AddPossibleExecutors(botOwner_0);
                                }
                                return;
                            }
                            else
                            {
                                if (isClose)
                                {
                                    botOwner_0.BotTalk.TrySay(EPhraseTrigger.Negative, false);
                                    botOwner_0.Gesture.TryGestus(EInteraction.NoGesture, true);
                                }
                                InteractableObjects.RemoveTaker(botOwner_0);
                            }
                        }
                    }
                }
                return;
            }

            if (info.phrase == (EPhraseTrigger)CustomPhrases.TeamStatus)
            {
                if (notBusy && isClose)
                {
                    botOwner_0.Gesture.TryGestus(EInteraction.FriendlyGesture, true);
                }
                return;
            }

            if (info.phrase == EPhraseTrigger.ExitLocated)
            {
                float proximity = (botOwner_0.GetPlayer.Transform.position - requester.Transform.position).sqrMagnitude;

                if (proximity > 15f * 15f)
                {
                    (botOwner_0.Brain.BaseBrain as FollowerBrain).BossOrdersChanged();
                }
                else
                {
                    if (notBusy) botOwner_0.BotTalk.TrySay(EPhraseTrigger.GoodWork, false);
                    return;
                }

                StopCurrRequest(botOwner_0, playerRequester, true);

                FollowerRegroup gclass = new FollowerRegroup(requester);
                gclass.AddPossibleExecutors(botOwner_0);

                if (playerRequester.AIData.AskRequests.TryAdd(gclass, botOwner_0.BotsGroup.RequestsController))
                {
                    if (isClose && notBusy)
                    {
                        botOwner_0.BotTalk.TrySay(EPhraseTrigger.GoodWork, false);
                    }
                }
                return;
            }
            // on On Your Own do not cover player when under attack
            else if (info.phrase == EPhraseTrigger.OnYourOwn && (botLookedAt == null || botLookedAt.ProfileId == botOwner_0.ProfileId))
            {
                var brain = (botOwner_0.Brain.BaseBrain as FollowerBrain);
                brain.SetBossNeedsProtection(false);
                brain.SetBossTactic(null);
                brain.SetFollowDistance(20);
                brain.SetCanPatrol(true);

                StopCurrRequest(botOwner_0, playerRequester, true);

                if (isClose) botOwner_0.BotTalk.TrySay(EPhraseTrigger.Roger, false);

            }

            if (info.phrase == EPhraseTrigger.InTheFront)
            {

                FollowerBrain brain = botOwner_0.Brain.BaseBrain as FollowerBrain;
                if (brain != null) brain.FakeShot(playerRequester.MainParts[BodyPartType.head].Position + playerRequester.LookDirection * 20f);
                return;
            }
            if (info.phrase == EPhraseTrigger.OnSix && (botLookedAt == null || botLookedAt.ProfileId == botOwner_0.ProfileId))
            {
                FollowerBrain brain = botOwner_0.Brain.BaseBrain as FollowerBrain;
                if (brain != null) brain.FakeShot(playerRequester.MainParts[BodyPartType.head].Position - playerRequester.LookDirection * 20f);
                return;
            }
            if (info.phrase == EPhraseTrigger.LeftFlank && (botLookedAt == null || botLookedAt.ProfileId == botOwner_0.ProfileId))
            {
                FollowerBrain brain = botOwner_0.Brain.BaseBrain as FollowerBrain;
                if (brain != null) brain.FakeShot(playerRequester.MainParts[BodyPartType.head].Position + Quaternion.Euler(0, -90, 0) * playerRequester.LookDirection * 20f);
                return;
            }
            if (info.phrase == EPhraseTrigger.RightFlank && (botLookedAt == null || botLookedAt.ProfileId == botOwner_0.ProfileId))
            {
                FollowerBrain brain = botOwner_0.Brain.BaseBrain as FollowerBrain;
                if (brain != null) brain.FakeShot(playerRequester.MainParts[BodyPartType.head].Position + Quaternion.Euler(0, 90, 0) * playerRequester.LookDirection * 20f);
                return;
            }

            base.method_0(info);
        }

        public new void Dispose()
        {
            Destroy();
        }
    }
}
