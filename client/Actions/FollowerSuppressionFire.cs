using EFT;
using EFT.InventoryLogic;
using friendlySAIN.Components;
using System;
using UnityEngine;

using StandardBrain = GClass26;

namespace friendlySAIN.Actions
{
    /**
     * Enhancement of the suppression fire action for a follower
     */
    public class FollowerSuppressionFire : GClass274
    {
        private readonly GClass180 gclass145_0;

        private bool _init = false;
        private bool grSupport = false;

        private Vector3? grPosition = null;
        private Vector3? grTarget = null;
        private bool grAllowed = true;

        private bool bool_1 = false;
        public FollowerSuppressionFire(BotOwner bot) : base(bot)
        {
            gclass145_0 = new GClass180(bot);
        }

        protected virtual void Init()
        {
            (botOwner_0.Brain.Agent as FollowerAIAgent<BotLogicDecision>).OnUpdate += OnAgentUpdate;
            (botOwner_0.Brain.Agent as FollowerAIAgent<BotLogicDecision>).OnDispose += OnAgentDispose;

            _init = true;
        }
        protected void OnAgentUpdate(AICoreActionResultStruct<BotLogicDecision, StandardBrain> decision)
        {
            if (
                decision.Action != BotLogicDecision.suppressFire && decision.Reason != "suppressFireLauncher"
            )
            {
                grSupport = false;
                grPosition = null;
                grAllowed = true;
                bool_1 = false;
            }
        }

        protected void OnAgentDispose(object sender, EventArgs e)
        {
            (botOwner_0.Brain.Agent as FollowerAIAgent<BotLogicDecision>).OnUpdate -= OnAgentUpdate;
            (botOwner_0.Brain.Agent as FollowerAIAgent<BotLogicDecision>).OnDispose -= OnAgentDispose;
            _init = false;
            grSupport = false;
            grPosition = null;
            grAllowed = true;
            bool_1 = false;
        }

        public override void UpdateNodeByBrain(GClass27 data)
        {
            if (!_init) Init();

            if (grSupport && !botOwner_0.WeaponManager.Selector.IsWeaponReady) return;

            BotWeaponSelector selector = botOwner_0.WeaponManager.Selector;

            if (bool_1)
            {
                gclass145_0.UpdateNodeByBrain(data);
                return;
            }

            // our system that handles doing supression fire
            if (grAllowed)
            {
                if (selector.SecondPrimaryWeaponItem as Weapon == null || !(selector.SecondPrimaryWeaponItem as Weapon).IsGrenadeLauncher)
                {
                    // - no grenade launcher, switch to game's system
                    grAllowed = false;
                    base.UpdateNodeByBrain(data);
                    return;
                }

                botOwner_0.DoorOpener.Update();

                if (grPosition.HasValue)
                {

                    if (bool_1 && botOwner_0.GoToSomePointData.IsCome())
                    {
                        botOwner_0.StopMove();
                        if (grTarget.HasValue) botOwner_0.Steering.LookToPoint(grTarget.Value);
                        gclass145_0.UpdateNodeByBrain(data);
                        bool_1 = true;
                    }
                    else
                    {
                        botOwner_0.GoToSomePointData.UpdateToGo(true);
                    }

                    return;
                }

                var lastDecision = botOwner_0.Brain.Agent.LastResult();

                // can suppress from place
                if (lastDecision.Reason == "SupFire")
                {
                    bool_1 = true;
                    Vector3? target = botOwner_0.SuppressShoot.GetPoint();
                    if (target.HasValue) botOwner_0.Steering.LookToPoint(target.Value);
                    return;
                }

                if (lastDecision.Reason == "suppressFireLauncher")
                {
                    grSupport = true;
                }

                // has spot to suppress from
                if (botOwner_0.SuppressShoot.PointToSuppressFrom != null)
                {
                    grPosition = botOwner_0.SuppressShoot.PointToSuppressFrom.Position;
                    botOwner_0.GoToSomePointData.SetPoint(grPosition.Value);
                    Vector3? trg = botOwner_0.SuppressShoot.GetPoint();
                    grTarget = trg;
                    if (trg.HasValue) botOwner_0.Steering.LookToPoint(trg.Value);
                    botOwner_0.GoToSomePointData.UpdateToGo(true);
                    return;
                }


                Vector3? point = botOwner_0.SuppressShoot.GetPoint();

                // need to find a spot to suppress from
                if (point.HasValue)
                {
                    ShootPointClass shootPointClass = new ShootPointClass(point.Value, 1f);

                    if (Utils.Utils.CanShootToTarget(shootPointClass, botOwner_0.WeaponRoot.position, botOwner_0.LookSensor.Mask, false))
                    {
                        bool_1 = true;
                        botOwner_0.StopMove();
                        botOwner_0.Steering.LookToPoint(shootPointClass.Point);
                        return;
                    }

                    Vector3? firePosition = Utils.Covers.FindShootPosition(botOwner_0, 12f, 50f, position =>
                    {
                        if (Utils.Utils.CanShootToTarget(shootPointClass, position + botOwner_0.WeaponRoot.position, botOwner_0.LookSensor.Mask, false))
                        {
                            return true;
                        }

                        return false;

                    }, shootPointClass.Point);

                    if (firePosition.HasValue)
                    {
                        botOwner_0.GoToSomePointData.SetPoint(firePosition.Value);
                        botOwner_0.Steering.LookToPoint(shootPointClass.Point);
                        botOwner_0.GoToSomePointData.UpdateToGo(true);

                        grPosition = firePosition;
                        grTarget = shootPointClass.Point;

                        return;
                    }
                }

            }
            // game's system suppression 
            grAllowed = false;
            base.UpdateNodeByBrain(data);
        }
    }
}
