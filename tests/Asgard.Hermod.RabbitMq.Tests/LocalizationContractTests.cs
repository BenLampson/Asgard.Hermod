using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Asgard.Hermod.RabbitMq.Tests;

public sealed class LocalizationContractTests
{
    [Fact]
    public void NewResources_HaveBothLanguages_AndMatchingPlaceholders()
    {
        string root = FindRoot(); var english = Read("Strings.resx"); var chinese = Read("Strings.zh-Hans.resx");
        foreach (string key in english.Keys.Where(key => key.StartsWith("RabbitMq", StringComparison.Ordinal)))
        {
            Assert.True(chinese.ContainsKey(key), key); Assert.False(string.IsNullOrWhiteSpace(english[key]), key); Assert.False(string.IsNullOrWhiteSpace(chinese[key]), key);
            Assert.Equal(Placeholders(english[key]), Placeholders(chinese[key]));
        }
        foreach (string file in new[] { "Asgard.Hermod.RabbitMq", "Asgard.Hermod.RabbitMq.UI" }.SelectMany(module => Directory.EnumerateFiles(Path.Combine(root, "src", module), "*", SearchOption.AllDirectories))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal) && (path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".xaml", StringComparison.Ordinal))))
        {
            string source = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(source, "(?:Text\\.(?:Get|Format|Exception)\\(\"|loc:Loc )(?<key>RabbitMq[A-Za-z0-9_]+)(?:\"|\\})"))
                if (match.Groups["key"].Value != "RabbitMqChoice") Assert.True(english.ContainsKey(match.Groups["key"].Value), match.Groups["key"].Value + " in " + Path.GetFileName(file));
        }
        foreach (var operation in Enum.GetValues<RabbitMqOperation>()) Assert.True(english.ContainsKey("RabbitMq" + operation));
        Dictionary<string, string> Read(string name)
        {
            var nodes = XDocument.Load(Path.Combine(root, "src", "Asgard.Hermod.Abstractions", "Localization", name)).Root!.Elements("data").ToArray();
            Assert.Equal(nodes.Length, nodes.Select(node => node.Attribute("name")!.Value).Distinct().Count());
            return nodes.ToDictionary(node => node.Attribute("name")!.Value, node => node.Element("value")!.Value);
        }
    }
    private static string[] Placeholders(string value) => Regex.Matches(value, "\\{[0-9]+[^}]*\\}").Select(match => match.Value).Order().ToArray();
    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Asgard.Hermod.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }
}
