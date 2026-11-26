using System;
using System.Net.Sockets;
using System.Text.RegularExpressions;

/// <summary>State of the WebSocket connection.</summary>
public enum RealtimeState { Disconnected, Connecting, Connected }

/// <summary>Where the API key was found. The key itself is never exposed.</summary>
public enum KeySource { None, Inspector, Environment, DataFolder, ProjectFolder, ProgramFolder }

/// <summary>What can go wrong in a live conversation.</summary>
public enum RealtimeProblem
{
    None,
    NoKey,
    KeyRejected,
    NoAccess,
    NoCredit,
    RateLimit,
    NoInternet,
    ConnectFailed,
    ConnectionLost,
    SessionEnded,
    Server,
    MicDenied,
    MicMissing,
    MicRate
}

/// <summary>Turns exceptions and server errors into problems, and removes everything that looks like a key from texts.</summary>
public static class RealtimeDiagnosis
{
    private const int MaximumLength = 160;

    private static readonly Regex Key = new Regex(@"\bsk-[A-Za-z0-9_\-*]{4,}|Bearer \S+");
    private static readonly Regex Unauthorized = new Regex(@"\b401\b");
    private static readonly Regex Forbidden = new Regex(@"\b(403|404)\b");
    private static readonly Regex TooMany = new Regex(@"\b429\b");

    /// <summary>A short version of a text without anything that looks like a key.</summary>
    public static string Sanitize(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        text = Key.Replace(text, "[key]").Trim();
        return text.Length > MaximumLength ? text.Substring(0, MaximumLength) : text;
    }

    /// <summary>The problem behind an exception that was thrown while connecting.</summary>
    public static RealtimeProblem FromConnectException(Exception exception, bool hasNetwork)
    {
        if (!hasNetwork) return RealtimeProblem.NoInternet;

        for (Exception e = exception; e != null; e = e.InnerException)
        {
            if (e is SocketException) return RealtimeProblem.NoInternet;
            if (Unauthorized.IsMatch(e.Message)) return RealtimeProblem.KeyRejected;
            if (Forbidden.IsMatch(e.Message)) return RealtimeProblem.NoAccess;
            if (TooMany.IsMatch(e.Message)) return RealtimeProblem.NoCredit;
        }
        return RealtimeProblem.ConnectFailed;
    }

    /// <summary>The problem behind an error event.</summary>
    public static RealtimeProblem FromServerError(string code, string message)
    {
        if (message != null && message.Contains("API key")) return RealtimeProblem.KeyRejected;
        switch (code)
        {
            case "invalid_api_key": return RealtimeProblem.KeyRejected;
            case "model_not_found":
            case "permission_denied": return RealtimeProblem.NoAccess;
            case "insufficient_quota":
            case "billing_hard_limit_reached": return RealtimeProblem.NoCredit;
            case "rate_limit_exceeded": return RealtimeProblem.RateLimit;
            case "session_expired": return RealtimeProblem.SessionEnded;
            default: return RealtimeProblem.Server;
        }
    }

    /// <summary>Error events that only say that the server was a moment ahead.</summary>
    public static bool IsHarmless(string code, string message)
    {
        if (code == "conversation_already_has_active_response" || code == "response_cancel_not_active") return true;
        if (string.IsNullOrEmpty(message)) return false;
        return message.Contains("Cancellation failed: no active response found") || message.Contains("already shorter");
    }
}
