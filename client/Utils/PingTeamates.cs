
using Comfort.Common;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.HealthSystem;

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

using pitTeam.Components;
using pitTeam.Modules;

namespace pitTeam.Utils
{
    internal class BotData
    {
        public void SetData(BotOwner botData)
        {
            LastUpdate = Time.time;
            Data = botData;
            // Cached BotData instances are reused across pings; reset marker state so stale zones don't suppress triangles.
            EnemyPos = null;
            EnemyZone = null;
            EnemyMarkerUntil = 0f;
        }

        public void SetEnemyMarker(Vector3 enemyPosition, float untilTime)
        {
            EnemyZone = PingTeamates.GetEnemyMarkerZone(enemyPosition);
            EnemyPos = enemyPosition + (Vector3.up * 1.6f);
            EnemyMarkerUntil = untilTime;
        }

        public float LastUpdate;
        public BotOwner Data;
        public GUIContent GuiContent;
        public Rect GuiRect;

        public Rect MarkRect;

        public Vector3? EnemyPos = null;
        public Vector3? EnemyZone = null;
        public float EnemyMarkerUntil;
    }

    internal class PingTeamates : MonoBehaviour, IDisposable
    {

        public List<BotData> botMap = new List<BotData>();
        private readonly Dictionary<string, BotData> botDataCache = new Dictionary<string, BotData>(StringComparer.Ordinal);

        private float lasttime = 0f;

        private float nextUpdateTime;

        private GUIStyle guiStyle;
        private GUIStyle makerGuiStyle;

        private float screenScale = 1.0f;
        private float fovFactor = 1f;
        private readonly StringBuilder _guiTextBuilder = new StringBuilder(256);
        private static readonly EBodyPart[] TrackedBodyParts =
        {
            EBodyPart.Head,
            EBodyPart.Chest,
            EBodyPart.Stomach,
            EBodyPart.RightArm,
            EBodyPart.LeftArm,
            EBodyPart.RightLeg,
            EBodyPart.LeftLeg
        };

        Player myPlayer;

        private bool guiUpdate = false;

        public static PingTeamates Instance = null;

        private static RadioSound radioSound;
        private const float MaxSpatialPingDistance = 40f;
        private const float DirectionCalloutCooldownSeconds = 15f;

        private bool locationPing = false;
        private float _nextDirectionCalloutTime = 0f;

        public void Ping(pitAIBossPlayer player)
        {
            if (lasttime > Time.time) return;

            lasttime = Time.time + pitFireTeam.pingTime.Value;

            locationPing = false;

            List<Components.BotFollowerPlayer> followers = BossPlayers.GetFollowersByBoss(player.realPlayer.ProfileId);

            myPlayer = player.realPlayer;

            botMap.Clear();

            BotOwner closestEnemySpeaker = null;
            Vector3 closestEnemyPosition = Vector3.zero;
            float closestEnemySpeakerSqr = float.MaxValue;

            foreach (Components.BotFollowerPlayer fl in followers)
            {
                BotOwner bot = fl?.GetBot();
                if (bot == null || bot.IsDead)
                {
                    continue;
                }

                string profileId = bot.ProfileId;
                if (string.IsNullOrEmpty(profileId))
                {
                    continue;
                }

                if (!botDataCache.TryGetValue(profileId, out BotData botData))
                {
                    botData = new BotData();
                    botDataCache[profileId] = botData;
                }

                botData.SetData(bot);
                botMap.Add(botData);

                if (!bot.Memory.HaveEnemy)
                {
                    continue;
                }

                if (!TryGetGoalEnemyPosition(bot, out Vector3 enemyPosition))
                {
                    continue;
                }

                botData.SetEnemyMarker(enemyPosition, lasttime);

                float enemySpeakerSqr = (bot.Position - player.Position).sqrMagnitude;
                if (enemySpeakerSqr < closestEnemySpeakerSqr)
                {
                    closestEnemySpeaker = bot;
                    closestEnemyPosition = enemyPosition;
                    closestEnemySpeakerSqr = enemySpeakerSqr;
                }

                locationPing = true;
            }

            if (radioSound != null && locationPing)
            {
                Vector3 position = GetLimitedPosition(player.Position, closestEnemyPosition, MaxSpatialPingDistance);
                radioSound.PlayLocationSound(position);
            }

            if (closestEnemySpeaker != null && Time.time >= _nextDirectionCalloutTime)
            {
                TrySpeakBossRelativeEnemyDirection(closestEnemySpeaker, player.realPlayer, closestEnemyPosition);
                _nextDirectionCalloutTime = Time.time + DirectionCalloutCooldownSeconds;
            }

            if (radioSound != null && !locationPing)
            {
                if (HasAnyAliveFollower())
                {
                    Vector3 closestFollowerPos = GetClosestFollowerPosition(player.Position);
                    Vector3 clampedRadioPos = GetLimitedPosition(player.Position, closestFollowerPos, MaxSpatialPingDistance);
                    radioSound.PlayRadioSound(clampedRadioPos);
                }
                else
                {
                    radioSound.PlayRadioSound();
                }
            }

        }

        private void TrySpeakBossRelativeEnemyDirection(BotOwner speaker, Player bossPlayer, Vector3 enemyPosition)
        {
            if (speaker == null || bossPlayer == null)
            {
                return;
            }

            EPhraseTrigger trigger = GetBossRelativeDirectionTrigger(bossPlayer, enemyPosition);
            if (trigger == EPhraseTrigger.PhraseNone)
            {
                return;
            }

            speaker.BotTalk.TrySay(trigger, true);
        }

        private EPhraseTrigger GetBossRelativeDirectionTrigger(Player bossPlayer, Vector3 enemyPosition)
        {
            Vector3 toEnemy = enemyPosition - bossPlayer.Transform.position;
            toEnemy.y = 0f;
            if (toEnemy.sqrMagnitude <= 0.0001f)
            {
                return EPhraseTrigger.OnRepeatedContact;
            }

            Vector3 bossLookDirection = bossPlayer.MovementContext?.PlayerRealForward ?? bossPlayer.LookDirection;
            bossLookDirection.y = 0f;
            if (bossLookDirection.sqrMagnitude <= 0.0001f)
            {
                bossLookDirection = bossPlayer.Transform.forward;
                bossLookDirection.y = 0f;
            }

            if (bossLookDirection.sqrMagnitude <= 0.0001f)
            {
                return EPhraseTrigger.OnRepeatedContact;
            }

            float signedAngle = Vector3.SignedAngle(bossLookDirection.normalized, toEnemy.normalized, Vector3.up);
            float absoluteAngle = Mathf.Abs(signedAngle);

            if (absoluteAngle <= 35f)
            {
                return EPhraseTrigger.InTheFront;
            }

            if (absoluteAngle >= 145f)
            {
                return EPhraseTrigger.OnSix;
            }

            return signedAngle < 0f ? EPhraseTrigger.LeftFlank : EPhraseTrigger.RightFlank;
        }

        public void Dispose()
        {
            botMap.Clear();
            botDataCache.Clear();
            Destroy(this);
            Destroy(radioSound);
            radioSound = null;
        }

        public void Update()
        {
            if (botMap.Count > 0)
            {
                guiUpdate = true;
                if (lasttime <= Time.time)
                {
                    guiUpdate = false;
                    botMap.Clear();
                }
            }


            if (Time.time < nextUpdateTime)
            {
                return;
            }
            nextUpdateTime = Time.time + 1.0f;

            if (CameraClass.Instance.SSAA != null && CameraClass.Instance.SSAA.isActiveAndEnabled)
            {
                int outputWidth = CameraClass.Instance.SSAA.GetOutputWidth();
                float inputWidth = CameraClass.Instance.SSAA.GetInputWidth();
                screenScale = outputWidth / inputWidth;
            }
        }

        void OnGUI()
        {

            if (!guiUpdate) return;

            if (guiStyle == null)
            {
                CreateGuiStyle();
            }

            if (makerGuiStyle == null)
            {
                CreateMarkerGuiStyle();
            }


            if (botMap != null)
            {
                for (int i = 0; i < botMap.Count; i++)
                {
                    DrawBotGUI(botMap[i]);
                }

                if (pitFireTeam.enemyMarker.Value)
                {
                    for (int i = 0; i < botMap.Count; i++)
                    {
                        DrawEnemyMarkerGUI(botMap[i]);
                    }
                }
            }
        }

        private void DrawBotGUI(BotData bt)
        {
            if (!guiUpdate) return;

            if (bt == null || bt.Data == null || !bt.Data.HealthController.IsAlive) return;

            Vector3 aboveBotHeadPos = bt.Data.Position + (Vector3.up * 1.6f);
            Vector3 screenPos = Camera.main.WorldToScreenPoint(aboveBotHeadPos);

            if (screenPos.z > 0)
            {
                int dist = Mathf.RoundToInt((bt.Data.Position - myPlayer.Transform.position).magnitude);

                if (dist < 301)
                {
                    if (bt.GuiContent == null)
                    {
                        bt.GuiContent = new GUIContent();
                    }
                    if (bt.GuiRect == null)
                    {
                        bt.GuiRect = new Rect();
                    }

                    string botName = bt.Data.Profile.Nickname;

                    StringBuilder stringBuilder = _guiTextBuilder;
                    stringBuilder.Clear();
                    stringBuilder.Append(botName);
                    stringBuilder.Append(" - ");
                    stringBuilder.Append(dist);
                    stringBuilder.Append("m");

                    if (!bt.Data.HealthController.IsAlive)
                    {
                        stringBuilder.Append(": " + pitFireTeam.optionsLang.botStatus["Dead"]);
                    }
                    else if (bt.Data.Memory.HaveEnemy)
                    {
                        EnemyInfo goalEnemy = bt.Data.Memory.GoalEnemy;
                        if (goalEnemy != null)
                        {
                            float lastSeenAgo = Time.time - goalEnemy.PersonalLastSeenTime;
                            if (IsEnemyReliablyVisibleForMarker(bt.Data, goalEnemy) || lastSeenAgo < 5f)
                            {
                                stringBuilder.Append(": " + pitFireTeam.optionsLang.botStatus["Engaged"]);
                            }
                            else
                            {
                                stringBuilder.Append(": " + pitFireTeam.optionsLang.botStatus["Alerted"]);
                            }
                        }
                        else
                        {
                            stringBuilder.Append(": " + pitFireTeam.optionsLang.botStatus["Alerted"]);
                        }
                    }

                    float hp = 0;
                    float hpmax = 0;

                    string blackout = "";

                    for (int i = 0; i < TrackedBodyParts.Length; i++)
                    {
                        EBodyPart part = TrackedBodyParts[i];
                        bt.Data.Profile.Health.BodyParts.TryGetValue(part, out var bodyPart);
                        if (bodyPart != null)
                        {
                            ValueStruct value = bt.Data.HealthController.GetBodyPartHealth(part, true);
                            hp += value.Current;
                            hpmax += value.Maximum;
                            if (value.Current == 0)
                            {
                                if (blackout.Length > 0) blackout += ", ";
                                blackout += part.ToString().Localized();
                            }
                        }

                    }

                    if (hp > 0)
                    {
                        stringBuilder.Append(Environment.NewLine);
                        if (hp < hpmax)
                            stringBuilder.Append($"HP: {hp}/{hpmax}");
                        else stringBuilder.Append($"HP: {hpmax}");

                        if (blackout.Length > 0)
                        {
                            stringBuilder.Append(Environment.NewLine);
                            stringBuilder.Append("0%: " + blackout);
                        }
                    }

                    if (BossPlayers.IsFollower(bt.Data))
                    {
                        BotFollowerPlayer followerData = BossPlayers.Instance?.GetFollower(bt.Data);
                        if (IsFollowerCurrentlyHealing(bt.Data))
                        {
                            stringBuilder.Append($" | " + pitFireTeam.optionsLang.botStatus["Heal"]);
                        }
                        else if (DoesFollowerWantToHeal(bt.Data))
                        {
                            stringBuilder.Append($" | " + pitFireTeam.optionsLang.botStatus["WantToHeal"]);
                        }
                        else
                        {
                            string tactic = pitFireTeam.optionsLang.tacticOptions[0];
                            if (followerData != null)
                            {
                                tactic = followerData.CombatTactic switch
                                {
                                    FollowerCombatTactic.Marksman => "Marksman",
                                    FollowerCombatTactic.Protector => "Protector",
                                    _ => pitFireTeam.optionsLang.tacticOptions[0],
                                };
                            }
                            if (tactic != null)
                            {
                                stringBuilder.Append($" | MD: {tactic}");
                            }
                        }

                        if (pitFireTeam.IsDebugBuild)
                        {
                            if (followerData != null)
                            {
                                stringBuilder.Append($" | AGG: {Mathf.RoundToInt(followerData.EffectiveCombatAggression)}");
                                if (followerData.IsTemporaryCombatAggressionOverrideActive)
                                {
                                    stringBuilder.Append("*");
                                }
                            }
                        }
                    }

                    bt.GuiContent.text = stringBuilder.ToString();

                    Vector2 guiSize = guiStyle.CalcSize(bt.GuiContent);

                    bt.GuiRect.x = (screenPos.x * screenScale) - (guiSize.x / 2);
                    bt.GuiRect.y = Screen.height - ((screenPos.y * screenScale) + guiSize.y);
                    bt.GuiRect.size = guiSize;

                    GUI.Box(bt.GuiRect, bt.GuiContent.text, guiStyle);
                }
            }
        }

        private void DrawEnemyMarkerGUI(BotData bt)
        {
            if (!guiUpdate) return;

            if (bt == null || bt.Data == null || !bt.Data.HealthController.IsAlive) return;

            bool hasLiveEnemy = bt.Data.Memory?.HaveEnemy == true && bt.Data.Memory.GoalEnemy != null;
            if (hasLiveEnemy)
            {
                Vector3? enemyPosition;

                // I have seen the game throwing error when getting enemy position
                try
                {
                    enemyPosition = bt.Data.Memory.GoalEnemy.CurrPosition;
                }
                catch
                {

                    return;
                }

                Vector3 targetSpot = GetEnemyMarkerZone(enemyPosition.Value);

                if (targetSpot != bt.EnemyZone || !bt.EnemyPos.HasValue)
                {
                    bt.SetEnemyMarker(enemyPosition.Value, lasttime);
                }
            }

            if (!bt.EnemyPos.HasValue || Time.time > bt.EnemyMarkerUntil)
            {
                return;
            }

            Vector3 targetZone = bt.EnemyZone ?? GetEnemyMarkerZone(bt.EnemyPos.Value);
            Color marker = hasLiveEnemy && IsEnemyReliablyVisibleForMarker(bt.Data, bt.Data.Memory.GoalEnemy)
                ? Color.red
                : Color.yellow;

            foreach (var item in botMap)
            {
                if (item == bt)
                {
                    break;
                }

                if (item.EnemyPos.HasValue && item.EnemyZone.HasValue && item.EnemyZone == targetZone && marker != Color.red)
                {
                    return;
                }
            }

            if (bt.MarkRect == null)
            {
                bt.MarkRect = new Rect();
            }

            Vector3 screenPos;

            // take optics into consideration when triggering enemy location report
            if (
                CameraClass.Instance.OpticCameraManager.CurrentOpticSight != null &&
                CameraClass.Instance.OpticCameraManager.Camera != null
            )
            {
                return;

            }
            else
            {
                screenPos = Camera.main.WorldToScreenPoint(bt.EnemyPos.Value);
            }

            if (screenPos.z > 0)
            {
                DrawEnemyMarker(bt, screenPos, marker);
            }
        }

        public static Vector3 GetEnemyMarkerZone(Vector3 enemyPosition)
        {
            return new Vector3(
                Mathf.Floor(enemyPosition.x / 25f) * 25f,
                Mathf.Floor(enemyPosition.y / 25f) * 25f,
                Mathf.Floor(enemyPosition.z / 25f) * 25f
            );
        }

        private void CreateGuiStyle()
        {
            guiStyle = new GUIStyle(GUI.skin.box);
            guiStyle.alignment = TextAnchor.MiddleCenter;
            guiStyle.margin = new RectOffset(2, 2, 2, 2);
            guiStyle.fontStyle = FontStyle.Bold;
            guiStyle.fontSize = 20;
            guiStyle.richText = true;
            guiStyle.border = new RectOffset(0, 0, 0, 0);
            guiStyle.normal.background = MakeTexture(new Color(0, 0, 0, 0.3f));

            guiStyle.normal.textColor = Color.green;


        }

        private void CreateMarkerGuiStyle()
        {
            makerGuiStyle = new GUIStyle(GUI.skin.box);
            makerGuiStyle.alignment = TextAnchor.MiddleCenter;
            makerGuiStyle.margin = new RectOffset(0, 0, 0, 0);
            makerGuiStyle.fontStyle = FontStyle.Bold;
            makerGuiStyle.fontSize = 20;
            makerGuiStyle.richText = true;
            makerGuiStyle.border = new RectOffset(0, 0, 0, 0);
            makerGuiStyle.normal.background = MakeTexture(new Color(0, 0, 0, 0));

            makerGuiStyle.normal.textColor = Color.white;
        }

        private Texture2D MakeTexture(Color color)
        {
            Texture2D texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private Texture2D CreateTriangleTexture(Color color)
        {
            int width = 30;
            int height = 30;
            Texture2D texture = new Texture2D(width, height);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (y <= height / 2 && x >= (height / 2 - y) && x <= (height / 2 + y))
                    {
                        texture.SetPixel(x, y, color);
                    }
                    else
                    {
                        texture.SetPixel(x, y, Color.clear);
                    }
                }
            }

            texture.Apply();
            return texture;
        }

        private void DrawEnemyMarker(BotData bt, Vector3 markerPos, Color cl)
        {
            float animationOffset = Mathf.Sin(Time.time * 5f) * 5f;

            float size = 30f;

            bt.MarkRect.x = (markerPos.x * screenScale / fovFactor) - (size / 2);
            bt.MarkRect.y = Screen.height - ((markerPos.y * screenScale / fovFactor) + size) + animationOffset;
            bt.MarkRect.size = new Vector2(size, size);

            GUI.Box(bt.MarkRect, CreateTriangleTexture(cl), makerGuiStyle);
        }

        private static bool TryGetGoalEnemyPosition(BotOwner bot, out Vector3 enemyPosition)
        {
            enemyPosition = Vector3.zero;
            if (bot?.Memory == null || !bot.Memory.HaveEnemy)
            {
                return false;
            }

            try
            {
                EnemyInfo goalEnemy = bot.Memory.GoalEnemy;
                if (goalEnemy == null)
                {
                    return false;
                }

                enemyPosition = goalEnemy.CurrPosition;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsEnemyReliablyVisibleForMarker(BotOwner bot, EnemyInfo goalEnemy)
        {
            if (bot == null || goalEnemy == null)
            {
                return false;
            }

            if (!goalEnemy.IsVisible || !goalEnemy.CanShoot)
            {
                return false;
            }

            // UI red should mean "actively visible now", not stale memory visibility.
            if (Time.time - goalEnemy.PersonalLastSeenTime > 0.35f)
            {
                return false;
            }

            if (bot.LookSensor == null || !bot.LookSensor.EnoughDistToShoot(out _))
            {
                return false;
            }

            ShootPointClass? shootPoint = bot.CurrentEnemyTargetPosition(true);
            if (shootPoint == null)
            {
                return false;
            }

            return global::pitTeam.Utils.Utils.CanShootToTarget(
                shootPoint,
                bot.WeaponRoot.position,
                bot.LookSensor.Mask,
                false);
        }

        private static bool IsFollowerCurrentlyHealing(BotOwner bot)
        {
            if (bot?.Medecine == null)
            {
                return false;
            }

            if (bot.Medecine.Using ||
                bot.Medecine.FirstAid?.Using == true ||
                bot.Medecine.SurgicalKit?.Using == true ||
                bot.Medecine.Stimulators?.Using == true)
            {
                return true;
            }

            return false;
        }

        private static bool DoesFollowerWantToHeal(BotOwner bot)
        {
            if (bot?.Medecine == null || IsFollowerCurrentlyHealing(bot))
            {
                return false;
            }

            var decision = bot.Brain?.Agent?.LastResult();
            if (decision != null)
            {
                string reason = decision.Value.Reason ?? string.Empty;
                if (string.Equals(reason, "runToHeal", StringComparison.Ordinal) ||
                    string.Equals(reason, "moveToHeal", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return bot.Medecine.FirstAid?.Have2Do == true
                || bot.Medecine.SurgicalKit?.HaveWork == true;
        }

        public static void Enable()
        {
            if (Singleton<AbstractGame>.Instantiated)
            {
                var gameWorld = Singleton<GameWorld>.Instance;

                Instance = gameWorld.GetOrAddComponent<PingTeamates>();
                radioSound = gameWorld.GetOrAddComponent<RadioSound>();
                radioSound.Enable();
            }
        }
        private Vector3 GetLimitedPosition(Vector3 origin, Vector3 target, float maxDistance)
        {
            Vector3 delta = target - origin;
            float distance = delta.magnitude;
            if (distance > maxDistance && distance > 0.001f)
            {
                return origin + (delta / distance) * maxDistance;
            }

            return target;
        }

        private Vector3 GetClosestFollowerPosition(Vector3 playerPosition)
        {
            float best = float.MaxValue;
            Vector3 bestPos = playerPosition;

            foreach (BotData bt in botMap)
            {
                if (bt?.Data == null || bt.Data.IsDead) continue;

                float sqr = (bt.Data.Position - playerPosition).sqrMagnitude;
                if (sqr < best)
                {
                    best = sqr;
                    bestPos = bt.Data.Position;
                }
            }

            return bestPos;
        }

        private bool HasAnyAliveFollower()
        {
            foreach (BotData bt in botMap)
            {
                if (bt?.Data != null && !bt.Data.IsDead)
                {
                    return true;
                }
            }

            return false;
        }

        public static void Disable()
        {
            Instance.Dispose();
        }

    }

}
