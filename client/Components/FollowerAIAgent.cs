using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using StandardBrain = GClass26;

namespace friendlySAIN.Components
{
    internal class FollowerAIAgent<T> : AICoreAgentClass<T>
    {
        public FollowerAIAgent(AICoreControllerClass aiCoreController, AICoreStrategyAbstractClass<T> strategy, Dictionary<T, GClass168> nodesDictionary, GameObject monoBehObject, string name, Func<T, GClass168> lazyGetter) : base(aiCoreController, strategy, nodesDictionary, monoBehObject, name, lazyGetter)
        {

        }

        public event Action<AICoreActionResultStruct<T, StandardBrain>> OnUpdate;
        public event EventHandler<EventArgs> OnDispose;
        public override void Update()
        {
            try
            {
                base.Update();
                //TestUpdate();
                AICoreActionResultStruct<T, StandardBrain>? actionResultStruct = base.LastResult();

                if (actionResultStruct.HasValue) OnUpdate?.Invoke(actionResultStruct.Value);

            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("AIAgent Error");
                Modules.Logger.LogError(ex);
            }
        }

        private void TestUpdate()
        {

            Stopwatch stopwatch = Stopwatch.StartNew();

            this.method_10();
            if (stopwatch.ElapsedMilliseconds > 50)
            {
                Modules.Logger.LogInfo($"AIAgent Update took {stopwatch.ElapsedMilliseconds}ms for method_10");
                stopwatch.Restart();
            }
            AICoreActionResultStruct<T, GClass26>? aicoreActionResultStruct = this.aICoreStrategyAbstractClass.Update(this.aICoreActionResultStruct);
            if (stopwatch.ElapsedMilliseconds > 50 && aicoreActionResultStruct.HasValue)
            {
                Modules.Logger.LogInfo($"AIAgent Update took {stopwatch.ElapsedMilliseconds}ms for reason - {aicoreActionResultStruct.Value.Reason} and action - {aicoreActionResultStruct.Value.Action}");
                stopwatch.Restart();
            }

            if (aicoreActionResultStruct != null)
            {
                T action = aicoreActionResultStruct.Value.Action;
                GClass168 gclass;
                if (this.dictionary_0.TryGetValue(action, out gclass))
                {
                    gclass.UpdateNodeByMain(this.aICoreActionResultStruct.Data);

                    if (stopwatch.ElapsedMilliseconds > 50)
                    {
                        Modules.Logger.LogInfo($"AIAgent UpdateNodeByMain took {stopwatch.ElapsedMilliseconds}ms for {gclass.GetType()}");
                        stopwatch.Restart();
                    }
                }
                else
                {
                    GClass168 gclass2 = this.func_0.Invoke(action);
                    if (gclass2 != null)
                    {
                        this.dictionary_0.Add(action, gclass2);
                        gclass2.UpdateNodeByMain(this.aICoreActionResultStruct.Data);
                    }

                    if (stopwatch.ElapsedMilliseconds > 50)
                    {
                        Modules.Logger.LogInfo($"AIAgent UpdateNodeByMainLazy took {stopwatch.ElapsedMilliseconds}ms for {gclass2.GetType()}");
                        stopwatch.Restart();
                    }
                }
                this.aICoreActionResultStruct = aicoreActionResultStruct.Value;
            }

            stopwatch.Stop();
        }

        public new void Dispose()
        {
            base.Dispose();
            OnDispose?.Invoke(this, EventArgs.Empty);
        }
    }
}
