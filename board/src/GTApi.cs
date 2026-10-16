using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace UtilityBoard
{
    // Reflection layer over Gorilla Tag's own classes : hands, controller poller,
    // hand velocity trackers and room state. Read-only except for controller haptics. No networking is touched.
    public static class GTApi
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static Assembly asm;
        public static Type GameType(string name)
        {
            if (asm == null) foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) if (a.GetName().Name == "Assembly-CSharp") { asm = a; break; }
            return asm != null ? asm.GetType(name) : null;
        }
        public static object Instance(Type t)
        {
            if (t == null) return null;
            var p = t.GetProperty("Instance", All); if (p != null) { try { var v = p.GetValue(null, null); if (v != null) return v; } catch (Exception) { } }
            foreach (var n in new[] { "_instance", "Instance", "instance" }) { var f = t.GetField(n, All); if (f != null && f.IsStatic) { try { var v = f.GetValue(null); if (v != null) return v; } catch (Exception) { } } }
            return null;
        }

        private static Type playerT;
        public static Component Player
        {
            get
            {
                if (playerT == null) playerT = GameType("GorillaLocomotion.GTPlayer") ?? GameType("GorillaLocomotion.Player");
                return Instance(playerT) as Component;
            }
        }
        private static Type taggerT; private static FieldInfo lh, rh;
        public static Component Tagger { get { if (taggerT == null) taggerT = GameType("GorillaTagger"); return Instance(taggerT) as Component; } }

        private static MethodInfo followerM, trackerM, avgVelM;
        // The visible gorilla hand (GTPlayer.GetHandFollower) - falls back to GorillaTagger.left/rightHandTransform.
        public static Transform Hand(bool left)
        {
            var p = Player;
            if (p != null)
            {
                if (followerM == null) followerM = p.GetType().GetMethod("GetHandFollower", All, null, new[] { typeof(bool) }, null);
                if (followerM != null) { try { var t = followerM.Invoke(p, new object[] { left }) as Transform; if (t != null) return t; } catch (Exception) { } }
            }
            var tg = Tagger; if (tg == null) return null;
            if (lh == null) { lh = taggerT.GetField("leftHandTransform", All); rh = taggerT.GetField("rightHandTransform", All); }
            var f = left ? lh : rh; return f != null ? f.GetValue(tg) as Transform : null;
        }
        public static string HandNote(bool left)
        {
            var p = Player; if (p == null) return "GTPlayer missing";
            if (followerM == null) followerM = p.GetType().GetMethod("GetHandFollower", All, null, new[] { typeof(bool) }, null);
            return followerM != null ? "GTPlayer.GetHandFollower(" + left + ")" : "GorillaTagger." + (left ? "left" : "right") + "HandTransform";
        }
        // GT's own hand velocity estimate: GTPlayer.GetHandVelocityTracker(isLeft).GetAverageVelocity(worldSpace: true, 0.15 s)
        public static bool GameHandVelocity(bool left, out Vector3 v)
        {
            v = Vector3.zero; var p = Player; if (p == null) return false;
            try
            {
                if (trackerM == null) trackerM = p.GetType().GetMethod("GetHandVelocityTracker", All, null, new[] { typeof(bool) }, null);
                if (trackerM == null) return false;
                var tr = trackerM.Invoke(p, new object[] { left }); if (tr == null) return false;
                if (avgVelM == null) avgVelM = tr.GetType().GetMethod("GetAverageVelocity", All, null, new[] { typeof(bool), typeof(float), typeof(bool) }, null);
                if (avgVelM == null) return false;
                v = (Vector3)avgVelM.Invoke(tr, new object[] { true, 0.15f, false }); return true;
            }
            catch (Exception) { return false; }
        }

        private static Type pollT; private static FieldInfo pollInst; private static readonly Dictionary<string, FieldInfo> pf = new Dictionary<string, FieldInfo>();
        public static object Poller
        {
            get
            {
                if (pollT == null) { pollT = GameType("ControllerInputPoller"); if (pollT != null) pollInst = pollT.GetField("instance", All); }
                return pollInst != null ? pollInst.GetValue(null) : null;
            }
        }
        public static T PollField<T>(string name)
        {
            var poller = Poller; if (poller == null) return default(T);
            FieldInfo f; if (!pf.TryGetValue(name, out f)) { f = pollT.GetField(name, All); pf[name] = f; }
            if (f == null) return default(T);
            var v = f.GetValue(poller); return v is T ? (T)v : default(T);
        }
        public static bool HasPollField(string name) { if (Poller == null) return false; FieldInfo f; if (!pf.TryGetValue(name, out f)) { f = pollT.GetField(name, All); pf[name] = f; } return f != null; }
        public static string PollerNote
        {
            get
            {
                if (Poller == null) return "ControllerInputPoller.instance not found";
                var miss = new List<string>();
                foreach (var n in new[] { "leftControllerGripFloat", "rightControllerGripFloat", "leftControllerPrimaryButton", "leftControllerSecondaryButton", "leftControllerDevice", "rightControllerDevice" })
                    if (pollT.GetField(n, All) == null) miss.Add(n);
                return miss.Count == 0 ? "ControllerInputPoller: grip floats, X/Y buttons and devices found" : "ControllerInputPoller missing: " + string.Join(", ", miss.ToArray());
            }
        }
        public static void Haptic(bool left, float amp, float dur)
        {
            var d = PollField<UnityEngine.XR.InputDevice>(left ? "leftControllerDevice" : "rightControllerDevice");
            if (d.isValid) try { d.SendHapticImpulse(0, amp, dur); } catch (Exception) { }
        }

        public static int LocomotionMask()
        {
            var p = Player; if (p == null) return 0;
            var f = playerT.GetField("locomotionEnabledLayers", All); if (f == null) return 0;
            try { return ((LayerMask)f.GetValue(p)).value; } catch (Exception) { return 0; }
        }
        public static Rigidbody PlayerBody
        {
            get
            {
                var p = Player; if (p == null) return null;
                var pr = playerT.GetProperty("playerRigidBody", All);
                return pr != null ? pr.GetValue(p, null) as Rigidbody : p.GetComponent<Rigidbody>();
            }
        }
        // Every collider that belongs to the local player (GTPlayer + GorillaTagger hierarchies and the offline VRRig).
        public static List<Collider> PlayerColliders()
        {
            var list = new List<Collider>();
            var roots = new List<Transform>();
            var p = Player; if (p != null) roots.Add(p.transform.root);
            var tg = Tagger; if (tg != null)
            {
                roots.Add(tg.transform.root);
                var f = taggerT.GetField("offlineVRRig", All);
                if (f != null) { var rig = f.GetValue(tg) as Component; if (rig != null) roots.Add(rig.transform.root); }
            }
            var seen = new HashSet<Transform>();
            foreach (var r in roots) if (r != null && seen.Add(r)) list.AddRange(r.GetComponentsInChildren<Collider>(true));
            return list;
        }

        private static Type netT;
        public static Type AnyType(string full)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) { Type t = null; try { t = a.GetType(full, false); } catch (Exception) { } if (t != null) return t; }
            return null;
        }
        private static Type pnT; private static bool pnLooked;
        private static Type PN { get { if (!pnLooked) { pnLooked = true; pnT = AnyType("Photon.Pun.PhotonNetwork"); } return pnT; } }
        // Photon's own room name (PhotonNetwork.CurrentRoom.Name) - the code the lobby really uses
        public static string PhotonRoom()
        {
            try { var r = PN?.GetProperty("CurrentRoom", All)?.GetValue(null, null); if (r == null) return null; return r.GetType().GetProperty("Name", All)?.GetValue(r, null) as string; }
            catch (Exception) { return null; }
        }
        public static int Ping() { try { var m = PN?.GetMethod("GetPing", All, null, Type.EmptyTypes, null); return m != null ? (int)m.Invoke(null, null) : -1; } catch (Exception) { return -1; } }
        public static string LastRoomSource = "";
        // Current room code + player count (local read only). Photon first, NetworkSystem as fallback. null = not in a room.
        public static string RoomName(out int players)
        {
            players = 0; if (netT == null) netT = GameType("NetworkSystem");
            var n = Instance(netT); string ns = null; bool inRoom = false;
            if (n != null)
                try
                {
                    inRoom = (bool)netT.GetProperty("InRoom", All).GetValue(n, null);
                    if (inRoom)
                    {
                        var pc = netT.GetProperty("RoomPlayerCount", All); if (pc != null) { var v = pc.GetValue(n, null); if (v is int i) players = i; }
                        ns = netT.GetProperty("RoomName", All)?.GetValue(n, null) as string;
                    }
                }
                catch (Exception) { }
            if (!inRoom) return null;
            var ph = PhotonRoom();
            if (!string.IsNullOrEmpty(ph)) { LastRoomSource = ns != null && ns != ph ? "Photon (NetworkSystem said '" + ns + "')" : "Photon"; return ph; }
            LastRoomSource = "NetworkSystem"; return ns;
        }
        // Room state from GT's NetworkSystem. Unknown/not found = offline.
        public static bool InRoom(out bool isPrivate, out string gameMode)
        {
            isPrivate = false; gameMode = "";
            if (netT == null) netT = GameType("NetworkSystem");
            var n = Instance(netT); if (n == null) return false;
            try
            {
                bool inRoom = (bool)netT.GetProperty("InRoom", All).GetValue(n, null);
                if (!inRoom) return false;
                var sp = netT.GetProperty("SessionIsPrivate", All); isPrivate = sp != null && (bool)sp.GetValue(n, null);
                var gm = netT.GetProperty("GameModeString", All); if (gm != null) gameMode = gm.GetValue(n, null) as string ?? "";
                return true;
            }
            catch (Exception) { return false; }
        }
    }
}
