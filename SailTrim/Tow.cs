using System.Collections.Generic;
using UnityEngine;

namespace SailTrim
{
    /// <summary>
    /// A tow line. A boat marked tow-capable (the vikings' longship, through the API) carries a bollard at her
    /// stern; use it to take the nearest boat in tow, or to cast off. The line is a spring on the towed boat's
    /// bow, run on the towed boat's owner (a boat is moved only by whoever owns her), and a rope drawn between
    /// the two. The record is the tug's ZDO: which boat she tows. A towed boat counts as crewed to the hull
    /// physics (vanilla otherwise takes nine tenths of her way every step) but still furls as an empty boat.
    /// </summary>
    internal static class Tow
    {
        internal const string CapableKey = "SailTrim_TowCapable";
        internal const string ToKey = "SailTrim_TowTo";
        // A ZDOID means nothing after a world load: the game hands every object a fresh one. The towed boat's
        // own tag is what the record is really made of, and the id is repaired from it (as the cleat, the
        // mooring and the lashing all do). Without this a reloaded tug either towed nothing or, if the id had
        // been handed to another boat in the meantime, towed a stranger.
        internal const string ToTagKey = "SailTrim_TowToTag";
        internal const string RpcName = "SailTrim_Tow";
        private static readonly Dictionary<Ship, float> _towedCheckAt = new Dictionary<Ship, float>();
        private static readonly Dictionary<Ship, Ship> _towedBy = new Dictionary<Ship, Ship>();

        internal static bool Capable(Ship ship)
        {
            var nv = ship != null ? ship.m_nview : null;
            return nv != null && nv.IsValid() && nv.GetZDO().GetBool(CapableKey);
        }

        internal static void SetCapable(Ship ship, bool on)
        {
            var nv = ship != null ? ship.m_nview : null;
            if (nv == null || !nv.IsValid()) return;
            if (nv.IsOwner()) nv.GetZDO().Set(CapableKey, on);
            if (on) EnsurePost(ship);
        }

        internal static void OnShipAwake(Ship ship)
        {
            if (Capable(ship)) EnsurePost(ship);
        }

        internal static void OnShipStart(Ship ship)
        {
            var nv = ship.m_nview;
            if (nv == null || !nv.IsValid()) return;
            nv.Register<ZDOID>(RpcName, (sender, id) => RPC_Tow(ship, id));
        }

        private static void RPC_Tow(Ship tug, ZDOID id)
        {
            var nv = tug.m_nview;
            if (nv == null || !nv.IsValid() || !nv.IsOwner()) return;
            var zdo = nv.GetZDO();
            zdo.Set(ToKey, id);
            var tz = id.IsNone() || ZDOMan.instance == null ? null : ZDOMan.instance.GetZDO(id);
            zdo.Set(ToTagKey, tz != null ? tz.GetLong(Tag.Key, 0L) : 0L);
        }

        private static float _relinkAt;

        /// <summary>
        /// Owner of the tug, once a second: make the written id point at the boat the tag names again. The tag
        /// may also arrive late -- a boat only writes one when its own owner gets round to it -- so it is picked
        /// up here as well.
        /// </summary>
        private static void Relink(Ship tug)
        {
            var nv = tug.m_nview;
            if (nv == null || !nv.IsValid() || !nv.IsOwner()) return;
            var zdo = nv.GetZDO();
            ZDOID have = zdo.GetZDOID(ToKey);
            if (have.IsNone()) return;
            long want = zdo.GetLong(ToTagKey, 0L);
            var hz = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(have) : null;
            if (hz != null)
            {
                long tag = hz.GetLong(Tag.Key, 0L);
                if (want == 0L && tag != 0L) { zdo.Set(ToTagKey, tag); return; }
                if (tag == want) return;
            }
            else if (want == 0L) return;
            if (want == 0L) return;
            foreach (var other in Gangway.NearbyShips())
            {
                var ov = other != null ? other.m_nview : null;
                if (ov == null || !ov.IsValid() || Tag.Read(ov) != want) continue;
                zdo.Set(ToKey, ov.GetZDO().m_uid);
                Plugin.Log.LogInfo("SailTrim: " + tug.name + " found the boat it had in tow again after a reload");
                return;
            }
        }

        /// <summary>Ask the tug's owner to take a boat in tow (null = cast off).</summary>
        /// <summary>Owner of a towed boat: make sure she carries a tag, which is what the tug's record survives on.</summary>
        internal static void EnsureTag(Ship towed)
        {
            var nv = towed != null ? towed.m_nview : null;
            if (nv != null && nv.IsValid() && nv.IsOwner()) Tag.Of(nv);
        }

        internal static void Request(Ship tug, Ship towed)
        {
            var nv = tug != null ? tug.m_nview : null;
            if (nv == null || !nv.IsValid()) return;
            EnsureTag(towed);
            var id = towed != null && towed.m_nview != null && towed.m_nview.IsValid() ? towed.m_nview.GetZDO().m_uid : ZDOID.None;
            nv.InvokeRPC(RpcName, id);
        }

        internal static Ship Towing(Ship tug)
        {
            var nv = tug != null ? tug.m_nview : null;
            if (nv == null || !nv.IsValid()) return null;
            if (nv.IsOwner() && Time.time >= _relinkAt) { _relinkAt = Time.time + 1f; Relink(tug); }
            var zdo = nv.GetZDO();
            ZDOID id = zdo.GetZDOID(ToKey);
            if (id.IsNone() || ZNetScene.instance == null) return null;
            var go = ZNetScene.instance.FindInstance(id);
            var ship = go != null ? go.GetComponent<Ship>() : null;
            if (ship == null) return null;
            // The id survived a reload but the tag says it is somebody else's hull now: tow nothing until the
            // repair above finds the right one. Dragging a boat nobody asked to be dragged is the worse answer.
            long want = zdo.GetLong(ToTagKey, 0L);
            if (want != 0L && Tag.Read(ship.m_nview) != want) return null;
            return ship;
        }

        /// <summary>The boat towing this one, if any is loaded here. Looked up once a second.</summary>
        internal static Ship TowedBy(Ship ship)
        {
            if (ship == null) return null;
            if (_towedCheckAt.TryGetValue(ship, out float at) && Time.time < at) return _towedBy.TryGetValue(ship, out var t) ? t : null;
            _towedCheckAt[ship] = Time.time + 1f;
            Ship found = null;
            foreach (var other in Gangway.NearbyShips())
                if (other != null && other != ship && Towing(other) == ship) { found = other; break; }
            _towedBy[ship] = found;
            return found;
        }

        internal static bool IsTowed(Ship ship) => TowedBy(ship) != null;

        private static float HalfLen(Ship s) => s.m_floatCollider != null ? s.m_floatCollider.size.z * 0.5f : 8f;
        internal static Vector3 SternPoint(Ship s) => s.transform.TransformPoint(new Vector3(0f, 0.9f, -HalfLen(s) * 0.92f));
        internal static Vector3 BowPoint(Ship s) => s.transform.TransformPoint(new Vector3(0f, 0.9f, HalfLen(s) * 0.85f));

        /// <summary>On the towed boat's owner each physics step: the line pulls her bow after the tug.</summary>
        internal static void FixedStep(Ship ship, float dt)
        {
            var tug = TowedBy(ship);
            if (tug == null || ship.m_body == null) return;
            // We are her owner, so we are the one who can give her a tag, and the tug's record is only as good
            // as that tag once the world has been reloaded.
            EnsureTag(ship);
            Vector3 bow = BowPoint(ship), stern = SternPoint(tug);
            Vector3 d = stern - bow;
            float dist = d.magnitude;
            float len = Plugin.TowLength.Value;
            if (dist > len + Plugin.TowBreak.Value)
            {
                Request(tug, null);
                var lp = Player.m_localPlayer;
                if (lp != null) lp.Message(MessageHud.MessageType.TopLeft, "The tow line parted");
                return;
            }
            if (dist <= len) return;
            Vector3 dir = d / dist;
            float stretch = dist - len;
            // A spring with a ceiling, at the bow, so she swings into line behind the tug; and her sideways
            // way is bled off so she follows instead of sheering about.
            float accel = Mathf.Min(stretch * Plugin.TowPull.Value, Plugin.TowPull.Value * 4f);
            ship.m_body.AddForceAtPosition(dir * (accel * ship.m_body.mass * dt), bow, ForceMode.Impulse);
            Vector3 v = ship.m_body.linearVelocity;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            float lateral = Vector3.Dot(v, side);
            ship.m_body.linearVelocity = v - side * (lateral * Mathf.Clamp01(2f * dt));
        }

        private static void EnsurePost(Ship ship)
        {
            if (ship == null || ship.transform.Find("SailTrim_TowPost") != null) return;
            var go = new GameObject("SailTrim_TowPost");
            go.transform.SetParent(ship.transform, false);
            go.AddComponent<TowPost>().Init(ship);
        }
    }

    internal class TowPost : MonoBehaviour, Hoverable, Interactable
    {
        private Ship _ship;
        private LineRenderer _rope;
        private GameObject _bollard;
        private float _textAt;
        private string _text = "";

        internal void Init(Ship ship)
        {
            _ship = ship;
            float halfLen = ship.m_floatCollider != null ? ship.m_floatCollider.size.z * 0.5f : 8f;
            // On the stern deck, a little to port of the tiller.
            transform.localPosition = new Vector3(-0.8f, 0.9f, -halfLen * 0.86f);
            gameObject.layer = ship.gameObject.layer;
            foreach (var c in ship.GetComponentsInChildren<Collider>(true))
                if (!c.isTrigger && c.gameObject != ship.gameObject) { gameObject.layer = c.gameObject.layer; break; }
            var box = gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.35f, 0f);
            box.size = new Vector3(0.45f, 0.7f, 0.45f);
            // No body of its own. The plank needs one because it swings and would otherwise prop the boat up on
            // the dock; a bollard is bolted to the deck and never moves, so it belongs to the hull's own body
            // like any other timber. Giving it one made it a kinematic body sitting inside a floating one, which
            // is what threw hulls on their beam ends before, and it struck out its collider pairs only once --
            // while a hull's colliders go on arriving for seconds after she wakes.
            // GangwayPart marks it as ours so a cleat ropes to the hull rather than to this.
            gameObject.AddComponent<GangwayPart>();
            _bollard = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(_bollard.GetComponent<Collider>());
            _bollard.transform.SetParent(transform, false);
            _bollard.transform.localPosition = new Vector3(0f, 0.3f, 0f);
            _bollard.transform.localScale = new Vector3(0.28f, 0.3f, 0.28f);
            var mat = Gangway.ShipTimber(ship);
            if (mat != null) _bollard.GetComponent<Renderer>().sharedMaterial = mat;
            var ropeGo = new GameObject("SailTrim_TowRope");
            ropeGo.transform.SetParent(transform, false);
            _rope = ropeGo.AddComponent<LineRenderer>();
            _rope.useWorldSpace = true;
            _rope.positionCount = 14;
            _rope.widthMultiplier = 0.09f;
            _rope.numCapVertices = 3;
            _rope.alignment = LineAlignment.View;
            _rope.textureMode = LineTextureMode.Tile;
            _rope.enabled = false;
        }

        private void Update()
        {
            if (_rope == null || _ship == null) return;   // a half-built post must not throw every frame
            var towed = Tow.Towing(_ship);
            if (towed == null) { if (_rope.enabled) _rope.enabled = false; return; }
            // No material, or another mod's error-shader copy of the placeholder (a magenta line): the rope's own.
            var cur = _rope.sharedMaterial;
            if (cur == null || cur.shader == null || cur.shader.name.Contains("InternalError")) { var m = CleatPiece.RopeMaterial(); if (m != null) _rope.sharedMaterial = m; }
            if (!_rope.enabled) _rope.enabled = true;
            Vector3 a = Tow.SternPoint(_ship), b = Tow.BowPoint(towed);
            float slack = Mathf.Clamp01(1f - (Vector3.Distance(a, b) / Plugin.TowLength.Value)) * 1.5f;
            for (int i = 0; i < _rope.positionCount; i++)
            {
                float t = i / (float)(_rope.positionCount - 1);
                Vector3 p = Vector3.Lerp(a, b, t);
                p.y -= slack * 4f * t * (1f - t);
                _rope.SetPosition(i, p);
            }
        }

        private Ship Nearest()
        {
            Ship best = null; float bestD = Plugin.TowRange.Value;
            foreach (var s in Gangway.NearbyShips())
            {
                if (s == null || s == _ship) continue;
                float d = Vector3.Distance(s.transform.position, _ship.transform.position);
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        public string GetHoverName() => "Tow line";
        public float GetHoverOffset() => 0f;

        public string GetHoverText()
        {
            if (Time.time >= _textAt)
            {
                _textAt = Time.time + 0.5f;
                var towed = Tow.Towing(_ship);
                if (towed != null) _text = $"Tow line to {Gangway.ShipName(towed)}\n[<color=yellow><b>$KEY_Use</b></color>] Cast off";
                else
                {
                    var near = Nearest();
                    _text = near != null ? $"Tow line\n[<color=yellow><b>$KEY_Use</b></color>] Take {Gangway.ShipName(near)} in tow"
                                         : $"Tow line\n(no boat within {Plugin.TowRange.Value:0} m)";
                }
            }
            return Localization.instance.Localize(_text);
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;
            var towed = Tow.Towing(_ship);
            if (towed != null)
            {
                Tow.Request(_ship, null);
                user.Message(MessageHud.MessageType.TopLeft, "Tow line cast off");
                return true;
            }
            var near = Nearest();
            if (near == null) { user.Message(MessageHud.MessageType.TopLeft, "No boat close enough to tow"); return false; }
            Tow.Request(_ship, near);
            user.Message(MessageHud.MessageType.TopLeft, $"{Gangway.ShipName(near)} taken in tow");
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    }
}
