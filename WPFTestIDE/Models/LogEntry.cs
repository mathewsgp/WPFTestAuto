using System;
using System.Text.RegularExpressions;

namespace WpfTestIde.Models
{
    /// <summary>
    /// One parsed line of the Robot Framework stdout stream as it is appended to
    /// <c>RunOutputLines</c>. <see cref="RunOutputText"/> stays the raw join used
    /// by the A5 bottom tail; <see cref="LogEntry"/> adds structure (Time / Level /
    /// Message) so the D4 RESULTS-tab ListView can sort + filter on columns.
    /// </summary>
    public sealed class LogEntry
    {
        /// <summary>The raw text of the line as received from Robot. Used by the
        /// non-matching-line fallback path (Level=Info, Message=raw) and to keep
        /// a single source of truth alongside <see cref="RunOutputLines"/>.
        /// </summary>
        public string Raw { get; }

        /// <summary>Time as parsed out of the Robot timestamp
        /// (<c>YYYYMMDD HH:MM:SS.nnn</c>), or <c>null</c> for lines that do not
        /// match (so the ListView can group unstructured lines at the top/bottom).
        /// Stored as a string in the original Robot format so no culture formatting
        /// surprises leak into the UI; sort still works because the timestamp is
        /// lexicographically ordered.</summary>
        public string? Time { get; }

        public LogLevel Level { get; }

        public string Message { get; }

        public LogEntry(string raw, string? time, LogLevel level, string message)
        {
            Raw = raw;
            Time = time;
            Level = level;
            Message = message;
        }

        /// <summary>LevelBadge text exposed to the View for the Level column
        /// (kept short so the narrow column doesn't push the Message width).
        /// Mirrors Robot's own short uppercase labels.</summary>
        public string LevelText => Level switch
        {
            LogLevel.Trace => "TRACE",
            LogLevel.Debug => "DEBUG",
            LogLevel.Info => "INFO",
            LogLevel.Warn => "WARN",
            LogLevel.Error => "ERROR",
            LogLevel.Fail => "FAIL",
            LogLevel.Raw => "RAW",
            _ => "INFO",
        };
    }

    public enum LogLevel
    {
        Trace,
        Debug,
        Info,
        Warn,
        Error,
        Fail,
        /// <summary>Unstructured line that did not match the Robot timestamp
        /// prefix; kept distinct from Info so the filter shortlist (Info/Warn/
        /// Error in D4) doesn't accidentally surface continuation lines, blank
        /// lines, separator bars, etc.</summary>
        Raw,
    }

    /// <summary>Parses a Robot Framework stdout line into a
    /// <see cref="LogEntry"/>. Robot writes lines in the shape
    /// <c>YYYYMMDD HH:MM:SS.nnn | LEVEL | message</c> (LEVEL is one of TRACE
    /// / DEBUG / INFO / WARN / ERROR / FAIL). Non-matching lines are reported
    /// as <see cref="LogLevel.Raw"/> with the original line as Message so the
    /// ListView still shows them under the "RAW" filter. The class is static
    /// (immutable regex state) so it is safe to call from any thread; the
    /// callers that build LogEntry collections already marshal onto the
    /// Dispatcher before mutating the bound ObservableCollection.</summary>
    public static class LogLineParser
    {
        // Examples this regex intentionally matches:
        //   20260817 09:25:13.123 | INFO  | Starting test ...
        //   20260817 09:25:14.001 | FAIL  | MyKeyword :: Some failure
        //   20260817 09:25:15.120 | WARN  | X
        // The separator is `|` with surrounding optional whitespace — Robot
        // pads Level to 5 chars (TRACE/DEBUG/INFO/WARN/ERROR/FAIL) and that is
        // absorbed by the optional trailing spaces in the level group.
        private static readonly Regex _lineRegex = new Regex(
            @"^(?<time>\d{8}\ \d{2}:\d{2}:\d{2}\.\d{3})\s*\|\s*(?<level>TRACE|DEBUG|INFO|WARN|ERROR|FAIL)\s*\|\s*(?<msg>.*)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static LogEntry Parse(string line)
        {
            if (line is null) return new LogEntry("", null, LogLevel.Raw, "");
            var m = _lineRegex.Match(line);
            if (!m.Success) return new LogEntry(line, null, LogLevel.Raw, line);
            return new LogEntry(
                raw: line,
                time: m.Groups["time"].Value,
                level: MapLevel(m.Groups["level"].Value),
                message: m.Groups["msg"].Value);
        }

        private static LogLevel MapLevel(string s) => s switch
        {
            "TRACE" => LogLevel.Trace,
            "DEBUG" => LogLevel.Debug,
            "INFO" => LogLevel.Info,
            "WARN" => LogLevel.Warn,
            "ERROR" => LogLevel.Error,
            "FAIL" => LogLevel.Fail,
            _ => LogLevel.Info,
        };
    }
}
