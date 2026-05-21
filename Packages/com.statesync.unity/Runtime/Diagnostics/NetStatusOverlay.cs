using StateSync.Config;
using StateSync.Server;
using UnityEngine;

namespace StateSync.Diagnostics
{
    /// <summary>
    /// On-screen overlay showing the State Sync runtime state: role, port,
    /// connected clients, send/receive counts, parse errors. Uses OnGUI so the
    /// package stays TMP/Canvas-free.
    ///
    /// Compiled out of release builds (kept only in <c>DEVELOPMENT_BUILD</c> and
    /// the Editor).
    /// </summary>
    public sealed class NetStatusOverlay : MonoBehaviour
    {
        [SerializeField] WsServer server;
        [SerializeField] int fontSize = 14;
        [Tooltip("Pixel margin from the top-left corner of the screen.")]
        [SerializeField] Vector2 margin = new Vector2(10f, 10f);

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        void Awake()
        {
            if (server == null) server = GetComponent<WsServer>();
            if (server == null) enabled = false;
        }

        void OnGUI()
        {
            if (server == null) return;

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                normal = { textColor = Color.white },
                padding = new RectOffset(8, 8, 4, 4),
            };

            string role = ConnectionConfig.IsHost ? "host" : "follower";
            string text = $"[StateSync] role={role} port={server.Port} state={server.State} clients={server.ClientCount} " +
                          $"sent={server.TotalSent} recv={server.TotalReceived} errs={server.TotalParseErrors}";

            Vector2 size = style.CalcSize(new GUIContent(text));
            var rect = new Rect(margin.x, margin.y, size.x + 16f, size.y);

            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.Label(rect, text, style);
        }
#endif
    }
}
