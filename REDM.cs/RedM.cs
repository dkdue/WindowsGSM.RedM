using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using WindowsGSM.Functions;
using WindowsGSM.GameServer.Query;
using System;
using System.Linq;
using System.IO.Compression;

namespace WindowsGSM.Plugins
{
    public class REDM 
    {
        // - Plugin Details
        public Plugin Plugin = new Plugin
        {
            name = "WindowsGSM.RedM", // WindowsGSM.XXXX
            author = "kessef",
            description = "WindowsGSM plugin for supporting Red Dead Redemption 2 Dedicated Server",
            version = "1.0",
            url = "https://github.com/dkdue/WindowsGSM.RedM", // Github repository link (Best practice)
            color = "#34c9eb" // Color Hex
        };



        // - Standard Constructor and properties

        public REDM(ServerConfig serverData) => _serverData = serverData;
        private readonly ServerConfig _serverData;
        public string Error, Notice;


        // - Game server Fixed variables
        public string StartPath => @"server\FXServer.exe"; // Game server start path
        public string FullName = "Red Dead Redemption 2 Dedicated Server"; // Game server FullName
        public bool AllowsEmbedConsole = true;  // Does this server support output redirect?
        public int PortIncrements = 1; // This tells WindowsGSM how many ports should skip after installation
        public object QueryMethod = new FIVEM(); // Query method should be use on current server type. Accepted value: null or new A2S() or new FIVEM() or new UT3()


        // - Game server default values
        public string Port = "30120"; // Default port
        public string QueryPort = "30120"; // Default query port
        public string Defaultmap = "redm-map-one"; // Default map name
        public string Maxplayers = "32"; // Default maxplayers
        public string Additional = "+exec server.cfg"; // Additional server start parameter

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////


        public async void CreateServerCFG()
        {
            //Download server.cfg
            string configPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, @"cfx-server-data-master\server.cfg");
            if (await Functions.Github.DownloadGameServerConfig(configPath, FullName))
            {
                string configText = File.ReadAllText(configPath);
                configText = configText.Replace("{{hostname}}", _serverData.ServerName);
                configText = configText.Replace("{{rcon_password}}", _serverData.GetRCONPassword());
                configText = configText.Replace("{{ip}}", _serverData.GetIPAddress());
                configText = configText.Replace("{{port}}", _serverData.ServerPort);
                configText = configText.Replace("{{maxplayers}}", Maxplayers);
                File.WriteAllText(configPath, configText);
            }

            //Download sample logo
            string logoPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, @"cfx-server-data-master\myLogo.png");
            await Functions.Github.DownloadGameServerConfig(logoPath, FullName);
        }

        public async Task<Process> Start()
        {
            string fxServerPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, @"server\FXServer.exe");
            if (!File.Exists(fxServerPath))
            {
                Error = $"FXServer.exe not found ({fxServerPath})";
                return null;
            }

            string citizenPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, @"server\citizen");
            if (!Directory.Exists(citizenPath))
            {
                Error = $"Directory citizen not found ({citizenPath})";
                return null;
            }

            string serverDataPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, "cfx-server-data-master");
            if (!Directory.Exists(serverDataPath))
            {
                Error = $"Directory cfx-server-data-master not found ({serverDataPath})";
                return null;
            }

            string configPath = Path.Combine(serverDataPath, "server.cfg");
            if (!File.Exists(configPath))
            {
                Notice = $"server.cfg not found ({configPath})";
            }

            Process p;
            if (!AllowsEmbedConsole)
            {
                p = new Process
                {
                    StartInfo =
                    {
                        WorkingDirectory = serverDataPath,
                        FileName = fxServerPath,
                        Arguments = $"+set citizen_dir \"{citizenPath}\" {_serverData.ServerParam}",
                        WindowStyle = ProcessWindowStyle.Minimized,
                        UseShellExecute = false
                    },
                    EnableRaisingEvents = true
                };
                p.Start();
            }
            else
            {
                p = new Process
                {
                    StartInfo =
                    {
                        WorkingDirectory = serverDataPath,
                        FileName = fxServerPath,
                        Arguments = $"+set citizen_dir \"{citizenPath}\" {_serverData.ServerParam}",
                        WindowStyle = ProcessWindowStyle.Minimized,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    },
                    EnableRaisingEvents = true
                };
                var serverConsole = new Functions.ServerConsole(_serverData.ServerID);
                p.OutputDataReceived += serverConsole.AddOutput;
                p.ErrorDataReceived += serverConsole.AddOutput;
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
            }

            return p;
        }

        public async Task Stop(Process p)
        {
            await Task.Run(() =>
            {
                if (p.StartInfo.RedirectStandardInput)
                {
                    p.StandardInput.WriteLine("quit");
                }
                else
                {
                    Functions.ServerConsole.SendMessageToMainWindow(p.MainWindowHandle, "quit");
                }
            });
        }

        public async Task<Process> Install()
        {
            try
            {
                using (WebClient webClient = new WebClient())
                {
                    string html = await webClient.DownloadStringTaskAsync("https://runtime.fivem.net/artifacts/fivem/build_server_windows/master/");
                    Regex regex = new Regex(@"[0-9]{4}-[ -~][^\s]{39}");
                    var matches = regex.Matches(html);

                    if (matches.Count <= 0)
                    {
                        return null;
                    }

                    //Match 1 is the latest recommended
                    string recommended = regex.Match(html).ToString();

                    //Download server.zip and extract then delete server.zip
                    string serverPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, "server");
                    Directory.CreateDirectory(serverPath);
                    string zipPath = Path.Combine(serverPath, "server.zip");
                    await webClient.DownloadFileTaskAsync($"https://runtime.fivem.net/artifacts/fivem/build_server_windows/master/{recommended}/server.zip", zipPath);
                    await Task.Run(async () =>
                    {
                        try
                        {
                            await FileManagement.ExtractZip(zipPath, serverPath);
                        }
                        catch
                        {
                            Error = "Path too long";
                        }
                    });
                    await Task.Run(() => File.Delete(zipPath));

                    //Create FiveM-version.txt and write the downloaded version with hash
                    File.WriteAllText(Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, "RedM-version.txt"), recommended);

                    //Download cfx-server-data-master and extract to folder cfx-server-data-master then delete cfx-server-data-master.zip
                    zipPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, "cfx-server-data-master.zip");
                    await webClient.DownloadFileTaskAsync("https://github.com/citizenfx/cfx-server-data/archive/master.zip", zipPath);
                    await Task.Run(() => FileManagement.ExtractZip(zipPath, Functions.ServerPath.GetServersServerFiles(_serverData.ServerID)));
                    await Task.Run(() => File.Delete(zipPath));
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        public async Task<Process> Update()
        {
            try
            {
                using (WebClient webClient = new WebClient())
                {
                    string remoteBuild = await GetRemoteBuild();

                    //Download server.zip and extract then delete server.zip
                    string serverPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, "server");
                    await Task.Run(() =>
                    {
                        try
                        {
                            Directory.Delete(serverPath, true);
                        }
                        catch
                        {
                            //ignore
                        }
                    });

                    if (Directory.Exists(serverPath))
                    {
                        Error = $"Unable to delete server folder. Path: {serverPath}";
                        return null;
                    }

                    Directory.CreateDirectory(serverPath);
                    string zipPath = Path.Combine(serverPath, "server.zip");
                    await webClient.DownloadFileTaskAsync($"https://runtime.fivem.net/artifacts/fivem/build_server_windows/master/{remoteBuild}/server.zip", zipPath);
                    await Task.Run(async () =>
                    {
                        try
                        {
                            await FileManagement.ExtractZip(zipPath, serverPath);
                        }
                        catch
                        {
                            Error = "Path too long";
                        }
                    });
                    await Task.Run(() => File.Delete(zipPath));

                    //Create FiveM-version.txt and write the downloaded version with hash
                    File.WriteAllText(Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, "RedM-version.txt"), remoteBuild);
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        public bool IsInstallValid()
        {
            string exeFile = @"server\FXServer.exe";
            string exePath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, exeFile);

            return File.Exists(exePath);
        }

        public bool IsImportValid(string path)
        {
            string exeFile = @"server\FXServer.exe";
            string exePath = Path.Combine(path, exeFile);

            Error = $"Invalid Path! Fail to find {exeFile}";
            return File.Exists(exePath);
        }

        public string GetLocalBuild()
        {
            string versionPath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, "RedM-version.txt");
           // Error = $"Fail to get local build";
            return File.Exists(versionPath) ? File.ReadAllText(versionPath) : string.Empty;
        }

        public async Task<string> GetRemoteBuild()
        {
            try
            {
                using (WebClient webClient = new WebClient())
                {
                    string html = await webClient.DownloadStringTaskAsync("https://runtime.fivem.net/artifacts/fivem/build_server_windows/master/");
                    Regex regex = new Regex(@"[0-9]{4}-[ -~][^\s]{39}");
                    var matches = regex.Matches(html);

                    return matches[0].Value;
                }
            }
            catch
            {
                //ignore
            }

            Error = $"Fail to get remote build";
            return string.Empty;
        }
    }
}
