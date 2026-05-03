using System;
using MCP.Protocol;
using MCP.Reflection;
using MCP.Server;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MCP.Tools
{
    [InitializeOnLoad]
    public static class PrefabTools
    {
        static PrefabTools()
        {
            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "prefab_status",
                Description = "Report prefab state for a target Object: whether it's a prefab asset, an instance, or a variant, plus the source asset path and any corresponding source object.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["required"] = new JArray { "target" },
                    ["properties"] = new JObject
                    {
                        ["target"] = new JObject { ["type"] = "object", ["description"] = "ObjectRef of a GameObject or Component." }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => Status(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "prefab_apply_overrides",
                Description = "Apply all instance overrides on a Prefab instance back to the source asset. Target must be part of a Prefab instance.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["required"] = new JArray { "instance" },
                    ["properties"] = new JObject
                    {
                        ["instance"] = new JObject { ["type"] = "object", ["description"] = "ObjectRef of any GameObject/Component inside the Prefab instance." }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => ApplyOverrides(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "prefab_revert_overrides",
                Description = "Revert all instance overrides on a Prefab instance back to the source values. Target must be part of a Prefab instance.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["required"] = new JArray { "instance" },
                    ["properties"] = new JObject
                    {
                        ["instance"] = new JObject { ["type"] = "object" }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => RevertOverrides(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "prefab_unpack",
                Description = "Unpack a Prefab instance into plain GameObjects. mode = OutermostRoot (only top) | Completely (recursive through nested prefabs).",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["required"] = new JArray { "instance" },
                    ["properties"] = new JObject
                    {
                        ["instance"] = new JObject { ["type"] = "object" },
                        ["mode"] = new JObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JArray { "OutermostRoot", "Completely" },
                            ["default"] = "OutermostRoot"
                        }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => Unpack(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "prefab_instantiate",
                Description = "Instantiate a Prefab Asset into the active scene (or specified loaded scene). Provide path OR prefab (ObjectRef of the asset). Optional parent (must be a scene GameObject), and transform setup. Returns the new instance's GameObject ref.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["path"] = new JObject { ["type"] = "string", ["description"] = "Project-relative .prefab path." },
                        ["prefab"] = new JObject { ["type"] = "object", ["description"] = "Alternative to path — ObjectRef of a Prefab Asset." },
                        ["parent"] = new JObject { ["type"] = "object", ["description"] = "ObjectRef of a scene GameObject. Prefab Assets are rejected." },
                        ["sceneName"] = new JObject { ["type"] = "string" },
                        ["position"] = new JObject { ["type"] = "object" },
                        ["rotation"] = new JObject { ["type"] = "object" },
                        ["eulerAngles"] = new JObject { ["type"] = "object" },
                        ["localScale"] = new JObject { ["type"] = "object" },
                        ["isLocalSpace"] = new JObject { ["type"] = "boolean", ["default"] = true }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => Instantiate(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "prefab_open",
                Description = "Open a Prefab Asset in Prefab Stage (isolated edit mode). Returns the stage's root GameObject ObjectRef. Use prefab_close to save and exit. While open, gameobject_create/update/destroy and member_set on the stage's contents edit the prefab itself, not a scene instance.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["required"] = new JArray { "path" },
                    ["properties"] = new JObject
                    {
                        ["path"] = new JObject { ["type"] = "string", ["description"] = "Project-relative .prefab path." }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => OpenPrefabStage(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "prefab_close",
                Description = "Close the currently-open Prefab Stage. With save=true (default), saves edits back to the prefab asset before closing. With save=false, discards edits. Response separates sceneDirty (preview-scene flag) and rootDirty (root asset object dirtiness) — member_set via SerializedProperty typically only sets rootDirty, so a saved=true response with sceneDirty=false but rootDirty=true is normal and means the asset was written.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["save"] = new JObject { ["type"] = "boolean", ["default"] = true }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => ClosePrefabStage(args))
            });
        }

        static GameObject ResolveInstance(JObject args, string key = "instance")
        {
            var resolved = ObjectRef.Resolve(args[key]);
            if (resolved == null) throw new ArgumentException($"{key} does not resolve");
            var go = resolved as GameObject ?? (resolved as Component)?.gameObject;
            if (go == null) throw new ArgumentException($"{key} must resolve to a GameObject or Component");
            return go;
        }

        static JToken Status(JObject args)
        {
            var resolved = ObjectRef.Resolve(args["target"]);
            if (resolved == null) throw new ArgumentException("target does not resolve");

            bool isAsset = PrefabUtility.IsPartOfPrefabAsset(resolved);
            bool isInstance = PrefabUtility.IsPartOfPrefabInstance(resolved);
            bool isVariant = PrefabUtility.IsPartOfVariantPrefab(resolved);

            string assetPath = null;
            JObject correspondingRef = null;
            if (isInstance)
            {
                var go = resolved as GameObject ?? (resolved as Component)?.gameObject;
                if (go != null)
                    assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                var src = PrefabUtility.GetCorrespondingObjectFromSource(resolved);
                if (src != null) correspondingRef = ObjectRef.ToRef(src);
            }
            else if (isAsset)
            {
                assetPath = AssetDatabase.GetAssetPath(resolved);
            }

            return new JObject
            {
                ["target"] = ObjectRef.ToRef(resolved),
                ["isPartOfPrefabAsset"] = isAsset,
                ["isPartOfPrefabInstance"] = isInstance,
                ["isPartOfVariantPrefab"] = isVariant,
                ["assetPath"] = assetPath,
                ["correspondingObjectFromSource"] = correspondingRef
            };
        }

        static JToken ApplyOverrides(JObject args)
        {
            var go = ResolveInstance(args);
            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                throw new InvalidOperationException("Target is not part of a Prefab instance");

            var path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            if (string.IsNullOrEmpty(path))
                throw new InvalidOperationException("Could not resolve source prefab asset path");

            PrefabUtility.ApplyPrefabInstance(go, InteractionMode.AutomatedAction);

            return new JObject
            {
                ["ok"] = true,
                ["instance"] = ObjectRef.ToRef(go),
                ["assetPath"] = path
            };
        }

        static JToken RevertOverrides(JObject args)
        {
            var go = ResolveInstance(args);
            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                throw new InvalidOperationException("Target is not part of a Prefab instance");

            PrefabUtility.RevertPrefabInstance(go, InteractionMode.AutomatedAction);
            if (go.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(go.scene);

            return new JObject
            {
                ["ok"] = true,
                ["instance"] = ObjectRef.ToRef(go)
            };
        }

        static JToken Unpack(JObject args)
        {
            var go = ResolveInstance(args);
            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                throw new InvalidOperationException("Target is not part of a Prefab instance");

            var modeStr = (string)args["mode"] ?? "OutermostRoot";
            if (!Enum.TryParse<PrefabUnpackMode>(modeStr, out var mode))
                throw new ArgumentException($"Invalid mode: {modeStr}");

            Undo.SetCurrentGroupName($"MCP Prefab Unpack ({mode})");
            int g = Undo.GetCurrentGroup();
            PrefabUtility.UnpackPrefabInstance(go, mode, InteractionMode.AutomatedAction);
            if (go.scene.IsValid()) EditorSceneManager.MarkSceneDirty(go.scene);
            Undo.CollapseUndoOperations(g);

            return new JObject
            {
                ["ok"] = true,
                ["target"] = ObjectRef.ToRef(go),
                ["mode"] = modeStr
            };
        }

        static JToken Instantiate(JObject args)
        {
            GameObject prefab = null;
            var path = (string)args["path"];
            if (!string.IsNullOrEmpty(path))
            {
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) throw new ArgumentException($"No GameObject prefab at path: {path}");
            }
            else if (args["prefab"] != null && args["prefab"].Type != JTokenType.Null)
            {
                var resolved = ObjectRef.Resolve(args["prefab"]);
                prefab = resolved as GameObject;
                if (prefab == null) throw new ArgumentException("prefab does not resolve to a GameObject");
                if (!PrefabUtility.IsPartOfPrefabAsset(prefab))
                    throw new ArgumentException("Resolved object is not a Prefab Asset (it lives in a scene). Use a prefab path or an asset ref.");
            }
            else
            {
                throw new ArgumentException("Provide path or prefab (ObjectRef of a Prefab Asset)");
            }

            // Validate parent BEFORE instantiating so a guard failure doesn't leak.
            GameObject parentGo = null;
            if (args["parent"] != null && args["parent"].Type != JTokenType.Null)
            {
                var resolved = ObjectRef.Resolve(args["parent"]);
                parentGo = resolved as GameObject ?? (resolved as Component)?.gameObject;
                if (parentGo == null) throw new ArgumentException("parent does not resolve to a GameObject");
                if (!parentGo.scene.IsValid())
                    throw new ArgumentException("parent is a Prefab Asset (not a scene object). Use prefab_open to enter Prefab edit mode, or pass a scene GameObject.");
            }

            Undo.SetCurrentGroupName($"MCP Instantiate Prefab '{prefab.name}'");
            int undoGroup = Undo.GetCurrentGroup();

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (instance == null) throw new InvalidOperationException("PrefabUtility.InstantiatePrefab returned null");
            Undo.RegisterCreatedObjectUndo(instance, "Instantiate " + prefab.name);

            var sceneName = (string)args["sceneName"];
            if (parentGo != null)
            {
                Undo.SetTransformParent(instance.transform, parentGo.transform, "Reparent " + prefab.name);
            }
            else if (!string.IsNullOrEmpty(sceneName))
            {
                var scene = EditorSceneManager.GetSceneByName(sceneName);
                if (!scene.IsValid()) throw new ArgumentException($"Scene '{sceneName}' not loaded");
                EditorSceneManager.MoveGameObjectToScene(instance, scene);
            }

            bool local = args["isLocalSpace"] == null || (bool)args["isLocalSpace"];
            Undo.RecordObject(instance.transform, "Set Transform " + prefab.name);
            if (args["position"] is JObject pos)
            {
                var v = (Vector3)ValueCoercion.Coerce(pos, typeof(Vector3));
                if (local) instance.transform.localPosition = v; else instance.transform.position = v;
            }
            if (args["rotation"] is JObject rot)
            {
                var q = (Quaternion)ValueCoercion.Coerce(rot, typeof(Quaternion));
                if (local) instance.transform.localRotation = q; else instance.transform.rotation = q;
            }
            else if (args["eulerAngles"] is JObject ea)
            {
                var v = (Vector3)ValueCoercion.Coerce(ea, typeof(Vector3));
                if (local) instance.transform.localEulerAngles = v; else instance.transform.eulerAngles = v;
            }
            if (args["localScale"] is JObject sc)
                instance.transform.localScale = (Vector3)ValueCoercion.Coerce(sc, typeof(Vector3));

            if (instance.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(instance.scene);
            Undo.CollapseUndoOperations(undoGroup);

            return new JObject
            {
                ["ok"] = true,
                ["instance"] = ObjectRef.ToRef(instance),
                ["sourcePrefab"] = ObjectRef.ToRef(prefab)
            };
        }

        static JToken OpenPrefabStage(JObject args)
        {
            var path = (string)args["path"];
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("path required");

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing == null) throw new ArgumentException($"No prefab at path: {path}");

            var stage = PrefabStageUtility.OpenPrefab(path);
            if (stage == null) throw new InvalidOperationException("PrefabStageUtility.OpenPrefab returned null (check console for details)");

            return new JObject
            {
                ["ok"] = true,
                ["path"] = stage.assetPath,
                ["mode"] = stage.mode.ToString(),
                ["root"] = ObjectRef.ToRef(stage.prefabContentsRoot)
            };
        }

        static JToken ClosePrefabStage(JObject args)
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null)
                throw new InvalidOperationException("No Prefab Stage is currently open");

            bool save = args["save"] == null || (bool)args["save"];
            string savedPath = stage.assetPath;

            // Two distinct dirty signals:
            //   - sceneDirty: stage's preview scene flagged dirty (e.g. via MarkSceneDirty)
            //   - rootDirty: root asset object flagged dirty (e.g. via SerializedProperty.ApplyModifiedProperties)
            // SerializedProperty edits dirty the asset object but don't always propagate to the preview
            // scene flag — checking only sceneDirty would miss real changes.
            bool sceneDirty = stage.scene.isDirty;
            bool rootDirty = stage.prefabContentsRoot != null && EditorUtility.IsDirty(stage.prefabContentsRoot);
            bool wasDirty = sceneDirty || rootDirty;

            bool savedOk = false;
            if (save && wasDirty)
            {
                // EditorSceneManager.SaveScene fails on preview scenes; use the prefab-specific API.
                if (stage.prefabContentsRoot == null)
                    throw new InvalidOperationException("Stage has no prefabContentsRoot to save");
                PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, stage.assetPath, out savedOk);
            }

            stage.ClearDirtiness();
            StageUtility.GoToMainStage();

            return new JObject
            {
                ["ok"] = true,
                ["assetPath"] = savedPath,
                ["sceneDirty"] = sceneDirty,
                ["rootDirty"] = rootDirty,
                ["wasDirty"] = wasDirty,
                ["saved"] = save && wasDirty,
                ["saveSceneOk"] = savedOk
            };
        }
    }
}
