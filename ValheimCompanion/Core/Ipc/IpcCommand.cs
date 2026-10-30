namespace ValheimCompanion.Core.Ipc
{
    /// <summary>
    /// Fixed voice/IPC command set. No free-form actions, no coordinates from outside.
    /// </summary>
    public enum IpcCommandKind
    {
        FollowMe,
        ComeHere,
        Stop,
        Status
    }

    public static class IpcCommandParser
    {
        /// <summary>Accepts only the four canonical tokens (case-insensitive).</summary>
        public static bool TryParse(string raw, out IpcCommandKind kind)
        {
            kind = IpcCommandKind.Status;
            if (string.IsNullOrEmpty(raw)) return false;
            switch (raw.Trim().ToUpperInvariant())
            {
                case "FOLLOW_ME":
                    kind = IpcCommandKind.FollowMe;
                    return true;
                case "COME_HERE":
                    kind = IpcCommandKind.ComeHere;
                    return true;
                case "STOP":
                    kind = IpcCommandKind.Stop;
                    return true;
                case "STATUS":
                    kind = IpcCommandKind.Status;
                    return true;
                default:
                    return false;
            }
        }
    }

    public enum IpcJobStatus
    {
        Accepted,
        Running,
        Succeeded,
        Failed,
        Timeout,
        Rejected,
        Error
    }

    public static class IpcJobStatusName
    {
        public static string Of(IpcJobStatus s)
        {
            switch (s)
            {
                case IpcJobStatus.Accepted: return "accepted";
                case IpcJobStatus.Running: return "running";
                case IpcJobStatus.Succeeded: return "succeeded";
                case IpcJobStatus.Failed: return "failed";
                case IpcJobStatus.Timeout: return "timeout";
                case IpcJobStatus.Rejected: return "rejected";
                default: return "error";
            }
        }
    }
}
