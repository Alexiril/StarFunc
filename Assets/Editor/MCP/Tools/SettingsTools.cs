using System;
using MCP.Protocol;
using MCP.Server;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCP.Tools
{
    [InitializeOnLoad]
    public static class SettingsTools
    {
        const string TagManagerAssetPath = "ProjectSettings/TagManager.asset";
        const int ReservedLayerCount = 8; // 0..7 are Unity built-ins

        static SettingsTools()
        {
            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "tags_list",
                Description = "List all tags defined in TagManager.asset.",
                InputSchema = new JObject { ["type"] = "object", ["properties"] = new JObject() },
                Handler = _ => MainThreadDispatcher.Run(() => ListTags())
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "tags_add",
                Description = "Add a tag to TagManager.asset. Idempotent — re-adding an existing tag returns alreadyExisted=true with no change.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["required"] = new JArray { "name" },
                    ["properties"] = new JObject
                    {
                        ["name"] = new JObject { ["type"] = "string" }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => AddTag(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "tags_remove",
                Description = "Remove a tag from TagManager.asset. GameObjects already tagged with this name will fall back to 'Untagged' implicitly.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["required"] = new JArray { "name" },
                    ["properties"] = new JObject
                    {
                        ["name"] = new JObject { ["type"] = "string" }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => RemoveTag(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "layers_list",
                Description = "List the 32 layer slots from TagManager.asset, returning only non-empty ones with their indices. Indices 0..7 are Unity built-ins (Default, TransparentFX, Ignore Raycast, ...).",
                InputSchema = new JObject { ["type"] = "object", ["properties"] = new JObject() },
                Handler = _ => MainThreadDispatcher.Run(() => ListLayers())
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "layers_set",
                Description = "Set the name of a user layer slot (index 8..31). Indices 0..7 are Unity-reserved and rejected. Pass empty name to clear the slot.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["required"] = new JArray { "index", "name" },
                    ["properties"] = new JObject
                    {
                        ["index"] = new JObject { ["type"] = "integer", ["minimum"] = 8, ["maximum"] = 31 },
                        ["name"] = new JObject { ["type"] = "string" }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => SetLayer(args))
            });
        }

        static SerializedObject OpenTagManager()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(TagManagerAssetPath);
            if (assets == null || assets.Length == 0)
                throw new InvalidOperationException("Could not load TagManager.asset");
            return new SerializedObject(assets[0]);
        }

        // ---- tags ----

        static JToken ListTags()
        {
            var so = OpenTagManager();
            var tagsProp = so.FindProperty("tags");
            var arr = new JArray();
            for (int i = 0; i < tagsProp.arraySize; i++)
                arr.Add(tagsProp.GetArrayElementAtIndex(i).stringValue);
            return new JObject { ["count"] = tagsProp.arraySize, ["tags"] = arr };
        }

        static JToken AddTag(JObject args)
        {
            var name = (string)args["name"];
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name required");

            var so = OpenTagManager();
            var tagsProp = so.FindProperty("tags");
            for (int i = 0; i < tagsProp.arraySize; i++)
                if (tagsProp.GetArrayElementAtIndex(i).stringValue == name)
                    return new JObject { ["ok"] = true, ["name"] = name, ["alreadyExisted"] = true };

            int idx = tagsProp.arraySize;
            tagsProp.InsertArrayElementAtIndex(idx);
            tagsProp.GetArrayElementAtIndex(idx).stringValue = name;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();

            return new JObject { ["ok"] = true, ["name"] = name, ["alreadyExisted"] = false };
        }

        static JToken RemoveTag(JObject args)
        {
            var name = (string)args["name"];
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name required");

            var so = OpenTagManager();
            var tagsProp = so.FindProperty("tags");
            for (int i = 0; i < tagsProp.arraySize; i++)
            {
                if (tagsProp.GetArrayElementAtIndex(i).stringValue == name)
                {
                    tagsProp.DeleteArrayElementAtIndex(i);
                    so.ApplyModifiedPropertiesWithoutUndo();
                    AssetDatabase.SaveAssets();
                    return new JObject { ["ok"] = true, ["name"] = name };
                }
            }
            throw new ArgumentException($"Tag not found: {name}");
        }

        // ---- layers ----

        static JToken ListLayers()
        {
            var so = OpenTagManager();
            var layersProp = so.FindProperty("layers");
            var arr = new JArray();
            for (int i = 0; i < layersProp.arraySize; i++)
            {
                var n = layersProp.GetArrayElementAtIndex(i).stringValue;
                if (string.IsNullOrEmpty(n)) continue;
                arr.Add(new JObject
                {
                    ["index"] = i,
                    ["name"] = n,
                    ["reserved"] = i < ReservedLayerCount
                });
            }
            return new JObject { ["layers"] = arr, ["totalSlots"] = layersProp.arraySize };
        }

        static JToken SetLayer(JObject args)
        {
            int index = (int)args["index"];
            if (index < ReservedLayerCount)
                throw new ArgumentException($"Layer index {index} is Unity-reserved (0..{ReservedLayerCount - 1})");
            if (index > 31)
                throw new ArgumentException($"Layer index {index} exceeds maximum of 31");

            var name = (string)args["name"] ?? "";

            var so = OpenTagManager();
            var layersProp = so.FindProperty("layers");
            if (index >= layersProp.arraySize)
                throw new InvalidOperationException($"TagManager has only {layersProp.arraySize} layer slots");

            var slot = layersProp.GetArrayElementAtIndex(index);
            var previousName = slot.stringValue;
            slot.stringValue = name;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();

            return new JObject
            {
                ["ok"] = true,
                ["index"] = index,
                ["name"] = name,
                ["previousName"] = previousName
            };
        }
    }
}
