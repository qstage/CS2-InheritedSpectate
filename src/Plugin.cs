using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.ValveConstants.Protobuf;

namespace InheritedSpectate;

public class Plugin : BasePlugin
{
    public override string ModuleName => "InheritedSpectate";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "xstage";
    public override string ModuleDescription => "Plugin fixes problem when after changing map, spectators is automatically moved to T/CT team";

    private enum JoinTeamSignature
    {
        [EnumMember(Value = "55 48 89 E5 41 57 41 56 41 55 41 89 F5 41 54 49 89 FC 53 48 81 EC ? ? ? ? 48 8D 05")]
        Linux,

        [EnumMember(Value = "48 89 5C 24 ? 44 88 44 24 ? 55 56 57 41 54 41 55 41 56 41 57 48 8D 6C 24")]
        Windows
    }

    private readonly MemoryFunctionWithReturn<CCSPlayerController, CsTeam, bool, bool> JoinTeam = new(Plugin.GetSignature<JoinTeamSignature>());
    private readonly List<ulong> _lastSpectators = [];

    public unsafe override void Load(bool hotReload)
    {
        JoinTeam.Hook(Hook_JoinTeam, HookMode.Pre);

        AddCommandListener("changelevel", CreateWrapper(MapEnd), HookMode.Post);
        AddCommandListener("map", CreateWrapper(MapEnd), HookMode.Post);

        RegisterEventHandler<EventNextlevelChanged>(MapEnd);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
    }

    private HookResult MapEnd<T, Y>(T _, Y _1)
    {
        _lastSpectators.Clear();

        foreach (var player in Utilities.GetPlayers().Where(p => p.Team == CsTeam.Spectator).ToArray())
        {
            _lastSpectators.Add(player.SteamID);
        }

        return HookResult.Continue;
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;

        if (player == null || player.IsBot) return HookResult.Continue;

        if ((NetworkDisconnectionReason)@event.Reason != NetworkDisconnectionReason.NETWORK_DISCONNECT_SHUTDOWN)
        {
            _lastSpectators.Remove(player.SteamID);
        }

        return HookResult.Continue;
    }

    public override void Unload(bool hotReload)
    {
        JoinTeam.Unhook(Hook_JoinTeam, HookMode.Pre);
    }

    private HookResult Hook_JoinTeam(DynamicHook hook)
    {
        var player = hook.GetParam<CCSPlayerController>(0);
        CsTeam team = hook.GetParam<CsTeam>(1);

        if (player.IsValid && team == CsTeam.None && _lastSpectators.Contains(player.SteamID))
        {
            hook.SetParam(1, CsTeam.Spectator);
            _lastSpectators.Remove(player.SteamID);

            return HookResult.Changed;
        }

        return HookResult.Continue;
    }

    private static string GetSignature<T>() where T: Enum
    {
        var osPlatform = (T)typeof(T).GetEnumValues().GetValue(Convert.ToInt32(!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)))!;
        
        return EnumUtils.GetEnumMemberAttributeValue(osPlatform)!;
    }

    private static CommandInfo.CommandListenerCallback CreateWrapper(CommandInfo.CommandListenerCallback cb)
    {
        return new CommandInfo.CommandListenerCallback((param1, param2) => cb(param1, param2));
    }
}
