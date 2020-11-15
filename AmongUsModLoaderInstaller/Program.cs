using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace AmongUsModLoaderInstaller
{
    internal static class Program
    {
        private static readonly bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        
        private static async Task<int> Main(string[] args)
        {
            if (args.Length == 0)
            {
                GuiHandler.RunGui(IsLinux);
            }
            else
            {
                Dictionary<string, string> options = new Dictionary<string, string>();
                foreach (var argument in args)
                {
                    if (argument.Contains("="))
                    {
                        var index = argument.IndexOf("=", StringComparison.Ordinal);
                        options[argument.Substring(0, index).ToLower()] = argument.Substring(index + 1);
                    }
                    else
                    {
                        await Console.Error.WriteLineAsync($"Argument {argument} is invalid. The format for arguments is key=value.");
                        return 1;
                    }
                }

                var server = options.ContainsKey("server") && bool.TryParse(options["server"], out var isServer) && isServer;
                var steamPath = options.ContainsKey("steam_path") ? options["steam_path"] : null;
                var steamOptionSpecified = options.ContainsKey("steam");
                var steam = steamPath != null || !steamOptionSpecified || bool.TryParse(options["steam"], out var isSteam) && isSteam;

                if (server && steamOptionSpecified && steam)
                {
                    await Console.Error.WriteLineAsync("Options 'steam' and 'server' can not be both true.");
                    return 1;
                }

                if (steamPath == null && steam)
                {
                    steamPath = IsLinux
                        ? Environment.GetEnvironmentVariable("HOME") + "/.local/share/Steam/"
                        : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) + "/Steam/";
                }
                
                var directorySpecified = options.ContainsKey("dir");
                string directory;

                if (directorySpecified)
                {
                    directory = options["dir"];
                }
                else
                {
                    if (server || !steam)
                    {
                        await Console.Error.WriteLineAsync(
                            "Option 'dir' must be specified when server=true or steam=false.");
                        return 1;
                    }

                    directory = steamPath + "/steamapps/common/Among Us/";
                }

                var findSteamPrefix = true;
                string runDir = "";
                if (!server)
                {
                    if (steamPath != null)
                    {
                        runDir = steamPath;
                    }
                    else
                    {
                        if (options.ContainsKey("wine_prefix"))
                        {
                            runDir = options["wine_prefix"];
                            if (steam)
                            {
                                findSteamPrefix = false;
                            }
                        }
                    }

                    if (runDir.Length == 0)
                    {
                        await Console.Error.WriteLineAsync(steam ? "Neither steam_path nor wine_prefix was specified." : "wine_prefix was not specified");
                        return 1;
                    }
                }
                
                await Installer.Run(IsLinux, server, steam, findSteamPrefix, directory, runDir);
                await Task.Delay(-1);
            }
            
            return 0;
        }
    }
}
