using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using AssemblyUnhollower;
using Gtk;
using Il2CppDumper;

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

            //TODO please remember to change this back before pushing
            const string relativeGameSteamLocation = "steamapps/common/Among_Us2/";
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
                    using var dialog = new MessageDialog(window, DialogFlags.Modal, MessageType.Error,
                        ButtonsType.Close, false, "{0}: {1}\n{2}", e, e.Message, e.StackTrace);
                    dialog.Run();
                    throw;
                }
            };
            window.DeleteEvent += (sender, args) => Application.Quit();
            window.ShowAll();
            Application.Run();
        }

        private static async Task Install(bool server, bool steam, string gameDir, string runDir)
        {
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

                var tempPath = gameDir + "/.temp/";
                (string, string)? asset = null;

                var release = await JsonDocument.ParseAsync(
                    await client.GetStreamAsync("https://api.github.com/repos/BepInEx/BepInEx/releases/latest"));
                var assets = release.RootElement.GetProperty("assets");
                for (var i = 0; i < assets.GetArrayLength(); i++)
                {
                    var assetElement = assets[i];
                    var name = assetElement.GetProperty("name").GetString();
                    if (name == null || !name.Contains("x86")) continue;
                    asset = (assetElement.GetProperty("browser_download_url").GetString()!, name);
                    break;
                }

                if (asset.HasValue)
                {
                    var (url, name) = asset.Value;
                    var path = tempPath + name;
                    if (!File.Exists(path))
                    {
                        Directory.CreateDirectory(tempPath);
                        var response = await client.GetAsync(url);
                        await using var fs = File.OpenWrite(path);
                        await response.Content.CopyToAsync(fs);
                    }

                    ZipFile.ExtractToDirectory(path, gameDir + "/", true);
                    var method = typeof(Config).Assembly.GetType("Il2CppDumper.Program")
                        ?.GetMethod("Main", BindingFlags.Static | BindingFlags.NonPublic, null,
                            new[] {typeof(string[])}, null);
                    if (method != null)
                    {
                        var data = gameDir + "/Among Us_Data/il2cpp_data/";
                        var dump = tempPath + "AssemblyDump/";
                        Directory.CreateDirectory(dump);
                        method.Invoke(null, new object[]
                        {
                            new[]
                            {
                                gameDir + "/GameAssembly.dll",
                                gameDir + "/Among Us_Data/il2cpp_data/Metadata/global-metadata.dat",
                                dump
                            }
                        });

                        AssemblyUnhollower.Program.Main(new UnhollowerOptions
                        {
                            SourceDir = dump,
                            OutputDir = gameDir + "/BepInEx/unhollowed/",
                            //TODO I have no idea where to get this library from
                            MscorlibPath = gameDir + "/mscorlib.dll"
                        });
                    }
                }
                else
                {
                    //TODO tell the user that the x86 file for bepinex wasnt found and make them select it from the prop.Assets
                }
            }
        }
    }
}
