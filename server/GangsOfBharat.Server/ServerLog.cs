using System;
using System.Globalization;
using System.Text;

namespace GangsOfBharat.Server
{
    /// <summary>
    /// Structured logging as one JSON object per line. Chat lines go to a separate sink so they
    /// can be routed to an evidence store. Voice content is never logged.
    /// </summary>
    public sealed class ServerLog
    {
        private readonly object _lock = new object();

        /// <summary>Receives general lines (connect, kick, stats). Default: stdout.</summary>
        public Action<string> Output = Console.WriteLine;
        /// <summary>Receives chat lines. Default: stdout. Set to null to drop.</summary>
        public Action<string> ChatOutput = Console.WriteLine;
        public bool DebugEnabled;

        public static ServerLog Silent()
        {
            ServerLog log = new ServerLog();
            log.Output = null;
            log.ChatOutput = null;
            return log;
        }

        public void Info(string evt, params object[] fields)
        {
            Write("info", evt, fields, Output);
        }

        public void Warn(string evt, params object[] fields)
        {
            Write("warn", evt, fields, Output);
        }

        public void Debug(string evt, params object[] fields)
        {
            if (DebugEnabled)
            {
                Write("debug", evt, fields, Output);
            }
        }

        public void Chat(params object[] fields)
        {
            Write("info", "chat", fields, ChatOutput);
        }

        private void Write(string level, string evt, object[] fields, Action<string> sink)
        {
            if (sink == null)
            {
                return;
            }
            StringBuilder sb = new StringBuilder(128);
            sb.Append("{\"ts\":\"").Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
            sb.Append("\",\"lvl\":\"").Append(level).Append("\",\"evt\":\"").Append(evt).Append('"');
            for (int i = 0; i + 1 < fields.Length; i += 2)
            {
                sb.Append(",\"").Append(Convert.ToString(fields[i], CultureInfo.InvariantCulture)).Append("\":");
                AppendValue(sb, fields[i + 1]);
            }
            sb.Append('}');
            string line = sb.ToString();
            lock (_lock)
            {
                sink(line);
            }
        }

        private static void AppendValue(StringBuilder sb, object value)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }
            if (value is bool)
            {
                sb.Append((bool)value ? "true" : "false");
                return;
            }
            if (value is int || value is long || value is uint || value is ushort || value is byte)
            {
                sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                return;
            }
            if (value is double || value is float)
            {
                sb.Append(Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("0.###", CultureInfo.InvariantCulture));
                return;
            }
            AppendJsonString(sb, Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        public static void AppendJsonString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 0x20)
                        {
                            sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(ch);
                        }
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
