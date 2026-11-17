namespace Asgard.Hermod.Mcp;

/// <summary>数据参数的协议约束，同时用来生成模型发现的 JSON Schema。</summary>
internal sealed record McpArgument(string Name, string DescriptionKey, bool Required = false,
    int MaximumLength = 0, int Minimum = 0, int Maximum = 0, string[]? Choices = null, int MaximumItems = 0, int[]? IntegerChoices = null)
{
    internal object Schema => MaximumItems > 0
        ? new
        {
            type = "array",
            description = Text.Get(DescriptionKey),
            maxItems = MaximumItems,
            items = new { type = "string", minLength = 1, maxLength = MaximumLength }
        }
        : MaximumLength > 0
        ? new { type = "string", description = Text.Get(DescriptionKey), maxLength = MaximumLength, @enum = Choices }
        : new { type = "integer", description = Text.Get(DescriptionKey), minimum = Minimum, maximum = Maximum, @enum = IntegerChoices };
}
