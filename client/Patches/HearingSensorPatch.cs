using Comfort.Common;
using EFT;

using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;
using UnityEngine;

using friendlySAIN.Modules;
using friendlySAIN.Brains;

namespace friendlySAIN.Patches
{
    internal class HearingSensorPatch : ModulePatch
    {
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
                    if (!botOwner_0.Memory.HaveEnemy) return;
                    if (botOwner_0.Memory.GoalEnemy.IsVisible && botOwner_0.Memory.GoalEnemy.CanShoot) return;

                    if (botOwner_0.Memory.GoalEnemy.PersonalLastSeenTime + 2f < Time.time)
                    {
                        return;
                    }

                    bool shouldReact = __instance.method_6(position, power, out var distance);

                    Player person = Singleton<GameWorld>.Instance.GetAlivePlayerByProfileID(player.ProfileId);
                    if (person != null && shouldReact) (botOwner_0.Brain.BaseBrain as FollowerBrain).SoundHeard(person, position, distance, type);
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

                    if (
                        botOwner_0.Memory.HaveEnemy && (
                            botOwner_0.Memory.GoalEnemy.PersonalLastSeenTime + 2f < Time.time ||
                            (position - botPosition).sqrMagnitude > (botOwner_0.Memory.GoalEnemy.EnemyLastPosition - botPosition).sqrMagnitude
                        )
                    )
                    {
                        shouldReact = false;
                    }

                    if (shouldReact && botOwner_0.Brain.BaseBrain is FollowerBrain)
                    {
                        Player person = Singleton<GameWorld>.Instance.GetAlivePlayerByProfileID(player.ProfileId);
                        if (person != null) (botOwner_0.Brain.BaseBrain as FollowerBrain).SoundHeard(person, position, distance, type);
                    }
                }
            }
        }
    }

    internal class FootstepSoundPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Player), "PlayStepSound");
        }

        [PatchPostfix]
        public static void PatchPostfix(Player __instance, BetterSource ___NestedStepSoundSource)
        {
            float volume = __instance.MovementContext.CovertMovementVolumeBySpeed * __instance.method_55();
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

                if (!shouldReact) continue;

                (bot.Brain.BaseBrain as FollowerBrain).SoundHeard(__instance, position, distance, AISoundType.step);

            }
        }

    }

    internal class PlayerSayPatch : ModulePatch
    {
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
                BotOwner bot = follower.GetBot();
                FollowerBrain brain = bot.Brain.BaseBrain as FollowerBrain;

                if (brain == null || brain.WasHit || bot.Memory.HaveEnemy || bot.BotsGroup == null) return;
                if (
                    bot.HearingSensor.method_6(__instance.Transform.position, 40f, out var distance) &&
                    (bot.EnemiesController.IsEnemy(__instance) || bot.BotsGroup.IsEnemy(__instance))
                )
                {
                    if (distance < 16f)
                    {
                        if (!reportEnemy) bot.BotsGroup.ReportAboutEnemy(__instance, EEnemyPartVisibleType.Visible);
                        EnemyInfo info = Utils.Enemy.MakeEnemy(bot, __instance);

                        reported = Time.time + 3f;
                        reportEnemy = true;

                        info?.SetVisible(true);
                    }
                    else if (distance <= 40f)
                    {
                        reported = Time.time + 3f;
                        brain.FakeShot(__instance.MainParts[BodyPartType.body].Position);
                        if (!reportEnemy)
                        {
                            bot.BotsGroup.ReportAboutEnemy(__instance, EEnemyPartVisibleType.NotVisible);
                            bot.BotTalk.TrySay(EPhraseTrigger.NoisePhrase);
                        }
                        reportEnemy = true;
                    }
                }
            });
        }
    }

}
