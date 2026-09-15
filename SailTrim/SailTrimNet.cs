using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace SailTrim
{
    public enum ServerEnforcement { Off, Warn, Require }

    /// <summary>
    /// Server/client handshake and server-authoritative config.
    ///
    /// Flow (mirrors the vanilla PeerInfo exchange, on the same ordered socket so ordering is guaranteed):
    ///   client: SendPeerInfo prefix  -> "SailTrim_Version" {version}          (arrives before PeerInfo)
    ///   server: on version           -> "SailTrim_ServerConfig" {version, settings}  (if LockConfig)
    ///   server: RPC_PeerInfo prefix  -> checks the recorded version; Require rejects, Warn notifies
    ///   client: "SailTrim_Reject" {reason} is shown on the connect-error panel; "SailTrim_Notice" {text}
    ///           is shown centre-screen once the player has spawned.
    /// Clients without the mod cannot receive our RPCs, so under Warn the server tells them through the
    /// vanilla player "Message" RPC once their character exists.
    /// </summary>
    internal static class SailTrimNet
    {
        private const string RpcVersion = "SailTrim_Version";
        private const string RpcServerConfig = "SailTrim_ServerConfig";
        private const string RpcReject = "SailTrim_Reject";
        private const string RpcNotice = "SailTrim_Notice";

        // server side
        private static readonly Dictionary<ZRpc, string> PeerVersions = new Dictionary<ZRpc, string>();
        private static readonly Dictionary<ZRpc, string> UnmoddedToWarn = new Dictionary<ZRpc, string>();
        private static readonly List<ZRpc> Scratch = new List<ZRpc>();

        // client side
        private static string _rejectReason;
        private static readonly Queue<string> ClientNotices = new Queue<string>();
        private static readonly Dictionary<ConfigEntryBase, string> LocalValues = new Dictionary<ConfigEntryBase, string>();
        internal static bool ServerConfigActive { get; private set; }
        internal static string ServerVersion { get; private set; }

        private static bool IsServer => ZNet.instance != null && ZNet.instance.IsServer();

        // ------------------------------------------------------------------
        // Registration (ZNet.OnNewConnection postfix, both sides)
        // ------------------------------------------------------------------
        internal static void OnNewConnection(ZNetPeer peer)
        {
            if (peer?.m_rpc == null) return;
            if (IsServer)
            {
                peer.m_rpc.Register<ZPackage>(RpcVersion, OnClientVersion);
            }
            else
            {
                peer.m_rpc.Register<ZPackage>(RpcServerConfig, OnServerConfig);
                peer.m_rpc.Register<string>(RpcReject, (rpc, reason) => _rejectReason = reason);
                peer.m_rpc.Register<string>(RpcNotice, (rpc, text) => ClientNotices.Enqueue(text));
            }
        }

        // Client: announce our version right before PeerInfo goes out.
        internal static void BeforeSendPeerInfo(ZRpc rpc)
        {
            if (IsServer || rpc == null) return;
            var pkg = new ZPackage();
            pkg.Write(Plugin.VERSION);
            rpc.Invoke(RpcVersion, pkg);
        }

        // Server: record the client's version and push our settings.
        private static void OnClientVersion(ZRpc rpc, ZPackage pkg)
        {
            if (!IsServer) return;
            string v = pkg.ReadString();
            PeerVersions[rpc] = v;
            if (Plugin.LockConfig.Value)
                rpc.Invoke(RpcServerConfig, BuildConfigPackage());
        }

        /// <summary>Server, before vanilla processes PeerInfo. Returns false to reject the peer.</summary>
        internal static bool CheckPeer(ZRpc rpc)
        {
            if (!IsServer) return true;
            var mode = Plugin.Enforcement.Value;
            bool has = PeerVersions.TryGetValue(rpc, out string theirs);
            if (has && theirs == Plugin.VERSION) return true;

            string who = "peer";
            try { who = rpc.GetSocket().GetHostName(); } catch { }
            string reason = has
                ? $"SailTrim version mismatch: this server runs {Plugin.VERSION}, you have {theirs}. Update the mod."
                : $"This server runs SailTrim {Plugin.VERSION}. Install it (Hexium: Max-SailTrim) so the boats sail the same for everyone.";

            switch (mode)
            {
                case ServerEnforcement.Require:
                    Plugin.Log.LogWarning($"SailTrim: rejecting {who}: {reason}");
                    if (has) rpc.Invoke(RpcReject, reason);
                    rpc.Invoke("Error", (int)ZNet.ConnectionStatus.ErrorVersion);
                    return false;
                case ServerEnforcement.Warn:
                    Plugin.Log.LogWarning($"SailTrim: {who} joined without a matching SailTrim: {reason}");
                    if (has) rpc.Invoke(RpcNotice, reason);
                    else UnmoddedToWarn[rpc] = reason;
                    return true;
                default:
                    return true;
            }
        }

        /// <summary>Server tick: deliver the warning to unmodded players once their character exists.</summary>
        internal static void ServerUpdate()
        {
            if (!IsServer || UnmoddedToWarn.Count == 0 || ZRoutedRpc.instance == null) return;
            Scratch.Clear();
            foreach (var kv in UnmoddedToWarn)
            {
                var peer = ZNet.instance.GetPeer(kv.Key);
                if (peer == null || !kv.Key.IsConnected()) { Scratch.Add(kv.Key); continue; }
                if (peer.m_characterID == ZDOID.None) continue;
                ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, peer.m_characterID, "Message",
                    (int)MessageHud.MessageType.Center, kv.Value, 0);
                Scratch.Add(kv.Key);
            }
            foreach (var rpc in Scratch) UnmoddedToWarn.Remove(rpc);
        }

        internal static void OnPeerDisconnected(ZNetPeer peer)
        {
            if (peer?.m_rpc == null) return;
            PeerVersions.Remove(peer.m_rpc);
            UnmoddedToWarn.Remove(peer.m_rpc);
        }

        // ------------------------------------------------------------------
        // Client side
        // ------------------------------------------------------------------
        internal static void ClientUpdate()
        {
            if (ClientNotices.Count == 0) return;
            if (Player.m_localPlayer == null || MessageHud.instance == null) return;
            string text = ClientNotices.Dequeue();
            MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, text);
            Plugin.Log.LogWarning("SailTrim (from server): " + text);
        }

        /// <summary>Appends the server's reason under the vanilla "incompatible version" text.</summary>
        internal static void OnShowConnectError(FejdStartup startup)
        {
            if (string.IsNullOrEmpty(_rejectReason) || startup == null || startup.m_connectionFailedError == null) return;
            startup.m_connectionFailedError.text += "\n\n" + _rejectReason;
            _rejectReason = null;
        }

        private static ZPackage BuildConfigPackage()
        {
            var pkg = new ZPackage();
            pkg.Write(Plugin.VERSION);
            var entries = Plugin.ServerSyncedEntries;
            pkg.Write(entries.Count);
            foreach (var e in entries)
            {
                pkg.Write(e.Definition.Section + "|" + e.Definition.Key);
                pkg.Write(e.GetSerializedValue());
            }
            return pkg;
        }

        private static void OnServerConfig(ZRpc rpc, ZPackage pkg)
        {
            if (IsServer) return;
            ServerVersion = pkg.ReadString();
            int n = pkg.ReadInt();
            var byKey = new Dictionary<string, ConfigEntryBase>();
            foreach (var e in Plugin.ServerSyncedEntries) byKey[e.Definition.Section + "|" + e.Definition.Key] = e;

            var cfg = Plugin.Instance.Config;
            bool save = cfg.SaveOnConfigSet;
            cfg.SaveOnConfigSet = false;
            int applied = 0;
            try
            {
                for (int i = 0; i < n; i++)
                {
                    string key = pkg.ReadString();
                    string value = pkg.ReadString();
                    if (!byKey.TryGetValue(key, out var entry)) continue;
                    if (!LocalValues.ContainsKey(entry)) LocalValues[entry] = entry.GetSerializedValue();
                    try { entry.SetSerializedValue(value); applied++; }
                    catch (System.Exception ex) { Plugin.Log.LogWarning($"SailTrim: could not apply server value for {key}: {ex.Message}"); }
                }
            }
            finally { cfg.SaveOnConfigSet = save; }
            ServerConfigActive = applied > 0;
            Plugin.Log.LogInfo($"SailTrim: applied {applied} settings from the server (server version {ServerVersion}). Your own values for those settings are ignored while connected.");
        }

        /// <summary>Client: put our own config values back when we leave the server.</summary>
        internal static void RestoreLocalConfig()
        {
            if (LocalValues.Count == 0) { ServerConfigActive = false; return; }
            var cfg = Plugin.Instance.Config;
            bool save = cfg.SaveOnConfigSet;
            cfg.SaveOnConfigSet = false;
            try
            {
                foreach (var kv in LocalValues)
                {
                    try { kv.Key.SetSerializedValue(kv.Value); } catch { }
                }
            }
            finally { cfg.SaveOnConfigSet = save; }
            LocalValues.Clear();
            ServerConfigActive = false;
            ServerVersion = null;
            Plugin.Log.LogInfo("SailTrim: restored local settings.");
        }

        internal static void ResetAll()
        {
            PeerVersions.Clear();
            UnmoddedToWarn.Clear();
            ClientNotices.Clear();
            RestoreLocalConfig();
        }
    }
}
