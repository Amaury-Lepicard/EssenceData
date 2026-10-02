using System.Collections;

namespace EssenceData.code
{
    /// <summary>
    /// Applies the upgrade in param_upgrade to each target once per unused point of
    /// friendly capacity on the target's floor, measured before the first application.
    /// With a +1 size upgrade the unit grows to fill the floor.
    /// </summary>
    public sealed class CardEffectAddCardUpgradeToUnitsPerFreeCapacity : CardEffectAddCardUpgradeToUnits
    {
        public override PropDescriptions CreateEditorInspectorDescriptions()
        {
            return new PropDescriptions
            {
                [CardEffectFieldNames.ParamCardUpgradeData.GetFieldName()] = new PropDescription("Card Upgrade", "Applied once per unused capacity on the target's floor."),
                [CardEffectFieldNames.AdditionalParamInt1.GetFieldName()] = new PropDescription("Upgrade Lifetime", CardUpgradeHelper.UpgradeLifetimeEditorTooltip, typeof(UnitUpgradeLifetime)),
            };
        }

        public override bool TestEffect(CardEffectState cardEffectState, CardEffectParams cardEffectParams, ICoreGameManagers coreGameManagers)
        {
            foreach (CharacterState target in cardEffectParams.targets)
            {
                if (FreeCapacity(target) > 0)
                {
                    return true;
                }
            }
            return false;
        }

        public override IEnumerator ApplyEffect(CardEffectState cardEffectState, CardEffectParams cardEffectParams, ICoreGameManagers coreGameManagers, ISystemManagers sysManagers)
        {
            foreach (CharacterState target in cardEffectParams.targets)
            {
                int count = FreeCapacity(target);
                for (int i = 0; i < count; i++)
                {
                    yield return base.ApplyEffect(cardEffectState, cardEffectParams, coreGameManagers, sysManagers);
                }
            }
        }

        private static int FreeCapacity(CharacterState target)
        {
            RoomState? room = target.GetCurrentRoom();
            if (room == null)
            {
                return 0;
            }
            CapacityInfo capacity = room.GetCapacityInfo(target.GetTeamType());
            return capacity.max - capacity.count;
        }
    }
}
