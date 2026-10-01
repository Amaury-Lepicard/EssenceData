using System.Collections;
using System.Collections.Generic;

namespace EssenceData.code
{
    /// <summary>
    /// Adds the status effects in param_status_effects to each target, once per enemy unit
    /// in the target's room that has at least param_int stacks of the status named by param_str.
    /// </summary>
    public sealed class CardEffectAddStatusEffectPerEnemyWithStatus : CardEffectBase
    {
        public override PropDescriptions CreateEditorInspectorDescriptions()
        {
            return new PropDescriptions
            {
                [CardEffectFieldNames.ParamStatusEffects.GetFieldName()] = new PropDescription("Status Effects To Add", "Added once per qualifying enemy unit."),
                [CardEffectFieldNames.ParamStr.GetFieldName()] = new PropDescription("Counted Status", "Status id an enemy unit must have."),
                [CardEffectFieldNames.ParamInt.GetFieldName()] = new PropDescription("Minimum Stacks", "Stacks of the counted status an enemy unit needs to count."),
            };
        }

        public override IEnumerator ApplyEffect(CardEffectState cardEffectState, CardEffectParams cardEffectParams, ICoreGameManagers coreGameManagers, ISystemManagers sysManagers)
        {
            StatusEffectStackData[] stacks = cardEffectState.GetParamStatusEffectStackData();
            string countedStatus = cardEffectState.GetParamStr();
            int minimumStacks = cardEffectState.GetParamInt();
            if (stacks == null || stacks.Length == 0 || string.IsNullOrEmpty(countedStatus))
            {
                yield break;
            }

            foreach (CharacterState target in cardEffectParams.targets)
            {
                RoomState? room = target.GetCurrentRoom();
                if (room == null)
                {
                    continue;
                }
                room.GetCharactersWithStatus(Team.Type.Heroes, countedStatus, out List<CharacterState> enemies);
                int count = 0;
                foreach (CharacterState enemy in enemies)
                {
                    if (!enemy.IsDead && enemy.GetStatusEffectStacks(countedStatus) >= minimumStacks)
                    {
                        count++;
                    }
                }
                if (count == 0)
                {
                    continue;
                }
                foreach (StatusEffectStackData stack in stacks)
                {
                    target.AddStatusEffect(stack.statusId, stack.count * count, cardEffectParams.selfTarget);
                }
            }
            yield return coreGameManagers.GetCombatManager().RunTriggerQueue();
        }

        public override void GetTooltipsStatusList(CardEffectState cardEffectState, ref List<string> outStatusIdList)
        {
            foreach (StatusEffectStackData stack in cardEffectState.GetSourceCardEffectData().GetParamStatusEffects())
            {
                outStatusIdList.Add(stack.statusId);
            }
            string countedStatus = cardEffectState.GetParamStr();
            if (!string.IsNullOrEmpty(countedStatus))
            {
                outStatusIdList.Add(countedStatus);
            }
        }
    }
}
