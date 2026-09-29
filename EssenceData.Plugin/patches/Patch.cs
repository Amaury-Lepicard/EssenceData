using Conductor.Extensions;
using EssenceData;
using HarmonyLib;
using ShinyShoe;
using System.Reflection;
using System.Reflection.Emit;
using TMPro;
using UnityEngine;

[HarmonyPatch(typeof(CharacterData), nameof(CharacterData.GetCharacterCardText))]
class CharacterData_GetCharacterCardText_AddFusedMonster_Patch
{
    public static void Postfix(ref string text, CardState cardState)
    {
        var upgrades = cardState?.GetCardStateModifiers().GetCardUpgrades();
        if (upgrades.IsNullOrEmpty()) return;
        foreach (var upgrade in upgrades!)
        {
            if (upgrade.IsEssenceUpgrade())
            {
                var character = upgrade.GetSourceEssenceCharacter();
                var addedText = string.Format("TextFormat_Fused".Localize(), character!.GetName());
                text += $"{Environment.NewLine}{addedText}{Environment.NewLine}";
            }
        }
    }
}

[HarmonyPatch(typeof(CardTooltipContainer), "AddUpgradedCharacterTriggers")]
class CardTooltipContainer_AddUpgradedCharacterTriggers_SynthesisTooltipsPatch
{
    public static void Postfix(CardStateModifiers cardStateModifiers, CardTooltipContainer __instance)
    {
        if (cardStateModifiers == null)
        {
            return;
        }
        foreach (CardUpgradeState cardUpgrade in cardStateModifiers.GetCardUpgrades())
        {
            var sourceCharacter = cardUpgrade.GetSourceEssenceCharacter();
            if (cardUpgrade.IsEssenceUpgrade() && sourceCharacter != null)
            {
                TooltipUI tooltipUI = __instance.InstantiateTooltip("synthesis", Plugin.Synthesis, false);
                string title = string.Format("CardFrameUI_SynthesisTextFormat".Localize(null), sourceCharacter.GetName());
                tooltipUI?.Set(title, cardUpgrade.GetUpgradeDescriptionKey().Localize(new CardEffectLocalizationContext(cardUpgrade.GetSourceCardUpgradeData()!)));
            }
        }
    }
}

[HarmonyPatch(typeof(CardUI), nameof(CardUI.UpdateTextContent))]
class CardUI_UpdateTextContent_ShowSynthesisEffectPatch
{
    public static bool EnableShowingSynthesis = false;
    public static bool Prefix(CardState cardState, CardFrameUI ____cardFrame)
    {
        if (!EnableShowingSynthesis || !cardState.IsMonsterCard())
            return true;

        var essence = cardState.GetSpawnCharacterData()?.GetEssence();

        cardState.GetCardTypeCardText(out string outCardText);
        string text = essence?.GetUpgradeDescriptionKey()?.Localize(new CardEffectLocalizationContext(essence, null, cardState)) ?? "No essence";
        text = $"Effect: {text}";
        ____cardFrame.SetTextContent(cardState.GetCardType(), cardState.GetTitle(), text, outCardText);
        return false;
    }
}

// "Show Essence" switches the card lists between unit card text and unit essences, in the
// deck screen (deck, piles, upgrade and purge pickers), the card draft screen and the logbook.
// Bound to the Dragon's Hoard control (H / right stick click by default, as the forge toggle),
// and so is the button added to each screen: a click arrives as that control too. The button
// carries the settings screen's switch lozenge, its gem lit while essences show.
static class UnitEssenceToggle
{
    private const string ButtonName = "ToggleUnitEssencesButton";
    private const string GemName = "UnitEssencesGem";

    private static void ShowState(Component screen)
    {
        foreach (var gem in screen.GetComponentsInChildren<Transform>(true).Where(t => t.name == GemName))
            gem.gameObject.SetActive(CardUI_UpdateTextContent_ShowSynthesisEffectPatch.EnableShowingSynthesis);
    }

    private static IEnumerable<MethodBase> Screens(string deck, string draft, string logbook) =>
    [
        AccessTools.Method(typeof(DeckScreen), deck),
        AccessTools.Method(typeof(CardDraftScreen), draft),
        AccessTools.Method(typeof(CompendiumSectionCards), logbook),
    ];

    [HarmonyPatch]
    static class Toggle
    {
        static IEnumerable<MethodBase> TargetMethods() => Screens("ApplyScreenInput", "ApplyScreenInput", "ApplyScreenInput");

        static bool Prefix(MonoBehaviour __instance, InputManager.Controls triggeredMappingID, ref bool __result)
        {
            // An "h" typed into the logbook's search field is text, not the toggle.
            var selected = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            if (triggeredMappingID != InputManager.Controls.DragonsHoard
                || (selected != null && selected.TryGetComponent<TMP_InputField>(out var field) && field.isFocused))
                return true;

            CardUI_UpdateTextContent_ShowSynthesisEffectPatch.EnableShowingSynthesis ^= true;
            // Null in the logbook, which draws its cards without statistics.
            var stats = Traverse.Create(__instance).Field<CardStatistics>("cardStatistics").Value;
            foreach (var card in __instance.GetComponentsInChildren<CardUI>())
                if (card.GetCardState() is { } state)
                    card.UpdateTextContent(state, stats);
            ShowState(__instance);
            __result = true;
            return false;
        }
    }

    [HarmonyPatch]
    static class AddButton
    {
        static IEnumerable<MethodBase> TargetMethods() => Screens("Setup", "Setup", "Open");

        // Also runs when the button exists: the flag was reset on close, or set by a fusion.
        static void Postfix(MonoBehaviour __instance)
        {
            if (!__instance.GetComponentsInChildren<GameUISelectableButton>(true).Any(b => b.name == ButtonName))
                Create(__instance);
            ShowState(__instance);
        }

        private static void Create(MonoBehaviour __instance)
        {
            // A dialog button: the style of the deck and draft screens' own, and the dialog screen
            // is loaded everywhere, the main menu's logbook included.
            var template = Traverse.Create(UnityEngine.Object.FindObjectOfType<DialogScreen>(true))
                .Field("dialogPrefab").Field("button1").GetValue<GameUISelectableButton>();
            if (template == null)
                return;

            // In the logbook, under the search box of the filter sidebar; elsewhere, top right
            // under the deck screen's sort dropdown.
            var logbook = __instance is CompendiumSectionCards;
            var parent = logbook ? Traverse.Create(__instance).Field<FilterToolbar>("filterToolbar").Value.transform : __instance.transform;
            var button = UnityEngine.Object.Instantiate(template, parent);
            button.name = ButtonName;
            Traverse.Create(button).Field("inputType").SetValue((int)new CoreSymbol(nameof(InputManager.Controls.DragonsHoard)).m_value);

            var rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = logbook ? new Vector2(0.5f, 0f) : Vector2.one;
            rect.anchoredPosition = logbook ? new Vector2(0f, 8f) : new Vector2(-23.5f, -201f);
            rect.sizeDelta = new Vector2(255f, 64f);
            if (button.transform.Find("Target Graphic") is RectTransform graphic)
                graphic.sizeDelta = new Vector2(graphic.sizeDelta.x, 80f); // 104 by default

            // Its own label, not the key hint's text, which the game fills from inputType.
            var label = button.GetComponentsInChildren<TMP_Text>(true).First(text => text.GetComponentInParent<GameUIControlMapping>() == null);
            label.SetLocKey("DeckScreen_ShowEssence");
            label.fontSizeMax = label.fontSize;
            label.enableAutoSizing = true;
            label.margin = new Vector4(0f, 0f, 60f, 0f); // clear of the lozenge

            // The settings switch's lozenge, right in the button. Its gem, lit by the switch's own
            // ToggleOnToggleOn, is lit by ShowState here: the copy leaves that behind.
            var handle = Traverse.Create(UnityEngine.Object.FindObjectOfType<SettingsDialog>(true))
                .Field("backgroundMuteToggle").Field("handle").GetValue<RectTransform>();
            if (handle == null)
                return;
            var lozenge = UnityEngine.Object.Instantiate(handle, button.transform);
            lozenge.anchorMin = lozenge.anchorMax = lozenge.pivot = new Vector2(1f, 0.5f);
            lozenge.anchoredPosition = new Vector2(-20f, 0f);
            lozenge.sizeDelta = new Vector2(56f, 56f);
            // Nested under the frame image: Find would look only one level down.
            lozenge.GetComponentsInChildren<Transform>(true).First(t => t.name == "Image toggle on").name = GemName;
        }
    }

    // The flag is global: left on, it would also rewrite cards in hand once the screen closes.
    [HarmonyPatch]
    static class Reset
    {
        static IEnumerable<MethodBase> TargetMethods() => Screens("Close", "Close", "Close");

        static void Postfix() => CardUI_UpdateTextContent_ShowSynthesisEffectPatch.EnableShowingSynthesis = false;
    }
}

[HarmonyPatch(typeof(CardState), "SetupBodyUpgradeText")]
public static class SetupBodyUpgradeText_Patch
{
    public static bool ExcludeUpgrade(CardUpgradeState cardUpgrade)
    {
        // Return true to skip/continue, false to process normally
        return cardUpgrade.IsEssenceUpgrade();
    }

    private static readonly MethodInfo FilterMethod = AccessTools.Method(
        typeof(SetupBodyUpgradeText_Patch),
        nameof(ExcludeUpgrade)
    );

    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
    {
        var matcher = new CodeMatcher(instructions, il);

        // 1. Locate the MoveNext call (supports both List<T>.Enumerator and interface calls)
        matcher.MatchForward(false,
            new CodeMatch(i => (i.opcode == OpCodes.Call || i.opcode == OpCodes.Callvirt) &&
                               i.operand?.ToString().Contains("MoveNext") == true)
        );

        if (matcher.IsInvalid)
        {
            UnityEngine.Debug.LogError("[SetupBodyUpgradeText_Patch] Still failed to locate MoveNext call. Dumping opcodes for debugging.");
            return instructions;
        }

        // Attach a label to the MoveNext instruction so 'continue' jumps straight to it
        Label loopContinueLabel = il.DefineLabel();
        matcher.Instruction.labels.Add(loopContinueLabel);

        // 2. Go back and find get_Current followed by the local variable assignment (stloc)
        matcher.Start();
        matcher.MatchForward(false,
            new CodeMatch(i => (i.opcode == OpCodes.Call || i.opcode == OpCodes.Callvirt) &&
                               i.operand?.ToString().Contains("get_Current") == true),
            new CodeMatch(i => i.IsStloc())
        );

        if (matcher.IsInvalid)
        {
            UnityEngine.Debug.LogError("[SetupBodyUpgradeText_Patch] Failed to locate get_Current assignment.");
            return instructions;
        }

        // Move to the stloc instruction
        matcher.Advance(1);
        var stlocInstruction = matcher.Instruction;
        var ldlocInstruction = ConvertStlocToLdloc(stlocInstruction);

        // Move past stloc to insert the filter
        matcher.Advance(1);

        // Inject: ldloc <cardUpgrade> -> call FilterMethod -> brtrue <loopContinueLabel>
        matcher.Insert(
            ldlocInstruction,
            new CodeInstruction(OpCodes.Call, FilterMethod),
            new CodeInstruction(OpCodes.Brtrue, loopContinueLabel)
        );

        return matcher.InstructionEnumeration();
    }

    private static CodeInstruction ConvertStlocToLdloc(CodeInstruction stloc)
    {
        if (stloc.opcode == OpCodes.Stloc_0) return new CodeInstruction(OpCodes.Ldloc_0);
        if (stloc.opcode == OpCodes.Stloc_1) return new CodeInstruction(OpCodes.Ldloc_1);
        if (stloc.opcode == OpCodes.Stloc_2) return new CodeInstruction(OpCodes.Ldloc_2);
        if (stloc.opcode == OpCodes.Stloc_3) return new CodeInstruction(OpCodes.Ldloc_3);
        if (stloc.opcode == OpCodes.Stloc_S) return new CodeInstruction(OpCodes.Ldloc_S, stloc.operand);
        if (stloc.opcode == OpCodes.Stloc) return new CodeInstruction(OpCodes.Ldloc, stloc.operand);

        throw new InvalidOperationException($"Unexpected store local opcode: {stloc.opcode}");
    }
}