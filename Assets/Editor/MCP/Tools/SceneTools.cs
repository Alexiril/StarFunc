using System;
using System.Collections;
using System.Reflection;
using System.Threading.Tasks;
using MCP.Protocol;
using MCP.Server;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace MCP.Tools
{
    [InitializeOnLoad]
    public static class SceneTools
    {
        static SceneTools()
        {
            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "scenes_list",
                Description = "List scenes from the active BuildProfile (Unity 6) or EditorBuildSettings.",
                InputSchema = new JObject { ["type"] = "object", ["properties"] = new JObject() },
                Handler = _ => MainThreadDispatcher.Run(() => ListScenes())
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "scene_current",
                Description = "Return the active scene and all loaded scenes.",
                InputSchema = new JObject { ["type"] = "object", ["properties"] = new JObject() },
                Handler = _ => MainThreadDispatcher.Run(() => CurrentScene())
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "scene_open",
                Description = "Open a scene by path or guid. mode = Single | Additive | AdditiveWithoutLoading.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["path"] = new JObject { ["type"] = "string" },
                        ["guid"] = new JObject { ["type"] = "string" },
                        ["mode"] = new JObject { ["type"] = "string", ["enum"] = new JArray { "Single", "Additive", "AdditiveWithoutLoading" } }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => OpenScene(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "scene_save",
                Description = "Save a scene. With sceneName, saves that loaded scene; without, saves the active scene. With path, saves to that path (Save As); the scene must be loaded. Returns post-save state — isDirty:false means EITHER the save just succeeded OR there was nothing to save (e.g. a prior assets_save / focus event already flushed it). Both outcomes are 'ok'.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["sceneName"] = new JObject { ["type"] = "string", ["description"] = "Name of a loaded scene; defaults to the active scene." },
                        ["path"] = new JObject { ["type"] = "string", ["description"] = "Optional Save-As path. If omitted, saves to the scene's existing path." }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => SaveScene(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "scene_save_all",
                Description = "Save all open dirty scenes. Wraps EditorSceneManager.SaveOpenScenes.",
                InputSchema = new JObject { ["type"] = "object", ["properties"] = new JObject() },
                Handler = _ => MainThreadDispatcher.Run(() => SaveAllScenes())
            });
        }

        static JToken ListScenes()
        {
            var arr = new JArray();
            string profileName = null;
            var fromProfile = TryGetActiveBuildProfile(out profileName);
            var sources = fromProfile ?? (IEnumerable)EditorBuildSettings.scenes;

            foreach (var s in sources)
            {
                string path = ReadString(s, "path");
                bool enabled = ReadBool(s, "enabled");
                arr.Add(new JObject
                {
                    ["path"] = path,
                    ["guid"] = AssetDatabase.AssetPathToGUID(path ?? ""),
                    ["enabled"] = enabled
                });
            }

            var result = new JObject
            {
                ["scenes"] = arr,
                ["source"] = fromProfile != null ? "BuildProfile" : "EditorBuildSettings",
                ["count"] = arr.Count
            };
            if (fromProfile != null) result["profileName"] = profileName;
            return result;
        }

        static IEnumerable TryGetActiveBuildProfile(out string profileName)
        {
            profileName = null;
            try
            {
                var bpType = Type.GetType("UnityEditor.Build.Profile.BuildProfile, UnityEditor.CoreModule");
                if (bpType == null) return null;
                var get = bpType.GetMethod("GetActiveBuildProfile", BindingFlags.Public | BindingFlags.Static);
                if (get == null) return null;
                var profile = get.Invoke(null, null);
                if (profile == null) return null;
                var scenesField = bpType.GetField("scenes", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var scenesProp = bpType.GetProperty("scenes", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var val = scenesField != null ? scenesField.GetValue(profile) : scenesProp?.GetValue(profile);
                if (profile is UnityEngine.Object uo) profileName = uo.name;
                return val as IEnumerable;
            }
            catch { return null; }
        }

        static string ReadString(object o, string name)
        {
            var f = o.GetType().GetField(name); if (f != null) return f.GetValue(o)?.ToString();
            var p = o.GetType().GetProperty(name); return p?.GetValue(o)?.ToString();
        }

        static bool ReadBool(object o, string name)
        {
            var f = o.GetType().GetField(name); if (f != null) return (bool)f.GetValue(o);
            var p = o.GetType().GetProperty(name); return p != null && (bool)p.GetValue(o);
        }

        static JToken CurrentScene()
        {
            var loaded = new JArray();
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var s = EditorSceneManager.GetSceneAt(i);
                loaded.Add(new JObject
                {
                    ["name"] = s.name,
                    ["path"] = s.path,
                    ["isLoaded"] = s.isLoaded,
                    ["isDirty"] = s.isDirty,
                    ["rootCount"] = s.rootCount
                });
            }
            var active = EditorSceneManager.GetActiveScene();
            return new JObject
            {
                ["activeScene"] = new JObject { ["name"] = active.name, ["path"] = active.path },
                ["loadedScenes"] = loaded
            };
        }

        static JToken OpenScene(JObject args)
        {
            var path = (string)args["path"];
            var guid = (string)args["guid"];
            var modeStr = (string)args["mode"] ?? "Single";

            if (string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(guid))
                path = AssetDatabase.GUIDToAssetPath(guid);

            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("scene_open requires path or guid");

            if (!Enum.TryParse<OpenSceneMode>(modeStr, out var mode))
                throw new ArgumentException($"Invalid mode: {modeStr}");

            if (mode == OpenSceneMode.Single)
                EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

            var scene = EditorSceneManager.OpenScene(path, mode);
            return new JObject
            {
                ["name"] = scene.name,
                ["path"] = scene.path,
                ["isLoaded"] = scene.isLoaded
            };
        }

        static JToken SaveScene(JObject args)
        {
            var sceneName = (string)args["sceneName"];
            var savePath = (string)args["path"];

            UnityEngine.SceneManagement.Scene scene;
            if (!string.IsNullOrEmpty(sceneName))
            {
                scene = EditorSceneManager.GetSceneByName(sceneName);
                if (!scene.IsValid()) throw new ArgumentException($"Scene '{sceneName}' is not loaded");
            }
            else
            {
                scene = EditorSceneManager.GetActiveScene();
            }

            bool ok;
            if (!string.IsNullOrEmpty(savePath))
                ok = EditorSceneManager.SaveScene(scene, savePath);
            else
                ok = EditorSceneManager.SaveScene(scene);

            return new JObject
            {
                ["ok"] = ok,
                ["name"] = scene.name,
                ["path"] = scene.path,
                ["isDirty"] = scene.isDirty
            };
        }

        static JToken SaveAllScenes()
        {
            bool ok = EditorSceneManager.SaveOpenScenes();
            var arr = new JArray();
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var s = EditorSceneManager.GetSceneAt(i);
                arr.Add(new JObject { ["name"] = s.name, ["path"] = s.path, ["isDirty"] = s.isDirty });
            }
            return new JObject { ["ok"] = ok, ["scenes"] = arr };
        }
    }
}
