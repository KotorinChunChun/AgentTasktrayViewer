namespace AgentTasktrayViewer;

internal sealed record LaunchOptions(bool Hidden, string? SettingsDirectory, bool Passive)
{
    public static LaunchOptions Parse(string[] args)
    {
        bool hidden = false, passive = false;
        string? directory = null;
        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--startup": case "--hidden": hidden = true; break;
                case "--passive": passive = true; break;
                case "--settings-dir":
                    if (++index >= args.Length || args[index].StartsWith("--"))
                        throw new ArgumentException("--settings-dirに保存先を指定してください。");
                    directory = Path.GetFullPath(args[index]);
                    break;
                case "--launch-origin":
                    if (++index >= args.Length || args[index] is not ("standalone" or "host_manual" or "direct_task" or "via_kotorintoys"))
                        throw new ArgumentException("--launch-originの起動経路が不正です。");
                    break;
                default: throw new ArgumentException("未対応の引数です：" + args[index]);
            }
        }
        return new(hidden, directory, passive);
    }
}
