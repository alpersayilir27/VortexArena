using System.Collections.Generic;
using UnityEngine;
using VortexArena.Protocol;

namespace VortexArena.Net
{
    /// <summary>Relays this device's own warnings/errors to the server as <c>client_log</c> (§5.1).</summary>
    /// <remarks>The player app runs on the headset and a venue has no USB, so without this channel a
    /// field fault is simply unreadable. The caps (rate, duplicate window, queue depth) are not tuning:
    /// a per-frame error is the normal case here and must never crowd out authority traffic.</remarks>
    public class ClientLogRelay : MonoBehaviour
    {
        private struct Line
        {
            public string Level;
            public string Text;
        }

        private static ClientLogRelay _instance;

        /// <summary>Guards <see cref="_queue"/>: <c>logMessageReceived</c> also fires on background
        /// threads (async network/task code), while the drain runs in Update.</summary>
        private readonly object _gate = new object();

        private readonly Queue<Line> _queue = new Queue<Line>();

        /// <summary>Recursion guard: a failure raised INSIDE the handler would re-enter it forever.
        /// For the same reason nothing below ever calls <c>Debug.*</c>.</summary>
        private bool _inHandler;

        // Duplicate suppression state (send side, main thread only).
        private string _lastText = "";
        private string _lastLevel = ArenaProtocol.LOG_LEVEL_WARN;
        private float _lastSentTime = float.NegativeInfinity;
        private int _pendingRepeat;

        // Rate window (unscaled seconds).
        private float _windowStart;
        private int _sentInWindow;

        /// <summary>Installs the singleton. ⚠️ <b>Unconditional</b> — the "is it needed in this
        /// session" decision belongs to the App layer's single install point.</summary>
        public static void Install()
        {
            if (_instance != null)
            {
                return;
            }

            var go = new GameObject("[ClientLogRelay]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ClientLogRelay>();
        }

        /// <summary>Deliberately relayed line that is neither a warning nor an error (§10.6 calibration
        /// result); silently dropped when the relay is not installed.</summary>
        public static void Report(string text)
        {
            if (_instance == null)
            {
                return;
            }

            _instance.Enqueue(ArenaProtocol.LOG_LEVEL_INFO, text);
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
        }

        private void OnEnable()
        {
            if (_instance != this)
            {
                return;
            }

            Application.logMessageReceived += HandleLog;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= HandleLog;
        }

        private void HandleLog(string condition, string stackTrace, LogType type)
        {
            if (_inHandler)
            {
                return;
            }

            string level;
            switch (type)
            {
                case LogType.Warning:
                case LogType.Assert:
                    level = ArenaProtocol.LOG_LEVEL_WARN;
                    break;
                case LogType.Error:
                case LogType.Exception:
                    level = ArenaProtocol.LOG_LEVEL_ERROR;
                    break;
                default:
                    return; // plain logs are not relayed
            }

            _inHandler = true;
            try
            {
                string text = condition ?? "";
                if (level == ArenaProtocol.LOG_LEVEL_ERROR)
                {
                    // Only the first trace line: it names the fault, the rest belongs in a debugger.
                    string origin = FirstLine(stackTrace);
                    if (origin.Length > 0)
                    {
                        text = text + " ← " + origin;
                    }
                }

                Enqueue(level, text);
            }
            finally
            {
                _inHandler = false;
            }
        }

        private void Enqueue(string level, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (text.Length > ArenaProtocol.LOG_TEXT_MAX_CHARS)
            {
                text = text.Substring(0, ArenaProtocol.LOG_TEXT_MAX_CHARS);
            }

            var line = new Line { Level = level, Text = text };

            lock (_gate)
            {
                // A full queue drops its OLDEST line — the newest fault is the one being chased.
                while (_queue.Count >= ArenaProtocol.LOG_QUEUE_MAX)
                {
                    _queue.Dequeue();
                }

                _queue.Enqueue(line);
            }
        }

        private static string FirstLine(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            int end = value.IndexOf('\n');
            string first = end >= 0 ? value.Substring(0, end) : value;
            return first.TrimEnd('\r');
        }

        private void Update()
        {
            if (_instance != this)
            {
                return;
            }

            ArenaClient client = ArenaClient.Instance;
            if (client == null || !client.IsConnected)
            {
                return; // held, never dropped: a fault raised while the link is down is the valuable one
            }

            float now = Time.unscaledTime;
            if (now - _windowStart >= 1f)
            {
                _windowStart = now;
                _sentInWindow = 0;
            }

            while (_sentInWindow < ArenaProtocol.LOG_MAX_LINES_PER_SECOND)
            {
                Line line;
                lock (_gate)
                {
                    if (_queue.Count == 0)
                    {
                        break;
                    }

                    line = _queue.Dequeue();
                }

                if (line.Text == _lastText &&
                    now - _lastSentTime < ArenaProtocol.LOG_DUPLICATE_WINDOW_SECONDS)
                {
                    _pendingRepeat++;
                    continue; // counted, not sent — a stuck fault stays one line
                }

                bool sameAsLast = line.Text == _lastText;
                if (_pendingRepeat > 0 && !sameAsLast)
                {
                    // ⚠️ The count belongs to the line it was counted FOR — a different line inheriting
                    // it would name the wrong fault. Flushed under its own text; the pair may overshoot
                    // the window by one line.
                    SendLine(client, _lastLevel, _lastText, _pendingRepeat);
                    _sentInWindow++;
                }

                SendLine(client, line.Level, line.Text, sameAsLast ? _pendingRepeat : 0);

                _pendingRepeat = 0;
                _lastText = line.Text;
                _lastLevel = line.Level;
                _lastSentTime = now;
                _sentInWindow++;
            }
        }

        private static void SendLine(ArenaClient client, string level, string text, int repeat)
        {
            client.Send(new ClientLogMsg { level = level, text = text, repeat = repeat });
        }
    }
}
