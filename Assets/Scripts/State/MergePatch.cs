using Newtonsoft.Json.Linq;

namespace VRSIM.State
{
    /// <summary>
    /// RFC 7386 JSON Merge Patch.
    ///
    /// The engine sends a full <c>state.snapshot</c> on connect and thereafter
    /// <c>state.delta</c> messages carrying only what changed. A delta is a
    /// merge patch: present keys replace, an explicit null deletes, and absent
    /// keys are left alone.
    ///
    /// The null-deletes-a-key rule is the reason deltas are handled as JToken
    /// rather than being deserialised straight into typed objects -- a POCO
    /// cannot distinguish "field absent, leave it" from "field null, remove it".
    /// </summary>
    public static class MergePatch
    {
        /// <summary>
        /// Applies <paramref name="patch"/> to <paramref name="target"/> and
        /// returns the result. <paramref name="target"/> may be mutated, so
        /// callers that need the previous state should clone it first.
        /// </summary>
        public static JToken Apply(JToken target, JToken patch)
        {
            // A non-object patch replaces the target wholesale.
            if (!(patch is JObject patchObj))
                return patch?.DeepClone();

            if (!(target is JObject targetObj))
                targetObj = new JObject();

            foreach (var property in patchObj.Properties())
            {
                if (property.Value == null || property.Value.Type == JTokenType.Null)
                {
                    targetObj.Remove(property.Name);
                    continue;
                }

                var existing = targetObj[property.Name];
                targetObj[property.Name] = Apply(existing, property.Value);
            }

            return targetObj;
        }
    }
}
