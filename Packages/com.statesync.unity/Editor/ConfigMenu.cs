using StateSync.Config;
using UnityEditor;

namespace StateSync.Editor
{
    /// <summary>
    /// <c>Tools / State Sync / Mode | Port</c> menu items. Sets per-user EditorPrefs
    /// that are applied to <see cref="ConnectionConfig"/> at Editor startup, so the
    /// Editor can simulate a chosen role without CLI args (which aren't available to
    /// the Editor process).
    /// </summary>
    public static class ConfigMenu
    {
        const string ModePrefKey = "StateSync.Mode";
        const string PortPrefKey = "StateSync.Port";

        const string MenuHost      = "Tools/State Sync/Mode/Host";
        const string MenuFollower  = "Tools/State Sync/Mode/Follower";
        const string MenuModeCli   = "Tools/State Sync/Mode/Use CLI (clear override)";
        const string MenuPort5050  = "Tools/State Sync/Port/5050";
        const string MenuPort5051  = "Tools/State Sync/Port/5051";
        const string MenuPortCli   = "Tools/State Sync/Port/Use CLI default";

        [InitializeOnLoadMethod]
        static void ApplyEditorPrefsOnStartup()
        {
            string mode = EditorPrefs.GetString(ModePrefKey, "cli");
            ConnectionConfig.OverrideIsHost = mode switch
            {
                "host" => true,
                "follower" => false,
                _ => null
            };

            int port = EditorPrefs.GetInt(PortPrefKey, 0);
            ConnectionConfig.OverridePort = port > 0 ? port : (int?)null;
        }

        // -------- Mode --------

        [MenuItem(MenuHost, priority = 100)]
        static void SetHost()
        {
            ConnectionConfig.OverrideIsHost = true;
            EditorPrefs.SetString(ModePrefKey, "host");
        }
        [MenuItem(MenuHost, validate = true)]
        static bool ValidateHost()
        {
            Menu.SetChecked(MenuHost, EditorPrefs.GetString(ModePrefKey, "cli") == "host");
            return true;
        }

        [MenuItem(MenuFollower, priority = 101)]
        static void SetFollower()
        {
            ConnectionConfig.OverrideIsHost = false;
            EditorPrefs.SetString(ModePrefKey, "follower");
        }
        [MenuItem(MenuFollower, validate = true)]
        static bool ValidateFollower()
        {
            Menu.SetChecked(MenuFollower, EditorPrefs.GetString(ModePrefKey, "cli") == "follower");
            return true;
        }

        [MenuItem(MenuModeCli, priority = 102)]
        static void ClearMode()
        {
            ConnectionConfig.OverrideIsHost = null;
            EditorPrefs.SetString(ModePrefKey, "cli");
        }
        [MenuItem(MenuModeCli, validate = true)]
        static bool ValidateClearMode()
        {
            Menu.SetChecked(MenuModeCli, EditorPrefs.GetString(ModePrefKey, "cli") == "cli");
            return true;
        }

        // -------- Port --------

        [MenuItem(MenuPort5050, priority = 200)]
        static void Set5050()
        {
            ConnectionConfig.OverridePort = 5050;
            EditorPrefs.SetInt(PortPrefKey, 5050);
        }
        [MenuItem(MenuPort5050, validate = true)]
        static bool Validate5050()
        {
            Menu.SetChecked(MenuPort5050, EditorPrefs.GetInt(PortPrefKey, 0) == 5050);
            return true;
        }

        [MenuItem(MenuPort5051, priority = 201)]
        static void Set5051()
        {
            ConnectionConfig.OverridePort = 5051;
            EditorPrefs.SetInt(PortPrefKey, 5051);
        }
        [MenuItem(MenuPort5051, validate = true)]
        static bool Validate5051()
        {
            Menu.SetChecked(MenuPort5051, EditorPrefs.GetInt(PortPrefKey, 0) == 5051);
            return true;
        }

        [MenuItem(MenuPortCli, priority = 202)]
        static void ClearPort()
        {
            ConnectionConfig.OverridePort = null;
            EditorPrefs.DeleteKey(PortPrefKey);
        }
        [MenuItem(MenuPortCli, validate = true)]
        static bool ValidateClearPort()
        {
            Menu.SetChecked(MenuPortCli, !EditorPrefs.HasKey(PortPrefKey));
            return true;
        }
    }
}
