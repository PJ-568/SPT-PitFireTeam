# pitFireTeam 4.x Brain Migration Notes

## Goal

Move from custom follower brains/actions/receiver to:

- SAIN + BigBrain as the primary combat/action authority
- pitFireTeam adds only a lightweight follower-regroup layer
- no custom `BaseBrain` replacement, no custom `AICoreAgentClass`, no custom `BotReceiver`

## What Makes A Bot Tick (4.x)

### Runtime chain

1. `BotsController` drives AI each frame via `AICoreController.Update()`.
   - `Client-Decompiled-4.x/Assembly-CSharp/EFT/BotsController.cs`
2. `AICoreControllerClass.Update()` iterates registered agents and calls `agent.Update()`.
   - `Client-Decompiled-4.x/Assembly-CSharp/AICoreControllerClass.cs`
3. `AICoreAgentClass<T>.Update()` asks the strategy (brain) for the current decision and updates the action node.
   - `Client-Decompiled-4.x/Assembly-CSharp/AICoreAgentClass.cs`
4. `AICoreStrategyAbstractClass<T>.Update()` selects the first active layer where `ShallUseNow()` is true (highest priority active layer wins).
   - `Client-Decompiled-4.x/Assembly-CSharp/AICoreStrategyAbstractClass.cs`
5. `AICoreLayerClass<T>.Update()` runs `ShallEndCurrentDecision()` and may request a new decision via `GetDecision()`.
   - `Client-Decompiled-4.x/Assembly-CSharp/AICoreLayerClass.cs`
6. Decision enum (`BotLogicDecision`) is mapped to node implementations by `BotActionNodesClass`.
   - `Client-Decompiled-4.x/Assembly-CSharp/BotActionNodesClass.cs`

### BotOwner side

- `BotOwner.method_10()` activates components including `Brain.Activate()`.
- `BotOwner.UpdateManual()` updates sensors/state systems.
- Agent decision loop itself is still executed by `BotsController -> AICoreController`.
- Files:
  - `Client-Decompiled-4.x/Assembly-CSharp/EFT/BotOwner.cs`
  - `Client-Decompiled-4.x/Assembly-CSharp/EFT/BotsController.cs`

## SAIN Pattern To Follow

SAIN registers BigBrain layers per brain short-name and per role via `BrainManager.AddCustomLayer(...)`.

- `SAIN-master/SAIN/Plugin/BigBrainHandler.cs`
- `SAIN-master/SAIN/Layers/SAINLayer.cs`

This is the correct architecture for 4.x when avoiding custom brain replacement.

## FriendlyPMC/PitFireTeam 3.11 -> 4.x Client Mapping

Verified mappings already present in ported `pitFireTeam` patches:

- `Class303` -> `Class308` (`SendRaidSettings` target)
  - `friendlypmc/client/Patches/RaidStartPatch.cs`
  - `pitFireTeam/client/Patches/RaidStartPatch.cs`
- `GClass3497` -> `ContextInteractionsClass` (group remove context)
  - `friendlypmc/client/Patches/RaidStartPatch.cs`
  - `pitFireTeam/client/Patches/RaidStartPatch.cs`
- `MatchmakerPlayerControllerClass` method targets changed to `GClass3926<RaidSettings>` for `method_39` and `method_21`
  - `friendlypmc/client/Patches/RaidStartPatch.cs`
  - `pitFireTeam/client/Patches/RaidStartPatch.cs`
- `GClass567` -> `PlayerAIDataClass` (constructor patch)
  - `friendlypmc/client/Patches/PlayerPatch.cs`
  - `pitFireTeam/client/Patches/PlayerPatch.cs`
- `"<AIBossPlayer>k__BackingField"` -> `"AibossPlayer_0"`
  - `friendlypmc/client/Patches/PlayerPatch.cs`
  - `pitFireTeam/client/Patches/PlayerPatch.cs`
- `BotReceiver` private field `"botOwner_0"` -> `"BotOwner_0"`
  - `friendlypmc/client/Patches/BotReceiverPatch.cs`
  - `pitFireTeam/client/Patches/BotReceiverPatch.cs`
- `BotMemoryClass` private field `"botOwner_0"` -> `"BotOwner_0"`
  - `friendlypmc/client/Patches/BotMemoryPatch.cs`
  - `pitFireTeam/client/Patches/BotMemoryPatch.cs`
- `BaseLogicLayerAbstractClass` flag `"bool_1"` -> `"Bool_1"`
  - seen in custom layer ports
- `BotsPresets`/session fields:
  - `"ginterface21_0"` -> `"Ginterface21_0"`
  - `"iSession"` -> `"ISession"`
  - `ProfileEndPoint` `"gclass1321_0"` -> `"Gclass1392_0"`
  - `friendlypmc/client/Patches/BotsControllerPatch.cs`
  - `pitFireTeam/client/Patches/BotsControllerPatch.cs`
- `GesturesMenu` list fields:
  - `"list_0"`/`"list_1"` -> `"list_1"`/`"list_2"`
  - `friendlypmc/client/Patches/GestureMenuPatch.cs`
  - `pitFireTeam/client/Patches/GestureMenuPatch.cs`

## Server 3.11 TS -> Server 4.08 C# Mapping (for mod API touchpoints)

FriendlyPMC server code used many 3.11 TS services/controllers/callbacks under `@spt/*`:

- `GameCallbacks`, `DialogueCallbacks`, `MatchCallbacks`
- `ProfileController`, `DialogueController`, `BotController`, `GameController`
- `BotGenerator`
- `QuestItemEventRouter`
- `NotificationSendHelper`, `MailSendService`, `ProfileHelper`
- `DatabaseService`, `DatabaseServer`, `ConfigServer`, `SaveServer`
- `LocaleService`, `JsonUtil`, `RandomUtil`, `HashUtil`, `HttpResponseUtil`

4.08 C# equivalents exist in:

- `SERVER-4.08/Libraries/SPTarkov.Server.Core/Callbacks/*`
- `SERVER-4.08/Libraries/SPTarkov.Server.Core/Controllers/*`
- `SERVER-4.08/Libraries/SPTarkov.Server.Core/Generators/*`
- `SERVER-4.08/Libraries/SPTarkov.Server.Core/Routers/ItemEvents/*`
- `SERVER-4.08/Libraries/SPTarkov.Server.Core/Helpers/*`
- `SERVER-4.08/Libraries/SPTarkov.Server.Core/Services/*`
- `SERVER-4.08/Libraries/SPTarkov.Server.Core/Servers/*`

## Practical Cleanup Direction (Follower Layer Only)

Keep:

- follower assignment/state management needed to mark bots as player followers
- compatibility patches needed for group/friendliness state only
- BigBrain custom layer registration (`FollowerRegroupLayer`)

Remove/disable:

- custom brain types (`FollowerBrain`, goon follower brains)
- custom agent (`FollowerAIAgent`)
- custom receiver (`FollowerReceiver`) and receiver patches
- custom tactical action/layer stack that competes with SAIN combat layers
- phrase/gesture command actions until basic follow behavior is stable

## Status in current branch

Already moved toward this model:

- plugin no longer enables receiver/talk/gesture/hearing/bullet phrase-control patches
- bot follow setup no longer swaps brain/agent/receiver to custom types
- BigBrain follower regroup layer added:
  - `pitFireTeam/client/BigBrain/FollowerRegroupLayer.cs`

