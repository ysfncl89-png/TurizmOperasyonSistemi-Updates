using System.Diagnostics;

namespace TurizmOperasyonSistemi.Services;

internal static class MobileSyncFirewall
{
    private const string RuleName = "TOS Lite Mobile Sync TCP 49321";

    public static bool IsRuleInstalled()
    {
        using var process = Process.Start(new ProcessStartInfo("netsh.exe", $"advfirewall firewall show rule name=\"{RuleName}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
        if (process is null) return false;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0 && output.Contains("49321", StringComparison.Ordinal);
    }

    public static void AddRule()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Uygulama dosya yolu bulunamadı.");
        var arguments = $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow program=\"{executable}\" protocol=TCP localport=49321 remoteip=LocalSubnet profile=any enable=yes";
        using var process = Process.Start(new ProcessStartInfo("netsh.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        });
        if (process is null) throw new InvalidOperationException("Güvenlik duvarı kuralı başlatılamadı.");
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException("Windows güvenlik duvarı kuralı eklenemedi.");
    }
}
