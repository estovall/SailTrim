using UnityEngine;

namespace SailTrim
{
    /// <summary>
    /// "Hold fast" on the mast: a passenger interacts with the mast to take the sheet. They are attached at
    /// the mast foot (same attach mechanism as the rudder), stand still, and trim with W/S while the pilot
    /// steers. Interacting again, jumping or attacking lets go. Added to Ship.m_mastObject in Ship.Awake.
    /// </summary>
    public class MastHold : MonoBehaviour, Hoverable, Interactable
    {
        private Ship _ship;
        private Transform _attachPoint;
        private const float UseRange = 3.5f;

        private const string PointName = "SailTrim_MastHoldPoint";

        internal void Init(Ship ship)
        {
            _ship = ship;
            var existing = ship.transform.Find(PointName);
            if (existing != null) { _attachPoint = existing; return; }
            var go = new GameObject(PointName);
            _attachPoint = go.transform;
            _attachPoint.SetParent(ship.transform, false);
            Vector3 local = ship.transform.InverseTransformPoint(ship.m_mastObject != null ? ship.m_mastObject.transform.position : transform.position);
            local.y = 0f;
            local.z -= 0.9f; // just aft of the mast foot, facing forward
            _attachPoint.localPosition = local;
            _attachPoint.localRotation = Quaternion.identity;
        }

        private Vector3 MastPos => _ship != null && _ship.m_mastObject != null ? _ship.m_mastObject.transform.position : transform.position;
        private bool InRange(Humanoid h) => h != null && Vector3.Distance(h.transform.position, MastPos) < UseRange;

        public string GetHoverText()
        {
            var st = SailTrimShip.Get(_ship);
            if (!Plugin.Enabled.Value || !Plugin.CrewCanTrim.Value || st == null || !st.ManualMode) return "";
            var lp = Player.m_localPlayer;
            if (lp == null) return "";
            if (!InRange(lp)) return Localization.instance.Localize("<color=#888888>$piece_toofar</color>");
            if (Plugin.CrewShip == _ship)
                return Localization.instance.Localize("[<color=yellow><b>$KEY_Use</b></color>] Let go of the sheet");
            if (lp.GetControlledShip() == _ship) return "";
            if (st.SheetHand != 0L && st.SheetHand != lp.GetPlayerID()) return "Someone has the sheet";
            return Localization.instance.Localize("[<color=yellow><b>$KEY_Use</b></color>] Hold fast: trim the sail");
        }

        public string GetHoverName() => "Mast";
        public float GetHoverOffset() => 0f;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;
            var player = user as Player;
            var st = SailTrimShip.Get(_ship);
            if (player == null || player != Player.m_localPlayer || st == null) return false;
            if (!Plugin.Enabled.Value || !Plugin.CrewCanTrim.Value || !st.ManualMode) return false;
            if (!InRange(player)) return false;

            if (Plugin.CrewShip == _ship)
            {
                Plugin.ReleaseSheet();
                return false;
            }
            if (player.GetControlledShip() != null) return false;
            if (!_ship.IsPlayerInBoat(player)) return false;
            if (st.SheetHand != 0L && st.SheetHand != player.GetPlayerID())
            {
                player.Message(MessageHud.MessageType.Center, "Someone has the sheet");
                return false;
            }

            string anim = _ship.m_shipControlls != null ? _ship.m_shipControlls.m_attachAnimation : "attach_chair";
            Vector3 detach = _ship.m_shipControlls != null ? _ship.m_shipControlls.m_detachOffset : new Vector3(0f, 0.5f, 0f);
            player.AttachStart(_attachPoint, null, hideWeapons: false, isBed: false, onShip: true, anim, detach);
            Plugin.TakeSheet(_ship);
            return false;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    }
}
