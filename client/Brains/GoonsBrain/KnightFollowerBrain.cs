using EFT;

using friendlySAIN.Components;
using friendlySAIN.Layers;
using friendlySAIN.Layers.GoonsLayer;

namespace friendlySAIN.Brains.GoonsBrain
{
    public class KnightFollowerBrain : FollowerBrain
    {
        KnightFightLayer gclass65_0_1;

        public KnightFollowerBrain(BotOwner owner, pitAIBossPlayer boss) : base(owner, boss)
        {
            _currentTactic = "Assist";
        }

        public override void AddLayers()
        {
            // follow layer
            BossFollowLayer layer6 = new BossFollowLayer(_owner, 50);
            base.method_0(1, layer6, true);
            // requests
            FollowerRequestLayer layer4 = new FollowerRequestLayer(_owner, 55);
            method_0(2, layer4, true);
            // avoid danger
            FollowerAvoidDanger layer = new FollowerAvoidDanger(_owner, 80);
            base.method_0(3, layer, true);
            // weapon maintenance during combat
            KnightWeaponMtnLayer layer2 = new KnightWeaponMtnLayer(_owner, 78);
            base.method_0(4, layer2, true);
            // fight logic
            gclass65_0_1 = new KnightFightLayer(_owner, 65);
            base.method_0(5, gclass65_0_1, true);
            // item taker
            FollowerLootLayer layer9 = new FollowerLootLayer(_owner, 40);
            method_0(6, layer9, true);
        }

        public override string ShortName()
        {
            return "KnightFLW";
        }

        public void ForceRecalcShootPos()
        {
            gclass65_0_1.ForceRecalcShootPos();
        }

        protected override void OnDead(EDamageType damageType)
        {
            if (_boss == null) return;
            // watch for active quests requiring Knight as teamate and mark them as failed if Knight dies
            _boss.realPlayer.AbstractQuestControllerClass.Quests.ExecuteForEach(quest =>
            {
                foreach (var id in Utils.Props.Quests["Knight"])
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

        public override void BossOrdersChanged()
        {
            gclass65_0_1.OrdersChanged();
        }
    }
}
