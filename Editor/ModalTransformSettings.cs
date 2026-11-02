// Copyright (c) 2026 Figix. All rights reserved.

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Figix.ModalTransformUnity
{
    /// <summary>
    /// Per-user key bindings. Values live in EditorPrefs, outside the package folder,
    /// so they survive package updates.
    /// </summary>
    internal static class ModalTransformSettings
    {
        internal const string GitUrl = "https://github.com/Figix/ModalTransformUnity.git";
        private const string PrefsPrefix = "Figix.ModalTransform.Key.";

        internal enum Action
        {
            Translate,
            Rotate,
            Scale,
            AxisX,
            AxisY,
            AxisZ
        }

        private static readonly Dictionary<Action, KeyCode> Defaults = new Dictionary<Action, KeyCode>
        {
            { Action.Translate, KeyCode.W },
            { Action.Rotate, KeyCode.E },
            { Action.Scale, KeyCode.R },
            { Action.AxisX, KeyCode.X },
            { Action.AxisY, KeyCode.Y },
            { Action.AxisZ, KeyCode.Z }
        };

        private static readonly Dictionary<Action, KeyCode> Cache = new Dictionary<Action, KeyCode>();

        internal static KeyCode Get(Action action)
        {
            KeyCode key;
            if (!Cache.TryGetValue(action, out key))
            {
                key = (KeyCode)EditorPrefs.GetInt(PrefsPrefix + action, (int)Defaults[action]);
                Cache[action] = key;
            }
            return key;
        }

        internal static void Set(Action action, KeyCode key)
        {
            Cache[action] = key;
            EditorPrefs.SetInt(PrefsPrefix + action, (int)key);
        }

        internal static void ResetAll()
        {
            foreach (KeyValuePair<Action, KeyCode> pair in Defaults)
            {
                EditorPrefs.DeleteKey(PrefsPrefix + pair.Key);
            }
            Cache.Clear();
        }

        [SettingsProvider]
        private static SettingsProvider CreateProvider()
        {
            return new SettingsProvider("Preferences/Figix/Modal Transform", SettingsScope.User)
            {
                label = "Modal Transform",
                keywords = new HashSet<string> { "modal", "transform", "shortcut", "key", "figix" },
                guiHandler = DrawGui
            };
        }

        private static void DrawGui(string searchContext)
        {
            EditorGUILayout.LabelField("Key bindings", EditorStyles.boldLabel);
            foreach (Action action in (Action[])System.Enum.GetValues(typeof(Action)))
            {
                EditorGUI.BeginChangeCheck();
                KeyCode key = (KeyCode)EditorGUILayout.EnumPopup(action.ToString(), Get(action));
                if (EditorGUI.EndChangeCheck())
                {
                    Set(action, key);
                }
            }

            if (HasDuplicates())
            {
                EditorGUILayout.HelpBox("The same key is assigned to more than one action.", MessageType.Warning);
            }

            if (GUILayout.Button("Reset to defaults", GUILayout.Width(140f)))
            {
                ResetAll();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Package", EditorStyles.boldLabel);
            UnityEditor.PackageManager.PackageInfo info =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ModalTransformSettings).Assembly);
            if (info != null)
            {
                EditorGUILayout.LabelField("Installed version", info.version);
                if (info.git != null)
                {
                    EditorGUILayout.LabelField("Git revision", info.git.revision);
                }
            }

            if (GUILayout.Button("Update to latest (re-resolve Git URL)", GUILayout.Width(260f)))
            {
                Client.Add(GitUrl);
            }
            EditorGUILayout.HelpBox(
                "This resolves the unpinned Git URL to the newest commit. If you pinned a tag (#vX.Y.Z), " +
                "edit Packages/manifest.json instead.",
                MessageType.None);
        }

        private static bool HasDuplicates()
        {
            HashSet<KeyCode> seen = new HashSet<KeyCode>();
            foreach (Action action in (Action[])System.Enum.GetValues(typeof(Action)))
            {
                if (!seen.Add(Get(action)))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
