// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using PalTimer.RuntimeLauncher;
static class LauncherTests
{
    static int count;
    static void Check(bool ok, string label) { if (!ok) throw new Exception(label); count++; Console.WriteLine("PASS " + label); }
    static void Main()
    {
        var sdl = new RuntimeCandidate { Pid = 11, Path = @"C:\sdl\renamed.exe", Kind = RuntimeKind.SDL };
        var original = new RuntimeCandidate { Pid = 22, Path = @"C:\original\PAL.exe", Kind = RuntimeKind.Original };
        var unknown = new RuntimeCandidate { Pid = 33, Path = @"C:\other.exe", Kind = RuntimeKind.Unknown };
        Check(RuntimeSelection.Select(new[] { sdl }, 0, original).core == "SdlpalGeshi", "live SDL overrides an original installation");
        Check(RuntimeSelection.Select(new[] { original }, 0, sdl).core == "Pal98Dx9Automatic", "live original overrides an SDL installation");
        Check(RuntimeSelection.Select(new[] { sdl, original }, 11, original).pid == 11, "foreground SDL wins");
        Check(RuntimeSelection.Select(new[] { sdl, original }, 22, sdl).pid == 22, "foreground original wins");
        Check(RuntimeSelection.Select(new[] { sdl, original }, 0, sdl).pid == 11, "both live: installed running game breaks a focus tie");
        Check(RuntimeSelection.Select(new[] { unknown }, 33, sdl).reason == "installed-game", "unknown foreground is not interpreted as a game");
        Check(RuntimeSelection.Select(new RuntimeCandidate[0], 0, original).core == "Pal98Dx9Automatic", "offline original installation default");
        Check(RuntimeSelection.Select(new RuntimeCandidate[0], 0, sdl).core == "SdlpalGeshi", "offline SDL installation default");
        Check(RuntimeSelection.Select(new[] { sdl, new RuntimeCandidate { Pid = 44, Path = "second.exe", Kind = RuntimeKind.SDL } }, 0, null).core == "SdlpalGeshi", "multiple processes of one runtime agree");
        foreach (var live in new[] { new RuntimeCandidate[0], new[] { sdl, original } }) {
            bool rejected = false; try { RuntimeSelection.Select(live, 0, null); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "missing or ambiguous evidence is rejected");
        }
        foreach (string text in new[] { "", "hello", "中文 空格", @"C:\a b\", "x\"y", "a\\\\\"b", "`$()&%" })
            Check(Unquote(RuntimeSelection.Quote(text)) == text, "argument preserves " + text);
        Check(RuntimeSelection.ImageKind(@"C:\does-not-exist.exe") == RuntimeKind.Unknown, "missing image is unknown");
        Console.WriteLine("PASS launcher total=" + count + "; game/process/network=none");
    }
    // Independent command-line inverse: paired backslashes precede a literal quote.
    static string Unquote(string command)
    {
        var result = new System.Text.StringBuilder();
        for (int i = 1; i < command.Length - 1;) {
            int n = 0; while (command[i] == '\\') { n++; i++; }
            if (command[i] == '"') {
                result.Append('\\', n / 2); if (n % 2 != 0) result.Append('"'); i++;
            } else { result.Append('\\', n).Append(command[i++]); }
        }
        return result.ToString();
    }
}
