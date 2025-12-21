using EFT;
using friendlySAIN.Actions;
using System.Collections.Generic;

using Action = GClass168;

namespace friendlySAIN.Components
{
    // replication of GClass507
    public class FollowerCreateNode
    {

        public static Action CreateNode(BotLogicDecision type, BotOwner bot)
        {
            if (type == BotLogicDecision.doorOpen)
                return new FollowerOpenDoor(bot);

            if (type == BotLogicDecision.botTakeItem)
                return new FollowerTakeLoot(bot);

            if (type == BotLogicDecision.attackMoving)
                return new FollowerAttackMove(bot);

            if (type == BotLogicDecision.attackMovingWithSuppress)
                return new FollowerAttackMove(bot, true);

            if (type == BotLogicDecision.holdPosition)
                return new FollowerHoldPosition(bot);

            if (type == BotLogicDecision.goToPoint)
                return new FollowerGoToPoint(bot);

            if (type == BotLogicDecision.goToEnemy)
                return new FollowerGoToEnemy(bot);

            if (type == BotLogicDecision.shootFromPlace)
                return new FollowerShootFromPlace(bot);

            if (type == (BotLogicDecision)CustomBotDecisions.DogFight)
                return new FollowerDogFight(bot);

            if (type == BotLogicDecision.dogFight)
                return new AltDogFight(bot);

            if (type == BotLogicDecision.suppressFire)
                return new FollowerSuppressionFire(bot);

            if (type == BotLogicDecision.followerPatrol)
            {
                return new FollowerPatrol(bot);
            }

            if (type == BotLogicDecision.goToPointTactical)
                return new FollowerGoToPointTactical(bot);

            if (type == BotLogicDecision.goToCoverPointTactical)
                return new FollowerGoToCoverTactical(bot);

            if (type == (BotLogicDecision)CustomBotDecisions.attackRetreat)
                return new FollowerAttackRetreat(bot);

            if (type == (BotLogicDecision)CustomBotDecisions.MoveToPoint)
                return new FollowerMoveToPoint(bot);

            if (type == (BotLogicDecision)CustomBotDecisions.SniperSearch)
                return new FollowerSniperSearch(bot);

            if (type == (BotLogicDecision)CustomBotDecisions.CoverToCover)
                return new FollowerCoverToCover(bot);

            if (type == (BotLogicDecision)CustomBotDecisions.GuardToCover)
                return new FollowerGuardCover(bot);

            if (type == (BotLogicDecision)CustomBotDecisions.EnemySearch)
                return new FollowerEnemySearch(bot);

            return GClass522.CreateNode(type, bot);
        }

        public static Dictionary<BotLogicDecision, Action> ActionsList(BotOwner bot)
        {

            Dictionary<BotLogicDecision, Action> dictionary = new Dictionary<BotLogicDecision, Action>();

            smethod_0(dictionary, BotLogicDecision.holdPosition, bot);
            smethod_0(dictionary, BotLogicDecision.goToCoverPoint, bot);
            smethod_0(dictionary, BotLogicDecision.attackMoving, bot);
            smethod_0(dictionary, BotLogicDecision.attackMovingWithSuppress, bot);
            smethod_0(dictionary, BotLogicDecision.shootFromPlace, bot);
            smethod_0(dictionary, BotLogicDecision.simplePatrol, bot);
            smethod_0(dictionary, BotLogicDecision.followerPatrol, bot);
            smethod_0(dictionary, BotLogicDecision.lay, bot);
            smethod_0(dictionary, BotLogicDecision.plantMine, bot);
            smethod_0(dictionary, BotLogicDecision.crawl, bot);
            smethod_0(dictionary, BotLogicDecision.moveStealthy, bot);
            smethod_0(dictionary, BotLogicDecision.teleportToCover, bot);
            smethod_0(dictionary, BotLogicDecision.runToCover, bot);
            smethod_0(dictionary, BotLogicDecision.goToEnemy, bot);
            smethod_0(dictionary, BotLogicDecision.runToEnemy, bot);
            smethod_0(dictionary, BotLogicDecision.runToStationary, bot);
            smethod_0(dictionary, BotLogicDecision.suppressStationary, bot);
            smethod_0(dictionary, BotLogicDecision.shootFromStationary, bot);
            smethod_0(dictionary, BotLogicDecision.dogFight, bot);
            smethod_0(dictionary, BotLogicDecision.search, bot);
            smethod_0(dictionary, BotLogicDecision.shootFromCover, bot);
            smethod_0(dictionary, BotLogicDecision.deactivateMine, bot);
            smethod_0(dictionary, BotLogicDecision.runAwayGrenade, bot);
            smethod_0(dictionary, BotLogicDecision.runAwayBTR, bot);
            smethod_0(dictionary, BotLogicDecision.runToEnemyZigZag, bot);
            smethod_0(dictionary, BotLogicDecision.shootToSmoke, bot);
            smethod_0(dictionary, BotLogicDecision.suppressFire, bot);
            smethod_0(dictionary, BotLogicDecision.followPlayer, bot);
            smethod_0(dictionary, BotLogicDecision.heal, bot);
            smethod_0(dictionary, BotLogicDecision.repairMalfunction, bot);
            smethod_0(dictionary, BotLogicDecision.goToPoint, bot);
            smethod_0(dictionary, BotLogicDecision.goToPointTactical, bot);
            smethod_0(dictionary, BotLogicDecision.axeTarget, bot);
            smethod_0(dictionary, BotLogicDecision.oneMeleeAttack, bot);
            smethod_0(dictionary, BotLogicDecision.grenadeSuicide, bot);
            smethod_0(dictionary, BotLogicDecision.warnPlayer, bot);
            smethod_0(dictionary, BotLogicDecision.doorOpen, bot);
            smethod_0(dictionary, BotLogicDecision.panicSitting, bot);
            smethod_0(dictionary, BotLogicDecision.healStimulators, bot);
            smethod_0(dictionary, BotLogicDecision.healAnotherTarget, bot);
            smethod_0(dictionary, BotLogicDecision.deadBody, bot);
            smethod_0(dictionary, BotLogicDecision.friendlyTilt, bot);
            smethod_0(dictionary, BotLogicDecision.eatDrink, bot);
            smethod_0(dictionary, BotLogicDecision.watchSecondWeapon, bot);
            smethod_0(dictionary, BotLogicDecision.gesture, bot);
            smethod_0(dictionary, BotLogicDecision.peaceful, bot);
            smethod_0(dictionary, BotLogicDecision.followMeRequest, bot);
            smethod_0(dictionary, BotLogicDecision.peaceHardAim, bot);
            smethod_0(dictionary, BotLogicDecision.peaceLook, bot);
            smethod_0(dictionary, BotLogicDecision.suppressGrenade, bot);
            smethod_0(dictionary, BotLogicDecision.runAndThrowGrenadeFromPlace, bot);
            smethod_0(dictionary, BotLogicDecision.throwGrenadeFromPlace, bot);
            smethod_0(dictionary, BotLogicDecision.alternativePatrol, bot);
            smethod_0(dictionary, BotLogicDecision.botDropItem, bot);
            smethod_0(dictionary, BotLogicDecision.goToLootPointNode, bot);
            smethod_0(dictionary, BotLogicDecision.goToExfiltrationPointNode, bot);
            smethod_0(dictionary, BotLogicDecision.botTakeItem, bot);
            smethod_0(dictionary, BotLogicDecision.flashed, bot);
            smethod_0(dictionary, BotLogicDecision.standBy, bot);
            smethod_0(dictionary, BotLogicDecision.turnAwayLight, bot);
            smethod_0(dictionary, BotLogicDecision.leaveMap, bot);
            smethod_0(dictionary, BotLogicDecision.runToCoverZigZag, bot);
            smethod_0(dictionary, BotLogicDecision.summon, bot);
            smethod_0(dictionary, BotLogicDecision.khorovodChristmasEvent, bot);
            smethod_0(dictionary, BotLogicDecision.doGiftChristmasEvent, bot);
            smethod_0(dictionary, BotLogicDecision.goToCoverPointTactical, bot);
            // - our custom decisions
            smethod_0(dictionary, (BotLogicDecision)CustomBotDecisions.attackRetreat, bot);
            smethod_0(dictionary, (BotLogicDecision)CustomBotDecisions.MoveToPoint, bot);
            smethod_0(dictionary, (BotLogicDecision)CustomBotDecisions.SniperSearch, bot);
            smethod_0(dictionary, (BotLogicDecision)CustomBotDecisions.CoverToCover, bot);
            smethod_0(dictionary, (BotLogicDecision)CustomBotDecisions.GuardToCover, bot);
            smethod_0(dictionary, (BotLogicDecision)CustomBotDecisions.EnemySearch, bot);

            return dictionary;
        }

        public static void smethod_0(Dictionary<BotLogicDecision, Action> dictionary, BotLogicDecision botLogicDecision, BotOwner bot)
        {
            Action decision = CreateNode(botLogicDecision, bot);

            dictionary.Add(botLogicDecision, decision);
        }
    }
}
