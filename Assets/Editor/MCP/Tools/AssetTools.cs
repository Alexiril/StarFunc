using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MCP.Protocol;
using MCP.Reflection;
using MCP.Server;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MCP.Tools
{
    [InitializeOnLoad]
    public static class AssetTools
    {
        static AssetTools()
        {
            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "assets_hierarchy",
                Description = "List the asset folder hierarchy under a path. Returns a tree (or flat list with recursive=true and tree=false). filter uses AssetDatabase search syntax e.g. \"t:Texture2D l:MyLabel\".",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["path"] = new JObject { ["type"] = "string", ["default"] = "Assets" },
                        ["recursive"] = new JObject { ["type"] = "boolean", ["default"] = true },
                        ["filter"] = new JObject { ["type"] = "string", ["description"] = "AssetDatabase search filter, e.g. \"t:Texture2D\"" },
                        ["includeFolders"] = new JObject { ["type"] = "boolean", ["default"] = true },
                        ["tree"] = new JObject { ["type"] = "boolean", ["default"] = true, ["description"] = "If false, returns a flat list of paths." },
                        ["maxDepth"] = new JObject { ["type"] = "integer", ["default"] = 16 }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => ListHierarchy(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "asset_get",
                Description = "Get details for one asset: by path, guid, or any ObjectRef (e.g. a wired field on a component). Returns main asset ref, sub-assets, importer ref (use member_set on it for import settings), labels, bundle, dependencies. For meta-assets with many sub-assets (e.g. unity_builtin_extra has hundreds), use includeSubAssets=false to skip them entirely or subAssetNamePattern to filter by regex.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["path"] = new JObject { ["type"] = "string" },
                        ["guid"] = new JObject { ["type"] = "string" },
                        ["objectRef"] = new JObject { ["type"] = "object", ["description"] = "Any UnityEngine.Object ObjectRef; resolves to its asset on disk." },
                        ["includeSubAssets"] = new JObject { ["type"] = "boolean", ["default"] = true, ["description"] = "When false, sub-asset enumeration is skipped entirely (returns empty subAssets array)." },
                        ["subAssetNamePattern"] = new JObject { ["type"] = "string", ["description"] = "Case-insensitive regex; only sub-assets whose name matches are included." }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => GetAsset(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "asset_set_metadata",
                Description = "Update an asset's labels, AssetBundle name/variant, and/or userData on its importer. Persists to .meta and writes import settings. For actual import settings (texture compression, etc.) use member_set on the importer ObjectRef returned from asset_get, then call asset_refresh with the path.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["path"] = new JObject { ["type"] = "string" },
                        ["guid"] = new JObject { ["type"] = "string" },
                        ["labels"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" } },
                        ["assetBundleName"] = new JObject { ["type"] = "string" },
                        ["assetBundleVariant"] = new JObject { ["type"] = "string" },
                        ["userData"] = new JObject { ["type"] = "string" }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => SetMetadata(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "asset_refresh",
                Description = "Refresh the asset database. With path, calls AssetDatabase.ImportAsset(path) (use after editing an importer via member_set). Without path, full AssetDatabase.Refresh. NOTE: if this triggers a script recompile (e.g. you wrote new C# files), expect the same ~30-60s listener blackout as scripts_recompile — connection failures during that window are expected.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["path"] = new JObject { ["type"] = "string" },
                        ["forceUpdate"] = new JObject { ["type"] = "boolean", ["default"] = false }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => Refresh(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "asset_move",
                Description = "Move or rename an asset. newPath is the full destination project-relative path including filename and extension (e.g. 'Assets/NewFolder/Renamed.prefab'). The .meta file follows automatically; the GUID is preserved.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["required"] = new JArray { "newPath" },
                    ["properties"] = new JObject
                    {
                        ["path"] = new JObject { ["type"] = "string" },
                        ["guid"] = new JObject { ["type"] = "string" },
                        ["objectRef"] = new JObject { ["type"] = "object" },
                        ["newPath"] = new JObject { ["type"] = "string", ["description"] = "Project-relative destination including filename + extension." }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => MoveAsset(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "asset_copy",
                Description = "Duplicate an asset to newPath. Creates a new asset with a fresh GUID; the source is untouched.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["required"] = new JArray { "newPath" },
                    ["properties"] = new JObject
                    {
                        ["path"] = new JObject { ["type"] = "string" },
                        ["guid"] = new JObject { ["type"] = "string" },
                        ["objectRef"] = new JObject { ["type"] = "object" },
                        ["newPath"] = new JObject { ["type"] = "string", ["description"] = "Project-relative destination including filename + extension." }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => CopyAsset(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "asset_create_folder",
                Description = "Create a new folder under an existing folder. Returns the new folder's path and guid.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["required"] = new JArray { "parent", "name" },
                    ["properties"] = new JObject
                    {
                        ["parent"] = new JObject { ["type"] = "string", ["description"] = "Existing parent folder path, e.g. 'Assets' or 'Assets/Art'." },
                        ["name"] = new JObject { ["type"] = "string", ["description"] = "New folder name (no slashes)." }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => CreateFolder(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "asset_create",
                Description = "Create a new asset on disk from a UnityEngine.Object type. Works for Material (uses shaderName or pipeline default), any ScriptableObject subtype, and other types with a parameterless constructor (AnimationClip, PhysicsMaterial, RenderTexture, etc.). path must include the correct extension (.mat for Material, .asset for ScriptableObject, .anim for AnimationClip).",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["required"] = new JArray { "path", "typeName" },
                    ["properties"] = new JObject
                    {
                        ["path"] = new JObject { ["type"] = "string", ["description"] = "Project-relative destination including filename + extension." },
                        ["typeName"] = new JObject { ["type"] = "string", ["description"] = "Full or simple type name (e.g. 'UnityEngine.Material', 'Material', 'MyScriptableObject')." },
                        ["shaderName"] = new JObject { ["type"] = "string", ["description"] = "For Material only: shader to assign. Defaults to the active render pipeline's default shader." }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => CreateAsset(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "asset_create_prefab",
                Description = "Save a scene GameObject as a Prefab asset. With connectInstance=true, the source GameObject becomes a Prefab instance linked to the new asset.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["required"] = new JArray { "gameObject", "path" },
                    ["properties"] = new JObject
                    {
                        ["gameObject"] = new JObject { ["type"] = "object", ["description"] = "ObjectRef of the scene GameObject." },
                        ["path"] = new JObject { ["type"] = "string", ["description"] = "Project-relative path ending in .prefab." },
                        ["connectInstance"] = new JObject { ["type"] = "boolean", ["default"] = false }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => CreatePrefab(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "assets_save",
                Description = "Flush all unsaved asset edits to disk. Wraps AssetDatabase.SaveAssets.",
                InputSchema = new JObject { ["type"] = "object", ["properties"] = new JObject() },
                Handler = _ => MainThreadDispatcher.Run(() => SaveAssets())
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "assets_find",
                Description = "Find assets by filter. filter uses AssetDatabase search syntax (e.g. 't:Texture2D l:UI', 'name:Player'). typeName is a convenience that prepends 't:typeName' (full or simple Type name). searchInFolders narrows the search.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["filter"] = new JObject { ["type"] = "string", ["description"] = "AssetDatabase search filter; pass empty with typeName for type-only search." },
                        ["typeName"] = new JObject { ["type"] = "string", ["description"] = "Convenience: prepends 't:<typeName>' to filter." },
                        ["searchInFolders"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" } },
                        ["limit"] = new JObject { ["type"] = "integer", ["default"] = 1000 }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => FindAssets(args))
            });
        }

        // ---- assets_hierarchy ----

        static JToken ListHierarchy(JObject args)
        {
            var rootPath = NormalizePath((string)args["path"] ?? "Assets");
            bool recursive = args["recursive"] == null || (bool)args["recursive"];
            bool includeFolders = args["includeFolders"] == null || (bool)args["includeFolders"];
            bool tree = args["tree"] == null || (bool)args["tree"];
            int maxDepth = args["maxDepth"] != null ? (int)args["maxDepth"] : 16;
            var filter = (string)args["filter"];

            if (!AssetDatabase.IsValidFolder(rootPath))
                throw new ArgumentException($"Not a folder: {rootPath}");

            if (!string.IsNullOrEmpty(filter))
            {
                var guids = AssetDatabase.FindAssets(filter, new[] { rootPath });
                var arr = new JArray();
                foreach (var guid in guids)
                {
                    var p = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(p)) continue;
                    if (!includeFolders && AssetDatabase.IsValidFolder(p)) continue;
                    arr.Add(MakeAssetEntry(p, guid));
                }
                return new JObject { ["root"] = rootPath, ["filter"] = filter, ["entries"] = arr };
            }

            if (tree)
                return new JObject { ["root"] = rootPath, ["tree"] = WalkTree(rootPath, recursive, includeFolders, maxDepth) };

            var flat = new JArray();
            WalkFlat(rootPath, recursive, includeFolders, maxDepth, flat);
            return new JObject { ["root"] = rootPath, ["entries"] = flat };
        }

        static JObject WalkTree(string folder, bool recursive, bool includeFolders, int depth)
        {
            var node = MakeAssetEntry(folder, AssetDatabase.AssetPathToGUID(folder));
            if (depth <= 0) return node;

            var children = new JArray();
            foreach (var sub in AssetDatabase.GetSubFolders(folder))
                children.Add(recursive ? WalkTree(sub, true, includeFolders, depth - 1) : MakeAssetEntry(sub, AssetDatabase.AssetPathToGUID(sub)));

            foreach (var f in EnumerateFiles(folder))
                children.Add(MakeAssetEntry(f, AssetDatabase.AssetPathToGUID(f)));

            if (!includeFolders)
            {
                var filtered = new JArray();
                foreach (var c in children)
                    if (!(bool)(c["isFolder"] ?? false)) filtered.Add(c);
                children = filtered;
            }

            node["children"] = children;
            return node;
        }

        static void WalkFlat(string folder, bool recursive, bool includeFolders, int depth, JArray sink)
        {
            if (depth < 0) return;
            if (includeFolders && folder != "Assets")
                sink.Add(MakeAssetEntry(folder, AssetDatabase.AssetPathToGUID(folder)));
            foreach (var f in EnumerateFiles(folder))
                sink.Add(MakeAssetEntry(f, AssetDatabase.AssetPathToGUID(f)));
            if (!recursive) return;
            foreach (var sub in AssetDatabase.GetSubFolders(folder))
                WalkFlat(sub, true, includeFolders, depth - 1, sink);
        }

        static IEnumerable<string> EnumerateFiles(string folder)
        {
            string abs = ToAbsolute(folder);
            if (!Directory.Exists(abs)) yield break;
            foreach (var fp in Directory.GetFiles(abs))
            {
                if (fp.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                yield return ToProjectRelative(fp);
            }
        }

        static JObject MakeAssetEntry(string path, string guid)
        {
            bool isFolder = AssetDatabase.IsValidFolder(path);
            var entry = new JObject
            {
                ["path"] = path,
                ["guid"] = guid,
                ["name"] = Path.GetFileName(path),
                ["isFolder"] = isFolder,
            };
            if (!isFolder)
            {
                var t = AssetDatabase.GetMainAssetTypeAtPath(path);
                entry["type"] = t?.FullName;
                try { entry["fileSize"] = new FileInfo(ToAbsolute(path)).Length; } catch { }
            }
            return entry;
        }

        // ---- asset_get ----

        static JToken GetAsset(JObject args)
        {
            string path = ResolvePath(args, out string guid);

            if (string.IsNullOrEmpty(path) || !AssetExists(path))
                throw new ArgumentException("Asset not found");

            bool includeSubAssets = args["includeSubAssets"] == null || (bool)args["includeSubAssets"];
            var subAssetNamePattern = (string)args["subAssetNamePattern"];
            System.Text.RegularExpressions.Regex subAssetRegex = null;
            if (!string.IsNullOrEmpty(subAssetNamePattern))
            {
                try { subAssetRegex = new System.Text.RegularExpressions.Regex(subAssetNamePattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase); }
                catch (ArgumentException e) { throw new ArgumentException("Invalid subAssetNamePattern regex: " + e.Message); }
            }

            var main = AssetDatabase.LoadMainAssetAtPath(path);
            var subs = new JArray();
            int subAssetTotal = 0;
            // Scenes can't be enumerated via LoadAllAssetsAtPath — Unity logs
            // "Do not use ReadObjectThreaded on scene objects!". Skip for SceneAsset.
            if (includeSubAssets && !(main is SceneAsset))
            {
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (o == null || o == main) continue;
                    subAssetTotal++;
                    if (subAssetRegex != null && !subAssetRegex.IsMatch(o.name ?? "")) continue;
                    subs.Add(ObjectRef.ToRef(o));
                }
            }

            var importer = AssetImporter.GetAtPath(path);
            JObject importerRef = importer != null ? ObjectRef.ToRef(importer) : null;

            var labels = main != null ? AssetDatabase.GetLabels(main) : Array.Empty<string>();
            var labelsArr = new JArray(); foreach (var l in labels) labelsArr.Add(l);

            var deps = AssetDatabase.GetDependencies(path, recursive: false);
            var depArr = new JArray();
            foreach (var d in deps) if (d != path) depArr.Add(d);

            return new JObject
            {
                ["path"] = path,
                ["guid"] = guid,
                ["isFolder"] = AssetDatabase.IsValidFolder(path),
                ["mainAsset"] = main != null ? ObjectRef.ToRef(main) : null,
                ["mainType"] = main?.GetType().FullName,
                ["subAssets"] = subs,
                ["subAssetTotal"] = subAssetTotal,
                ["subAssetReturned"] = subs.Count,
                ["importer"] = importerRef,
                ["importerType"] = importer?.GetType().FullName,
                ["labels"] = labelsArr,
                ["assetBundleName"] = importer?.assetBundleName ?? "",
                ["assetBundleVariant"] = importer?.assetBundleVariant ?? "",
                ["userData"] = importer?.userData ?? "",
                ["dependencies"] = depArr,
                ["fileSize"] = SafeFileSize(path)
            };
        }

        static long? SafeFileSize(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return null;
            try { return new FileInfo(ToAbsolute(path)).Length; } catch { return null; }
        }

        // ---- asset_set_metadata ----

        static JToken SetMetadata(JObject args)
        {
            string path = ResolvePath(args, out _);
            if (string.IsNullOrEmpty(path) || !AssetExists(path))
                throw new ArgumentException("Asset not found");

            var changed = new JArray();

            if (args["labels"] is JArray labelsArr)
            {
                var labels = labelsArr.Select(t => (string)t).Where(s => s != null).ToArray();
                var main = AssetDatabase.LoadMainAssetAtPath(path);
                if (main == null) throw new InvalidOperationException("Cannot set labels: failed to load main asset");
                AssetDatabase.SetLabels(main, labels);
                changed.Add("labels");
            }

            var importer = AssetImporter.GetAtPath(path);
            bool importerDirty = false;

            if (args["assetBundleName"] != null || args["assetBundleVariant"] != null)
            {
                if (importer == null) throw new InvalidOperationException("No AssetImporter for this asset (cannot set bundle name)");
                var name = (string)args["assetBundleName"] ?? importer.assetBundleName;
                var variant = (string)args["assetBundleVariant"] ?? importer.assetBundleVariant;
                importer.SetAssetBundleNameAndVariant(name, variant);
                changed.Add("assetBundle");
            }

            if (args["userData"] != null)
            {
                if (importer == null) throw new InvalidOperationException("No AssetImporter for this asset (cannot set userData)");
                importer.userData = (string)args["userData"];
                importerDirty = true;
                changed.Add("userData");
            }

            if (importerDirty)
                AssetDatabase.WriteImportSettingsIfDirty(path);

            AssetDatabase.SaveAssets();

            return new JObject { ["ok"] = true, ["path"] = path, ["changed"] = changed };
        }

        // ---- asset_refresh ----

        static JToken Refresh(JObject args)
        {
            var path = (string)args["path"];
            bool forceUpdate = args["forceUpdate"] != null && (bool)args["forceUpdate"];
            var opts = forceUpdate ? ImportAssetOptions.ForceUpdate : ImportAssetOptions.Default;

            if (!string.IsNullOrEmpty(path))
            {
                if (!AssetExists(path)) throw new ArgumentException("Asset not found: " + path);
                AssetDatabase.WriteImportSettingsIfDirty(path);
                AssetDatabase.ImportAsset(path, opts);
                return new JObject { ["ok"] = true, ["mode"] = "ImportAsset", ["path"] = path };
            }

            AssetDatabase.Refresh(opts);
            return new JObject { ["ok"] = true, ["mode"] = "Refresh" };
        }

        // ---- asset_move ----

        static JToken MoveAsset(JObject args)
        {
            string from = ResolvePath(args, out _);
            if (string.IsNullOrEmpty(from) || !AssetExists(from))
                throw new ArgumentException("Source asset not found");

            var newPath = NormalizePath((string)args["newPath"] ?? "");
            if (string.IsNullOrEmpty(newPath)) throw new ArgumentException("newPath required");

            var validation = AssetDatabase.ValidateMoveAsset(from, newPath);
            if (!string.IsNullOrEmpty(validation))
                throw new InvalidOperationException("Cannot move: " + validation);

            var error = AssetDatabase.MoveAsset(from, newPath);
            if (!string.IsNullOrEmpty(error))
                throw new InvalidOperationException("MoveAsset failed: " + error);

            return new JObject
            {
                ["ok"] = true,
                ["oldPath"] = from,
                ["newPath"] = newPath,
                ["guid"] = AssetDatabase.AssetPathToGUID(newPath)
            };
        }

        // ---- asset_copy ----

        static JToken CopyAsset(JObject args)
        {
            string from = ResolvePath(args, out _);
            if (string.IsNullOrEmpty(from) || !AssetExists(from))
                throw new ArgumentException("Source asset not found");
            if (AssetDatabase.IsValidFolder(from))
                throw new InvalidOperationException("CopyAsset does not support folders; copy individual assets instead");

            var newPath = NormalizePath((string)args["newPath"] ?? "");
            if (string.IsNullOrEmpty(newPath)) throw new ArgumentException("newPath required");

            if (!AssetDatabase.CopyAsset(from, newPath))
                throw new InvalidOperationException($"CopyAsset failed: {from} -> {newPath}");

            return new JObject
            {
                ["ok"] = true,
                ["sourcePath"] = from,
                ["newPath"] = newPath,
                ["guid"] = AssetDatabase.AssetPathToGUID(newPath)
            };
        }

        // ---- asset_create_folder ----

        static JToken CreateFolder(JObject args)
        {
            var parent = NormalizePath((string)args["parent"] ?? "");
            var name = (string)args["name"];
            if (string.IsNullOrEmpty(parent)) throw new ArgumentException("parent required");
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name required");
            if (name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0)
                throw new ArgumentException("name must not contain slashes; nest by chaining create_folder calls");
            if (!AssetDatabase.IsValidFolder(parent))
                throw new ArgumentException($"Parent folder not found: {parent}");

            var newGuid = AssetDatabase.CreateFolder(parent, name);
            if (string.IsNullOrEmpty(newGuid))
                throw new InvalidOperationException($"CreateFolder failed: {parent}/{name}");

            var newPath = AssetDatabase.GUIDToAssetPath(newGuid);
            return new JObject { ["ok"] = true, ["path"] = newPath, ["guid"] = newGuid };
        }

        // ---- asset_create ----

        static JToken CreateAsset(JObject args)
        {
            var path = NormalizePath((string)args["path"] ?? "");
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("path required");
            if (string.IsNullOrEmpty(Path.GetExtension(path)))
                throw new ArgumentException("path must include filename + extension (e.g. .mat, .asset, .anim)");

            var typeName = (string)args["typeName"];
            if (string.IsNullOrEmpty(typeName)) throw new ArgumentException("typeName required");

            var matches = TypeUtil.ResolveAll(typeName);
            matches.RemoveAll(t => !typeof(Object).IsAssignableFrom(t) || t.IsAbstract);
            if (matches.Count == 0)
                throw new ArgumentException($"No concrete UnityEngine.Object type matches '{typeName}'");
            if (matches.Count > 1)
            {
                var names = new JArray();
                foreach (var t in matches) names.Add(t.AssemblyQualifiedName);
                throw new ArgumentException($"Ambiguous type '{typeName}'. Candidates: {string.Join(", ", names)}");
            }
            var type = matches[0];

            if (typeof(GameObject).IsAssignableFrom(type))
                throw new InvalidOperationException("Use asset_create_prefab to create Prefab assets from GameObjects");
            if (AssetDatabase.IsValidFolder(path) || !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)))
                throw new InvalidOperationException("Asset already exists at " + path);

            var parentDir = path.Contains('/') ? path.Substring(0, path.LastIndexOf('/')) : "";
            if (!string.IsNullOrEmpty(parentDir) && !AssetDatabase.IsValidFolder(parentDir))
                throw new ArgumentException($"Parent folder does not exist: {parentDir}");

            Object asset;
            if (typeof(Material).IsAssignableFrom(type))
            {
                var shaderName = (string)args["shaderName"];
                Shader shader = null;
                if (!string.IsNullOrEmpty(shaderName))
                {
                    shader = Shader.Find(shaderName);
                    if (shader == null) throw new ArgumentException($"Shader not found: {shaderName}");
                }
                if (shader == null)
                {
                    var rp = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
                    if (rp != null) shader = rp.defaultShader;
                }
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                if (shader == null) throw new InvalidOperationException("No usable default shader found; pass shaderName explicitly");
                asset = new Material(shader);
            }
            else if (typeof(ScriptableObject).IsAssignableFrom(type))
            {
                asset = ScriptableObject.CreateInstance(type);
                if (asset == null) throw new InvalidOperationException($"ScriptableObject.CreateInstance returned null for {type.FullName}");
            }
            else
            {
                try { asset = (Object)Activator.CreateInstance(type); }
                catch (Exception e)
                {
                    throw new InvalidOperationException(
                        $"Cannot instantiate {type.FullName} from nothing ({e.GetType().Name}: {e.Message}). " +
                        "This asset type likely needs source data (e.g. a texture file) — import it instead.");
                }
            }

            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();

            var loaded = AssetDatabase.LoadMainAssetAtPath(path);
            return ObjectRef.ToRef(loaded ?? asset);
        }

        // ---- asset_create_prefab ----

        static JToken CreatePrefab(JObject args)
        {
            var resolved = ObjectRef.Resolve(args["gameObject"]);
            var go = resolved as GameObject ?? (resolved as Component)?.gameObject;
            if (go == null) throw new ArgumentException("gameObject does not resolve to a GameObject");

            var path = NormalizePath((string)args["path"] ?? "");
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("path required");
            if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Prefab path must end in .prefab");

            var parentDir = path.Contains('/') ? path.Substring(0, path.LastIndexOf('/')) : "";
            if (!string.IsNullOrEmpty(parentDir) && !AssetDatabase.IsValidFolder(parentDir))
                throw new ArgumentException($"Parent folder does not exist: {parentDir}");

            bool connect = args["connectInstance"] != null && (bool)args["connectInstance"];

            GameObject prefab = connect
                ? PrefabUtility.SaveAsPrefabAssetAndConnect(go, path, InteractionMode.AutomatedAction, out bool successConnect)
                : PrefabUtility.SaveAsPrefabAsset(go, path, out bool successPlain);

            if (prefab == null) throw new InvalidOperationException("SaveAsPrefab failed (check console)");
            return ObjectRef.ToRef(prefab);
        }

        // ---- assets_save ----

        static JToken SaveAssets()
        {
            AssetDatabase.SaveAssets();
            return new JObject { ["ok"] = true };
        }

        // ---- assets_find ----

        static JToken FindAssets(JObject args)
        {
            var filter = (string)args["filter"] ?? "";
            var typeName = (string)args["typeName"];
            int limit = args["limit"] != null ? (int)args["limit"] : 1000;

            if (!string.IsNullOrEmpty(typeName))
                filter = $"t:{typeName} {filter}".TrimEnd();

            if (string.IsNullOrEmpty(filter))
                throw new ArgumentException("Provide filter or typeName");

            string[] searchFolders = null;
            if (args["searchInFolders"] is JArray sf && sf.Count > 0)
            {
                var list = new System.Collections.Generic.List<string>(sf.Count);
                foreach (var t in sf)
                {
                    var p = NormalizePath((string)t ?? "");
                    if (string.IsNullOrEmpty(p)) continue;
                    if (!AssetDatabase.IsValidFolder(p)) throw new ArgumentException($"Not a folder: {p}");
                    list.Add(p);
                }
                searchFolders = list.ToArray();
            }

            var guids = searchFolders != null
                ? AssetDatabase.FindAssets(filter, searchFolders)
                : AssetDatabase.FindAssets(filter);

            var entries = new JArray();
            int count = 0;
            foreach (var guid in guids)
            {
                if (count >= limit) break;
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(p)) continue;
                entries.Add(MakeAssetEntry(p, guid));
                count++;
            }

            return new JObject
            {
                ["filter"] = filter,
                ["totalFound"] = guids.Length,
                ["returned"] = count,
                ["entries"] = entries
            };
        }

        // ---- helpers ----

        static string ResolvePath(JObject args, out string guid)
        {
            guid = (string)args["guid"];
            var path = (string)args["path"];
            if (!string.IsNullOrEmpty(path))
            {
                guid = AssetDatabase.AssetPathToGUID(NormalizePath(path));
                return NormalizePath(path);
            }
            if (!string.IsNullOrEmpty(guid))
                return AssetDatabase.GUIDToAssetPath(guid);

            if (args["objectRef"] != null && args["objectRef"].Type != JTokenType.Null)
            {
                var obj = ObjectRef.Resolve(args["objectRef"]);
                if (obj == null) throw new ArgumentException("objectRef does not resolve");
                var p = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(p))
                    throw new ArgumentException("Resolved object is not an asset (it lives in a scene, not on disk)");
                guid = AssetDatabase.AssetPathToGUID(p);
                return p;
            }
            throw new ArgumentException("Provide one of: path, guid, objectRef");
        }

        static bool AssetExists(string path)
        {
            return AssetDatabase.IsValidFolder(path) || !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path));
        }

        static string NormalizePath(string p) => p.Replace('\\', '/').TrimEnd('/');

        static string ToAbsolute(string projectRel)
        {
            var dataPath = Application.dataPath; // .../<project>/Assets
            var projectRoot = dataPath.Substring(0, dataPath.Length - "Assets".Length);
            return Path.Combine(projectRoot, projectRel.Replace('/', Path.DirectorySeparatorChar));
        }

        static string ToProjectRelative(string abs)
        {
            var dataPath = Application.dataPath;
            var projectRoot = dataPath.Substring(0, dataPath.Length - "Assets".Length);
            var p = abs.Replace(Path.DirectorySeparatorChar, '/');
            var root = projectRoot.Replace(Path.DirectorySeparatorChar, '/');
            if (p.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return p.Substring(root.Length);
            return p;
        }
    }
}
