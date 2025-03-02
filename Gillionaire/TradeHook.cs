using Dalamud.Hooking;
using System;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using GIllionaire;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Gillionaire
{
    using ReceiveEventDelegate = AgentInterface.Delegates.ReceiveEvent;

    // This hook is just for testing purposes. It will log the values of the trade window interactions
    public unsafe class TradeHook : IDisposable
    {

        private readonly Hook<ReceiveEventDelegate>? receiveEventHook;

        public TradeHook()
        {
            var agent = (AgentInterface*)Plugin.GameGui.FindAgentInterface("Trade");
            this.receiveEventHook = Plugin.GameInteropProvider.HookFromAddress<ReceiveEventDelegate>(
                agent->VirtualTable->ReceiveEvent,
                this.DetourRecieveEvent
            );

            this.receiveEventHook.Enable();
        }

        private AtkValue* DetourRecieveEvent(AgentInterface* thisPtr, AtkValue* returnValue, AtkValue* values, uint valueCount, ulong eventKind)
        {
            var retValue = receiveEventHook!.Original(thisPtr, returnValue, values, valueCount, eventKind);
            if (returnValue != null)
                Plugin.Log.Info($"Return Value {returnValue->ToString()}");

            if (values != null)
                Plugin.Log.Info($"values: {values->ToString()}");
            Plugin.Log.Info($"Value Count {valueCount}");
            Plugin.Log.Info($"Event Kind {eventKind}");
            return retValue;
        }

        public void Dispose()
        {
            this.receiveEventHook?.Dispose();
        }
    }
}
