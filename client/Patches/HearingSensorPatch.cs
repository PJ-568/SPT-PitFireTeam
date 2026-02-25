using Comfort.Common;
using EFT;

using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;
using UnityEngine;

using friendlySAIN.Modules;
using friendlySAIN.Utils;

namespace friendlySAIN.Patches
{
    internal class HearingSensorPatch : ModulePatch
    {
        private const bool EnableReactionTrace = false;
        private static readonly System.Collections.Generic.Dictionary<string, float> _traceThrottleUntil = new();
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotHearingSensor), "method_0");
        }

        [PatchPostfix]
        public static void PatchPostfix(BotHearingSensor __instance, IPlayer player, Vector3 position, float power, AISoundType type)
        {
            BotOwner botOwner_0 = __instance.BotOwner;
            // check if enemy is trying to sneak up on the bot - only during combat
            if (type == AISoundType.step)
            {
                if (BossPlayers.IsFollower(botOwner_0) && player != null && !BossPlayers.IsPlayerBoss(player.ProfileId))
                {
                    if (player.IsAI && BossPlayers.IsFollower(player.AIData.BotOwner)) return;
                    if (!(botOwner_0.EnemiesController.IsEnemy(player) || botOwner_0.BotsGroup.IsEnemy(player))) return;
                    if (!botOwner_0.Memory.HaveEnemy)
                    {
                        TraceThrottled(botOwner_0, $"step-noEnemy-{player.ProfileId}", 1f, $"Hearing.step ignore noEnemyMemory src={player.ProfileId}");
                        return;
                    }
                    if (botOwner_0.Memory.GoalEnemy.IsVisible && botOwner_0.Memory.GoalEnemy.CanShoot)
                    {
                        Trace(botOwner_0, $"Hearing.step ignore visibleShootable goal={botOwner_0.Memory.GoalEnemy.ProfileId}");
                        return;
                    }

                    if (botOwner_0.Memory.GoalEnemy.PersonalLastSeenTime + 2f < Time.time)
                    {
                        Trace(botOwner_0, "Hearing.step ignore lastSeenTooOld");
                        return;
                    }

                    bool shouldReact = __instance.method_6(position, power, out var distance);
                    Trace(botOwner_0, $"Hearing.step check shouldReact={shouldReact} dist={distance:F1} power={power:F1}");

                    Player person = Singleton<GameWorld>.Instance.GetAlivePlayerByProfileID(player.ProfileId);
                    if (person != null && shouldReact)
                    {
                        FollowerAwareness.SoundHeard(botOwner_0, person, position, distance, type);
                    }
                }

                return;
            }
            else if (BossPlayers.IsFollower(botOwner_0) && player != null && !BossPlayers.IsPlayerBoss(player.ProfileId))
            {
                if (player.IsAI && BossPlayers.IsFollower(player.AIData.BotOwner)) return;

                if (botOwner_0.EnemiesController.IsEnemy(player) || botOwner_0.BotsGroup.IsEnemy(player))
                {
                    bool shouldReact = __instance.method_6(position, power, out var distance);
                    Vector3 botPosition = botOwner_0.GetPlayer.Transform.position;
                    Trace(botOwner_0, $"Hearing.{type} precheck shouldReact={shouldReact} dist={distance:F1} power={power:F1} haveEnemy={botOwner_0.Memory.HaveEnemy}");

                    if (
                        botOwner_0.Memory.HaveEnemy && (
                            botOwner_0.Memory.GoalEnemy.PersonalLastSeenTime + 2f < Time.time ||
                            (position - botPosition).sqrMagnitude > (botOwner_0.Memory.GoalEnemy.EnemyLastPosition - botPosition).sqrMagnitude
                        )
                    )
                    {
                        Trace(botOwner_0, $"Hearing.{type} suppress older/worse sound");
                        shouldReact = false;
                    }

                    if (shouldReact)
                    {
                        Player person = Singleton<GameWorld>.Instance.GetAlivePlayerByProfileID(player.ProfileId);
                        if (person != null)
                        {
                            FollowerAwareness.SoundHeard(botOwner_0, person, position, distance, type);
                        }
                    }
                    else
                    {
                        Trace(botOwner_0, $"Hearing.{type} no-react");
                    }
                }
            }
        }

        private static void Trace(BotOwner bot, string msg)
        {
            if (!EnableReactionTrace || bot == null) return;
            Modules.Logger.LogInfo($"[ReactTrace] bot={bot.Profile?.Nickname ?? bot.name} {msg}");
        }

        private static void TraceThrottled(BotOwner bot, string keySuffix, float seconds, string msg)
        {
            if (!EnableReactionTrace || bot == null) return;
            string key = $"{bot.ProfileId}:{keySuffix}";
            if (_traceThrottleUntil.TryGetValue(key, out float until) && Time.time < until) return;
            _traceThrottleUntil[key] = Time.time + seconds;
            Trace(bot, msg);
        }
    }

    internal class FootstepSoundPatch : ModulePatch
    {
        private const bool EnableReactionTrace = false;
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Player), "PlayStepSound");
        }

        [PatchPostfix]
        public static void PatchPostfix(Player __instance, BetterSource ___NestedStepSoundSource)
        {
            float volume = __instance.MovementContext.CovertMovementVolumeBySpeed * __instance.method_57();
            float range = ___NestedStepSoundSource.MaxDistance * 0.85f;

            if (BossPlayers.IsPlayerBoss(__instance.ProfileId)) return;

            foreach (var follower in BossPlayers.GetFollowers())
            {
                BotOwner bot = follower.GetBot();
                if (bot.ProfileId == __instance.ProfileId || bot.Memory.HaveEnemy) continue;
                if (!bot.EnemiesController.IsEnemy(__instance) && !bot.BotsGroup.IsEnemy(__instance)) continue;

                Vector3 position = __instance.Transform.position;

                float power = range * volume;

                power = Mathf.Min(25f, power);

                float distance = Vector3.Distance(bot.GetPlayer.Transform.position, position);

                bool shouldReact = distance <= power;
                if (EnableReactionTrace)
                {
                    Modules.Logger.LogInfo($"[ReactTrace] bot={bot.Profile?.Nickname ?? bot.name} FootstepPatch src={__instance.ProfileId} dist={distance:F1} power={power:F1} shouldReact={shouldReact}");
                }

                if (!shouldReact) continue;

                FollowerAwareness.SoundHeard(bot, __instance, position, distance, AISoundType.step);

            }
        }

    }

    internal class PlayerSayPatch : ModulePatch
    {
        private const float VoiceReactDistance = 40f;
        private static float reported = 0f;
        private static float freq = 0;
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Player), "Say");
        }

        // Patch Player.Say to make followers turn towards the enemy when they talk
        [PatchPostfix]
        private static void PatchPostfix(Player __instance)
        {
            if (__instance == null) return;
            if (Time.time < reported || Time.time < freq) return;

            freq = Time.time + 0.5f;

            if (BossPlayers.IsPlayerBoss(__instance.ProfileId)) return;

            bool isfollower = false;
            foreach (var f in BossPlayers.GetFollowers())
            {
                if (f.GetBot().ProfileId == __instance.ProfileId)
                {
                    isfollower = true;
                    break;
                }
            }

            if (isfollower) return;

            bool reportEnemy = false;
            BossPlayers.GetFollowers().ForEach(follower =>
            {
                if (follower == null) return;
                BotOwner bot = follower.GetBot();
                if (bot == null || bot.IsDead || bot.GetPlayer == null || bot.HearingSensor == null) return;
                if (bot.EnemiesController == null || bot.Memory == null || bot.BotsGroup == null) return;
                if (FollowerAwareness.WasRecentlyHit(bot) || bot.Memory.HaveEnemy) return;
                if (!(bot.EnemiesController.IsEnemy(__instance) || bot.BotsGroup.IsEnemy(__instance))) return;

                bool heardVoice = bot.HearingSensor.method_6(__instance.Transform.position, VoiceReactDistance, out var distance);
                bool speakerHasLosToFollower = EnemySpeakerHasLineOfSightToFollower(__instance, bot, out float losDistance);
                bool shouldReact = heardVoice || (speakerHasLosToFollower && losDistance <= VoiceReactDistance);

                if (shouldReact)
                {
                    float effectiveDistance = heardVoice ? distance : losDistance;
                    if (effectiveDistance < 16f)
                    {
                        if (!reportEnemy) bot.BotsGroup.ReportAboutEnemy(__instance, EEnemyPartVisibleType.Visible,bot);
                        EnemyInfo info = Utils.Enemy.MakeEnemy(bot, __instance);

                        reported = Time.time + 3f;
                        reportEnemy = true;

                        info?.SetVisible(true);
                    }
                    else if (effectiveDistance <= VoiceReactDistance)
                    {
                        reported = Time.time + 3f;
                        Vector3 sourcePos = __instance.Transform.position;
                        if (__instance.MainParts != null && __instance.MainParts.TryGetValue(BodyPartType.body, out var mainBodyPart) && mainBodyPart != null)
                        {
                            sourcePos = mainBodyPart.Position;
                        }
                        FollowerAwareness.FakeShot(bot, sourcePos);
                        if (!reportEnemy)
                        {
                            bot.BotsGroup.ReportAboutEnemy(__instance, EEnemyPartVisibleType.NotVisible,bot);
                            bot.BotTalk.TrySay(EPhraseTrigger.NoisePhrase);
                        }
                        reportEnemy = true;
                    }
                }
            });
        }

        private static bool EnemySpeakerHasLineOfSightToFollower(Player speaker, BotOwner follower, out float distance)
        {
            distance = float.MaxValue;
            if (speaker == null || follower == null || follower.GetPlayer == null) return false;
            if (!speaker.IsAI || speaker.AIData?.BotOwner == null) return false;

            BotOwner speakerBot = speaker.AIData.BotOwner;
            Vector3 speakerPos = speaker.Transform.position;
            if (speaker.MainParts != null && speaker.MainParts.TryGetValue(BodyPartType.body, out var bodyPart) && bodyPart != null)
            {
                speakerPos = bodyPart.Position;
            }

            var followerPlayer = follower.GetPlayer;
            Vector3 followerHead = followerPlayer.MainParts?[BodyPartType.head]?.Position ?? (followerPlayer.Transform.position + Vector3.up * 1.4f);
            Vector3 followerBody = followerPlayer.MainParts?[BodyPartType.body]?.Position ?? (followerPlayer.Transform.position + Vector3.up * 1.0f);

            distance = Vector3.Distance(speakerPos, followerBody);

            LayerMask mask = speakerBot.LookSensor != null ? speakerBot.LookSensor.Mask : LayerMaskClass.HighPolyWithTerrainMask;
            bool canSeeHead = Utils.Utils.CanShootToTarget(new ShootPointClass(followerHead, 1f), speakerPos, mask, false);
            if (canSeeHead) return true;

            bool canSeeBody = Utils.Utils.CanShootToTarget(new ShootPointClass(followerBody, 1f), speakerPos, mask, false);
            return canSeeBody;
        }
    }

}
