using EFT;

using friendlySAIN.Components;
using friendlySAIN.Layers;
using friendlySAIN.Layers.GoonsLayer;

namespace friendlySAIN.Brains.GoonsBrain
{
    public class BirdEyeFollowerBrain : FollowerBrain
    {

        protected BirdEyeFightLayer fightLayer;
        public BirdEyeFollowerBrain(BotOwner owner, pitAIBossPlayer boss) : base(owner, boss)
        {
            _currentTactic = "Assist";
            _defaultFollowDistance = 16;
            _followDistance = _defaultFollowDistance;
        }

        public override string ShortName()
        {
            return "BirdEyeFLW";
        }

        public override void AddLayers()
        {
            // order matters for which layer get the initial priority
            // follow
            BossFollowLayer followLayer = new BossFollowLayer(_owner, 50);
            method_0(1, followLayer, true);
            // requests
            FollowerRequestLayer layer4 = new FollowerRequestLayer(_owner, 55);
            method_0(2, layer4, true);
            // avoid danger
            FollowerAvoidDanger layer = new FollowerAvoidDanger(_owner, 80);
            base.method_0(3, layer, true);
            // weapon maintenance during combat
            KnightWeaponMtnLayer layer2 = new KnightWeaponMtnLayer(_owner, 78);
            base.method_0(4, layer2, true);
            // sniper fight
            fightLayer = new BirdEyeFightLayer(_owner, 65);
            method_0(5, fightLayer, true);
            // - item taker
            FollowerLootLayer layer7 = new FollowerLootLayer(_owner, 40);
            method_0(6, layer7, true);
        }

        public override void BossOrdersChanged()
        {
            fightLayer.OrdersChanged();
        }

        protected override void OnDead(EDamageType damageType)
        {
            if (_boss == null) return;
            // watch for active quests requiring BirdEye as teamate and mark them as failed if he dies
            _boss.realPlayer.AbstractQuestControllerClass.Quests.ExecuteForEach(quest =>
            {
                foreach (var id in Utils.Props.Quests["BirdEye"])
                {
                    if (quest.Template.Id == id && (quest.QuestStatus == EFT.Quests.EQuestStatus.Started || quest.QuestStatus == EFT.Quests.EQuestStatus.AvailableForFinish))
                    {
                        if (Utils.Utils.FlagGet("questGoons"))
                        {
                            quest.SetStatus(EFT.Quests.EQuestStatus.Fail, true, false);

                        }
                    }
                }
            });

            base.OnDead(damageType);

        }
    }
}
