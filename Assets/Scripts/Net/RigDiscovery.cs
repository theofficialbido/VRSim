using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace VRSIM.Net
{
    /// <summary>Where the engine lives, as answered by a discovery reply.</summary>
    public struct DiscoveredHost
    {
        public string Address;
        public string HostName;
        public int EnginePort;
        public int AgentPort;
        public bool EngineRunning;

        public override string ToString() => $"{HostName} ({Address}:{EnginePort})";
    }

    /// <summary>
    /// Finds the PC by UDP broadcast, so nobody has to type an IP address on a
    /// VR keyboard.
    ///
    /// Discovery is a convenience, never a dependency: some networks block
    /// broadcast, and guest Wi-Fi frequently isolates clients from each other.
    /// <see cref="RigConnection"/> always falls back to a configured address,
    /// and that fallback is the supported path when discovery is unavailable.
    /// </summary>
    public static class RigDiscovery
    {
        public const int DiscoveryPort = 8769;

        /// <summary>
        /// Broadcasts and waits up to <paramref name="timeoutSeconds"/> for the
        /// first reply. Runs as a coroutine so it never blocks the render
        /// thread -- a blocking socket wait in VR shows up immediately as
        /// judder and is a comfort problem, not just a performance one.
        /// </summary>
        public static IEnumerator Discover(float timeoutSeconds, Action<DiscoveredHost?> onComplete)
        {
            UdpClient client = null;
            DiscoveredHost? result = null;
            var deadline = Time.realtimeSinceStartup + timeoutSeconds;

            // A coroutine cannot yield out of a catch clause, so failure is
            // recorded and acted on after the try block.
            var broadcastFailed = false;
            try
            {
                client = new UdpClient { EnableBroadcast = true };
                client.Client.ReceiveTimeout = 200;
                var request = Encoding.UTF8.GetBytes(
                    "{\"v\":1,\"type\":\"discover\",\"client\":\"vrsim-quest\"}");
                client.Send(request, request.Length,
                    new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
            }
            catch (Exception ex)
            {
                // Broadcast is blocked on plenty of networks, and on guest
                // Wi-Fi that isolates clients. Not an error -- the caller falls
                // back to the configured address.
                Debug.LogWarning($"[RigDiscovery] broadcast unavailable: {ex.Message}");
                broadcastFailed = true;
            }

            if (broadcastFailed)
            {
                client?.Dispose();
                onComplete?.Invoke(null);
                yield break;
            }

            // try/finally, because the caller stops this coroutine whenever it
            // reconnects or disconnects. Unity disposes the stopped iterator,
            // which runs the finally -- without it every restart mid-discovery
            // abandoned a bound UDP socket.
            try
            {
                while (Time.realtimeSinceStartup < deadline && result == null)
                {
                    // Poll rather than block, so we yield a frame between checks.
                    if (client.Available > 0)
                    {
                        IPEndPoint from = null;
                        byte[] data = null;
                        try
                        {
                            from = new IPEndPoint(IPAddress.Any, 0);
                            data = client.Receive(ref from);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[RigDiscovery] receive failed: {ex.Message}");
                        }

                        if (data != null)
                            result = Parse(data, from);
                    }

                    yield return null;
                }
            }
            finally
            {
                client.Dispose();
            }

            onComplete?.Invoke(result);
        }

        private static DiscoveredHost? Parse(byte[] data, IPEndPoint from)
        {
            try
            {
                var json = JObject.Parse(Encoding.UTF8.GetString(data));
                if ((string)json["type"] != "discover.reply") return null;

                return new DiscoveredHost
                {
                    Address = from.Address.ToString(),
                    HostName = (string)json["host"] ?? from.Address.ToString(),
                    EnginePort = (int?)json["engine_port"] ?? 8765,
                    AgentPort = (int?)json["agent_port"] ?? 8770,
                    EngineRunning = (bool?)json["engine_running"] ?? false,
                };
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RigDiscovery] malformed reply: {ex.Message}");
                return null;
            }
        }
    }
}
