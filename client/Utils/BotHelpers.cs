using EFT;
using System.Collections;
using UnityEngine;

using friendlySAIN.Brains;

namespace friendlySAIN.Utils
{
    public class BotHelpers : MonoBehaviour
    {
        private Coroutine watchCoroutine;

        private BotOwner botOwner;
        public void AttachStuckWatcher(BotOwner bot)
        {

            FollowerBrain brain = bot.Brain.BaseBrain as FollowerBrain;
            brain.OnDispose += DetachStuckWatcher;

            botOwner = bot;

            watchCoroutine = StartCoroutine(UpdateWatchCoroutine());

            botOwner.GetPlayer.HealthController.DiedEvent += OnDead;
            botOwner.LeaveData.OnLeave += OnLeave;
        }

        private void DetachStuckWatcher(BotOwner bot)
        {
            FollowerBrain brain = bot.Brain.BaseBrain as FollowerBrain;
            brain.OnDispose -= DetachStuckWatcher;
            StopCoroutine(watchCoroutine);
            botOwner.GetPlayer.HealthController.DiedEvent -= OnDead;
            botOwner.LeaveData.OnLeave -= OnLeave;
        }

        private void OnDead(EDamageType damageType)
        {
            DetachStuckWatcher(botOwner);

        }
        private void OnLeave(BotOwner _bot)
        {
            DetachStuckWatcher(botOwner);
        }
        private IEnumerator UpdateWatchCoroutine()
        {
            while (true)
            {
                if (botOwner.BotState != EBotState.Active || botOwner.IsDead) yield break;
                yield return new WaitForSeconds(2f);
            }
        }

        private bool isBotMoving()
        {
            bool isMoving = false;
            isMoving = botOwner.Mover.IsMoving || botOwner.Mover.Sprinting;

            return isMoving;
        }
    }
}
