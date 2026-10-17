namespace Asgard.Hermod.UI.Controls;

/// <summary>公共编辑器只负责键盘交互，元数据加载和候选语义由模块实现。</summary>
public interface ICodeCompletionProvider
{
    /// <summary>返回有界候选；取消不会改动文稿或执行文稿中的命令。</summary>
    Task<IReadOnlyList<CodeCompletionItem>> CompleteAsync(CodeCompletionRequest request, CancellationToken token);
}
