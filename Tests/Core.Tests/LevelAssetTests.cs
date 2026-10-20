using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using YamlDotNet.RepresentationModel;

namespace UnityCursory.Core.Tests
{
    /// <summary>
    /// The committed level ScriptableObjects (<c>Assets/UnityCursory/Levels/L01..L14.asset</c>) and the level
    /// library are read straight from their Unity YAML and checked against <see cref="LevelSeeds"/>, so the
    /// assets the game loads can't drift from the ported levels. If this fails after a seed change, run the
    /// editor's <c>UnityCursory/Write Level Assets</c> menu and commit the assets.
    /// </summary>
    public class LevelAssetTests
    {
        private static readonly string AssetsRoot = FindAssetsRoot();
        private static string LevelsFolder => Path.Combine(AssetsRoot, "UnityCursory", "Levels");
        private static string LibraryPath => Path.Combine(AssetsRoot, "UnityCursory", "Resources", "UnityCursory", "LevelLibrary.asset");

        private static IEnumerable<int> Numbers() => Enumerable.Range(1, LevelSeeds.Count);

        [TestCaseSource(nameof(Numbers))]
        public void Level_asset_matches_its_seed(int number)
        {
            var path = Path.Combine(LevelsFolder, $"L{number:00}.asset");
            Assert.That(File.Exists(path), Is.True, $"{path} is missing");
            var body = ReadAsset(path);

            Assert.That(Scalar(body, "m_Name"), Is.EqualTo($"L{number:00}"));
            Assert.That(ScriptGuid(body), Is.EqualTo(MetaGuid(Path.Combine(AssetsRoot, "UnityCursory", "Runtime", "Levels", "LevelAsset.cs"))),
                "the asset is not a LevelAsset");

            var data = (LevelData)Read(typeof(LevelData), body.Children[new YamlScalarNode("Data")]);
            Assert.That(Dump(data), Is.EqualTo(Dump(LevelSeeds.Level(number))));
            Assert.That(data.Validate(), Is.Empty);
        }

        [Test]
        public void Library_lists_the_fourteen_levels_in_order()
        {
            var body = ReadAsset(LibraryPath);
            Assert.That(ScriptGuid(body), Is.EqualTo(MetaGuid(Path.Combine(AssetsRoot, "UnityCursory", "Runtime", "Levels", "LevelLibrary.cs"))));
            var listed = ((YamlSequenceNode)body.Children[new YamlScalarNode("Levels")]).Children
                .Select(n => ((YamlScalarNode)((YamlMappingNode)n).Children[new YamlScalarNode("guid")]).Value)
                .ToList();
            var expected = Numbers().Select(n => MetaGuid(Path.Combine(LevelsFolder, $"L{n:00}.asset"))).ToList();
            Assert.That(listed, Is.EqualTo(expected));
        }

        [Test]
        public void No_stray_level_assets()
        {
            var assets = Directory.GetFiles(LevelsFolder, "*.asset").Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal);
            Assert.That(assets, Is.EqualTo(Numbers().Select(n => $"L{n:00}.asset")));
        }

        // ── Unity YAML reading ───────────────────────────────────────────────

        private static YamlMappingNode ReadAsset(string path)
        {
            var stream = new YamlStream();
            using (var reader = new StringReader(File.ReadAllText(path)))
                stream.Load(reader);
            var root = (YamlMappingNode)stream.Documents.Single().RootNode;
            return (YamlMappingNode)root.Children[new YamlScalarNode("MonoBehaviour")];
        }

        private static string Scalar(YamlMappingNode node, string key) => ((YamlScalarNode)node.Children[new YamlScalarNode(key)]).Value;

        private static string ScriptGuid(YamlMappingNode body) =>
            Scalar((YamlMappingNode)body.Children[new YamlScalarNode("m_Script")], "guid");

        private static string MetaGuid(string assetPath)
        {
            var line = File.ReadAllLines(assetPath + ".meta").Single(l => l.StartsWith("guid:", StringComparison.Ordinal));
            return line.Substring("guid:".Length).Trim();
        }

        /// <summary>Builds a level data value from YAML the way Unity's serializer reads it: public fields by name.</summary>
        private static object Read(Type type, YamlNode node)
        {
            if (type == typeof(string)) return ((YamlScalarNode)node).Value ?? "";
            if (type == typeof(float)) return float.Parse(((YamlScalarNode)node).Value, CultureInfo.InvariantCulture);
            if (type == typeof(int)) return int.Parse(((YamlScalarNode)node).Value, CultureInfo.InvariantCulture);
            if (type.IsEnum) return Enum.ToObject(type, int.Parse(((YamlScalarNode)node).Value, CultureInfo.InvariantCulture));
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                var list = (IList)Activator.CreateInstance(type);
                foreach (var item in ((YamlSequenceNode)node).Children) list.Add(Read(type.GetGenericArguments()[0], item));
                return list;
            }

            var map = (YamlMappingNode)node;
            var value = Activator.CreateInstance(type);
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.That(map.Children.ContainsKey(new YamlScalarNode(field.Name)), Is.True, $"{type.Name}.{field.Name} is not in the asset");
                field.SetValue(value, Read(field.FieldType, map.Children[new YamlScalarNode(field.Name)]));
            }
            return value;
        }

        /// <summary>A readable dump of every public field, so a mismatch shows exactly where it is.</summary>
        private static string Dump(object value)
        {
            var sb = new StringBuilder();
            Dump(value, "", sb);
            return sb.ToString();
        }

        private static void Dump(object value, string path, StringBuilder sb)
        {
            switch (value)
            {
                case null:
                    sb.Append(path).Append(" = null\n");
                    return;
                case string s:
                    sb.Append(path).Append(" = \"").Append(s).Append("\"\n");
                    return;
                case float f:
                    sb.Append(path).Append(" = ").Append(f.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
                    return;
                case int or Enum:
                    sb.Append(path).Append(" = ").Append(value).Append('\n');
                    return;
                case IList list:
                    sb.Append(path).Append(".Count = ").Append(list.Count).Append('\n');
                    for (var i = 0; i < list.Count; i++) Dump(list[i], $"{path}[{i}]", sb);
                    return;
            }
            foreach (var field in value.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                Dump(field.GetValue(value), path.Length == 0 ? field.Name : $"{path}.{field.Name}", sb);
        }

        private static string FindAssetsRoot()
        {
            for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
            {
                var assets = Path.Combine(dir.FullName, "Assets");
                if (Directory.Exists(Path.Combine(assets, "UnityCursory"))) return assets;
            }
            throw new DirectoryNotFoundException("could not find the Unity project's Assets folder above the test output");
        }
    }
}
