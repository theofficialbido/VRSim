using Newtonsoft.Json.Linq;
using VRSIM.State;

namespace VRSIM.Binding
{
    /// <summary>
    /// Reads a dotted path out of rig state, e.g. <c>bop.components.annular_1</c>
    /// or <c>pumps.pump_1.active</c>.
    ///
    /// This is the seam that lets the 3D scene and the engine change
    /// independently. A binding names a value by path instead of a C# property,
    /// so:
    ///
    ///   * re-modelling the rig means re-assigning GameObjects in the Inspector
    ///   * renaming or adding an engine field means editing a string in the
    ///     Inspector
    ///
    /// Neither requires touching code or recompiling, which is the whole point
    /// of keeping the connection layer separate from both ends.
    ///
    /// It reads the raw JSON tree rather than the typed snapshot, so a field the
    /// engine adds is bindable immediately without a matching C# property.
    /// </summary>
    public static class RigStatePath
    {
        /// <summary>Returns the raw token at <paramref name="path"/>, or null.</summary>
        public static JToken Resolve(RigState state, string path)
        {
            if (state == null || string.IsNullOrWhiteSpace(path)) return null;
            var node = state.Raw;
            foreach (var part in path.Split('.'))
            {
                if (node == null) return null;
                node = node[part];
            }
            return node;
        }

        /// <summary>String form of the value, or null if the path does not exist.</summary>
        public static string ReadString(RigState state, string path)
        {
            var token = Resolve(state, path);
            return token == null || token.Type == JTokenType.Null ? null : token.ToString();
        }

        /// <summary>Numeric value, or <paramref name="fallback"/> if absent or not a number.</summary>
        public static double ReadNumber(RigState state, string path, double fallback = 0)
        {
            var token = Resolve(state, path);
            if (token == null) return fallback;
            return double.TryParse(token.ToString(),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var value)
                ? value : fallback;
        }

        /// <summary>
        /// Truthiness, tolerant of how a value might be expressed: real booleans,
        /// the strings an engine might send, and non-zero numbers.
        /// </summary>
        public static bool ReadBool(RigState state, string path, bool fallback = false)
        {
            var text = ReadString(state, path);
            if (text == null) return fallback;
            switch (text.ToLowerInvariant())
            {
                case "true": case "on": case "open": case "active": case "1": return true;
                case "false": case "off": case "closed": case "inactive": case "0": return false;
            }
            return double.TryParse(text,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var n)
                ? n != 0 : fallback;
        }

        /// <summary>True if the path exists at all, for validating bindings on connect.</summary>
        public static bool Exists(RigState state, string path) => Resolve(state, path) != null;
    }
}
