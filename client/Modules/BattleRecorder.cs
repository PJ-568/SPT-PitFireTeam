using BepInEx;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using pitTeam.BigBrain;
using pitTeam.Components;
using pitTeam.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace pitTeam.Modules
{
    internal static class BattleRecorder
    {
        private const string UpdateHubSubscriptionId = "pitTeam.BattleRecorder";

        private static readonly object SyncRoot = new object();
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Formatting = Formatting.None
        };

        private static readonly Dictionary<string, RecorderFollowerState> FollowerStates =
            new Dictionary<string, RecorderFollowerState>(StringComparer.Ordinal);

        private static StreamWriter? writer;
        private static string? currentRaidId;
        private static string? currentLocationId;
        private static string? currentFilePath;
        private static int eventSequence;
        private static bool initialized;
        private static bool writeErrorLogged;

        public static void Initialize()
        {
            if (initialized)
            {
                return;
            }

            BotOwnerUpdateHub.Register(UpdateHubSubscriptionId, OnBotManualUpdate);
            initialized = true;
        }

        public static void Shutdown()
        {
            if (!initialized)
            {
                return;
            }

            BotOwnerUpdateHub.Unregister(UpdateHubSubscriptionId);
            initialized = false;
            EndRaid("pluginShutdown");
        }

        public static void StartRaid(string? locationId)
        {
            if (!IsEnabled())
            {
                EndRaid("disabled");
                return;
            }

            EndRaid("newRaid");

            try
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                currentLocationId = string.IsNullOrWhiteSpace(locationId) ? "unknown" : locationId;
                currentRaidId = $"{timestamp}-{currentLocationId}";

                string rootDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BepInEx", "plugins", "pitFireTeam", "BattleRecords");
                Directory.CreateDirectory(rootDirectory);

                currentFilePath = Path.Combine(rootDirectory, $"{currentRaidId}.jsonl");
                writer = new StreamWriter(currentFilePath, false)
                {
                    AutoFlush = true
                };

                eventSequence = 0;
                writeErrorLogged = false;
                FollowerStates.Clear();

                WriteEventInternal("raidStart", null, new
                {
                    raidId = currentRaidId,
                    locationId = currentLocationId,
                    file = currentFilePath,
                    snapshotIntervalMs = GetSnapshotIntervalMs()
                });
            }
            catch (Exception ex)
            {
                SafeLogRecorderError("Failed to start battle recorder.", ex);
                DisposeWriter();
            }
        }

        public static void EndRaid(string reason)
        {
            try
            {
                if (writer != null)
                {
                    WriteEventInternal("raidEnd", null, new
                    {
                        raidId = currentRaidId,
                        locationId = currentLocationId,
                        reason
                    });
                }
            }
            catch (Exception ex)
            {
                SafeLogRecorderError("Failed to finalize battle recorder.", ex);
            }
            finally
            {
                DisposeWriter();
                FollowerStates.Clear();
                currentRaidId = null;
                currentLocationId = null;
                currentFilePath = null;
                eventSequence = 0;
                writeErrorLogged = false;
            }
        }

        public static void RecordCommandSet(
            BotFollowerPlayer follower,
            FollowerCommandType command,
            Vector3 target,
            float untilTime,
            string source)
        {
            BotOwner? bot = follower?.GetBot();
            if (!CanRecordBot(bot))
            {
                return;
            }

            RecorderFollowerState state = GetOrCreateState(bot!);

            WriteEventInternal("commandSet", bot, new
            {
                source,
                command = command.ToString(),
                untilTime = SanitizeFloat(untilTime),
                target = CreateVector(target),
                tactic = follower.CombatTactic.ToString(),
                state = CreateRecorderStatePayload(state)
            });
        }

        public static void RecordCommandCleared(
            BotFollowerPlayer follower,
            FollowerCommandType previousCommand,
            Vector3 previousTarget,
            float previousUntilTime,
            string reason)
        {
            BotOwner? bot = follower?.GetBot();
            if (!CanRecordBot(bot) || previousCommand == FollowerCommandType.None)
            {
                return;
            }

            RecorderFollowerState state = GetOrCreateState(bot!);

            WriteEventInternal("commandCleared", bot, new
            {
                reason,
                command = previousCommand.ToString(),
                untilTime = SanitizeFloat(previousUntilTime),
                target = CreateVector(previousTarget),
                tactic = follower.CombatTactic.ToString(),
                state = CreateRecorderStatePayload(state)
            });
        }

        public static void RecordCombatLayerState(BotOwner bot, bool active, string reason)
        {
            if (!CanRecordBot(bot))
            {
                return;
            }

            RecorderFollowerState state = GetOrCreateState(bot);
            state.InCombat = active;
            state.LastCombatSeenTime = Time.time;
            if (active)
            {
                state.NextSnapshotTime = Time.time;
            }

            WriteEventInternal(active ? "combatStart" : "combatStop", bot, new
            {
                reason,
                state = CreateRecorderStatePayload(state),
                snapshot = active ? CreateBotSnapshot(bot, state) : null
            });

            if (active)
            {
                RecordPreexistingGoalEnemy(bot, state, reason);
            }
        }

        private static void RecordPreexistingGoalEnemy(BotOwner bot, RecorderFollowerState state, string combatStartReason)
        {
            EnemyInfo? goalEnemy = bot?.Memory?.GoalEnemy;
            if (goalEnemy == null)
            {
                return;
            }

            Player? enemyPlayer = goalEnemy.Person as Player;
            Vector3 botPosition = bot.Position;
            Vector3 enemyPosition = enemyPlayer?.Transform?.position
                ?? enemyPlayer?.Position
                ?? goalEnemy.EnemyLastPositionReal;

            WriteEventInternal("enemyAcquisitionObserved", bot, new
            {
                source = "combatStartPreexistingGoalEnemy",
                reason = "Combat layer started with GoalEnemy already present; original acquisition happened before pitFireTeam Enemy.MakeEnemy logging.",
                combatStartReason,
                enemy = new
                {
                    profileId = goalEnemy.ProfileId,
                    nickname = enemyPlayer?.Profile?.Nickname,
                    side = enemyPlayer?.Side.ToString(),
                    role = enemyPlayer?.Profile?.Info?.Settings?.Role.ToString(),
                    isAi = enemyPlayer?.IsAI,
                    position = CreateVector(enemyPosition),
                    distance = SanitizeFloat(Vector3.Distance(botPosition, enemyPosition)),
                    lineOfSight = enemyPlayer != null && HasSimpleLineOfSight(bot.GetPlayer, enemyPlayer),
                    isVisible = goalEnemy.IsVisible,
                    canShoot = goalEnemy.CanShoot,
                    haveSeen = goalEnemy.HaveSeen,
                    personalSeenTime = SanitizeFloat(goalEnemy.PersonalSeenTime),
                    personalLastSeenTime = SanitizeFloat(goalEnemy.PersonalLastSeenTime),
                    lastKnownPosition = CreateVector(goalEnemy.EnemyLastPositionReal)
                },
                memory = new
                {
                    haveEnemy = bot.Memory?.HaveEnemy == true,
                    underFire = bot.Memory?.IsUnderFire == true,
                    inCover = bot.Memory?.IsInCover == true
                },
                state = CreateRecorderStatePayload(state)
            });
        }

        public static void RecordFollowerDeath(
            BotFollowerPlayer follower,
            Player player,
            IPlayer? aggressor,
            EBodyPart bodyPart,
            EDamageType lethalDamageType)
        {
            BotOwner? bot = follower?.GetBot();
            if (!IsRecording() || player == null || string.IsNullOrEmpty(player.ProfileId))
            {
                return;
            }

            RecorderFollowerState state = bot != null
                ? GetOrCreateState(bot)
                : GetOrCreateState(player.ProfileId);

            WriteEventInternal("followerDeath", bot, new
            {
                profileId = player.ProfileId,
                nickname = player.Profile?.Nickname,
                position = CreateVector(player.Transform.position),
                bodyPart = bodyPart.ToString(),
                lethalDamageType = lethalDamageType.ToString(),
                aggressor = aggressor != null
                    ? new
                    {
                        profileId = aggressor.ProfileId,
                        nickname = aggressor.Profile?.Nickname,
                        side = aggressor.Side.ToString()
                    }
                    : null,
                botState = bot?.BotState.ToString(),
                isDead = bot?.IsDead,
                medical = new
                {
                    healthStatus = player.HealthStatus.ToString()
                },
                state = CreateRecorderStatePayload(state),
                snapshot = bot != null ? CreateBotSnapshot(bot, state) : null
            });
        }

        public static void RecordDecisionSelected(
            BotOwner bot,
            AICoreActionResultStruct<BotLogicDecision, GClass26>? previousDecision,
            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision,
            string? objectiveName)
        {
            if (!CanRecordBot(bot))
            {
                return;
            }

            RecorderFollowerState state = GetOrCreateState(bot);
            state.InCombat = true;
            state.LastCombatSeenTime = Time.time;
            state.LastDecisionAction = nextDecision.Action.ToString();
            state.LastDecisionReason = nextDecision.Reason;
            if (!string.IsNullOrEmpty(objectiveName))
            {
                state.CurrentObjective = objectiveName;
            }

            WriteEventInternal("decisionSelected", bot, new
            {
                objective = objectiveName,
                previous = previousDecision.HasValue ? CreateDecisionPayload(previousDecision.Value) : null,
                next = CreateDecisionPayload(nextDecision),
                state = CreateRecorderStatePayload(state)
            });
        }

        public static void RecordDecisionEnd(
            BotOwner bot,
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision,
            AICoreActionEndStruct endResult,
            string? objectiveName)
        {
            if (!CanRecordBot(bot))
            {
                return;
            }

            RecorderFollowerState state = GetOrCreateState(bot);
            if (!string.IsNullOrEmpty(objectiveName))
            {
                state.CurrentObjective = objectiveName;
            }

            WriteEventInternal("decisionEnd", bot, new
            {
                objective = objectiveName,
                decision = CreateDecisionPayload(currentDecision),
                end = new
                {
                    shouldEnd = endResult.Value,
                    reason = endResult.Reason
                },
                state = CreateRecorderStatePayload(state)
            });
        }

        public static void RecordObjectiveSwitch(BotOwner bot, string objectiveName, string reason)
        {
            if (!CanRecordBot(bot))
            {
                return;
            }

            RecorderFollowerState state = GetOrCreateState(bot);
            state.CurrentObjective = objectiveName;
            state.LastCombatSeenTime = Time.time;

            WriteEventInternal("objectiveSwitch", bot, new
            {
                objective = objectiveName,
                reason,
                state = CreateRecorderStatePayload(state)
            });
        }

        public static void RecordEnemyAcquired(
            BotOwner bot,
            Player enemy,
            EnemyInfo info,
            EBotEnemyCause cause,
            string source,
            bool hadGroupEnemyBefore,
            bool hadEnemyInfoBefore,
            bool hadGoalEnemyBefore,
            string? previousGoalEnemyProfileId,
            bool retriedWithCheckAdd,
            bool forcedMemoryAdd)
        {
            if (!CanRecordBot(bot) || enemy == null || info == null)
            {
                return;
            }

            RecorderFollowerState state = GetOrCreateState(bot);
            Vector3 botPosition = bot.Position;
            Vector3 enemyPosition = enemy.Transform?.position ?? enemy.Position;
            bool followerLineOfSight = HasSimpleLineOfSight(bot.GetPlayer, enemy);
            Player? bossPlayer = (bot.BotFollower?.BossToFollow as pitAIBossPlayer)?.realPlayer;
            BotOwner? enemyBot = enemy.IsAI ? enemy.AIData?.BotOwner : null;

            WriteEventInternal("enemyAcquired", bot, new
            {
                source,
                cause = cause.ToString(),
                acquisition = new
                {
                    hadGroupEnemyBefore,
                    hadEnemyInfoBefore,
                    hadGoalEnemyBefore,
                    previousGoalEnemyProfileId,
                    retriedWithCheckAdd,
                    forcedMemoryAdd
                },
                enemy = new
                {
                    profileId = enemy.ProfileId,
                    nickname = enemy.Profile?.Nickname,
                    side = enemy.Side.ToString(),
                    role = enemy.Profile?.Info?.Settings?.Role.ToString(),
                    isAi = enemy.IsAI,
                    position = CreateVector(enemyPosition),
                    distance = SanitizeFloat(Vector3.Distance(botPosition, enemyPosition)),
                    lineOfSight = followerLineOfSight,
                    groupId = enemyBot?.BotsGroup?.Id.ToString(),
                    isVisible = info.IsVisible,
                    canShoot = info.CanShoot,
                    haveSeen = info.HaveSeen,
                    personalSeenTime = SanitizeFloat(info.PersonalSeenTime),
                    personalLastSeenTime = SanitizeFloat(info.PersonalLastSeenTime),
                    lastKnownPosition = CreateVector(info.EnemyLastPositionReal)
                },
                boss = bossPlayer != null
                    ? new
                    {
                        profileId = bossPlayer.ProfileId,
                        distanceToEnemy = SanitizeFloat(Vector3.Distance(bossPlayer.Transform.position, enemyPosition)),
                        lineOfSightToEnemy = HasSimpleLineOfSight(bossPlayer, enemy)
                    }
                    : null,
                relations = new
                {
                    sameSide = bot.Side == enemy.Side,
                    followerGroupEnemy = bot.BotsGroup?.IsEnemy(enemy) == true,
                    followerGroupPlayerEnemy = bot.BotsGroup?.IsPlayerEnemy(enemy) == true,
                    enemyGroupHasFollowerEnemy = enemyBot?.BotsGroup?.IsEnemy(bot.GetPlayer) == true,
                    enemyGroupHasBossEnemy = bossPlayer != null && enemyBot?.BotsGroup?.IsEnemy(bossPlayer) == true
                },
                enemyBotState = CreateObservedEnemyBotState(enemyBot, bot.GetPlayer, bossPlayer),
                memory = new
                {
                    haveEnemy = bot.Memory?.HaveEnemy == true,
                    goalEnemyProfileId = bot.Memory?.GoalEnemy?.ProfileId,
                    underFire = bot.Memory?.IsUnderFire == true,
                    inCover = bot.Memory?.IsInCover == true
                },
                state = CreateRecorderStatePayload(state),
                snapshot = CreateBotSnapshot(bot, state)
            });
        }

        public static void RecordGroupAddEnemyResult(BotsGroup? group, IPlayer? target, EBotEnemyCause cause, bool result)
        {
            if (!IsRecording() || group == null || target == null || !IsBossOrFollowerTarget(target))
            {
                return;
            }

            WriteEventInternal("groupAddEnemyResult", null, new
            {
                cause = cause.ToString(),
                result,
                target = CreatePlayerRef(target),
                targetIsPlayerBoss = BossPlayers.IsPlayerBoss(target.ProfileId),
                targetIsFollower = BossPlayers.IsFollowerProfileId(target.ProfileId),
                group = new
                {
                    id = group.Id.ToString(),
                    side = group.Side.ToString(),
                    type = group.GetType().Name,
                    initialBotType = group.InitialBotType.ToString(),
                    membersCount = group.MembersCount,
                    hasTargetEnemy = group.IsEnemy(target),
                    hasTargetPlayerEnemy = group.IsPlayerEnemy(target)
                },
                flags = new
                {
                    pitFireTeam = Utils.Utils.FlagGet("pitFireTeam"),
                    badGuy = Utils.Utils.FlagGet("isBadGuy")
                },
                members = CreateGroupMemberStates(group, target)
            });
        }

        public static void RecordDirectDamageRetaliationBridge(
            BotsGroup? group,
            IPlayer? attacker,
            BotOwner? damagedBot,
            bool addedAttacker,
            int promoted,
            bool addedBoss,
            int promotedBoss,
            int lootingInterrupted,
            int lootingInterruptedBoss)
        {
            if (!IsRecording() || group == null || attacker == null || damagedBot == null)
            {
                return;
            }

            WriteEventInternal("directDamageRetaliationBridge", damagedBot, new
            {
                attacker = CreatePlayerRef(attacker),
                damaged = CreateBotOwnerRef(damagedBot),
                addedAttacker,
                promoted,
                addedBoss,
                promotedBoss,
                lootingInterrupted,
                lootingInterruptedBoss,
                group = new
                {
                    id = group.Id.ToString(),
                    side = group.Side.ToString(),
                    type = group.GetType().Name,
                    initialBotType = group.InitialBotType.ToString(),
                    membersCount = group.MembersCount,
                    hasAttackerEnemy = group.IsEnemy(attacker),
                    hasAttackerPlayerEnemy = group.IsPlayerEnemy(attacker)
                },
                members = CreateGroupMemberStates(group, attacker)
            });
        }

        public static void RecordPushEmitted(
            BotOwner owner,
            string enemyProfileId,
            Vector3 enemyPosition,
            Vector3 destination,
            string reason,
            bool isSearchPush)
        {
            if (!CanRecordBot(owner))
            {
                return;
            }

            RecorderFollowerState state = GetOrCreateState(owner);
            if (!IsBotInRecordedCombat(owner, state))
            {
                return;
            }

            WriteEventInternal("pushEvent", owner, new
            {
                action = "emit",
                enemyProfileId,
                enemyPosition = CreateVector(enemyPosition),
                destination = CreateVector(destination),
                reason,
                isSearchPush
            });
        }

        public static void RecordPushReleased(BotOwner owner, string reason)
        {
            if (!CanRecordBot(owner))
            {
                return;
            }

            RecorderFollowerState state = GetOrCreateState(owner);
            if (!IsBotInRecordedCombat(owner, state))
            {
                return;
            }

            WriteEventInternal("pushEvent", owner, new
            {
                action = "release",
                reason
            });
        }

        public static void RecordGrenadeEvent(BotOwner bot, string action, string reason, bool? completed = null)
        {
            if (!CanRecordBot(bot))
            {
                return;
            }

            RecorderFollowerState state = GetOrCreateState(bot);
            if (!IsBotInRecordedCombat(bot, state))
            {
                return;
            }

            WriteEventInternal("grenadeEvent", bot, new
            {
                action,
                reason,
                completed,
                state = CreateRecorderStatePayload(state)
            });
        }

        public static void RecordPushCleared(string reason)
        {
            if (!IsRecording() || !AnyFollowerInRecordedCombat())
            {
                return;
            }

            WriteEventInternal("pushEvent", null, new
            {
                action = "clear",
                reason
            });
        }

        public static void RecordCommitmentEvent(
            BotOwner bot,
            string commitment,
            string action,
            string? reason,
            AICoreActionResultStruct<BotLogicDecision, GClass26>? decision = null,
            Vector3? target = null,
            int? coverId = null,
            bool? preferCover = null,
            float? untilTime = null)
        {
            if (!CanRecordBot(bot))
            {
                return;
            }

            RecorderFollowerState state = GetOrCreateState(bot);
            if (!IsBotInRecordedCombat(bot, state))
            {
                return;
            }

            WriteEventInternal("commitmentEvent", bot, new
            {
                commitment,
                action,
                reason,
                decision = decision.HasValue ? CreateDecisionPayload(decision.Value) : null,
                target = target.HasValue && IsFinite(target.Value) ? CreateVector(target.Value) : null,
                coverId,
                preferCover,
                untilTime = untilTime.HasValue ? SanitizeFloat(untilTime.Value) : null,
                context = CreateTransitionContext(bot, state)
            });
        }

        private static void OnBotManualUpdate(BotOwner owner)
        {
            if (!CanRecordBot(owner))
            {
                return;
            }

            RecorderFollowerState state = GetOrCreateState(owner);
            bool layerActive = FollowerCombatLayer.IsFollowerCombatLayerActive(owner);
            bool shouldSnapshot = state.InCombat || layerActive;
            if (!shouldSnapshot)
            {
                return;
            }

            if (Time.time < state.NextSnapshotTime)
            {
                return;
            }

            state.NextSnapshotTime = Time.time + GetSnapshotIntervalSeconds();
            WriteEventInternal("snapshot", owner, CreateBotSnapshot(owner, state));
        }

        private static object CreateBotSnapshot(BotOwner bot, RecorderFollowerState state)
        {
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(bot);
            FollowerCommandType command = FollowerCommandType.None;
            Vector3 commandTarget = Vector3.zero;
            float commandUntilTime = 0f;
            if (followerData != null)
            {
                followerData.TryPeekActiveCommand(out command, out commandTarget, out commandUntilTime);
            }

            Vector3 currentPosition = bot.Position;
            Vector3 lookDirection = NormalizePlanar(bot.LookDirection);
            bool hasMoveTarget = TryGetCurrentMoveTarget(bot, out Vector3 moveTarget);
            Vector3 moveTargetDirection = hasMoveTarget
                ? NormalizePlanar(moveTarget - currentPosition)
                : Vector3.zero;

            Vector3 movementDirection = Vector3.zero;
            bool hasMovementDirection = false;
            if (state.HasPreviousSnapshot)
            {
                Vector3 delta = currentPosition - state.LastSnapshotPosition;
                if (delta.sqrMagnitude > 0.0025f)
                {
                    movementDirection = NormalizePlanar(delta);
                    hasMovementDirection = movementDirection.sqrMagnitude > 0.0001f;
                }
            }

            EnemyInfo? goalEnemy = bot.Memory?.GoalEnemy;
            object? enemySnapshot = CreateEnemySnapshot(goalEnemy, currentPosition, lookDirection, moveTargetDirection);
            object? bossSnapshot = CreateBossSnapshot(bot, currentPosition, lookDirection);

            var snapshot = new
            {
                state = CreateRecorderStatePayload(state),
                botState = bot.BotState.ToString(),
                position = CreateVector(currentPosition),
                lookDirection = CreateVector(lookDirection),
                currentMoveTarget = hasMoveTarget ? CreateVector(moveTarget) : null,
                movement = new
                {
                    sprinting = bot.Mover?.Sprinting == true,
                    hasPathTarget = bot.GoToSomePointData?.HaveTarget() == true,
                    reachedTarget = bot.GoToSomePointData?.IsCome() == true,
                    direction = hasMovementDirection ? CreateVector(movementDirection) : null,
                    lookVsMoveAngle = hasMovementDirection ? SanitizeFloat(Vector3.Angle(lookDirection, movementDirection)) : null,
                    lookVsMoveTargetAngle = hasMoveTarget ? SanitizeFloat(Vector3.Angle(lookDirection, moveTargetDirection)) : null
                },
                memory = new
                {
                    haveEnemy = bot.Memory?.HaveEnemy == true,
                    underFire = bot.Memory?.IsUnderFire == true,
                    inCover = bot.Memory?.IsInCover == true,
                    damagedRecently = FollowerCombatCommon.WasHitRecently(bot, 0.5f) ||
                                      FollowerAwareness.WasRecentlyDamaged(bot),
                    threatenedRecently = FollowerAwareness.WasRecentlyThreatened(bot),
                    hitRecently = FollowerCombatCommon.WasHitRecently(bot, 0.5f) ||
                                  FollowerAwareness.WasRecentlyHit(bot)
                },
                command = followerData != null && command != FollowerCommandType.None
                    ? new
                    {
                        type = command.ToString(),
                        target = CreateVector(commandTarget),
                        untilTime = SanitizeFloat(commandUntilTime)
                    }
                    : null,
                medical = new
                {
                    firstAidPending = bot.Medecine?.FirstAid?.Have2Do == true,
                    firstAidUsing = bot.Medecine?.FirstAid?.Using == true,
                    surgeryPending = bot.Medecine?.SurgicalKit?.HaveWork == true,
                    surgeryUsing = bot.Medecine?.SurgicalKit?.Using == true,
                    healthStatus = bot.GetPlayer?.HealthStatus.ToString()
                },
                dogFight = bot.DogFight?.DogFightState.ToString(),
                weapon = CreateLightWeaponSnapshot(bot),
                enemy = enemySnapshot,
                boss = bossSnapshot,
                tactic = followerData?.CombatTactic.ToString()
            };

            state.LastSnapshotPosition = currentPosition;
            state.HasPreviousSnapshot = true;
            return snapshot;
        }

        private static object CreateDecisionPayload(AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            return new
            {
                action = decision.Action.ToString(),
                reason = decision.Reason,
                dataType = decision.Data?.GetType().Name
            };
        }

        private static object CreateRecorderStatePayload(RecorderFollowerState state)
        {
            return new
            {
                inCombat = state.InCombat,
                objective = state.CurrentObjective,
                lastDecisionAction = state.LastDecisionAction,
                lastDecisionReason = state.LastDecisionReason
            };
        }

        private static object CreateTransitionContext(BotOwner bot, RecorderFollowerState state)
        {
            EnemyInfo? goalEnemy = bot.Memory?.GoalEnemy;
            return new
            {
                state = CreateRecorderStatePayload(state),
                memory = new
                {
                    haveEnemy = bot.Memory?.HaveEnemy == true,
                    underFire = bot.Memory?.IsUnderFire == true,
                    inCover = bot.Memory?.IsInCover == true,
                    damagedRecently = FollowerCombatCommon.WasHitRecently(bot, 0.5f) ||
                                      FollowerAwareness.WasRecentlyDamaged(bot),
                    threatenedRecently = FollowerAwareness.WasRecentlyThreatened(bot),
                    hitRecently = FollowerCombatCommon.WasHitRecently(bot, 0.5f) ||
                                  FollowerAwareness.WasRecentlyHit(bot)
                },
                enemy = CreateTransitionEnemyContext(goalEnemy),
                boss = CreateTransitionBossContext(bot)
            };
        }

        private static object? CreateTransitionEnemyContext(EnemyInfo? goalEnemy)
        {
            if (goalEnemy == null)
            {
                return null;
            }

            return new
            {
                profileId = goalEnemy.ProfileId,
                distance = SanitizeFloat(goalEnemy.Distance),
                isVisible = goalEnemy.IsVisible,
                canShoot = goalEnemy.CanShoot,
                personalSeenTime = SanitizeFloat(goalEnemy.PersonalSeenTime),
                personalLastSeenTime = SanitizeFloat(goalEnemy.PersonalLastSeenTime)
            };
        }

        private static object? CreateTransitionBossContext(BotOwner bot)
        {
            if (bot.BotFollower?.BossToFollow is not pitAIBossPlayer boss)
            {
                return null;
            }

            Vector3 bossPosition = boss.realPlayer != null
                ? boss.realPlayer.Transform.position
                : boss.Position;
            return new
            {
                distance = SanitizeFloat(Vector3.Distance(bot.Position, bossPosition))
            };
        }

        private static object? CreateWeaponSnapshot(BotOwner bot)
        {
            BotWeaponSelector? selector = bot.WeaponManager?.Selector;
            Weapon? activeWeapon = bot.WeaponManager?.ShootController?.Item;
            Weapon? firstPrimary = bot.GetPlayer?.InventoryController?.Inventory?.Equipment?.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem as Weapon;
            Weapon? secondPrimary = bot.GetPlayer?.InventoryController?.Inventory?.Equipment?.GetSlot(EquipmentSlot.SecondPrimaryWeapon)?.ContainedItem as Weapon;

            return new
            {
                currentSlot = selector?.LastEquipmentSlot.ToString(),
                active = CreateWeaponSlotSnapshot(activeWeapon),
                firstPrimary = CreateWeaponSlotSnapshot(firstPrimary),
                secondPrimary = CreateWeaponSlotSnapshot(secondPrimary)
            };
        }

        private static object? CreateLightWeaponSnapshot(BotOwner bot)
        {
            BotWeaponSelector? selector = bot.WeaponManager?.Selector;
            Weapon? activeWeapon = bot.WeaponManager?.ShootController?.Item;

            return new
            {
                currentSlot = selector?.LastEquipmentSlot.ToString(),
                active = activeWeapon != null
                    ? new
                    {
                        type = activeWeapon.GetType().Name,
                        magazineCount = activeWeapon.GetCurrentMagazine()?.Cartridges?.Count
                    }
                    : null
            };
        }

        private static bool IsBossOrFollowerTarget(IPlayer target)
        {
            return BossPlayers.IsPlayerBoss(target.ProfileId) ||
                   BossPlayers.IsFollowerProfileId(target.ProfileId);
        }

        private static object CreatePlayerRef(IPlayer player)
        {
            return new
            {
                profileId = player.ProfileId,
                nickname = player.Profile?.Nickname,
                side = player.Side.ToString(),
                role = player.Profile?.Info?.Settings?.Role.ToString(),
                isAi = player.IsAI,
                groupId = player.IsAI ? player.AIData?.BotOwner?.BotsGroup?.Id.ToString() : null
            };
        }

        private static object CreateBotOwnerRef(BotOwner bot)
        {
            return new
            {
                profileId = bot.ProfileId,
                nickname = bot.Profile?.Nickname,
                side = bot.Side.ToString(),
                role = bot.Profile?.Info?.Settings?.Role.ToString(),
                groupId = bot.BotsGroup?.Id.ToString()
            };
        }

        private static object[] CreateGroupMemberStates(BotsGroup group, IPlayer target)
        {
            int count = Math.Max(0, group.MembersCount);
            List<object> members = new List<object>(count);
            Player? targetPlayer = target as Player;

            for (int i = 0; i < count; i++)
            {
                BotOwner member = group.Member(i);
                if (member == null)
                {
                    continue;
                }

                EnemyInfo? targetEnemyInfo = null;
                if (targetPlayer != null)
                {
                    member.EnemiesController?.EnemyInfos?.TryGetValue(targetPlayer, out targetEnemyInfo);
                }

                members.Add(CreateGroupMemberState(member, targetEnemyInfo, target));
            }

            return members.ToArray();
        }

        private static object CreateGroupMemberState(BotOwner member, EnemyInfo? targetEnemyInfo, IPlayer target)
        {
            EnemyInfo? goalEnemy = member.Memory?.GoalEnemy;
            return new
            {
                profileId = member.ProfileId,
                nickname = member.Profile?.Nickname,
                side = member.Side.ToString(),
                role = member.Profile?.Info?.Settings?.Role.ToString(),
                botState = member.BotState.ToString(),
                brain = member.Brain?.BaseBrain?.ShortName(),
                activeLayer = GetActiveLayerName(member),
                lastDecision = GetLastDecisionSnapshot(member),
                memory = new
                {
                    haveEnemy = member.Memory?.HaveEnemy == true,
                    underFire = member.Memory?.IsUnderFire == true,
                    inCover = member.Memory?.IsInCover == true,
                    isPeace = member.Memory?.IsPeace == true,
                    goalEnemy = CreateEnemyInfoRef(goalEnemy)
                },
                targetEnemyInfo = CreateEnemyInfoRef(targetEnemyInfo),
                sain = SainDiagnostics.CreateSnapshot(member, target)
            };
        }

        private static object? GetLastDecisionSnapshot(BotOwner bot)
        {
            try
            {
                var agent = bot.Brain?.Agent;
                if (agent == null)
                {
                    return null;
                }

                var result = agent.LastResult();
                return new
                {
                    action = result.Action.ToString(),
                    reason = result.Reason
                };
            }
            catch
            {
                return null;
            }
        }

        private static string? GetActiveLayerName(BotOwner bot)
        {
            try
            {
                return bot.Brain?.BaseBrain?.CurLayerInfo?.Name();
            }
            catch
            {
                return null;
            }
        }

        private static object? CreateEnemyInfoRef(EnemyInfo? info)
        {
            if (info == null)
            {
                return null;
            }

            return new
            {
                profileId = info.ProfileId,
                nickname = info.Person?.Profile?.Nickname,
                side = info.Person?.Side.ToString(),
                role = info.Person?.Profile?.Info?.Settings?.Role.ToString(),
                distance = SanitizeFloat(info.Distance),
                isVisible = info.IsVisible,
                canShoot = info.CanShoot,
                haveSeen = info.HaveSeen,
                personalSeenTime = SanitizeFloat(info.PersonalSeenTime),
                personalLastSeenTime = SanitizeFloat(info.PersonalLastSeenTime),
                lastKnownPosition = CreateVector(info.EnemyLastPositionReal)
            };
        }

        private static object? CreateObservedEnemyBotState(BotOwner? observedBot, Player follower, Player? boss)
        {
            if (observedBot == null)
            {
                return null;
            }

            EnemyInfo? goalEnemy = observedBot.Memory?.GoalEnemy;
            Vector3 position = observedBot.Position;
            Vector3 lookDirection = NormalizePlanar(observedBot.LookDirection);
            Vector3 toFollower = NormalizePlanar(follower.Transform.position - position);
            Vector3 toBoss = boss != null
                ? NormalizePlanar(boss.Transform.position - position)
                : Vector3.zero;

            return new
            {
                profileId = observedBot.ProfileId,
                nickname = observedBot.Profile?.Nickname,
                botState = observedBot.BotState.ToString(),
                brain = observedBot.Brain?.BaseBrain?.ShortName(),
                activeLayer = GetActiveLayerName(observedBot),
                lastDecision = GetLastDecisionSnapshot(observedBot),
                position = CreateVector(position),
                lookDirection = CreateVector(lookDirection),
                lookVsFollowerAngle = toFollower.sqrMagnitude > 0.0001f
                    ? SanitizeFloat(Vector3.Angle(lookDirection, toFollower))
                    : null,
                lookVsBossAngle = toBoss.sqrMagnitude > 0.0001f
                    ? SanitizeFloat(Vector3.Angle(lookDirection, toBoss))
                    : null,
                group = new
                {
                    id = observedBot.BotsGroup?.Id.ToString(),
                    side = observedBot.BotsGroup?.Side.ToString(),
                    hasFollowerEnemy = observedBot.BotsGroup?.IsEnemy(follower) == true,
                    hasBossEnemy = boss != null && observedBot.BotsGroup?.IsEnemy(boss) == true
                },
                memory = new
                {
                    haveEnemy = observedBot.Memory?.HaveEnemy == true,
                    underFire = observedBot.Memory?.IsUnderFire == true,
                    inCover = observedBot.Memory?.IsInCover == true,
                    isPeace = observedBot.Memory?.IsPeace == true,
                    goalEnemy = CreateEnemyInfoRef(goalEnemy)
                },
                sainAgainstFollower = SainDiagnostics.CreateSnapshot(observedBot, follower),
                sainAgainstBoss = boss != null ? SainDiagnostics.CreateSnapshot(observedBot, boss) : null
            };
        }

        private static object? CreateLimbStatusSnapshot(BotOwner bot)
        {
            IHealthController? health = bot.GetPlayer?.ActiveHealthController;
            if (health == null)
            {
                return null;
            }

            return new
            {
                head = CreateBodyPartStatus(health, EBodyPart.Head),
                chest = CreateBodyPartStatus(health, EBodyPart.Chest),
                stomach = CreateBodyPartStatus(health, EBodyPart.Stomach),
                leftArm = CreateBodyPartStatus(health, EBodyPart.LeftArm),
                rightArm = CreateBodyPartStatus(health, EBodyPart.RightArm),
                leftLeg = CreateBodyPartStatus(health, EBodyPart.LeftLeg),
                rightLeg = CreateBodyPartStatus(health, EBodyPart.RightLeg)
            };
        }

        private static object CreateBodyPartStatus(IHealthController health, EBodyPart bodyPart)
        {
            return new
            {
                broken = health.IsBodyPartBroken(bodyPart),
                destroyed = health.IsBodyPartDestroyed(bodyPart)
            };
        }

        private static object? CreateWeaponSlotSnapshot(Weapon? weapon)
        {
            if (weapon == null)
            {
                return null;
            }

            return new
            {
                id = weapon.Id,
                type = weapon.GetType().Name,
                magazineCount = weapon.GetCurrentMagazine()?.Cartridges?.Count
            };
        }

        private static object? CreateEnemySnapshot(
            EnemyInfo? goalEnemy,
            Vector3 botPosition,
            Vector3 lookDirection,
            Vector3 moveTargetDirection)
        {
            if (goalEnemy == null)
            {
                return null;
            }

            Vector3 position = goalEnemy.Person?.Transform != null
                ? goalEnemy.Person.Transform.position
                : goalEnemy.EnemyLastPositionReal;

            Vector3 toEnemyDirection = NormalizePlanar(position - botPosition);
            bool hasEnemyDirection = toEnemyDirection.sqrMagnitude > 0.0001f;

            return new
            {
                profileId = goalEnemy.ProfileId,
                role = goalEnemy.Person?.Profile?.Info?.Settings?.Role.ToString(),
                distance = SanitizeFloat(goalEnemy.Distance),
                isVisible = goalEnemy.IsVisible,
                canShoot = goalEnemy.CanShoot,
                haveSeen = goalEnemy.HaveSeen,
                personalSeenTime = SanitizeFloat(goalEnemy.PersonalSeenTime),
                personalLastSeenTime = SanitizeFloat(goalEnemy.PersonalLastSeenTime),
                lastKnownPosition = CreateVector(goalEnemy.EnemyLastPositionReal),
                position = CreateVector(position),
                direction = hasEnemyDirection ? CreateVector(toEnemyDirection) : null,
                lookVsEnemyAngle = hasEnemyDirection ? SanitizeFloat(Vector3.Angle(lookDirection, toEnemyDirection)) : null,
                moveTargetVsEnemyAngle = moveTargetDirection.sqrMagnitude > 0.0001f && hasEnemyDirection
                    ? SanitizeFloat(Vector3.Angle(moveTargetDirection, toEnemyDirection))
                    : null
            };
        }

        private static object? CreateBossSnapshot(BotOwner bot, Vector3 botPosition, Vector3 lookDirection)
        {
            if (bot.BotFollower?.BossToFollow is not pitAIBossPlayer boss)
            {
                return null;
            }

            Vector3 bossPosition = boss.realPlayer != null
                ? boss.realPlayer.Transform.position
                : boss.Position;
            Vector3 toBossDirection = NormalizePlanar(bossPosition - botPosition);
            bool hasBossDirection = toBossDirection.sqrMagnitude > 0.0001f;

            return new
            {
                profileId = boss.realPlayer?.ProfileId,
                position = CreateVector(bossPosition),
                distance = SanitizeFloat(Vector3.Distance(bot.Position, bossPosition)),
                lookDirection = boss.realPlayer != null ? CreateVector(NormalizePlanar(boss.realPlayer.LookDirection)) : null,
                direction = hasBossDirection ? CreateVector(toBossDirection) : null,
                lookVsBossAngle = hasBossDirection ? SanitizeFloat(Vector3.Angle(lookDirection, toBossDirection)) : null
            };
        }

        private static object CreateVector(Vector3 value)
        {
            return new
            {
                x = SanitizeFloat(value.x),
                y = SanitizeFloat(value.y),
                z = SanitizeFloat(value.z)
            };
        }

        private static bool TryGetCurrentMoveTarget(BotOwner bot, out Vector3 target)
        {
            target = Vector3.zero;
            if (bot?.GoToSomePointData == null || !bot.GoToSomePointData.HaveTarget())
            {
                return false;
            }

            target = bot.GoToSomePointData.Point;
            return true;
        }

        private static RecorderFollowerState GetOrCreateState(BotOwner bot)
        {
            string profileId = bot.ProfileId ?? string.Empty;
            return GetOrCreateState(profileId);
        }

        private static RecorderFollowerState GetOrCreateState(string profileId)
        {
            if (!FollowerStates.TryGetValue(profileId, out RecorderFollowerState? state))
            {
                state = new RecorderFollowerState();
                FollowerStates[profileId] = state;
            }

            return state;
        }

        private static bool IsBotInRecordedCombat(BotOwner bot, RecorderFollowerState state)
        {
            return state.InCombat || FollowerCombatLayer.IsFollowerCombatLayerActive(bot);
        }

        private static bool AnyFollowerInRecordedCombat()
        {
            foreach (RecorderFollowerState state in FollowerStates.Values)
            {
                if (state.InCombat)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CanRecordBot(BotOwner? bot)
        {
            return bot != null &&
                   IsRecording() &&
                   !string.IsNullOrEmpty(bot.ProfileId) &&
                   BossPlayers.IsFollower(bot);
        }

        private static bool IsEnabled()
        {
            return pitFireTeam.battleRecorderEnabled?.Value == true;
        }

        private static bool IsRecording()
        {
            return IsEnabled() && writer != null && !string.IsNullOrEmpty(currentRaidId);
        }

        private static float GetSnapshotIntervalSeconds()
        {
            return Mathf.Max(0.05f, GetSnapshotIntervalMs() / 1000f);
        }

        private static int GetSnapshotIntervalMs()
        {
            return Math.Max(50, pitFireTeam.battleRecorderSnapshotIntervalMs?.Value ?? 200);
        }

        private static float? SanitizeFloat(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return null;
            }

            return value;
        }

        private static Vector3 NormalizePlanar(Vector3 value)
        {
            value.y = 0f;
            if (value.sqrMagnitude <= 0.0001f)
            {
                return Vector3.zero;
            }

            return value.normalized;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) &&
                   !float.IsNaN(value.y) &&
                   !float.IsNaN(value.z) &&
                   !float.IsInfinity(value.x) &&
                   !float.IsInfinity(value.y) &&
                   !float.IsInfinity(value.z);
        }

        private static bool HasSimpleLineOfSight(Player? from, Player? to)
        {
            if (from?.MainParts == null || to?.MainParts == null)
            {
                return false;
            }

            if (!from.MainParts.TryGetValue(BodyPartType.head, out var fromHead) || fromHead == null)
            {
                return false;
            }

            if (!to.MainParts.TryGetValue(BodyPartType.head, out var toHead) || toHead == null)
            {
                return false;
            }

            Vector3 direction = toHead.Position - fromHead.Position;
            float distance = direction.magnitude;
            if (distance <= 0.01f)
            {
                return false;
            }

            return !Physics.Raycast(
                new Ray(fromHead.Position, direction),
                out _,
                distance,
                LayerMaskClass.HighPolyWithTerrainMask);
        }

        private static void WriteEventInternal(string eventType, BotOwner? bot, object payload)
        {
            if (!IsRecording() || writer == null)
            {
                return;
            }

            try
            {
                var envelope = new
                {
                    seq = ++eventSequence,
                    time = SanitizeFloat(Time.time),
                    utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    raidId = currentRaidId,
                    locationId = currentLocationId,
                    eventType,
                    bot = bot != null ? new
                    {
                        profileId = bot.ProfileId,
                        nickname = bot.Profile?.Nickname,
                        brain = bot.Brain?.BaseBrain?.ShortName()
                    } : null,
                    payload
                };

                lock (SyncRoot)
                {
                    writer.WriteLine(JsonConvert.SerializeObject(envelope, JsonSettings));
                }
            }
            catch (Exception ex)
            {
                if (!writeErrorLogged)
                {
                    writeErrorLogged = true;
                    SafeLogRecorderError("Battle recorder write failure.", ex);
                }
            }
        }

        private static void DisposeWriter()
        {
            lock (SyncRoot)
            {
                writer?.Dispose();
                writer = null;
            }
        }

        private static void SafeLogRecorderError(string message, Exception ex)
        {
            try
            {
                pitFireTeam.Log.LogError(message);
                pitFireTeam.Log.LogError(ex);
            }
            catch
            {
            }
        }

        private sealed class RecorderFollowerState
        {
            public bool InCombat;
            public float LastCombatSeenTime;
            public float NextSnapshotTime;
            public bool HasPreviousSnapshot;
            public Vector3 LastSnapshotPosition;
            public string? CurrentObjective;
            public string? LastDecisionAction;
            public string? LastDecisionReason;
        }
    }
}
