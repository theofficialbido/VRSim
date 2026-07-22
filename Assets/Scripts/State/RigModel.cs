using Newtonsoft.Json;

namespace VRSIM.State
{
    /// <summary>
    /// Typed view of the rig state defined in docs/protocol.md section 5.2.
    ///
    /// Field names carry their units, matching the wire format, so that a value
    /// can never be read in the wrong unit by accident -- the previous system
    /// passed bare numbers like "wob" and left the unit to be remembered.
    ///
    /// Enumerations arrive as strings and are deliberately NOT parsed into C#
    /// enums: the protocol requires that an unknown value is tolerated by
    /// holding the previous one and logging, never by throwing.
    /// </summary>
    public class RigSnapshot
    {
        [JsonProperty("drilling")] public DrillingState Drilling = new DrillingState();
        [JsonProperty("pumps")] public PumpsState Pumps = new PumpsState();
        [JsonProperty("bop")] public BopState Bop = new BopState();
        [JsonProperty("swaco")] public SwacoState Swaco = new SwacoState();
        [JsonProperty("alarms")] public string[] Alarms = new string[0];
    }

    public class DrillingState
    {
        [JsonProperty("bit_depth_ft")] public float BitDepthFt;
        [JsonProperty("hole_depth_ft")] public float HoleDepthFt;
        [JsonProperty("rop_fph")] public float RopFph;
        [JsonProperty("wob_klbs")] public float WobKlbs;
        [JsonProperty("hookload_klbs")] public float HookloadKlbs;
        [JsonProperty("torque_ftlb")] public float TorqueFtLb;
        [JsonProperty("rpm")] public float Rpm;
        [JsonProperty("tds_position_ft")] public float TdsPositionFt;
        [JsonProperty("tds_movement")] public string TdsMovement = "stop"; // up | down | stop
        [JsonProperty("auto_drill")] public bool AutoDrill;
    }

    public class PumpsState
    {
        [JsonProperty("pump_1")] public PumpState Pump1 = new PumpState();
        [JsonProperty("pump_2")] public PumpState Pump2 = new PumpState();
        [JsonProperty("pump_3")] public PumpState Pump3 = new PumpState();
        [JsonProperty("total_flow_gpm")] public float TotalFlowGpm;

        public PumpState ByIndex(int oneBased)
        {
            switch (oneBased)
            {
                case 1: return Pump1;
                case 2: return Pump2;
                case 3: return Pump3;
                default: return null;
            }
        }
    }

    public class PumpState
    {
        [JsonProperty("active")] public bool Active;
        [JsonProperty("spm")] public float Spm;
        [JsonProperty("pressure_psi")] public float PressurePsi;
    }

    public class BopState
    {
        /// <summary>
        /// Component id -> state, one of open | closed | moving | fault.
        ///
        /// A dictionary rather than fixed fields because the set of rams varies
        /// by rig. Newtonsoft handles this directly; the old code hand-wrote a
        /// brace-scanning parser here purely because JsonUtility cannot
        /// deserialise a Dictionary.
        /// </summary>
        [JsonProperty("components")]
        public System.Collections.Generic.Dictionary<string, string> Components
            = new System.Collections.Generic.Dictionary<string, string>();

        [JsonProperty("pressures_psi")] public BopPressures PressuresPsi = new BopPressures();
        [JsonProperty("master_valve")] public string MasterValve = "closed";

        public string ComponentState(string id)
        {
            return Components != null && Components.TryGetValue(id, out var s) ? s : "unknown";
        }
    }

    public class BopPressures
    {
        [JsonProperty("annular")] public float Annular;
        [JsonProperty("manifold")] public float Manifold;
        [JsonProperty("accumulator")] public float Accumulator;
        [JsonProperty("air")] public float Air;
    }

    public class SwacoState
    {
        [JsonProperty("standpipe_psi")] public float StandpipePsi;
        [JsonProperty("casing_psi")] public float CasingPsi;
        [JsonProperty("choke_position")] public float ChokePosition;
        [JsonProperty("total_strokes")] public float TotalStrokes;
        [JsonProperty("total_volume_bbl")] public float TotalVolumeBbl;

        /// <summary>
        /// Hold and Reset indicator lamps on the physical panel. Added in
        /// response to the panel having lamps the protocol did not model, which
        /// left them undrivable without inventing a meaning for them.
        /// </summary>
        [JsonProperty("hold_active")] public bool HoldActive;
        [JsonProperty("reset_active")] public bool ResetActive;
    }
}
