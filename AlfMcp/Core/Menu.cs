using System.Diagnostics;

namespace AlfMcp.Core;

// Elle çalıştırılınca açılan çok basit menü (MCP istemcisi başlattığında kullanılmaz).
public static class Menu {

    private static readonly string[] Keys = ["js.timeout"];

    public static void Run(Store store, ClaudeDesktop claude, Codex codex) {
        while(true) {
            var installed = claude.IsInstalled();
            var codexInstalled = Attempt(codex.IsInstalled);
            var choice = Choose("Alf MCP", [installed ? "Unsetup Claude" : "Setup Claude", "Restart Claude", codexInstalled ? "Unsetup Codex" : "Setup Codex", "Restart Codex", "Open folder", "Configuration", "Exit"]);
            switch(choice) {
                case 0:
                    Attempt(() => {
                        if(installed) {
                            claude.Remove();
                            return "Removed from Claude Desktop.";
                        }
                        claude.Install();
                        return "Added to Claude Desktop. Restart it to use Alf MCP.";
                    });
                    break;
                case 1:
                    Attempt(claude.Restart);
                    break;
                case 2:
                    Attempt(() => {
                        if(codexInstalled) {
                            codex.Remove();
                            return "Removed from Codex.";
                        }
                        codex.Install();
                        return "Added to Codex. Restart Codex to use Alf MCP.";
                    });
                    break;
                case 3:
                    Attempt(() => {
                        var running = codex.RunningCount();
                        if(running == 0) {
                            return codex.Stop();
                        }
                        Console.Write($"This stops {running} running Codex process(es), including open sessions. Continue? (y/N) ");
                        return Console.ReadKey(false).KeyChar is 'y' or 'Y' ? Environment.NewLine + codex.Stop() : Environment.NewLine + "Cancelled.";
                    });
                    break;
                case 4:
                    Attempt(() => {
                        Directory.CreateDirectory(store.Home);
                        Process.Start("explorer.exe", store.Home);
                        return store.Home;
                    });
                    break;
                case 5:
                    Configure(store);
                    break;
                default:
                    return;
            }
        }
    }

    private static void Configure(Store store) {
        while(true) {
            var options = Keys.Select(key => $"{key} = {store.GetSetting(key) ?? "(not set)"}").Append("Back").ToArray();
            var choice = Choose("Configuration", options);
            if(choice < 0 || choice == Keys.Length) {
                return;
            }

            var selected = Keys[choice];
            Console.Write($"{selected} (empty = cancel): ");
            var value = Console.ReadLine();
            if(!string.IsNullOrWhiteSpace(value)) {
                store.SetSetting(selected, value.Trim());
            }
        }
    }

    // Yukarı/aşağı ok ve Enter ile seçer; 1-9 tuşları doğrudan seçer, Esc -1 döndürür.
    private static int Choose(string title, string[] options) {
        var selected = 0;
        while(true) {
            Clear();
            Console.WriteLine(title);
            Console.WriteLine();
            for(var index = 0; index < options.Length; index++) {
                if(index == selected) {
                    Console.BackgroundColor = ConsoleColor.Gray;
                    Console.ForegroundColor = ConsoleColor.Black;
                }
                Console.WriteLine($" {index + 1}) {options[index]} ");
                Console.ResetColor();
            }

            var key = Console.ReadKey(true);
            if(key.Key == ConsoleKey.UpArrow) {
                selected = (selected + options.Length - 1) % options.Length;
            }
            else if(key.Key == ConsoleKey.DownArrow) {
                selected = (selected + 1) % options.Length;
            }
            else if(key.Key == ConsoleKey.Enter) {
                return selected;
            }
            else if(key.Key == ConsoleKey.Escape) {
                return -1;
            }
            else if(key.KeyChar >= '1' && key.KeyChar <= '9' && key.KeyChar - '1' < options.Length) {
                return key.KeyChar - '1';
            }
        }
    }

    // VS Code ve Windows Terminal'de Console.Clear kaydırma geçmişini silmez; ANSI ile onu da temizler.
    private static void Clear() {
        Console.Clear();
        var supportsAnsi = Environment.GetEnvironmentVariable("WT_SESSION") is not null || Environment.GetEnvironmentVariable("TERM_PROGRAM") is not null;
        if(supportsAnsi) {
            Console.Write("\u001b[3J");
        }
    }

    // Menü çizilirken hata çıkarsa (ör. Codex kurulu değil) seçenek "kurulu değil" sayılır.
    private static bool Attempt(Func<bool> check) {
        try {
            return check();
        }
        catch(InvalidOperationException) {
            return false;
        }
    }

    private static void Attempt(Func<string> work) {
        Console.WriteLine();
        var waitSeconds = 2;
        try {
            Console.WriteLine(work());
        }
        catch(Exception exception) {
            Console.WriteLine("Error: " + exception.Message);
            waitSeconds = 5;
        }
        Thread.Sleep(waitSeconds * 1000);
    }

}
