using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace AmongUsModLoaderInstaller
{
    internal static class Installer
    {
        internal static async ValueTask Run(bool isLinux, bool server, bool steam, bool findSteamPrefix, string gameDir, string runDir)
        {
            if (server)
            {
                throw new NotImplementedException("Servers are not installable yet, sorry!");
            }
            else
            {
                var wineCommand = "/usr/bin/wine";
                if (isLinux)
                {
                    if (steam)
                    {
                        static IEnumerable<string> GetProtonFolders(string dir)
                        {
                            return from folder in Directory.GetDirectories(dir)
                                where folder.Contains("Proton")
                                select folder;
                        }

                        var protonFolders = GetProtonFolders(runDir + "/steamapps/common/")
                            .Concat(GetProtonFolders(gameDir + "/../"));
                        var highestVersion = protonFolders
                            .OrderByDescending(folder => double.TryParse(folder, out var version) ? version : 0)
                            .FirstOrDefault();
                        if (highestVersion != null)
                        {
                            wineCommand = highestVersion + "/dist/bin/wine";
                        }

                        if (findSteamPrefix) runDir += "/steamapps/compatdata/945360/pfx/";
                    }

                    Process.Start(new ProcessStartInfo(wineCommand,
                        "REG ADD HKEY_CURRENT_USER\\Software\\Wine\\DllOverrides /v winhttp /t REG_SZ /f /d native,builtin")
                    {
                        EnvironmentVariables = {["WINEPREFIX"] = runDir},
                        CreateNoWindow = true
                    });
                }

                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.TryParseAdd("request");

                var tempPath = gameDir + "/.temp/";

                var bepInExZip = tempPath + "BepInEx.zip";
                if (!File.Exists(bepInExZip))
                {
                    Directory.CreateDirectory(tempPath);
                    var response = await client.GetAsync("http://msrandom.net/cdn/BepInEx.zip");
                    await using var fs = File.OpenWrite(bepInExZip);
                    await response.Content.CopyToAsync(fs);
                }

                ZipFile.ExtractToDirectory(bepInExZip, gameDir + "/", true);

                (string, string)? clientAsset = null;
                (string, string)? apiAsset = null;

                var release = await JsonDocument.ParseAsync(
                    await client.GetStreamAsync(
                        "https://api.github.com/repos/AmongUsModLoader/ModLoader/releases/latest"));
                var assets = release.RootElement.GetProperty("assets");
                for (var i = 0; i < assets.GetArrayLength(); i++)
                {
                    var assetElement = assets[i];
                    var name = assetElement.GetProperty("name").GetString();
                    if (name == null) continue;

                    string Url() => assetElement.GetProperty("browser_download_url").GetString()!;

                    if (name.Contains("Client"))
                    {
                        clientAsset = (Url(), name);
                    }
                    else if (name.Contains("Api"))
                    {
                        apiAsset = (Url(), name);
                    }

                    break;
                }

                await DownloadPlugin(apiAsset);
                await DownloadPlugin(clientAsset);

                async ValueTask DownloadPlugin((string, string)? asset)
                {
                    if (!asset.HasValue) return;

                    var (url, name) = asset.Value;
                    var path = tempPath + name;

                    if (!File.Exists(path))
                    {
                        Directory.CreateDirectory(tempPath!);
                        var response = await client!.GetAsync(url);
                        await using var fs = File.OpenWrite(path);
                        await response.Content.CopyToAsync(fs);
                    }

                    File.Copy(path, gameDir + "/BepInEx/plugins/" + name, true);
                }
            }
        }
    }
}