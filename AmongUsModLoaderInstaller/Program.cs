using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Gtk;

namespace AmongUsModLoaderInstaller
{
    internal static class Program
    {
        private static readonly bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        
        private static void Main()
        {
            var stream = typeof(Program).Assembly.GetManifestResourceStream("AmongUsModLoaderInstaller.Window.glade");
            if (stream == null) return;
            using var reader = new StreamReader(stream);
            Application.Init();
            var builder = new Builder();
            builder.AddFromString(reader.ReadToEnd());
            var window = Get<ApplicationWindow>("main_container");
            var typeSelector = Get<ComboBox>("type_selector");
            var path = Get<FileChooserButton>("path");
            var pathText = Get<Label>("path_label");
            var prefixPath = Get<FileChooserButton>("wine_prefix");
            var prefixText1 = Get<Label>("wine_prefix_label1");
            var prefixText2 = Get<Label>("wine_prefix_label2");
            var installButton = Get<Button>("install_button");
            var clientPathText = pathText.Text;
            var winePrefix1 = prefixText1.Text;
            var winePrefix2 = prefixText2.Text;
            var server = false;

            T Get<T>(string name) where T : Widget => (T) builder.GetObject(name);

            void SetText(bool steam)
            {
                if (steam)
                {
                    prefixText1.Text = "";
                    prefixText2.Text = "Steam Directory";
                }
                else
                {
                    prefixText1.Text = winePrefix1;
                    prefixText2.Text = winePrefix2;
                }
            }

            const string relativeGameSteamLocation = "steamapps/common/Among Us/";
            var steamCheck = Get<CheckButton>("steam_check");
            steamCheck.Active = true;
            
            ToggleHandler(window, EventArgs.Empty);
            steamCheck.Toggled += ToggleHandler;
            prefixPath.CurrentFolderChanged += (sender, args) =>
            {
                if (steamCheck.Active)
                {
                    path.SetCurrentFolder(prefixPath.CurrentFolder + "/" + relativeGameSteamLocation);
                }
            };

            typeSelector.Changed += (sender, args) =>
            {
                if (!typeSelector.GetActiveIter(out var iter)) return;
                var value = (string) typeSelector.Model.GetValue(iter, 0);
                if (value == "Server")
                {
                    server = true;
                    pathText.Text = "Installation Directory";
                    prefixPath.Hide();
                    prefixText1.Hide();
                    prefixText2.Hide();
                    steamCheck.Hide();
                }
                else
                {
                    server = false;
                    pathText.Text = clientPathText;
                    steamCheck.Show();
                    if (!steamCheck.Active && !IsLinux) return;
                    prefixPath.Show();
                    prefixText1.Show();
                    prefixText2.Show();
                }
            };
            
            void ToggleHandler(object? sender, EventArgs args)
            {
                if (steamCheck.Active)
                {
                    var steam = IsLinux
                        ? Environment.GetEnvironmentVariable("HOME") + "/.local/share/Steam/"
                        : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) + "/Steam/";
                    path.SetCurrentFolder(steam + relativeGameSteamLocation);

                    if (IsLinux)
                    {
                        prefixText1.Show();
                        prefixText2.Show();
                        SetText(true);
                    }
                    else
                    {
                        prefixText1.Hide();
                        prefixText2.Hide();
                        prefixPath.Hide();
                    }
                    prefixPath.SetCurrentFolder(steam);
                }
                else
                {
                    path.UnselectAll();
                    path.Show();
                    pathText.Show();

                    if (IsLinux)
                    {
                        SetText(false);
                        prefixText1.Show();
                        prefixText2.Show();
                        prefixPath.Show();
                        prefixPath.SetCurrentFolder(Environment.GetEnvironmentVariable("HOME") + "/.wine/");
                    }
                    else
                    {
                        prefixText1.Hide();
                        prefixText2.Hide();
                        prefixPath.Hide();
                    }
                }
            }

            installButton.Clicked += async (sender, args) =>
            {
                try
                {
                    await Install(server, steamCheck.Active, path.CurrentFolder, prefixPath.CurrentFolder);
                }
                catch (Exception e)
                {
                    using var dialog = new MessageDialog(window, DialogFlags.Modal, MessageType.Error, ButtonsType.Close, "{0}", e.Message);
                    dialog.Run();
                }
            };
            window.DeleteEvent += (sender, args) => Application.Quit();
            window.ShowAll();
            Application.Run();
        }

        private static async Task Install(bool server, bool steam, string gameDir, string runDir)
        {
            Console.WriteLine(gameDir);
            if (server)
            {
                throw new NotImplementedException("Servers are not installable yet, sorry!");
            }
            else
            {
                /*if (IsLinux)
                {
                    if (steam) runDir += "/steamapps/compatdata/945360/pfx/";
                    
                    //TODO this doesn't check if they key already exists
                    Process.Start(new ProcessStartInfo("/usr/bin/wine",
                        "REG ADD HKEY_CURRENT_USER\\Software\\Wine\\DllOverrides /v winhttp /t REG_SZ /d native,builtin")
                    {
                        EnvironmentVariables = {["WINEPREFIX"] = runDir},
                        CreateNoWindow = true
                    });
                }*/

                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.TryParseAdd("request");

                var url = JsonSerializer
                    .Deserialize<GithubRelease>(
                        await client.GetStringAsync("https://api.github.com/repos/BepInEx/BepInEx/releases/latest"))
                    ?.Url;

                var prop = JsonSerializer.Deserialize<AssetsProp>(await client.GetStringAsync(url));

                var x86Url = (from downloadableProp in prop.Assets where downloadableProp.Name.Contains("x86") select downloadableProp.Url).FirstOrDefault();

                if (!gameDir.EndsWith("/")) gameDir += "/";

                string zip = gameDir + "BepInEx.zip";

                if (x86Url == null)
                {
                    //TODO tell the user that the x86 file for bepinex wasnt found and make them select it from the prop.Assets
                }
                else
                {
                    var response = await client.GetAsync(x86Url);
                    await using (var fs = File.OpenWrite(zip))
                    {
                        await response.Content.CopyToAsync(fs);
                    }
                    
                    ZipFile.ExtractToDirectory(zip, gameDir + "/");
                    File.Delete(zip);
                }
            }
        }

        private class GithubRelease
        {
            [JsonPropertyName("url")]
            public string Url { get; set; }
        }
        
        private class AssetsProp
        {
            [JsonPropertyName("assets")]
            public DownloadableProp[] Assets { get; set; }
        }
        
        private class DownloadableProp
        {
            [JsonPropertyName("browser_download_url")]
            public string Url { get; set; }
            
            [JsonPropertyName("name")]
            public string Name { get; set; }
        }
    }
}
