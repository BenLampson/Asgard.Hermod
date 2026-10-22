using System.Globalization;
using System.Text;

namespace Asgard.Hermod.Ssh;

/// <summary>在专用、已选机的 PTY 中执行一次命令；编码隔离终端回显、换行和 Shell 提示符。</summary>
internal sealed class SshBastionCommand
{
    private const int MaximumOutput = 65536;
    private readonly string _id = Guid.NewGuid().ToString("N");
    internal string Begin => "__HERMOD_" + _id + "_BEGIN";
    internal string End => "__HERMOD_" + _id + "_END";
    internal string Status => "__HERMOD_" + _id + "_STATUS:";

    /// <summary>每个源代码行都短于 PTY 规范模式的行限制；原命令不会被外层 Shell 展开。</summary>
    internal string Script(string command)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(command);
        if (bytes.Length > MaximumOutput || command.Contains('\0')) throw Failure();
        string encoded = Convert.ToBase64String(bytes);
        var chunks = new List<string>();
        for (int i = 0; i < encoded.Length; i += 256) chunks.Add(encoded.Substring(i, Math.Min(256, encoded.Length - i)));
        string script = "stty -echo || exit 125\ncommand -v base64 >/dev/null 2>&1 || exit 125\n" +
            "printf '\\n%s%s\\n' '__HERMOD_' '" + _id + "_BEGIN'\n{\n" +
            "sh -c \"$(base64 -d <<'HERMOD_INPUT'\n" + string.Join('\n', chunks) + "\nHERMOD_INPUT\n)\" </dev/null 2>&1\n" +
            "__hermod_code=$?\nprintf '\\n%s%s:%s\\n' '__HERMOD_' '" + _id + "_STATUS' \"$__hermod_code\"\n" +
            "} | base64\nprintf '\\n%s%s\\n' '__HERMOD_' '" + _id + "_END'\n";
        // 标记在命令源里拆成两段，回显不能伪装成已执行的开始/结束帧。
        return "exec sh -c '" + script.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'\r";
    }

    /// <summary>结果只在收到完整结束帧后成功；取消或超限由调用方关闭专用通道，绝不重放命令。</summary>
    internal async Task<SshCommandResult> ExecuteAsync(ISshConsoleConnection console, string command, CancellationToken cancellationToken)
    {
        await console.WriteAsync(Encoding.UTF8.GetBytes(Script(command)), cancellationToken).ConfigureAwait(false);
        var received = new StringBuilder(); byte[] buffer = new byte[8192]; bool started = false;
        while (true)
        {
            int count = await console.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0) throw Failure();
            received.Append(Encoding.UTF8.GetString(buffer, 0, count));
            string text = received.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
            if (!started)
            {
                int begin = text.IndexOf("\n" + Begin + "\n", StringComparison.Ordinal);
                if (begin < 0) { if (received.Length > 524288) throw Failure(); continue; }
                text = text[(begin + Begin.Length + 2)..]; started = true;
            }
            int end = text.IndexOf("\n" + End + "\n", StringComparison.Ordinal);
            if (end >= 0) return Decode(text[..end], false);
            if (text.Length > 100000) return Decode(text, true);
            received.Clear(); received.Append(text);
        }
    }

    private SshCommandResult Decode(string encoded, bool truncated)
    {
        string compact = string.Concat(encoded.Where(c => !char.IsWhiteSpace(c)));
        if (truncated) compact = compact[..(compact.Length / 4 * 4)];
        byte[] bytes;
        try { bytes = Convert.FromBase64String(compact); }
        catch (FormatException) { throw Failure(); }
        if (truncated || bytes.Length > MaximumOutput + Status.Length + 16)
            return new(Encoding.UTF8.GetString(bytes.AsSpan(0, Math.Min(bytes.Length, MaximumOutput))), -1, true);
        string output = Encoding.UTF8.GetString(bytes);
        int status = output.LastIndexOf("\n" + Status, StringComparison.Ordinal);
        if (status < 0 || !output.EndsWith('\n') || !int.TryParse(output.AsSpan(status + Status.Length + 1).Trim(),
            NumberStyles.None, CultureInfo.InvariantCulture, out int code) || code is < 0 or > 255) throw Failure();
        string result = output[..status];
        return Encoding.UTF8.GetByteCount(result) > MaximumOutput
            ? new(Encoding.UTF8.GetString(bytes.AsSpan(0, MaximumOutput)), -1, true) : new(result, code, false);
    }

    private static SshException Failure() => new("SshBastionCommandFailed", "SSH_BASTION_COMMAND_FAILED");
}
