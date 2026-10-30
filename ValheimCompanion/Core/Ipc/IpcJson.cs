using System;
using System.Text;

namespace ValheimCompanion.Core.Ipc
{
    /// <summary>
    /// Minimal JSON for the fixed IPC schema (net48, no extra packages).
    /// Only used for localhost voice bridge — not a general serializer.
    /// </summary>
    public static class IpcJson
    {
        public static string Escape(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }

            return sb.ToString();
        }

        /// <summary>Extract a top-level string field from a flat JSON object.</summary>
        public static bool TryGetString(string json, string field, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(field)) return false;
            string needle = "\"" + field + "\"";
            int i = json.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return false;
            i = json.IndexOf(':', i + needle.Length);
            if (i < 0) return false;
            i++;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (i >= json.Length || json[i] != '"') return false;
            i++;
            var sb = new StringBuilder();
            while (i < json.Length)
            {
                char c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    char n = json[i + 1];
                    if (n == 'n') sb.Append('\n');
                    else if (n == 'r') sb.Append('\r');
                    else if (n == 't') sb.Append('\t');
                    else sb.Append(n);
                    i += 2;
                    continue;
                }

                if (c == '"') break;
                sb.Append(c);
                i++;
            }

            value = sb.ToString();
            return true;
        }

        public static string CommandAccepted(int jobId, string status, string message)
        {
            return "{\"accepted\":true,\"status\":\"" + Escape(status) + "\",\"job_id\":" + jobId +
                   ",\"message\":\"" + Escape(message) + "\"}";
        }

        public static string CommandRejected(string status, string message)
        {
            return "{\"accepted\":false,\"status\":\"" + Escape(status) + "\",\"message\":\"" + Escape(message) + "\"}";
        }

        public static string Health(bool ok, string version, bool inWorld, bool hasCompanion,
            string moveStatus, float moveDistance, string followTarget, float distToPlayer, string lastError)
        {
            return "{\"ok\":" + (ok ? "true" : "false") +
                   ",\"version\":\"" + Escape(version) + "\"" +
                   ",\"in_world\":" + (inWorld ? "true" : "false") +
                   ",\"has_companion\":" + (hasCompanion ? "true" : "false") +
                   ",\"move_status\":\"" + Escape(moveStatus) + "\"" +
                   ",\"move_distance\":" + Fmt(moveDistance) +
                   ",\"follow_target\":\"" + Escape(followTarget ?? "") + "\"" +
                   ",\"distance_to_player\":" + Fmt(distToPlayer) +
                   ",\"last_error\":\"" + Escape(lastError ?? "") + "\"}";
        }

        public static string Job(int jobId, string status, string message, string stateJson)
        {
            string s = "{\"job_id\":" + jobId + ",\"status\":\"" + Escape(status) +
                       "\",\"message\":\"" + Escape(message) + "\"";
            if (!string.IsNullOrEmpty(stateJson)) s += ",\"state\":" + stateJson;
            return s + "}";
        }

        public static string StateBlock(string prefab, string name, string zdoid,
            float x, float y, float z, float health, float maxHealth,
            bool isTamed, bool isDead, bool isValid, bool isOwner,
            string faction, float distToPlayer, string followTarget)
        {
            return "{\"prefab\":\"" + Escape(prefab) + "\"" +
                   ",\"name\":\"" + Escape(name) + "\"" +
                   ",\"zdoid\":\"" + Escape(zdoid) + "\"" +
                   ",\"position\":[" + Fmt(x) + "," + Fmt(y) + "," + Fmt(z) + "]" +
                   ",\"health\":" + Fmt(health) +
                   ",\"max_health\":" + Fmt(maxHealth) +
                   ",\"is_tamed\":" + (isTamed ? "true" : "false") +
                   ",\"is_dead\":" + (isDead ? "true" : "false") +
                   ",\"is_valid\":" + (isValid ? "true" : "false") +
                   ",\"is_owner\":" + (isOwner ? "true" : "false") +
                   ",\"faction\":\"" + Escape(faction) + "\"" +
                   ",\"distance_to_player\":" + Fmt(distToPlayer) +
                   ",\"follow_target\":\"" + Escape(followTarget ?? "") + "\"}";
        }

        private static string Fmt(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return "null";
            return v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
