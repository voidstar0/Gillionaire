using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState.Objects;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Dalamud.Game;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.UI;
using ECommons;
using ECommons.UIHelpers.AddonMasterImplementations;
using ECommons.Automation;
using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace GIllionaire;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ISigScanner SigScanner { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] public static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] public static IGameGui GameGui { get; private set; } = null!;
    [PluginService] public static ITargetManager TargetManager { get; private set; } = null!;

    private const int MaxTradeAmount = 1_000_000; // 1 million gil cap per trade

    private int remainingGil = 0;
    private bool isTradeInProgress = false;
    public Plugin()
    {
        ECommonsMain.Init(PluginInterface, this);
        ChatGui.ChatMessage += OnChatMessage;
        Framework.Update += SelectYes;
        CommandManager.AddHandler("/giltrade", new CommandInfo(OnCommand)
        {
            HelpMessage = "Starts automated gil trading: /giltrade <amount>"
        });

        Log.Information($"===Gillionaire Loaded===");
    }

    private unsafe void SelectYes(IFramework framework)
    {
        if (isTradeInProgress)
        {
            var addon = (AddonSelectYesno*)GameGui.GetAddonByName("SelectYesno");
            if (addon == null) return;
            new AddonMaster.SelectYesno(addon).Yes();
        }
    }

    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        // Hacky way to check if the trade is complete
        // TODO: Replace with a more reliable method
        if (message.TextValue.Contains("Trade complete.") && isTradeInProgress)
        {
            isTradeInProgress = false;
            if (remainingGil > 0)
            {
                // Wait a moment before starting next trade
                var timer = new System.Timers.Timer(500);
                timer.AutoReset = false;
                timer.Start();
                timer.Elapsed += (sender, e) =>
                {
                    StartNextTrade();
                };
            }
            else
            {
                Log.Information("Gil trading completed successfully.");
            }
        }
        else if (message.TextValue.Contains("Trade canceled."))
        {
            isTradeInProgress = false;
            remainingGil = 0;
            Log.Information("Gil trading canceled.");
        }
    }

    private void StartNextTrade()
    {
        if (TargetManager.Target == null)
        {
            ChatGui.Print("No target selected. Please target a player to trade with.");
            return;
        }
        if (isTradeInProgress || remainingGil <= 0) return;

        isTradeInProgress = true;
        // Trade the player you're targeting
        Chat.Instance.ExecuteCommand("/trade");

        var timer = new System.Timers.Timer(800); // Wait for trade window to open
        timer.AutoReset = false;
        timer.Start();
        timer.Elapsed += (sender, e) =>
        {
            int amountToSend = CalculateTradeAmount(remainingGil, MaxTradeAmount);
            remainingGil -= amountToSend;
            Log.Information($"Trading {amountToSend} gil. Remaining: {remainingGil}");
            TradeAmount(amountToSend);
        };
    }

    // Calculate amount to send in current trade
    public int CalculateTradeAmount(int totalRemaining, int maxPerTrade)
    {
        // If we can send it all, do so
        if (totalRemaining <= maxPerTrade)
            return totalRemaining;

        // Calculate the optimal amount to send
        int amountToSend = totalRemaining % maxPerTrade;

        // If the remainder is 0, we need to send the max amount
        if (amountToSend == 0)
            return maxPerTrade;

        return amountToSend;
    }

    public unsafe void TradeAmount(int amount)
    {
        // Get the Trade window
        var tradeAgent = (AgentInterface*)GameGui.FindAgentInterface("Trade");
        if (tradeAgent == null)
        {
            Log.Error("Trade window not found.");
            isTradeInProgress = false;
            return;
        }

        var timer = new System.Timers.Timer(300);
        timer.AutoReset = false;
        // This is a hacky way of specifying the Gil to send and pressing the Trade button.
        var clickatkReturn = new AtkValue { Bool = false };
        var clickvalues = new AtkValue { Type = ValueType.Int, Int = 2 };
        tradeAgent->ReceiveEvent(&clickatkReturn, &clickvalues, 2, 0);

        var atkReturn = new AtkValue { Bool = true };
        var values = new AtkValue { Type = ValueType.Int, Int = amount };
        tradeAgent->ReceiveEvent(&atkReturn, &values, 1, 1);

        timer.Start();
        timer.Elapsed += (sender, e) =>
        {
            var clickCloseatkReturn = new AtkValue { Bool = true };
            var clickClosevalues = new AtkValue { Type = ValueType.Int, Int = -1 };
            tradeAgent->ReceiveEvent(&clickCloseatkReturn, &clickClosevalues, 2, 1);

            var finalCloseReturn = new AtkValue { Bool = false };
            var finalCloseValues = new AtkValue { Type = ValueType.Int, Int = 4 };
            tradeAgent->ReceiveEvent(&finalCloseReturn, &finalCloseValues, 2, 0);

            var finalCloseReturn2 = new AtkValue { Bool = false };
            var finalCloseValues2 = new AtkValue { Type = ValueType.Int, Int = 0 };
            tradeAgent->ReceiveEvent(&finalCloseReturn2, &finalCloseValues2, 2, 0);
        };
    }

    public void Dispose()
    {
        ECommonsMain.Dispose();
        ChatGui.ChatMessage -= OnChatMessage;
        Framework.Update -= SelectYes;
        CommandManager.RemoveHandler("/giltrade");
    }

    private unsafe void OnCommand(string command, string args)
    {
        var currentGil = InventoryManager.Instance()->GetGil();
        // Get gil amount as argument
        var validAmount = int.TryParse(args, out var gilAmount);
        var hasEnoughGil = gilAmount <= currentGil;
        if (!validAmount || gilAmount == 0)
        {
            Log.Error("Invalid gil amount. Please specify a positive number.");
            return;
        }
        if (!hasEnoughGil)
        {
            Log.Information($"You do not have enough gil to start this trade.");
            return;
        }
        remainingGil = gilAmount;
        isTradeInProgress = false;
        Log.Information($"Starting trade sequence for {gilAmount} gil");
        StartNextTrade();
    }
}