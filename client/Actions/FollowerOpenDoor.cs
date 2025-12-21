using EFT;
using EFT.Interactive;
using friendlySAIN.Modules;
using UnityEngine;
using UnityEngine.AI;

using StandardBrain = GClass26;

namespace friendlySAIN.Actions
{
    /**
     * Overwrite the openDoor action for the follower to make him open the door properly and fix look direction.
     */
    public class FollowerOpenDoor : GClass193<StandardBrain>
    {

        private bool bool_0;
        private bool bool_1;

        private Door Door;

        public FollowerOpenDoor(BotOwner bot) : base(bot)
        {
        }

        public override void UpdateNodeByBrain(StandardBrain data)
        {

            botOwner_0.DoorOpener.Update();

            Door reqDoor = InteractableObjects.GetDoorToOpen(botOwner_0);
            if (Door == null || Door != reqDoor)
            {
                if (Door != null)
                {
                    bool_0 = false;
                    bool_1 = false;
                }
                Door = reqDoor;
            }

            if (bool_1) return;

            if (Door == null)
            {
                ClearOpener();
                return;
            }
            ;

            if (Door.DoorState == EDoorState.Open)
            {
                ClearOpener();
                return;
            }

            if (!bool_0)
            {
                Vector3 position = Door.transform.position;

                NavMeshHit navMeshHit;
                if (NavMesh.SamplePosition(position, out navMeshHit, 2f, -1) && botOwner_0.GoToPoint(navMeshHit.position, false, -1f, false, false) == NavMeshPathStatus.PathComplete)
                {
                    botOwner_0.GoToSomePointData.SetPoint(navMeshHit.position);
                    botOwner_0.Steering.LookToMovingDirection();

                }
                else
                {
                    ClearOpener();
                }


                bool_0 = true;
                return;
            }
            else if (!bool_1)
            {
                botOwner_0.GoToSomePointData.UpdateToGo(false);
            }

            if (!botOwner_0.GoToSomePointData.IsCome())
            {
                return;
            }

            if (!bool_1)
            {
                botOwner_0.StopMove();
                botOwner_0.DoorOpener.OnEndInteract += ClearOpener;
                botOwner_0.DoorOpener.Interact(Door, EInteractionType.Open);

                bool_1 = true;
            }

        }

        public void ClearOpener()
        {
            Door = null;
            bool_0 = false;
            bool_1 = false;
            if (botOwner_0.BotRequestController.CurRequest != null && botOwner_0.BotRequestController.CurRequest.BotRequestType == BotRequestType.doorOpen)
            {
                botOwner_0.BotRequestController.CurRequest.Complete();
            }
            botOwner_0.DoorOpener.OnEndInteract -= ClearOpener;

            InteractableObjects.RemoveOpener(botOwner_0);
        }
    }
}
