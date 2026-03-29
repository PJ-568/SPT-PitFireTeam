using ChatShared;
using EFT.UI.Chat;
using friendlySAIN.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace friendlySAIN.Patches
{
    internal class CourierDialogueAvatarPatch : ModulePatch
    {
        private const string CourierSenderId = "67b0f29e151899410b04aacb";
        private const int CourierAid = 900001;
        private const string CourierNickname = "Squadmate Courier";

        private static readonly FieldInfo DialogueClassField = AccessTools.Field(typeof(DialogueView), "dialogueClass");
        private static readonly FieldInfo DialogueIconField = AccessTools.Field(typeof(DialogueView), "_dialogueIcon");

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(DialogueView), "method_2");
        }

        [PatchPostfix]
        private static void PatchPostfix(DialogueView __instance)
        {
            try
            {
                DialogueClass dialogue = DialogueClassField?.GetValue(__instance) as DialogueClass;
                if (!CourierAvatarSpriteCache.IsCourierDialogue(dialogue))
                {
                    return;
                }

                Image dialogueIcon = DialogueIconField?.GetValue(__instance) as Image;
                if (dialogueIcon == null)
                {
                    return;
                }

                Sprite courierSprite = CourierAvatarSpriteCache.GetOrLoad();
                if (courierSprite == null)
                {
                    return;
                }

                dialogueIcon.sprite = courierSprite;
                dialogueIcon.preserveAspect = true;
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to apply courier dialogue avatar.");
                Logger.LogError(ex);
            }
        }

        private static class CourierAvatarSpriteCache
        {
            private static readonly string[] AvatarPathCandidates =
            [
                Path.Combine(Environment.CurrentDirectory, "user", "mods", "friendlySAIN-ServerMod", "Resources", "avatars", "courier.png"),
                Path.Combine(Environment.CurrentDirectory, "user", "mods", "friendlySAIN-ServerMod", "Resources", "avatar", "courier.png"),
                Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty, "courier.png"),
                Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty, "resources", "courier.png")
            ];

            private static Sprite? _sprite;
            private static bool _loadAttempted;

            public static bool IsCourierDialogue(DialogueClass? dialogue)
            {
                if (dialogue?.Type != EMessageType.UserMessage)
                {
                    return false;
                }

                ChatMessageClass message = dialogue.Message ?? dialogue.ChatMessages?.LastOrDefault();
                if (message?.Member == null)
                {
                    return false;
                }

                if (string.Equals(message.Member.Id, CourierSenderId, StringComparison.Ordinal))
                {
                    return true;
                }

                if (message.Member.AccountId == CourierAid.ToString())
                {
                    return true;
                }

                string nickname = dialogue.Profile?.Info?.Nickname
                    ?? message.Member.Info?.Nickname
                    ?? message.Member.LocalizedNickname;

                return string.Equals(nickname, CourierNickname, StringComparison.Ordinal);
            }

            public static Sprite? GetOrLoad()
            {
                if (_sprite != null)
                {
                    return _sprite;
                }

                if (_loadAttempted)
                {
                    return null;
                }

                _loadAttempted = true;

                string avatarPath = AvatarPathCandidates.FirstOrDefault(File.Exists);
                if (string.IsNullOrWhiteSpace(avatarPath))
                {
                    Logger.LogWarning("[UI] Courier avatar could not be found.");
                    return null;
                }

                try
                {
                    byte[] fileData = File.ReadAllBytes(avatarPath);
                    Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                    if (!texture.LoadImage(fileData))
                    {
                        UnityEngine.Object.Destroy(texture);
                        Logger.LogWarning($"[UI] Failed to decode courier avatar '{avatarPath}'.");
                        return null;
                    }

                    texture.name = "friendlySAIN_CourierAvatar";
                    _sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                    _sprite.name = "friendlySAIN_CourierAvatar";
                    return _sprite;
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to load courier avatar '{avatarPath}'.");
                    Logger.LogError(ex);
                    return null;
                }
            }
        }
    }
}
