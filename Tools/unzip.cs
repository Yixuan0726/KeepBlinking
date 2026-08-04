using System;
using System.Diagnostics;

public class UnzipShim {
    public static int Main(string[] args) {
        if (args.Length < 2) return 2;
        string tarArgs;
        if (args[0] == "-Z1") {
            tarArgs = "-tf \"" + args[1] + "\"";
        } else if (args[0] == "-p" && args.Length >= 3) {
            tarArgs = "-xOf \"" + args[1] + "\" \"" + args[2] + "\"";
        } else {
            return 2;
        }
        var psi = new ProcessStartInfo("tar.exe", tarArgs);
        psi.UseShellExecute = false;
        var p = Process.Start(psi);
        p.WaitForExit();
        return p.ExitCode;
    }
}
